using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Reflection;
using CSharpAiCli.AppHost.Protocol;
using CSharpAiCli.AppHost.Protocol.Generated;

namespace CSharpAiCli.Tests;

public sealed class DesktopProtocolTests
{
    [Fact]
    public async Task Framing_round_trips_multiline_utf8_with_partial_reads()
    {
        byte[] payload = Encoding.UTF8.GetBytes("{\"text\":\"line one\\nline two 中文\"}");
        using MemoryStream framed = new();
        await DesktopProtocolFraming.WriteFrameAsync(framed, payload);
        framed.Position = 0;
        using ChunkedReadStream chunks = new(framed, maxChunkSize: 2);

        byte[]? read = await DesktopProtocolFraming.ReadFrameAsync(chunks);

        Assert.Equal(payload, read);
        Assert.Null(await DesktopProtocolFraming.ReadFrameAsync(chunks));
    }

    [Theory]
    [InlineData("Content-Length: nope\r\n\r\n", "frame-content-length-invalid")]
    [InlineData("Content-Type: application/json\r\n\r\n", "frame-header-invalid")]
    [InlineData("Content-Length: 1048577\r\n\r\n", "frame-body-too-large")]
    [InlineData("Content-Length: 0\r\n\r\n", "frame-body-empty")]
    [InlineData("Content-Length: 1\r\nContent-Length: 1\r\n\r\nx", "frame-content-length-invalid")]
    [InlineData("Content-Length: 1\n\nx", "frame-header-invalid")]
    [InlineData("X-Unknown: value\r\nContent-Length: 1\r\n\r\nx", "frame-header-invalid")]
    public async Task Framing_rejects_invalid_or_oversized_headers(string header, string expectedError)
    {
        using MemoryStream stream = new(Encoding.ASCII.GetBytes(header));

        DesktopProtocolException exception = await Assert.ThrowsAsync<DesktopProtocolException>(
            async () => await DesktopProtocolFraming.ReadFrameAsync(stream));

        Assert.Equal(expectedError, exception.ErrorCode);
    }

    [Fact]
    public async Task Framing_accepts_the_reviewed_utf8_content_type()
    {
        using MemoryStream stream = new(Encoding.ASCII.GetBytes(
            "Content-Length: 2\r\nContent-Type: application/vscode-jsonrpc; charset=utf-8\r\n\r\n{}"));

        Assert.Equal("{}", Encoding.UTF8.GetString(
            Assert.IsType<byte[]>(await DesktopProtocolFraming.ReadFrameAsync(stream))));
    }

    [Theory]
    [InlineData(new byte[] { 0xC3, 0x28 }, "frame-utf8-invalid")]
    [InlineData(new byte[] { 0x7B }, "frame-body-incomplete")]
    public async Task Framing_rejects_invalid_utf8_and_partial_bodies(byte[] body, string expectedError)
    {
        int declaredLength = expectedError == "frame-body-incomplete" ? body.Length + 1 : body.Length;
        using MemoryStream stream = new();
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"Content-Length: {declaredLength}\r\n\r\n"));
        await stream.WriteAsync(body);
        stream.Position = 0;

        DesktopProtocolException exception = await Assert.ThrowsAsync<DesktopProtocolException>(
            async () => await DesktopProtocolFraming.ReadFrameAsync(stream));

        Assert.Equal(expectedError, exception.ErrorCode);
    }

    [Fact]
    public async Task Framing_rejects_non_ascii_header_bytes_and_empty_writes()
    {
        using MemoryStream input = new(Encoding.UTF8.GetBytes("Content-Léngth: 1\r\n\r\nx"));
        DesktopProtocolException readError = await Assert.ThrowsAsync<DesktopProtocolException>(
            async () => await DesktopProtocolFraming.ReadFrameAsync(input));
        Assert.Equal("frame-header-invalid", readError.ErrorCode);

        using MemoryStream output = new();
        DesktopProtocolException writeError = await Assert.ThrowsAsync<DesktopProtocolException>(
            async () => await DesktopProtocolFraming.WriteFrameAsync(output, ReadOnlyMemory<byte>.Empty));
        Assert.Equal("frame-body-empty", writeError.ErrorCode);
        Assert.Empty(output.ToArray());
    }

    [Fact]
    public async Task Server_requires_handshake_then_opens_workspace_and_shuts_down()
    {
        using TempDirectory temp = TempDirectory.Create();
        using MemoryStream input = new();
        await WriteRequest(input, 1, DesktopProtocolDefinition.InitializeMethod, new
        {
            schemaVersion = DesktopProtocolDefinition.SchemaVersion,
            protocolVersion = DesktopProtocolDefinition.Version,
            contractSha256 = DesktopProtocolDefinition.ContractSha256,
            clientName = "desktop-tests",
            clientVersion = "1.0.0",
            clientInstanceId = "test-client-1",
            requestedCapabilities = new[]
            {
                DesktopProtocolDefinition.FramedJsonRpcCapability,
                DesktopProtocolDefinition.WorkspaceSessionCapability,
                DesktopProtocolDefinition.ApplicationOutcomeCapability,
                DesktopProtocolDefinition.ThreadChangedCapability
            }
        });
        await WriteRequest(input, 2, DesktopProtocolDefinition.WorkspaceOpenMethod, new
        {
            schemaVersion = DesktopProtocolDefinition.SchemaVersion,
            path = temp.Path
        });
        await WriteRequest(input, 3, DesktopProtocolDefinition.ShutdownMethod, new
        {
            schemaVersion = DesktopProtocolDefinition.SchemaVersion,
            reason = "test-complete"
        });
        input.Position = 0;
        using MemoryStream output = new();
        DesktopRpcServer server = new();

        await server.RunAsync(input, output, CancellationToken.None);

        output.Position = 0;
        using JsonDocument initialize = await ReadResponse(output);
        using JsonDocument workspace = await ReadResponse(output);
        using JsonDocument shutdown = await ReadResponse(output);
        Assert.Equal(DesktopProtocolDefinition.Version,
            initialize.RootElement.GetProperty("result").GetProperty("protocolVersion").GetString());
        Assert.Equal(DesktopProtocolDefinition.ContractSha256,
            initialize.RootElement.GetProperty("result").GetProperty("contractSha256").GetString());
        Assert.Equal(DesktopProtocolDefinition.Methods.Count,
            initialize.RootElement.GetProperty("result").GetProperty("methods").GetArrayLength());
        Assert.False(initialize.RootElement.GetProperty("result").GetProperty("security")
            .GetProperty("rendererNodeAccess").GetBoolean());
        Assert.True(workspace.RootElement.GetProperty("result").GetProperty("succeeded").GetBoolean());
        Assert.Equal(Path.GetFullPath(temp.Path),
            workspace.RootElement.GetProperty("result").GetProperty("data").GetProperty("rootPath").GetString());
        Assert.True(shutdown.RootElement.GetProperty("result").GetProperty("accepted").GetBoolean());
        Assert.Null(await DesktopProtocolFraming.ReadFrameAsync(output));
    }

    [Fact]
    public void Contract_source_matches_generated_protocol_constants()
    {
        string contractPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "DesktopProtocol", "contract.json");
        using JsonDocument contract = JsonDocument.Parse(File.ReadAllText(contractPath));
        JsonElement root = contract.RootElement;
        string[] methods = root.GetProperty("methods")
            .EnumerateArray()
            .Select(method => method.GetProperty("name").GetString()!)
            .ToArray();

        Assert.Equal(DesktopProtocolDefinition.Version, root.GetProperty("protocolVersion").GetString());
        Assert.Equal(DesktopProtocolDefinition.MaxHeaderBytes,
            root.GetProperty("limits").GetProperty("maxHeaderBytes").GetInt32());
        Assert.Equal(DesktopProtocolDefinition.MaxBodyBytes,
            root.GetProperty("limits").GetProperty("maxBodyBytes").GetInt32());
        Assert.Equal(
            [
                DesktopProtocolDefinition.InitializeMethod,
                DesktopProtocolDefinition.CancelMethod,
                DesktopProtocolDefinition.ShutdownMethod,
                DesktopProtocolDefinition.WorkspaceOpenMethod,
                DesktopProtocolDefinition.ThreadListMethod,
                DesktopProtocolDefinition.ThreadGetMethod,
                DesktopProtocolDefinition.ThreadCreateMethod,
                DesktopProtocolDefinition.ThreadRenameMethod,
                DesktopProtocolDefinition.ThreadArchiveMethod,
                DesktopProtocolDefinition.ThreadDeleteMethod,
                DesktopProtocolDefinition.CatalogListMethod,
                DesktopProtocolDefinition.ChangesGetMethod,
                DesktopProtocolDefinition.ReportListMethod,
                DesktopProtocolDefinition.ReportGetMethod,
                DesktopProtocolDefinition.ArtifactListMethod,
                DesktopProtocolDefinition.ArtifactGetMethod
            ],
            methods);
    }

    [Fact]
    public void Checked_in_examples_validate_against_generated_csharp_types()
    {
        string root = FindRepositoryRoot();
        using JsonDocument contract = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, "protocol", "desktop-v1", "contract.json")));
        using JsonDocument examples = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, "protocol", "desktop-v1", "examples", "methods.json")));
        Dictionary<string, JsonElement> definitions = contract.RootElement.GetProperty("methods")
            .EnumerateArray()
            .ToDictionary(method => method.GetProperty("name").GetString()!, method => method);
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };

        foreach (JsonElement example in examples.RootElement.GetProperty("methods").EnumerateArray())
        {
            string methodName = example.GetProperty("method").GetString()!;
            JsonElement definition = definitions[methodName];
            AssertGeneratedValue(definition.GetProperty("params").GetString()!, example.GetProperty("params"), options);
            AssertGeneratedValue(definition.GetProperty("result").GetString()!, example.GetProperty("result"), options);
        }

        JsonElement notificationDefinition = contract.RootElement.GetProperty("notifications")[0];
        JsonElement notificationExample = examples.RootElement.GetProperty("notifications")[0];
        AssertGeneratedValue(
            notificationDefinition.GetProperty("params").GetString()!,
            notificationExample.GetProperty("params"),
            options);
    }

    [Fact]
    public void Server_rejects_unknown_envelope_members_and_non_integer_ids()
    {
        using DesktopRpcServer server = new();
        using JsonDocument unknown = Handle(server,
            """{"jsonrpc":"2.0","id":1,"method":"app.initialize","params":{},"extra":true}""");
        using JsonDocument decimalId = Handle(server,
            """{"jsonrpc":"2.0","id":1.0,"method":"app.initialize","params":{}}""");

        Assert.Equal(DesktopProtocolDefinition.InvalidRequestError, ErrorCode(unknown));
        Assert.Equal(DesktopProtocolDefinition.InvalidRequestError, ErrorCode(decimalId));
    }

    [Fact]
    public void Server_rejects_duplicate_members_inside_params()
    {
        using DesktopRpcServer server = new();
        string json = $$"""
            {
              "jsonrpc":"2.0",
              "id":1,
              "method":"app.initialize",
              "params":{
                "schemaVersion":1,
                "protocolVersion":"{{DesktopProtocolDefinition.Version}}",
                "contractSha256":"{{DesktopProtocolDefinition.ContractSha256}}",
                "clientName":"first",
                "clientName":"second",
                "clientVersion":"1.0.0",
                "clientInstanceId":"duplicate-test",
                "requestedCapabilities":["framed-json-rpc","workspace-session","application-outcome"]
              }
            }
            """;

        using JsonDocument response = Handle(server, json);

        Assert.Equal(DesktopProtocolDefinition.InvalidParamsError, ErrorCode(response));
    }

    [Fact]
    public void Server_fails_closed_for_params_hash_capability_and_repeat_initialize()
    {
        const string secretSentinel = "sk-secret-in-invalid-params";
        using DesktopRpcServer invalidParamsServer = new();
        using JsonDocument invalidParams = HandleRequest(
            invalidParamsServer,
            1,
            DesktopProtocolDefinition.InitializeMethod,
            new Dictionary<string, object?>(InitializeParameters()) { ["apiKey"] = secretSentinel });
        Assert.Equal(DesktopProtocolDefinition.InvalidParamsError, ErrorCode(invalidParams));
        Assert.DoesNotContain(secretSentinel, invalidParams.RootElement.GetRawText(), StringComparison.Ordinal);

        using DesktopRpcServer mismatchServer = new();
        Dictionary<string, object?> mismatchParameters = InitializeParameters();
        mismatchParameters["contractSha256"] = new string('0', 64);
        using JsonDocument mismatch = HandleRequest(
            mismatchServer, 1, DesktopProtocolDefinition.InitializeMethod, mismatchParameters);
        Assert.Equal(DesktopProtocolDefinition.ContractMismatchError, ErrorCode(mismatch));

        using DesktopRpcServer capabilityServer = new();
        Dictionary<string, object?> capabilityParameters = InitializeParameters();
        capabilityParameters["requestedCapabilities"] = new[]
        {
            DesktopProtocolDefinition.FramedJsonRpcCapability,
            DesktopProtocolDefinition.WorkspaceSessionCapability,
            DesktopProtocolDefinition.ApplicationOutcomeCapability,
            "unreviewed-capability"
        };
        using JsonDocument capability = HandleRequest(
            capabilityServer, 1, DesktopProtocolDefinition.InitializeMethod, capabilityParameters);
        Assert.Equal(DesktopProtocolDefinition.CapabilityInvalidError, ErrorCode(capability));
        Assert.DoesNotContain("unreviewed-capability", capability.RootElement.GetRawText(), StringComparison.Ordinal);

        using DesktopRpcServer repeatServer = new();
        using JsonDocument initialized = Initialize(repeatServer, 1);
        using JsonDocument repeat = Initialize(repeatServer, 2);
        Assert.True(initialized.RootElement.TryGetProperty("result", out _));
        Assert.Equal(DesktopProtocolDefinition.AlreadyInitializedError, ErrorCode(repeat));
    }

    [Fact]
    public void Server_rejects_explicit_null_for_optional_non_nullable_params()
    {
        using DesktopRpcServer server = new();
        using JsonDocument initialized = Initialize(server, 1);

        using JsonDocument response = HandleRequest(server, 2, DesktopProtocolDefinition.ShutdownMethod,
            new { schemaVersion = 1, reason = (string?)null });

        Assert.True(response.RootElement.TryGetProperty("error", out _));
        Assert.Equal(DesktopProtocolDefinition.InvalidParamsError, ErrorCode(response));
    }

    [Fact]
    public void Generated_validation_rejects_contradictory_application_outcomes()
    {
        WorkspaceOpenResult outcome = new()
        {
            SchemaVersion = 1,
            Succeeded = true,
            Data = null,
            Error = null,
            Diagnostics = [],
            Truncated = false
        };

        Assert.False(DesktopProtocolValidation.TryValidate(outcome, out _));
    }

    [Fact]
    public void Server_does_not_echo_raw_paths_secrets_or_exception_details()
    {
        const string pathSentinel = "workspace-path-secret-sentinel";
        using DesktopRpcServer server = new();
        using JsonDocument initialized = Initialize(server, 1);
        using JsonDocument workspace = HandleRequest(
            server,
            2,
            DesktopProtocolDefinition.WorkspaceOpenMethod,
            new { schemaVersion = 1, path = $"\0{pathSentinel}" });
        string serialized = workspace.RootElement.GetRawText();

        Assert.False(workspace.RootElement.GetProperty("result").GetProperty("succeeded").GetBoolean());
        Assert.DoesNotContain(pathSentinel, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("ArgumentException", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("System.", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("stack", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Server_requires_an_active_workspace_for_business_methods()
    {
        using DesktopRpcServer server = new();
        using JsonDocument initialized = Initialize(server, 1);
        using JsonDocument response = HandleRequest(
            server,
            2,
            DesktopProtocolDefinition.ThreadListMethod,
            new { schemaVersion = 1, pageSize = 50 });

        Assert.True(initialized.RootElement.TryGetProperty("result", out _));
        Assert.Equal(DesktopProtocolDefinition.WorkspaceRequiredError, ErrorCode(response));
    }

    [Fact]
    public void Server_dispatches_the_real_thread_lifecycle_with_revisions()
    {
        using TempDirectory temp = TempDirectory.Create();
        using DesktopRpcServer server = new();
        using JsonDocument initialized = Initialize(server, 1);
        using JsonDocument opened = OpenWorkspace(server, 2, temp.Path);
        Assert.True(opened.RootElement.GetProperty("result").GetProperty("succeeded").GetBoolean());

        using JsonDocument created = HandleRequest(server, 3, DesktopProtocolDefinition.ThreadCreateMethod,
            new { schemaVersion = 1, title = "protocol thread" });
        JsonElement createdData = created.RootElement.GetProperty("result").GetProperty("data");
        string threadId = createdData.GetProperty("threadId").GetString()!;
        long revision = createdData.GetProperty("revision").GetInt64();

        using JsonDocument listed = HandleRequest(server, 4, DesktopProtocolDefinition.ThreadListMethod,
            new { schemaVersion = 1, pageSize = 50 });
        Assert.Contains(listed.RootElement.GetProperty("result").GetProperty("data").GetProperty("threads")
            .EnumerateArray(), item => item.GetProperty("threadId").GetString() == threadId);

        using JsonDocument detail = HandleRequest(server, 5, DesktopProtocolDefinition.ThreadGetMethod,
            new { schemaVersion = 1, threadId, afterSequence = 0, timelinePageSize = 50 });
        Assert.Equal(threadId, detail.RootElement.GetProperty("result").GetProperty("data")
            .GetProperty("thread").GetProperty("threadId").GetString());

        using JsonDocument renamed = HandleRequest(server, 6, DesktopProtocolDefinition.ThreadRenameMethod,
            new { schemaVersion = 1, threadId, expectedRevision = revision, title = "renamed" });
        revision = renamed.RootElement.GetProperty("result").GetProperty("data").GetProperty("revision").GetInt64();
        Assert.Equal("renamed", renamed.RootElement.GetProperty("result").GetProperty("data")
            .GetProperty("title").GetString());

        using JsonDocument archived = HandleRequest(server, 7, DesktopProtocolDefinition.ThreadArchiveMethod,
            new { schemaVersion = 1, threadId, expectedRevision = revision });
        revision = archived.RootElement.GetProperty("result").GetProperty("data").GetProperty("revision").GetInt64();
        Assert.Equal("archived", archived.RootElement.GetProperty("result").GetProperty("data")
            .GetProperty("status").GetString());

        using JsonDocument deleted = HandleRequest(server, 8, DesktopProtocolDefinition.ThreadDeleteMethod,
            new { schemaVersion = 1, threadId, expectedRevision = revision, confirmation = threadId });
        Assert.True(deleted.RootElement.GetProperty("result").GetProperty("data")
            .GetProperty("deleted").GetBoolean());
    }

    [Fact]
    public void Server_dispatches_read_only_methods_and_preserves_domain_failures()
    {
        using TempDirectory temp = TempDirectory.Create();
        using DesktopRpcServer server = new();
        using JsonDocument initialized = Initialize(server, 1);
        using JsonDocument opened = OpenWorkspace(server, 2, temp.Path);

        using JsonDocument catalog = HandleRequest(server, 3, DesktopProtocolDefinition.CatalogListMethod,
            new { schemaVersion = 1, kind = "project-packs", pageSize = 50 });
        Assert.True(catalog.RootElement.GetProperty("result").GetProperty("succeeded").GetBoolean());
        Assert.NotEmpty(catalog.RootElement.GetProperty("result").GetProperty("data")
            .GetProperty("items").EnumerateArray());

        using JsonDocument changes = HandleRequest(server, 4, DesktopProtocolDefinition.ChangesGetMethod,
            new { schemaVersion = 1 });
        Assert.True(changes.RootElement.GetProperty("result").GetProperty("succeeded").GetBoolean());

        using JsonDocument reports = HandleRequest(server, 5, DesktopProtocolDefinition.ReportListMethod,
            new { schemaVersion = 1, pageSize = 50 });
        Assert.True(reports.RootElement.GetProperty("result").GetProperty("succeeded").GetBoolean());

        using JsonDocument missingReport = HandleRequest(server, 6, DesktopProtocolDefinition.ReportGetMethod,
            new { schemaVersion = 1, reportId = "job:missing" });
        Assert.False(missingReport.RootElement.GetProperty("result").GetProperty("succeeded").GetBoolean());
        Assert.Equal("not-found", missingReport.RootElement.GetProperty("result").GetProperty("error")
            .GetProperty("category").GetString());

        using JsonDocument artifacts = HandleRequest(server, 7, DesktopProtocolDefinition.ArtifactListMethod,
            new { schemaVersion = 1, pageSize = 50 });
        Assert.True(artifacts.RootElement.GetProperty("result").GetProperty("succeeded").GetBoolean());

        using JsonDocument missingArtifact = HandleRequest(server, 8, DesktopProtocolDefinition.ArtifactGetMethod,
            new { schemaVersion = 1, artifactId = "artifact_missing" });
        Assert.False(missingArtifact.RootElement.GetProperty("result").GetProperty("succeeded").GetBoolean());
    }

    private static async Task WriteRequest(Stream output, int id, string method, object parameters)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            jsonrpc = "2.0",
            id,
            method,
            @params = parameters
        });
        await DesktopProtocolFraming.WriteFrameAsync(output, payload);
    }

    private static Dictionary<string, object?> InitializeParameters() => new()
    {
        ["schemaVersion"] = DesktopProtocolDefinition.SchemaVersion,
        ["protocolVersion"] = DesktopProtocolDefinition.Version,
        ["contractSha256"] = DesktopProtocolDefinition.ContractSha256,
        ["clientName"] = "desktop-tests",
        ["clientVersion"] = "1.0.0",
        ["clientInstanceId"] = "test-client-1",
        ["requestedCapabilities"] = new[]
        {
            DesktopProtocolDefinition.FramedJsonRpcCapability,
            DesktopProtocolDefinition.WorkspaceSessionCapability,
            DesktopProtocolDefinition.ApplicationOutcomeCapability
        }
    };

    private static JsonDocument Initialize(DesktopRpcServer server, long id) =>
        HandleRequest(server, id, DesktopProtocolDefinition.InitializeMethod, InitializeParameters());

    private static JsonDocument OpenWorkspace(DesktopRpcServer server, long id, string path) =>
        HandleRequest(server, id, DesktopProtocolDefinition.WorkspaceOpenMethod, new
        {
            schemaVersion = DesktopProtocolDefinition.SchemaVersion,
            path
        });

    private static JsonDocument HandleRequest(DesktopRpcServer server, long id, string method, object parameters) =>
        Handle(server, JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id,
            method,
            @params = parameters
        }));

    private static JsonDocument Handle(DesktopRpcServer server, string json) =>
        JsonDocument.Parse(server.Handle(Encoding.UTF8.GetBytes(json)));

    private static string ErrorCode(JsonDocument response) => response.RootElement
        .GetProperty("error")
        .GetProperty("data")
        .GetProperty("errorCode")
        .GetString()!;

    private static void AssertGeneratedValue(
        string typeName,
        JsonElement value,
        JsonSerializerOptions options)
    {
        Type type = typeof(DesktopProtocolDefinition).Assembly.GetType(
            $"CSharpAiCli.AppHost.Protocol.Generated.{typeName}",
            throwOnError: true)!;
        object? deserialized = JsonSerializer.Deserialize(value.GetRawText(), type, options);
        Assert.NotNull(deserialized);
        Assert.IsType(type, deserialized);
        MethodInfo validator = typeof(DesktopProtocolValidation).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == "TryValidate" && method.GetParameters()[0].ParameterType == type);
        object?[] arguments = [deserialized, null];
        Assert.True((bool)validator.Invoke(null, arguments)!);
        Assert.Equal(string.Empty, arguments[1]);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private static async Task<JsonDocument> ReadResponse(Stream input)
    {
        byte[] payload = Assert.IsType<byte[]>(await DesktopProtocolFraming.ReadFrameAsync(input));
        return JsonDocument.Parse(payload);
    }

    private sealed class ChunkedReadStream : Stream
    {
        private readonly Stream inner;
        private readonly int maxChunkSize;

        public ChunkedReadStream(Stream inner, int maxChunkSize)
        {
            this.inner = inner;
            this.maxChunkSize = maxChunkSize;
        }

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) =>
            inner.Read(buffer, offset, Math.Min(count, maxChunkSize));
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer[..Math.Min(buffer.Length, maxChunkSize)], cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;
        public string Path { get; }

        public static TempDirectory Create()
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "caicli-apphost-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
