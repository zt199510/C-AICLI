using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CSharpAiCli.AppHost.Protocol;
using CSharpAiCli.AppHost.Protocol.Generated;

namespace CSharpAiCli.Tests;

public sealed class AppHostProcessTests
{
    [Fact]
    public async Task Real_process_runs_thread_lifecycle_notifications_and_clean_shutdown()
    {
        using TempDirectory workspace = TempDirectory.Create("workspace");
        using TempDirectory profile = TempDirectory.Create("profile");
        string appHost = Path.Combine(AppContext.BaseDirectory, "CSharpAiCli.AppHost.dll");
        Assert.True(File.Exists(appHost), appHost);
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo("dotnet")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add(appHost);
        process.StartInfo.Environment["CAICLI_USER_PROFILE"] = profile.Path;
        Assert.True(process.Start());
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        Stream input = process.StandardInput.BaseStream;
        Stream output = process.StandardOutput.BaseStream;

        await WriteRequest(input, 1, DesktopProtocolDefinition.InitializeMethod, new
        {
            schemaVersion = 1,
            protocolVersion = DesktopProtocolDefinition.Version,
            contractSha256 = DesktopProtocolDefinition.ContractSha256,
            clientName = "process-tests",
            clientVersion = "1.0.0",
            clientInstanceId = "process-test-1",
            requestedCapabilities = new[]
            {
                DesktopProtocolDefinition.FramedJsonRpcCapability,
                DesktopProtocolDefinition.WorkspaceSessionCapability,
                DesktopProtocolDefinition.ApplicationOutcomeCapability,
                DesktopProtocolDefinition.ThreadChangedCapability
            }
        });
        using JsonDocument initialized = await ReadFrame(output);
        Assert.Equal(1, initialized.RootElement.GetProperty("id").GetInt64());

        await WriteRequest(input, 2, DesktopProtocolDefinition.WorkspaceOpenMethod,
            new { schemaVersion = 1, path = workspace.Path });
        using JsonDocument opened = await ReadFrame(output);
        Assert.True(opened.RootElement.GetProperty("result").GetProperty("succeeded").GetBoolean());

        await WriteRequest(input, 3, DesktopProtocolDefinition.ThreadCreateMethod,
            new { schemaVersion = 1, title = "process thread" });
        using JsonDocument created = await ReadFrame(output);
        using JsonDocument createdEvent = await ReadFrame(output);
        string threadId = created.RootElement.GetProperty("result").GetProperty("data")
            .GetProperty("threadId").GetString()!;
        long revision = created.RootElement.GetProperty("result").GetProperty("data")
            .GetProperty("revision").GetInt64();
        Assert.Equal(3, created.RootElement.GetProperty("id").GetInt64());
        Assert.Equal(DesktopProtocolDefinition.ThreadChangedNotification,
            createdEvent.RootElement.GetProperty("method").GetString());
        Assert.Equal(1, createdEvent.RootElement.GetProperty("params").GetProperty("eventSequence").GetInt64());

        await WriteRequest(input, 4, DesktopProtocolDefinition.ThreadListMethod,
            new { schemaVersion = 1, pageSize = 50 });
        using JsonDocument listed = await ReadFrame(output);
        Assert.Contains(listed.RootElement.GetProperty("result").GetProperty("data").GetProperty("threads")
            .EnumerateArray(), item => item.GetProperty("threadId").GetString() == threadId);

        await WriteRequest(input, 5, DesktopProtocolDefinition.ThreadRenameMethod,
            new { schemaVersion = 1, threadId, expectedRevision = revision, title = "renamed" });
        using JsonDocument renamed = await ReadFrame(output);
        using JsonDocument renamedEvent = await ReadFrame(output);
        revision = renamed.RootElement.GetProperty("result").GetProperty("data")
            .GetProperty("revision").GetInt64();
        Assert.Equal(2, renamedEvent.RootElement.GetProperty("params").GetProperty("eventSequence").GetInt64());

        await WriteRequest(input, 6, DesktopProtocolDefinition.ThreadArchiveMethod,
            new { schemaVersion = 1, threadId, expectedRevision = revision });
        using JsonDocument archived = await ReadFrame(output);
        using JsonDocument archivedEvent = await ReadFrame(output);
        revision = archived.RootElement.GetProperty("result").GetProperty("data")
            .GetProperty("revision").GetInt64();
        Assert.Equal(3, archivedEvent.RootElement.GetProperty("params").GetProperty("eventSequence").GetInt64());

        await WriteRequest(input, 7, DesktopProtocolDefinition.ThreadDeleteMethod,
            new { schemaVersion = 1, threadId, expectedRevision = revision, confirmation = threadId });
        using JsonDocument deleted = await ReadFrame(output);
        using JsonDocument deletedEvent = await ReadFrame(output);
        Assert.True(deleted.RootElement.GetProperty("result").GetProperty("data")
            .GetProperty("deleted").GetBoolean());
        Assert.Equal(4, deletedEvent.RootElement.GetProperty("params").GetProperty("eventSequence").GetInt64());

        await WriteRequest(input, 8, DesktopProtocolDefinition.ShutdownMethod,
            new { schemaVersion = 1, reason = "process-test-complete" });
        using JsonDocument shutdown = await ReadFrame(output);
        Assert.True(shutdown.RootElement.GetProperty("result").GetProperty("accepted").GetBoolean());
        process.StandardInput.Close();
        using CancellationTokenSource exitTimeout = new(TimeSpan.FromSeconds(10));
        await process.WaitForExitAsync(exitTimeout.Token);

        string diagnostics = await stderr;
        Assert.Equal(0, process.ExitCode);
        Assert.DoesNotContain(workspace.Path, diagnostics, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("process thread", diagnostics, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Real_process_reports_only_a_stable_code_for_a_fatal_frame()
    {
        using Process process = StartProcess();
        const string secretSentinel = "fatal-secret-sentinel";
        byte[] invalid = Encoding.ASCII.GetBytes(
            $"X-Sentinel: {secretSentinel}\r\nContent-Length: 0\r\n\r\n");
        await process.StandardInput.BaseStream.WriteAsync(invalid);
        await process.StandardInput.BaseStream.FlushAsync();
        process.StandardInput.Close();
        using CancellationTokenSource exitTimeout = new(TimeSpan.FromSeconds(10));
        await process.WaitForExitAsync(exitTimeout.Token);
        string diagnostics = await process.StandardError.ReadToEndAsync();

        Assert.Equal(2, process.ExitCode);
        Assert.Contains("frame-header-invalid", diagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain(secretSentinel, diagnostics, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Real_process_cancels_session_on_stdin_disconnect()
    {
        using TempDirectory workspace = TempDirectory.Create("disconnect-workspace");
        using TempDirectory profile = TempDirectory.Create("disconnect-profile");
        using Process process = StartProcess(profile.Path);
        Stream input = process.StandardInput.BaseStream;
        Stream output = process.StandardOutput.BaseStream;
        await WriteRequest(input, 1, DesktopProtocolDefinition.InitializeMethod, new
        {
            schemaVersion = 1,
            protocolVersion = DesktopProtocolDefinition.Version,
            contractSha256 = DesktopProtocolDefinition.ContractSha256,
            clientName = "disconnect-tests",
            clientVersion = "1.0.0",
            clientInstanceId = "disconnect-test-1",
            requestedCapabilities = new[]
            {
                DesktopProtocolDefinition.FramedJsonRpcCapability,
                DesktopProtocolDefinition.WorkspaceSessionCapability,
                DesktopProtocolDefinition.ApplicationOutcomeCapability
            }
        });
        using JsonDocument initialized = await ReadFrame(output);
        await WriteRequest(input, 2, DesktopProtocolDefinition.WorkspaceOpenMethod,
            new { schemaVersion = 1, path = workspace.Path });
        using JsonDocument opened = await ReadFrame(output);
        await WriteRequest(input, 3, DesktopProtocolDefinition.ChangesGetMethod,
            new { schemaVersion = 1 });
        process.StandardInput.Close();

        using CancellationTokenSource exitTimeout = new(TimeSpan.FromSeconds(10));
        await process.WaitForExitAsync(exitTimeout.Token);
        string diagnostics = await process.StandardError.ReadToEndAsync();
        Assert.Equal(0, process.ExitCode);
        Assert.DoesNotContain(workspace.Path, diagnostics, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Real_process_dispatches_the_complete_read_only_path()
    {
        using TempDirectory workspace = TempDirectory.Create("read-only-workspace");
        using TempDirectory profile = TempDirectory.Create("read-only-profile");
        using Process process = StartProcess(profile.Path);
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        Stream input = process.StandardInput.BaseStream;
        Stream output = process.StandardOutput.BaseStream;
        await InitializeProcess(input, output, "read-only-process", notifications: false);
        await OpenProcessWorkspace(input, output, workspace.Path);

        await WriteRequest(input, 3, DesktopProtocolDefinition.CatalogListMethod,
            new { schemaVersion = 1, kind = "project-packs", pageSize = 50 });
        using JsonDocument catalog = await ReadFrame(output);
        Assert.True(catalog.RootElement.GetProperty("result").GetProperty("succeeded").GetBoolean());
        Assert.NotEmpty(catalog.RootElement.GetProperty("result").GetProperty("data")
            .GetProperty("items").EnumerateArray());

        await WriteRequest(input, 4, DesktopProtocolDefinition.ChangesGetMethod,
            new { schemaVersion = 1 });
        using JsonDocument changes = await ReadFrame(output);
        Assert.True(changes.RootElement.GetProperty("result").GetProperty("succeeded").GetBoolean());

        await WriteRequest(input, 5, DesktopProtocolDefinition.ReportListMethod,
            new { schemaVersion = 1, pageSize = 50 });
        using JsonDocument reports = await ReadFrame(output);
        Assert.True(reports.RootElement.GetProperty("result").GetProperty("succeeded").GetBoolean());

        await WriteRequest(input, 6, DesktopProtocolDefinition.ReportGetMethod,
            new { schemaVersion = 1, reportId = "job:missing" });
        using JsonDocument missingReport = await ReadFrame(output);
        Assert.False(missingReport.RootElement.GetProperty("result").GetProperty("succeeded").GetBoolean());
        Assert.Equal("not-found", missingReport.RootElement.GetProperty("result").GetProperty("error")
            .GetProperty("category").GetString());

        await WriteRequest(input, 7, DesktopProtocolDefinition.ArtifactListMethod,
            new { schemaVersion = 1, pageSize = 50 });
        using JsonDocument artifacts = await ReadFrame(output);
        Assert.True(artifacts.RootElement.GetProperty("result").GetProperty("succeeded").GetBoolean());

        await WriteRequest(input, 8, DesktopProtocolDefinition.ArtifactGetMethod,
            new { schemaVersion = 1, artifactId = "artifact_missing" });
        using JsonDocument missingArtifact = await ReadFrame(output);
        Assert.False(missingArtifact.RootElement.GetProperty("result").GetProperty("succeeded").GetBoolean());

        await ShutdownProcess(process, input, output, 9);
        string diagnostics = await stderr;
        Assert.Equal(0, process.ExitCode);
        Assert.DoesNotContain(workspace.Path, diagnostics, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Real_process_preserves_ids_and_orders_concurrent_mutation_notifications()
    {
        using TempDirectory workspace = TempDirectory.Create("concurrent-workspace");
        using TempDirectory profile = TempDirectory.Create("concurrent-profile");
        using Process process = StartProcess(profile.Path);
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        Stream input = process.StandardInput.BaseStream;
        Stream output = process.StandardOutput.BaseStream;
        await InitializeProcess(input, output, "concurrent-process", notifications: true);
        await OpenProcessWorkspace(input, output, workspace.Path);

        await WriteRequest(input, 3, DesktopProtocolDefinition.ThreadCreateMethod,
            new { schemaVersion = 1, title = "concurrent one" });
        await WriteRequest(input, 4, DesktopProtocolDefinition.ThreadCreateMethod,
            new { schemaVersion = 1, title = "concurrent two" });
        List<JsonDocument> frames = [];
        try
        {
            for (int index = 0; index < 4; index++)
            {
                frames.Add(await ReadFrame(output));
            }

            JsonDocument[] responses = frames.Where(frame => frame.RootElement.TryGetProperty("id", out _)).ToArray();
            JsonDocument[] notifications = frames.Where(frame => frame.RootElement.TryGetProperty("method", out _)).ToArray();
            Assert.Equal([3L, 4L], responses.Select(response => response.RootElement.GetProperty("id").GetInt64())
                .OrderBy(id => id).ToArray());
            Assert.Equal([1L, 2L], notifications.Select(notification => notification.RootElement
                .GetProperty("params").GetProperty("eventSequence").GetInt64()).ToArray());
            foreach (JsonDocument notification in notifications)
            {
                string threadId = notification.RootElement.GetProperty("params").GetProperty("threadId").GetString()!;
                int notificationIndex = frames.IndexOf(notification);
                int responseIndex = frames.FindIndex(frame =>
                    frame.RootElement.TryGetProperty("result", out JsonElement result) &&
                    result.TryGetProperty("data", out JsonElement data) &&
                    data.ValueKind == JsonValueKind.Object &&
                    data.TryGetProperty("threadId", out JsonElement responseThreadId) &&
                    responseThreadId.GetString() == threadId);
                Assert.InRange(responseIndex, 0, notificationIndex - 1);
            }
        }
        finally
        {
            foreach (JsonDocument frame in frames) frame.Dispose();
        }

        await ShutdownProcess(process, input, output, 5);
        string diagnostics = await stderr;
        Assert.Equal(0, process.ExitCode);
        Assert.DoesNotContain("concurrent one", diagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain("concurrent two", diagnostics, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Real_process_cancels_an_in_flight_changes_request()
    {
        using TempDirectory workspace = TempDirectory.Create("cancel-workspace");
        using TempDirectory profile = TempDirectory.Create("cancel-profile");
        InitializeGitRepository(workspace.Path);
        for (int index = 0; index < 2_000; index++)
        {
            File.WriteAllText(Path.Combine(workspace.Path, $"untracked-{index:D4}.txt"), "x");
        }

        using Process process = StartProcess(profile.Path);
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        Stream input = process.StandardInput.BaseStream;
        Stream output = process.StandardOutput.BaseStream;
        await InitializeProcess(input, output, "cancel-process", notifications: false);
        await OpenProcessWorkspace(input, output, workspace.Path);

        await WriteRequest(input, 3, DesktopProtocolDefinition.ChangesGetMethod,
            new { schemaVersion = 1 });
        await WriteRequest(input, 4, DesktopProtocolDefinition.CancelMethod,
            new { schemaVersion = 1, targetRequestId = 3 });
        using JsonDocument first = await ReadFrame(output);
        using JsonDocument second = await ReadFrame(output);
        JsonDocument cancel = first.RootElement.GetProperty("id").GetInt64() == 4 ? first : second;
        JsonDocument canceled = ReferenceEquals(cancel, first) ? second : first;
        Assert.True(cancel.RootElement.GetProperty("result").GetProperty("accepted").GetBoolean());
        Assert.Equal(3, canceled.RootElement.GetProperty("id").GetInt64());
        Assert.Equal(DesktopProtocolDefinition.RequestCanceledError, canceled.RootElement.GetProperty("error")
            .GetProperty("data").GetProperty("errorCode").GetString());

        await ShutdownProcess(process, input, output, 5);
        string diagnostics = await stderr;
        Assert.Equal(0, process.ExitCode);
        Assert.DoesNotContain(workspace.Path, diagnostics, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WriteRequest(Stream output, long id, string method, object parameters)
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

    private static async Task<JsonDocument> ReadFrame(Stream input)
    {
        byte[] payload = Assert.IsType<byte[]>(await DesktopProtocolFraming.ReadFrameAsync(input));
        return JsonDocument.Parse(payload);
    }

    private static async Task InitializeProcess(
        Stream input,
        Stream output,
        string clientInstanceId,
        bool notifications)
    {
        List<string> capabilities =
        [
            DesktopProtocolDefinition.FramedJsonRpcCapability,
            DesktopProtocolDefinition.WorkspaceSessionCapability,
            DesktopProtocolDefinition.ApplicationOutcomeCapability
        ];
        if (notifications) capabilities.Add(DesktopProtocolDefinition.ThreadChangedCapability);
        await WriteRequest(input, 1, DesktopProtocolDefinition.InitializeMethod, new
        {
            schemaVersion = 1,
            protocolVersion = DesktopProtocolDefinition.Version,
            contractSha256 = DesktopProtocolDefinition.ContractSha256,
            clientName = "process-tests",
            clientVersion = "1.0.0",
            clientInstanceId,
            requestedCapabilities = capabilities
        });
        using JsonDocument initialized = await ReadFrame(output);
        Assert.Equal(1, initialized.RootElement.GetProperty("id").GetInt64());
    }

    private static async Task OpenProcessWorkspace(Stream input, Stream output, string path)
    {
        await WriteRequest(input, 2, DesktopProtocolDefinition.WorkspaceOpenMethod,
            new { schemaVersion = 1, path });
        using JsonDocument opened = await ReadFrame(output);
        Assert.True(opened.RootElement.GetProperty("result").GetProperty("succeeded").GetBoolean());
    }

    private static async Task ShutdownProcess(Process process, Stream input, Stream output, long id)
    {
        await WriteRequest(input, id, DesktopProtocolDefinition.ShutdownMethod,
            new { schemaVersion = 1, reason = "process-test-complete" });
        using JsonDocument shutdown = await ReadFrame(output);
        Assert.True(shutdown.RootElement.GetProperty("result").GetProperty("accepted").GetBoolean());
        process.StandardInput.Close();
        using CancellationTokenSource exitTimeout = new(TimeSpan.FromSeconds(10));
        await process.WaitForExitAsync(exitTimeout.Token);
    }

    private static void InitializeGitRepository(string path)
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo("git")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("-C");
        process.StartInfo.ArgumentList.Add(path);
        process.StartInfo.ArgumentList.Add("init");
        process.StartInfo.ArgumentList.Add("--quiet");
        Assert.True(process.Start());
        Assert.True(process.WaitForExit(10_000));
        Assert.Equal(0, process.ExitCode);
    }

    private static Process StartProcess(string? profilePath = null)
    {
        string appHost = Path.Combine(AppContext.BaseDirectory, "CSharpAiCli.AppHost.dll");
        Process process = new()
        {
            StartInfo = new ProcessStartInfo("dotnet")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add(appHost);
        if (profilePath is not null) process.StartInfo.Environment["CAICLI_USER_PROFILE"] = profilePath;
        Assert.True(process.Start());
        return process;
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;

        public string Path { get; }

        public static TempDirectory Create(string suffix)
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"caicli-apphost-process-{suffix}-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
