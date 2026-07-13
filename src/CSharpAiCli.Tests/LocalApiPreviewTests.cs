using System.CommandLine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using CSharpAiCli.Cli;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class LocalApiPreviewTests
{
    [Fact]
    public void Route_catalog_is_versioned_read_only_and_contains_no_control_or_sse_routes()
    {
        Assert.Equal(1, LocalApiPreviewConstants.SchemaVersion);
        Assert.Equal(5, LocalApiRouteCatalog.Routes.Count);
        Assert.All(LocalApiRouteCatalog.Routes, route =>
        {
            Assert.Equal("GET", route.Method);
            Assert.Equal("read-only", route.Access);
            Assert.StartsWith("/v1/", route.Route, StringComparison.Ordinal);
        });
        Assert.DoesNotContain(LocalApiRouteCatalog.Routes, route =>
            route.Route.Contains("run", StringComparison.OrdinalIgnoreCase) ||
            route.Route.Contains("cancel", StringComparison.OrdinalIgnoreCase) ||
            route.Route.Contains("events", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Api_routes_json_is_parseable_and_does_not_create_runtime_context()
    {
        using StringWriter output = new();
        RootCommand command = CliCommandFactory.Create(
            output,
            _ => throw new InvalidOperationException("api routes must not create a workspace snapshot"));

        int exitCode = command.Parse(["api", "routes", "--output", "json"]).Invoke();
        JsonObject document = Assert.IsType<JsonObject>(JsonNode.Parse(output.ToString()));

        Assert.Equal(0, exitCode);
        Assert.Equal("api.routes", document["type"]?.GetValue<string>());
        Assert.False(document["defaultEnabled"]?.GetValue<bool>());
        Assert.Equal("deferred", document["controlRoutes"]?.GetValue<string>());
        Assert.Equal("deferred", document["sse"]?.GetValue<string>());
        Assert.Equal(5, Assert.IsType<JsonArray>(document["routes"]).Count);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("LOCALHOST")]
    [InlineData("127.0.0.1")]
    public void Bind_policy_accepts_only_explicit_ipv4_loopback_names(string requestedBind)
    {
        Assert.True(LocalApiDaemonBindPolicy.TryNormalize(requestedBind, out string bindAddress));
        Assert.Equal("127.0.0.1", bindAddress);
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("192.168.1.10")]
    [InlineData("host.internal")]
    [InlineData("*")]
    public void Bind_policy_rejects_wildcard_remote_ipv6_and_hostnames(string requestedBind)
    {
        Assert.False(LocalApiDaemonBindPolicy.TryNormalize(requestedBind, out string bindAddress));
        Assert.Empty(bindAddress);
    }

    [Fact]
    public void Daemon_start_requires_preview_and_rejects_remote_bind_before_snapshot_creation()
    {
        using StringWriter defaultOffOutput = new();
        RootCommand defaultOffCommand = CliCommandFactory.Create(
            defaultOffOutput,
            _ => throw new InvalidOperationException("default-off validation must happen before snapshot creation"));
        int defaultOffExitCode = defaultOffCommand.Parse(["daemon", "start"]).Invoke();

        using StringWriter remoteOutput = new();
        RootCommand remoteCommand = CliCommandFactory.Create(
            remoteOutput,
            _ => throw new InvalidOperationException("bind validation must happen before snapshot creation"));
        int remoteExitCode = remoteCommand.Parse([
            "daemon", "start", "--preview", "--bind", "0.0.0.0"
        ]).Invoke();

        Assert.Equal(2, defaultOffExitCode);
        Assert.Contains("--preview is required", defaultOffOutput.ToString(), StringComparison.Ordinal);
        Assert.Equal(2, remoteExitCode);
        Assert.Contains("remote and wildcard binds are disabled", remoteOutput.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Daemon_doctor_json_is_static_and_reports_default_off_boundary()
    {
        using StringWriter output = new();
        RootCommand command = CliCommandFactory.Create(
            output,
            _ => throw new InvalidOperationException("daemon doctor must not create a workspace snapshot"));

        int exitCode = command.Parse(["daemon", "doctor", "--output", "json"]).Invoke();
        JsonObject document = Assert.IsType<JsonObject>(JsonNode.Parse(output.ToString()));

        Assert.Equal(0, exitCode);
        Assert.Equal("daemon.doctor", document["type"]?.GetValue<string>());
        Assert.False(document["defaultEnabled"]?.GetValue<bool>());
        Assert.True(document["startRequiresPreview"]?.GetValue<bool>());
        Assert.Equal("read-only", document["access"]?.GetValue<string>());
    }

    [Fact]
    public async Task Daemon_endpoints_reuse_redacted_stores_and_renderers_without_control_surface()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspace = temp.CreateDirectory("workspace");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(workspace, temp.UserConfigPath);
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-13T09:00:00Z");

        JobRecord job = JobRecord.CreateRunning(
            JobIdGenerator.Create(now),
            now,
            new JobCommandSummary(
                "exec",
                Task: "inspect apiKey=daemon-job-secret",
                WorkspaceRoot: workspace));
        job = job.WithStatus(JobStatus.Succeeded, now.AddSeconds(1), exitCode: 0, summary: "done");
        JobRecordStore.Create(snapshot).Create(job);

        TaskQueueItem queueItem = TaskQueueItem.CreatePending(
            TaskQueueIdGenerator.Create(now),
            now,
            new TaskQueueRequest(
                TaskQueueCommandFamily.Exec,
                "inspect apiKey=daemon-queue-secret",
                workspace));
        TaskQueueStore.Create(snapshot).Create(queueItem);

        int port = GetAvailableLoopbackPort();
        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(15));
        using StringWriter daemonOutput = new();
        Task<int> daemonTask = LocalApiDaemonHost.RunAsync(snapshot, port, daemonOutput, cancellation.Token);
        using HttpClient client = new(new HttpClientHandler { UseProxy = false })
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}"),
            Timeout = TimeSpan.FromSeconds(3),
        };

        try
        {
            await WaitForReadyAsync(client, cancellation.Token);
            string jobsList = await client.GetStringAsync("/v1/jobs", cancellation.Token);
            string jobShow = await client.GetStringAsync($"/v1/jobs/{job.JobId}", cancellation.Token);
            string queueList = await client.GetStringAsync("/v1/queue?status=pending", cancellation.Token);
            string queueShow = await client.GetStringAsync($"/v1/queue/{queueItem.QueueId}", cancellation.Token);
            HttpResponseMessage invalidLimit = await client.GetAsync("/v1/jobs?limit=101", cancellation.Token);
            HttpResponseMessage missingRoute = await client.GetAsync("/v1/events", cancellation.Token);
            HttpResponseMessage post = await client.PostAsync("/v1/jobs", new StringContent("{}"), cancellation.Token);
            using HttpRequestMessage getWithBody = new(HttpMethod.Get, "/v1/jobs")
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };
            HttpResponseMessage bodyResponse = await client.SendAsync(getWithBody, cancellation.Token);

            Assert.Equal("jobs.list", JsonNode.Parse(jobsList)?["type"]?.GetValue<string>());
            Assert.Equal("jobs.show", JsonNode.Parse(jobShow)?["type"]?.GetValue<string>());
            Assert.Equal("queue.list", JsonNode.Parse(queueList)?["type"]?.GetValue<string>());
            Assert.Equal("queue.show", JsonNode.Parse(queueShow)?["type"]?.GetValue<string>());
            Assert.DoesNotContain("daemon-job-secret", jobShow, StringComparison.Ordinal);
            Assert.DoesNotContain("daemon-queue-secret", queueShow, StringComparison.Ordinal);
            Assert.Contains("[redacted]", jobShow, StringComparison.Ordinal);
            Assert.Contains("[redacted]", queueShow, StringComparison.Ordinal);
            Assert.Equal(HttpStatusCode.BadRequest, invalidLimit.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, missingRoute.StatusCode);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, post.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, bodyResponse.StatusCode);
            Assert.DoesNotContain("Server", invalidLimit.Headers.Select(header => header.Key));
            Assert.DoesNotContain("Access-Control-Allow-Origin", invalidLimit.Headers.Select(header => header.Key));
        }
        finally
        {
            await cancellation.CancelAsync();
            Assert.Equal(0, await daemonTask.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    private static async Task WaitForReadyAsync(HttpClient client, CancellationToken cancellationToken)
    {
        Exception? lastException = null;
        for (int attempt = 0; attempt < 40; attempt++)
        {
            try
            {
                HttpResponseMessage response = await client.GetAsync("/v1/health", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException exception)
            {
                lastException = exception;
            }

            await Task.Delay(50, cancellationToken);
        }

        throw new InvalidOperationException("Daemon did not become ready for the integration test.", lastException);
    }

    private static int GetAvailableLoopbackPort()
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static CliEnvironmentSnapshot CreateSnapshot(string workspaceRoot, string userConfigPath)
    {
        WorkspaceContext workspace = new(
            workspaceRoot,
            Path.Combine(workspaceRoot, ".caicli", "config.json"),
            WorkspaceStatus.Ready);
        EffectiveConfiguration configuration = new(
            WorkspaceRoot: workspaceRoot,
            UserConfigPath: userConfigPath,
            WorkspaceConfigPath: Path.Combine(workspaceRoot, ".caicli", "config.json"),
            Model: "not configured",
            ModelSource: "default",
            AgentBackend: "direct",
            AgentBackendSource: "default",
            DisabledTools: new HashSet<string>(StringComparer.Ordinal),
            ApiKey: null,
            ApiKeySource: "not configured",
            LoadedConfigPaths: [],
            Warnings: [],
            ConfigSources: []);
        return new CliEnvironmentSnapshot(
            workspace,
            configuration,
            "9.0.308",
            ".NET 9.0.0",
            "net9.0",
            false);
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;

        public string Path { get; }

        public string UserConfigPath => System.IO.Path.Combine(Path, "user", ".caicli", "config.json");

        public static TempDirectory Create()
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "caicli-local-api-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public string CreateDirectory(string name)
        {
            string path = System.IO.Path.Combine(Path, name);
            Directory.CreateDirectory(path);
            return path;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
