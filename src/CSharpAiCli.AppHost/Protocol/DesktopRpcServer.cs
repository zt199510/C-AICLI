using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CSharpAiCli.AppHost.Protocol.Generated;
using CSharpAiCli.Application;

namespace CSharpAiCli.AppHost.Protocol;

public sealed class DesktopRpcServer : IDisposable
{
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = DesktopProtocolDefinition.MaxJsonDepth
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = DesktopProtocolDefinition.MaxJsonDepth,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private readonly DesktopApplicationSessionFactory sessionFactory;
    private readonly DesktopThreadNotificationSequencer notificationSequencer = new(TimeProvider.System);
    private DesktopApplicationSession? applicationSession;
    private Func<long, bool>? cancelRequest;
    private bool threadNotificationsNegotiated;
    private SessionState state;

    public DesktopRpcServer()
        : this(new DesktopApplicationSessionFactory())
    {
    }

    public DesktopRpcServer(DesktopApplicationSessionFactory sessionFactory)
    {
        this.sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
    }

    public async Task RunAsync(Stream input, Stream output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        using CancellationTokenSource sessionCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using DesktopRpcOutputQueue outputQueue = new(
            DesktopProtocolDefinition.MaxOutputQueueFrames,
            DesktopProtocolDefinition.MaxOutputQueueBytes);
        using DesktopRpcRuntime runtime = new(
            DesktopProtocolDefinition.MaxInFlight,
            outputQueue,
            (payload, requestCancellation) => new ValueTask<byte[]>(Task.Run(
                () => HandleCore(payload, requestCancellation),
                requestCancellation)),
            rateLimiter: new DesktopRpcRateLimiter(
                DesktopProtocolDefinition.RequestRateBurst,
                DesktopProtocolDefinition.RequestRatePerSecond,
                TimeProvider.System),
            notificationFactory: CreateNotification);
        IDesktopRpcDeadlineScheduler deadlineScheduler = new DesktopRpcDeadlineScheduler(TimeProvider.System);
        DesktopRpcTaskDrain taskDrain = new(deadlineScheduler);
        cancelRequest = runtime.TryCancel;
        Task writer = WriteOutputAsync(output, outputQueue, sessionCancellation);
        List<Task> active = [];
        List<Task> detached = [];
        IDesktopRpcDeadline? shutdownDeadline = null;
        bool disconnected = false;
        try
        {
            while (state is not SessionState.ShuttingDown and not SessionState.Closed)
            {
                await ObserveCompletedAsync(active).ConfigureAwait(false);
                byte[]? payload = await DesktopProtocolFraming.ReadFrameAsync(
                    input,
                    sessionCancellation.Token).ConfigureAwait(false);
                if (payload is null)
                {
                    disconnected = true;
                    sessionCancellation.Cancel();
                    break;
                }

                string? method = TryReadMethod(payload);
                if (string.Equals(method, DesktopProtocolDefinition.WorkspaceOpenMethod, StringComparison.Ordinal) &&
                    state == SessionState.WorkspaceReady)
                {
                    runtime.CancelAll();
                    if (!await DrainActiveAsync(active, taskDrain).ConfigureAwait(false))
                    {
                        detached.AddRange(active);
                        active.Clear();
                        disconnected = true;
                        sessionCancellation.Cancel();
                        break;
                    }

                    active.Clear();
                }

                if (string.Equals(method, DesktopProtocolDefinition.ShutdownMethod, StringComparison.Ordinal))
                {
                    shutdownDeadline ??= deadlineScheduler.Create(
                        TimeSpan.FromMilliseconds(DesktopProtocolDefinition.ShutdownDrainMs));
                    runtime.CancelAll();
                    if (!await DrainActiveAsync(active, taskDrain, shutdownDeadline).ConfigureAwait(false))
                    {
                        detached.AddRange(active);
                        active.Clear();
                        disconnected = true;
                        sessionCancellation.Cancel();
                        break;
                    }

                    active.Clear();
                }

                if (state == SessionState.Created || IsControlMethod(method))
                {
                    await runtime.ProcessFrameAsync(payload, sessionCancellation.Token).ConfigureAwait(false);
                }
                else
                {
                    active.Add(ProcessBusinessAsync(runtime, payload, sessionCancellation));
                }
            }

            if (!disconnected)
            {
                shutdownDeadline ??= deadlineScheduler.Create(
                    TimeSpan.FromMilliseconds(DesktopProtocolDefinition.ShutdownDrainMs));
                if (await DrainActiveAsync(active, taskDrain, shutdownDeadline).ConfigureAwait(false))
                {
                    active.Clear();
                    await WaitForShutdownDrainAsync(
                        outputQueue,
                        sessionCancellation,
                        shutdownDeadline).ConfigureAwait(false);
                }
                else
                {
                    detached.AddRange(active);
                    active.Clear();
                    sessionCancellation.Cancel();
                }
            }
        }
        finally
        {
            cancelRequest = null;
            sessionCancellation.Cancel();
            if (active.Count > 0)
            {
                if (await taskDrain.WaitAsync(
                    active,
                    TimeSpan.FromMilliseconds(DesktopProtocolDefinition.ShutdownDrainMs),
                    CancellationToken.None).ConfigureAwait(false))
                {
                    await ObserveForCleanupAsync(active).ConfigureAwait(false);
                }
                else
                {
                    detached.AddRange(active);
                }
            }

            try
            {
                await writer.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (sessionCancellation.IsCancellationRequested)
            {
            }

            Close(detached);
            shutdownDeadline?.Dispose();
        }
    }

    public byte[] Handle(ReadOnlyMemory<byte> payload) => HandleCore(payload, CancellationToken.None);

    private byte[] HandleCore(ReadOnlyMemory<byte> payload, CancellationToken requestCancellation)
    {
        long? id = null;
        try
        {
            requestCancellation.ThrowIfCancellationRequested();
            using JsonDocument document = JsonDocument.Parse(payload, DocumentOptions);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !HasExactEnvelopeMembers(root))
            {
                return Error(id, DesktopProtocolDefinition.InvalidRequestRpcCode,
                    DesktopProtocolDefinition.InvalidRequestError, "JSON-RPC request is invalid.");
            }

            if (!TryReadRequestId(root, out long requestId))
            {
                return Error(id, DesktopProtocolDefinition.InvalidRequestRpcCode,
                    DesktopProtocolDefinition.InvalidRequestError, "JSON-RPC request id is invalid.");
            }

            id = requestId;
            if (!root.TryGetProperty("jsonrpc", out JsonElement version) ||
                version.ValueKind != JsonValueKind.String ||
                !string.Equals(version.GetString(), "2.0", StringComparison.Ordinal) ||
                !root.TryGetProperty("method", out JsonElement methodElement) ||
                methodElement.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("params", out JsonElement parameters) ||
                parameters.ValueKind != JsonValueKind.Object)
            {
                return Error(id, DesktopProtocolDefinition.InvalidRequestRpcCode,
                    DesktopProtocolDefinition.InvalidRequestError, "JSON-RPC request is invalid.");
            }

            string method = methodElement.GetString()!;
            if (Encoding.UTF8.GetByteCount(method) > DesktopProtocolDefinition.MaxMethodNameBytes)
            {
                return Error(id, DesktopProtocolDefinition.MethodNotFoundRpcCode,
                    DesktopProtocolDefinition.MethodNotFoundError, "Desktop protocol method is not available.");
            }

            if (state == SessionState.Created &&
                !string.Equals(method, DesktopProtocolDefinition.InitializeMethod, StringComparison.Ordinal))
            {
                return Error(id, DesktopProtocolDefinition.InitializeRequiredRpcCode,
                    DesktopProtocolDefinition.InitializeRequiredError, "app.initialize must complete first.");
            }

            if (state is SessionState.ShuttingDown or SessionState.Closed)
            {
                return Error(id, DesktopProtocolDefinition.ShutdownInProgressRpcCode,
                    DesktopProtocolDefinition.ShutdownInProgressError, "Protocol session is shutting down.");
            }

            if (state == SessionState.Initialized && IsWorkspaceMethod(method))
            {
                return Error(id, DesktopProtocolDefinition.WorkspaceRequiredRpcCode,
                    DesktopProtocolDefinition.WorkspaceRequiredError, "An active workspace is required.");
            }

            return method switch
            {
                DesktopProtocolDefinition.InitializeMethod => Initialize(id, parameters, requestCancellation),
                DesktopProtocolDefinition.CancelMethod => Cancel(id, parameters),
                DesktopProtocolDefinition.ShutdownMethod => Shutdown(id, parameters),
                DesktopProtocolDefinition.WorkspaceOpenMethod => OpenWorkspace(id, parameters, requestCancellation),
                DesktopProtocolDefinition.ThreadListMethod => ListThreads(id, parameters, requestCancellation),
                DesktopProtocolDefinition.ThreadGetMethod => GetThread(id, parameters, requestCancellation),
                DesktopProtocolDefinition.ThreadCreateMethod => CreateThread(id, parameters, requestCancellation),
                DesktopProtocolDefinition.ThreadRenameMethod => RenameThread(id, parameters, requestCancellation),
                DesktopProtocolDefinition.ThreadArchiveMethod => ArchiveThread(id, parameters, requestCancellation),
                DesktopProtocolDefinition.ThreadDeleteMethod => DeleteThread(id, parameters, requestCancellation),
                DesktopProtocolDefinition.CatalogListMethod => ListCatalog(id, parameters, requestCancellation),
                DesktopProtocolDefinition.ChangesGetMethod => GetChanges(id, parameters, requestCancellation),
                DesktopProtocolDefinition.ReportListMethod => ListReports(id, parameters, requestCancellation),
                DesktopProtocolDefinition.ReportGetMethod => GetReport(id, parameters, requestCancellation),
                DesktopProtocolDefinition.ArtifactListMethod => ListArtifacts(id, parameters, requestCancellation),
                DesktopProtocolDefinition.ArtifactGetMethod => GetArtifact(id, parameters, requestCancellation),
                _ => Error(id, DesktopProtocolDefinition.MethodNotFoundRpcCode,
                    DesktopProtocolDefinition.MethodNotFoundError, "Desktop protocol method is not available.")
            };
        }
        catch (JsonException)
        {
            return Error(id, DesktopProtocolDefinition.ParseErrorRpcCode,
                DesktopProtocolDefinition.ParseErrorError, "JSON payload is invalid.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Error(id, DesktopProtocolDefinition.InternalErrorRpcCode,
                DesktopProtocolDefinition.InternalErrorError, "Desktop protocol request failed.");
        }
    }

    public void Dispose() => Close();

    private byte[] Initialize(long? id, JsonElement parameters, CancellationToken cancellationToken)
    {
        if (state != SessionState.Created)
        {
            return Error(id, DesktopProtocolDefinition.AlreadyInitializedRpcCode,
                DesktopProtocolDefinition.AlreadyInitializedError, "Desktop protocol is already initialized.");
        }

        if (!TryDeserialize(parameters, out InitializeParams? request) || request is null ||
            !DesktopProtocolValidation.TryValidate(request, out _))
        {
            return InvalidParams(id, "Initialize parameters are invalid.");
        }

        if (!string.Equals(request.ProtocolVersion, DesktopProtocolDefinition.Version, StringComparison.Ordinal))
        {
            return Error(id, DesktopProtocolDefinition.ProtocolVersionUnsupportedRpcCode,
                DesktopProtocolDefinition.ProtocolVersionUnsupportedError,
                "Desktop protocol version is not supported.");
        }

        if (!string.Equals(request.ContractSha256, DesktopProtocolDefinition.ContractSha256, StringComparison.Ordinal))
        {
            return Error(id, DesktopProtocolDefinition.ContractMismatchRpcCode,
                DesktopProtocolDefinition.ContractMismatchError, "Desktop protocol contract does not match.");
        }

        HashSet<string> requested = request.RequestedCapabilities.ToHashSet(StringComparer.Ordinal);
        if (requested.Count != request.RequestedCapabilities.Count ||
            requested.Any(capability => !DesktopProtocolDefinition.Capabilities.Contains(capability, StringComparer.Ordinal)) ||
            DesktopProtocolDefinition.RequiredCapabilities.Any(capability => !requested.Contains(capability)))
        {
            return Error(id, DesktopProtocolDefinition.CapabilityInvalidRpcCode,
                DesktopProtocolDefinition.CapabilityInvalidError, "Requested protocol capabilities are invalid.");
        }

        state = SessionState.Initialized;
        threadNotificationsNegotiated = requested.Contains(DesktopProtocolDefinition.ThreadChangedCapability);
        InitializeResult result = new()
        {
            SchemaVersion = DesktopProtocolDefinition.SchemaVersion,
            ProtocolVersion = DesktopProtocolDefinition.Version,
            ContractSha256 = DesktopProtocolDefinition.ContractSha256,
            ServerVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0",
            ServerInstanceId = "server_" + Guid.NewGuid().ToString("N")[..24],
            NegotiatedCapabilities = DesktopProtocolDefinition.Capabilities.Where(requested.Contains).ToArray(),
            Methods = DesktopProtocolDefinition.Methods,
            Notifications = requested.Contains(DesktopProtocolDefinition.ThreadChangedCapability)
                ? DesktopProtocolDefinition.Notifications
                : [],
            Limits = CreateProtocolLimits(),
            Security = new SecuritySummary
            {
                RendererNodeAccess = false,
                ArbitraryFileAccess = false,
                ArbitraryProcessAccess = false,
                WorkspaceAuthority = "server",
                Transport = "framed-json-rpc-stdio"
            }
        };
        return Success(id, result);
    }

    private byte[] OpenWorkspace(long? id, JsonElement parameters, CancellationToken cancellationToken)
    {
        if (!TryDeserialize(parameters, out WorkspaceOpenParams? request) || request is null ||
            !DesktopProtocolValidation.TryValidate(request, out _))
        {
            return InvalidParams(id, "Workspace parameters are invalid.");
        }

        DesktopApplicationSessionOpenResult opened = sessionFactory.Open(
            request.Path,
            cancellationToken: cancellationToken);
        if (!opened.Succeeded || opened.Session is null)
        {
            return Success(id, new WorkspaceOpenResult
            {
                SchemaVersion = DesktopProtocolDefinition.SchemaVersion,
                Succeeded = false,
                Data = null,
                Error = Map(opened.Error ?? new ApplicationError(
                    "workspace-open-failed",
                    ApplicationErrorCategory.Workspace,
                    "Workspace could not be opened safely.",
                    Retryable: false)),
                Diagnostics = opened.Diagnostics.Select(Map).ToArray(),
                Truncated = false
            });
        }

        DesktopApplicationSession? previous = applicationSession;
        applicationSession = opened.Session;
        state = SessionState.WorkspaceReady;
        previous?.Dispose();
        return Success(id, new WorkspaceOpenResult
        {
            SchemaVersion = DesktopProtocolDefinition.SchemaVersion,
            Succeeded = true,
            Data = Map(opened.Session.Workspace),
            Error = null,
            Diagnostics = opened.Diagnostics.Select(Map).ToArray(),
            Truncated = false
        });
    }

    private byte[] Cancel(long? id, JsonElement parameters)
    {
        if (!TryDeserialize(parameters, out CancelParams? request) || request is null ||
            !DesktopProtocolValidation.TryValidate(request, out _))
        {
            return InvalidParams(id, "Cancel parameters are invalid.");
        }

        return Success(id, new CancelResult
        {
            SchemaVersion = DesktopProtocolDefinition.SchemaVersion,
            Accepted = request.TargetRequestId != id && cancelRequest?.Invoke(request.TargetRequestId) == true
        });
    }

    private byte[] ListThreads(long? id, JsonElement parameters, CancellationToken cancellationToken)
    {
        if (!TryDeserialize(parameters, out ThreadListParams? request) || request is null ||
            !DesktopProtocolValidation.TryValidate(request, out _))
        {
            return InvalidParams(id, "Thread list parameters are invalid.");
        }

        return Success(id, DesktopProtocolMapper.Map(applicationSession!.ListThreads(
            request.PageSize,
            cancellationToken)));
    }

    private byte[] GetThread(long? id, JsonElement parameters, CancellationToken cancellationToken)
    {
        if (!TryDeserialize(parameters, out ThreadGetParams? request) || request is null ||
            !DesktopProtocolValidation.TryValidate(request, out _))
        {
            return InvalidParams(id, "Thread get parameters are invalid.");
        }

        return Success(id, DesktopProtocolMapper.Map(applicationSession!.GetThread(
            request.ThreadId,
            request.AfterSequence,
            request.TimelinePageSize,
            cancellationToken)));
    }

    private byte[] CreateThread(long? id, JsonElement parameters, CancellationToken cancellationToken)
    {
        if (!TryDeserialize(parameters, out ThreadCreateParams? request) || request is null ||
            !DesktopProtocolValidation.TryValidate(request, out _))
        {
            return InvalidParams(id, "Thread create parameters are invalid.");
        }

        return Success(id, DesktopProtocolMapper.Map(applicationSession!.CreateThread(
            request.Title,
            cancellationToken)));
    }

    private byte[] RenameThread(long? id, JsonElement parameters, CancellationToken cancellationToken)
    {
        if (!TryDeserialize(parameters, out ThreadRenameParams? request) || request is null ||
            !DesktopProtocolValidation.TryValidate(request, out _))
        {
            return InvalidParams(id, "Thread rename parameters are invalid.");
        }

        return Success(id, DesktopProtocolMapper.Map(applicationSession!.RenameThread(
            request.ThreadId,
            request.ExpectedRevision,
            request.Title,
            cancellationToken)));
    }

    private byte[] ArchiveThread(long? id, JsonElement parameters, CancellationToken cancellationToken)
    {
        if (!TryDeserialize(parameters, out ThreadArchiveParams? request) || request is null ||
            !DesktopProtocolValidation.TryValidate(request, out _))
        {
            return InvalidParams(id, "Thread archive parameters are invalid.");
        }

        return Success(id, DesktopProtocolMapper.Map(applicationSession!.ArchiveThread(
            request.ThreadId,
            request.ExpectedRevision,
            cancellationToken)));
    }

    private byte[] DeleteThread(long? id, JsonElement parameters, CancellationToken cancellationToken)
    {
        if (!TryDeserialize(parameters, out ThreadDeleteParams? request) || request is null ||
            !DesktopProtocolValidation.TryValidate(request, out _))
        {
            return InvalidParams(id, "Thread delete parameters are invalid.");
        }

        return Success(id, DesktopProtocolMapper.Map(applicationSession!.DeleteThread(
            request.ThreadId,
            request.ExpectedRevision,
            request.Confirmation,
            cancellationToken)));
    }

    private byte[] ListCatalog(long? id, JsonElement parameters, CancellationToken cancellationToken)
    {
        if (!TryDeserialize(parameters, out CatalogListParams? request) || request is null ||
            !DesktopProtocolValidation.TryValidate(request, out _))
        {
            return InvalidParams(id, "Catalog list parameters are invalid.");
        }

        return Success(id, DesktopProtocolMapper.Map(applicationSession!.ListCatalog(
            request.Kind,
            request.PageSize,
            cancellationToken)));
    }

    private byte[] GetChanges(long? id, JsonElement parameters, CancellationToken cancellationToken)
    {
        if (!TryDeserialize(parameters, out ChangesGetParams? request) || request is null ||
            !DesktopProtocolValidation.TryValidate(request, out _))
        {
            return InvalidParams(id, "Changes parameters are invalid.");
        }

        return Success(id, DesktopProtocolMapper.Map(applicationSession!.GetChanges(
            request.SessionName,
            cancellationToken)));
    }

    private byte[] ListReports(long? id, JsonElement parameters, CancellationToken cancellationToken)
    {
        if (!TryDeserialize(parameters, out ReportListParams? request) || request is null ||
            !DesktopProtocolValidation.TryValidate(request, out _))
        {
            return InvalidParams(id, "Report list parameters are invalid.");
        }

        return Success(id, DesktopProtocolMapper.Map(applicationSession!.ListReports(
            request.PageSize,
            cancellationToken)));
    }

    private byte[] GetReport(long? id, JsonElement parameters, CancellationToken cancellationToken)
    {
        if (!TryDeserialize(parameters, out ReportGetParams? request) || request is null ||
            !DesktopProtocolValidation.TryValidate(request, out _))
        {
            return InvalidParams(id, "Report get parameters are invalid.");
        }

        return Success(id, DesktopProtocolMapper.Map(applicationSession!.GetReport(
            request.ReportId,
            cancellationToken)));
    }

    private byte[] ListArtifacts(long? id, JsonElement parameters, CancellationToken cancellationToken)
    {
        if (!TryDeserialize(parameters, out ArtifactListParams? request) || request is null ||
            !DesktopProtocolValidation.TryValidate(request, out _))
        {
            return InvalidParams(id, "Artifact list parameters are invalid.");
        }

        return Success(id, DesktopProtocolMapper.Map(applicationSession!.ListArtifacts(
            request.PageSize,
            request.RunId,
            request.Status,
            cancellationToken)));
    }

    private byte[] GetArtifact(long? id, JsonElement parameters, CancellationToken cancellationToken)
    {
        if (!TryDeserialize(parameters, out ArtifactGetParams? request) || request is null ||
            !DesktopProtocolValidation.TryValidate(request, out _))
        {
            return InvalidParams(id, "Artifact get parameters are invalid.");
        }

        return Success(id, DesktopProtocolMapper.Map(applicationSession!.GetArtifact(
            request.ArtifactId,
            cancellationToken)));
    }

    private byte[] Shutdown(long? id, JsonElement parameters)
    {
        if (!TryDeserialize(parameters, out ShutdownParams? request) || request is null ||
            !DesktopProtocolValidation.TryValidate(request, out _))
        {
            return InvalidParams(id, "Shutdown parameters are invalid.");
        }

        state = SessionState.ShuttingDown;
        return Success(id, new ShutdownResult
        {
            SchemaVersion = DesktopProtocolDefinition.SchemaVersion,
            Accepted = true
        });
    }

    private static ProtocolLimits CreateProtocolLimits() => new()
    {
        MaxHeaderBytes = DesktopProtocolDefinition.MaxHeaderBytes,
        MaxBodyBytes = DesktopProtocolDefinition.MaxBodyBytes,
        MaxJsonDepth = DesktopProtocolDefinition.MaxJsonDepth,
        MaxMethodNameBytes = DesktopProtocolDefinition.MaxMethodNameBytes,
        MaxRequestId = DesktopProtocolDefinition.MaxRequestId,
        InitializeTimeoutMs = DesktopProtocolDefinition.InitializeTimeoutMs,
        DefaultQueryTimeoutMs = DesktopProtocolDefinition.DefaultQueryTimeoutMs,
        MutationTimeoutMs = DesktopProtocolDefinition.MutationTimeoutMs,
        ShutdownDrainMs = DesktopProtocolDefinition.ShutdownDrainMs,
        MaxInFlight = DesktopProtocolDefinition.MaxInFlight,
        RequestRateBurst = DesktopProtocolDefinition.RequestRateBurst,
        RequestRatePerSecond = DesktopProtocolDefinition.RequestRatePerSecond,
        MaxOutputQueueFrames = DesktopProtocolDefinition.MaxOutputQueueFrames,
        MaxOutputQueueBytes = DesktopProtocolDefinition.MaxOutputQueueBytes,
        MaxDiagnosticBytes = DesktopProtocolDefinition.MaxDiagnosticBytes,
        MaxRetainedStderrBytes = DesktopProtocolDefinition.MaxRetainedStderrBytes,
        ApplicationTargetBytes = DesktopProtocolDefinition.ApplicationTargetBytes
    };

    private static WorkspaceSnapshotData Map(WorkspaceSnapshotProjection value) => new()
    {
        WorkspaceId = value.WorkspaceId,
        RootPath = value.RootPath,
        Status = value.Status,
        Capabilities = new WorkspaceCapabilityData
        {
            ReadOnlyQueries = value.Capabilities.ReadOnlyQueries,
            GitQueries = value.Capabilities.GitQueries,
            LocalCatalogs = value.Capabilities.LocalCatalogs,
            ManagedArtifacts = value.Capabilities.ManagedArtifacts
        },
        Configuration = new WorkspaceConfigurationData
        {
            HasApiKey = value.Configuration.HasApiKey,
            ApiKeySource = value.Configuration.ApiKeySource,
            ModelSource = value.Configuration.ModelSource,
            AgentBackendSource = value.Configuration.AgentBackendSource,
            ApprovalMode = value.Configuration.ApprovalMode,
            ApprovalModeSource = value.Configuration.ApprovalModeSource,
            LoadedSourceCount = value.Configuration.LoadedSourceCount
        }
    };

    private static ApplicationErrorData Map(ApplicationError value) => new()
    {
        Code = value.Code,
        Category = value.Category,
        SafeMessage = value.SafeMessage,
        Retryable = value.Retryable
    };

    private static ApplicationDiagnosticData Map(ApplicationDiagnostic value) => new()
    {
        Code = value.Code,
        Category = value.Category,
        SafeMessage = value.SafeMessage
    };

    private static bool TryDeserialize<T>(JsonElement element, out T? value)
    {
        if (HasDuplicateMembers(element))
        {
            value = default;
            return false;
        }

        try
        {
            value = element.Deserialize<T>(JsonOptions);
            return value is not null;
        }
        catch (JsonException)
        {
            value = default;
            return false;
        }
    }

    private static bool HasDuplicateMembers(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            HashSet<string> names = new(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Add(property.Name) || HasDuplicateMembers(property.Value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                if (HasDuplicateMembers(item))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasExactEnvelopeMembers(JsonElement root)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (!seen.Add(property.Name) || property.Name is not ("jsonrpc" or "id" or "method" or "params"))
            {
                return false;
            }
        }

        return seen.SetEquals(["jsonrpc", "id", "method", "params"]);
    }

    private static bool IsWorkspaceMethod(string method) =>
        DesktopProtocolDefinition.RequiresWorkspace(method);

    private static bool TryReadRequestId(JsonElement root, out long id)
    {
        id = 0;
        if (!root.TryGetProperty("id", out JsonElement element) || element.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        string raw = element.GetRawText();
        return raw.Length > 0 && raw.All(character => character is >= '0' and <= '9') &&
            element.TryGetInt64(out id) && id >= 1 && id <= DesktopProtocolDefinition.MaxRequestId;
    }

    private static byte[] Success<T>(long? id, T result)
    {
        byte[] response = JsonSerializer.SerializeToUtf8Bytes(new
        {
            jsonrpc = "2.0",
            id,
            result
        }, JsonOptions);
        return response.Length <= DesktopProtocolDefinition.MaxBodyBytes
            ? response
            : Error(id, DesktopProtocolDefinition.ResponseTooLargeRpcCode,
                DesktopProtocolDefinition.ResponseTooLargeError, "Protocol response is too large.");
    }

    private static byte[] InvalidParams(long? id, string message) =>
        Error(id, DesktopProtocolDefinition.InvalidParamsRpcCode,
            DesktopProtocolDefinition.InvalidParamsError, message);

    private static byte[] Error(long? id, int code, string errorCode, string safeMessage) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            jsonrpc = "2.0",
            id,
            error = new
            {
                code,
                message = safeMessage,
                data = new { errorCode, safeMessage }
            }
        }, JsonOptions);

    private static async Task ProcessBusinessAsync(
        DesktopRpcRuntime runtime,
        ReadOnlyMemory<byte> payload,
        CancellationTokenSource sessionCancellation)
    {
        try
        {
            await runtime.ProcessFrameAsync(payload, sessionCancellation.Token).ConfigureAwait(false);
        }
        catch
        {
            sessionCancellation.Cancel();
            throw;
        }
    }

    private static async Task WriteOutputAsync(
        Stream output,
        DesktopRpcOutputQueue queue,
        CancellationTokenSource sessionCancellation)
    {
        try
        {
            while (true)
            {
                DesktopRpcOutboundFrame frame = await queue.DequeueAsync(
                    sessionCancellation.Token).ConfigureAwait(false);
                await DesktopProtocolFraming.WriteFrameAsync(
                    output,
                    frame.Payload,
                    sessionCancellation.Token).ConfigureAwait(false);
                queue.CompleteWrite(frame);
            }
        }
        catch (OperationCanceledException) when (sessionCancellation.IsCancellationRequested)
        {
        }
        catch
        {
            sessionCancellation.Cancel();
            throw;
        }
    }

    private static async Task ObserveCompletedAsync(List<Task> active)
    {
        for (int index = active.Count - 1; index >= 0; index--)
        {
            Task task = active[index];
            if (!task.IsCompleted)
            {
                continue;
            }

            active.RemoveAt(index);
            await task.ConfigureAwait(false);
        }
    }

    private static async Task ObserveAllAsync(IReadOnlyList<Task> active)
    {
        if (active.Count > 0)
        {
            await Task.WhenAll(active).ConfigureAwait(false);
        }
    }

    private static async Task<bool> DrainActiveAsync(
        IReadOnlyCollection<Task> active,
        DesktopRpcTaskDrain taskDrain,
        IDesktopRpcDeadline? deadline = null)
    {
        bool completed = deadline is null
            ? await taskDrain.WaitAsync(
                active,
                TimeSpan.FromMilliseconds(DesktopProtocolDefinition.ShutdownDrainMs),
                CancellationToken.None).ConfigureAwait(false)
            : await taskDrain.WaitAsync(
                active,
                deadline,
                CancellationToken.None).ConfigureAwait(false);
        if (completed)
        {
            await ObserveAllAsync(active.ToArray()).ConfigureAwait(false);
        }

        return completed;
    }

    private static async Task ObserveForCleanupAsync(IReadOnlyCollection<Task> active)
    {
        try
        {
            await Task.WhenAll(active).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private static async Task WaitForShutdownDrainAsync(
        DesktopRpcOutputQueue output,
        CancellationTokenSource sessionCancellation,
        IDesktopRpcDeadline deadline)
    {
        using CancellationTokenSource drain = CancellationTokenSource.CreateLinkedTokenSource(
            sessionCancellation.Token,
            deadline.Token);
        try
        {
            await output.WaitUntilEmptyAsync(drain.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (deadline.IsExpired)
        {
            sessionCancellation.Cancel();
        }
    }

    private static string? TryReadMethod(ReadOnlyMemory<byte> payload)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload, DocumentOptions);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("method", out JsonElement method) &&
                method.ValueKind == JsonValueKind.String
                    ? method.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsControlMethod(string? method) =>
        method is null || !DesktopProtocolDefinition.RequiresWorkspace(method);

    private byte[]? CreateNotification(
        ReadOnlyMemory<byte> requestPayload,
        ReadOnlyMemory<byte> responsePayload) =>
        threadNotificationsNegotiated && applicationSession is not null
            ? notificationSequencer.TryCreate(
                applicationSession.Workspace.WorkspaceId,
                requestPayload,
                responsePayload)
            : null;

    private void Close(IReadOnlyCollection<Task>? pending = null)
    {
        if (state == SessionState.Closed)
        {
            return;
        }

        state = SessionState.Closed;
        DesktopApplicationSession? session = applicationSession;
        applicationSession = null;
        if (session is null)
        {
            return;
        }

        if (pending is null || pending.Count == 0)
        {
            session.Dispose();
            return;
        }

        _ = DisposeSessionAfterHandlersAsync(session, pending.ToArray());
    }

    private static async Task DisposeSessionAfterHandlersAsync(
        DesktopApplicationSession session,
        IReadOnlyCollection<Task> pending)
    {
        await ObserveForCleanupAsync(pending).ConfigureAwait(false);
        session.Dispose();
    }

    private enum SessionState
    {
        Created,
        Initialized,
        WorkspaceReady,
        ShuttingDown,
        Closed
    }
}
