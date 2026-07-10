using System.Text.Json.Nodes;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class TraceLoggerTests
{
    [Fact]
    public void AppendExecResult_writes_json_lines_with_core_fields_and_redacts_secrets()
    {
        string tempRoot = CreateTempDirectory();

        try
        {
            string workspaceRoot = Path.Combine(tempRoot, "workspace");
            Directory.CreateDirectory(workspaceRoot);
            CliEnvironmentSnapshot snapshot = CreateSnapshot(
                workspaceRoot: workspaceRoot,
                userProfile: Path.Combine(tempRoot, "home"),
                apiKey: "sk-test-secret",
                apiKeySource: "OPENAI_API_KEY");
            DiagnosticContext context = new(
                CommandId: "cmd-001",
                SessionId: "session-abc",
                Workspace: workspaceRoot,
                TimestampUtc: DateTimeOffset.Parse("2026-07-10T08:00:00Z"));
            ExecEvent modelTurn = new(
                Type: "model.turn",
                Sequence: 0,
                Timestamp: DateTimeOffset.Parse("2026-07-10T08:00:01Z"),
                Summary: "planned with apiKey=sk-test-secret",
                Payload: new Dictionary<string, string>
                {
                    ["authorization"] = "Bearer abcdef123",
                    ["model"] = "gpt-test"
                },
                Status: "success",
                DurationMs: 37);
            ExecEvent toolCompleted = new(
                Type: "tool.completed",
                Sequence: 1,
                Timestamp: DateTimeOffset.Parse("2026-07-10T08:00:02Z"),
                Summary: "tool completed",
                Payload: new Dictionary<string, string>
                {
                    ["command"] = "echo done",
                    ["toolName"] = "workspace.shell"
                },
                ApprovalStatus: "approved",
                ApprovalDurationMs: 5,
                Status: "success",
                DurationMs: 12);
            ExecResult result = ExecResult.Success(
                Summary: "finished sk-test-secret",
                Events: [modelTurn, toolCompleted],
                ApprovalStatus: "approved");

            TraceLogger.AppendExecResult("exec", snapshot, context, result);

            string tracePath = Path.Combine(workspaceRoot, ".caicli", "logs", "2026-07-10.trace.log");
            string trace = File.ReadAllText(tracePath);
            string[] lines = File.ReadAllLines(tracePath);
            JsonObject first = Assert.IsType<JsonObject>(JsonNode.Parse(lines[0]));
            JsonObject second = Assert.IsType<JsonObject>(JsonNode.Parse(lines[1]));
            JsonObject final = Assert.IsType<JsonObject>(JsonNode.Parse(lines[2]));

            Assert.Equal(3, lines.Length);
            Assert.Equal("2026-07-10T08:00:01.0000000Z", first["timestampUtc"]?.GetValue<string>());
            Assert.Equal("exec", first["command"]?.GetValue<string>());
            Assert.Equal("cmd-001", first["commandId"]?.GetValue<string>());
            Assert.Equal("session-abc", first["sessionId"]?.GetValue<string>());
            Assert.Equal(workspaceRoot, first["workspace"]?.GetValue<string>());
            Assert.Equal("model.turn", first["type"]?.GetValue<string>());
            Assert.Equal(0, first["sequence"]?.GetValue<long>());
            Assert.Equal("success", first["status"]?.GetValue<string>());
            Assert.Equal(37, first["durationMs"]?.GetValue<long>());
            Assert.Equal("planned with apiKey=[redacted]", first["summary"]?.GetValue<string>());
            Assert.Equal("Bearer [redacted]", first["payload"]?["authorization"]?.GetValue<string>());
            Assert.Equal("gpt-test", first["payload"]?["model"]?.GetValue<string>());
            Assert.Equal("tool.completed", second["type"]?.GetValue<string>());
            Assert.Equal("approved", second["approvalStatus"]?.GetValue<string>());
            Assert.Equal(5, second["approvalDurationMs"]?.GetValue<long>());
            Assert.Equal("exec.result", final["type"]?.GetValue<string>());
            Assert.Equal(2, final["sequence"]?.GetValue<long>());
            Assert.Equal("success", final["status"]?.GetValue<string>());
            Assert.Equal("finished [redacted]", final["summary"]?.GetValue<string>());
            Assert.Equal("approved", final["approvalStatus"]?.GetValue<string>());
            Assert.Equal(0, final["payload"]?["exitCode"]?.GetValue<int>());
            Assert.Equal(2, final["payload"]?["eventCount"]?.GetValue<int>());
            Assert.DoesNotContain("sk-test-secret", trace, StringComparison.Ordinal);
            Assert.DoesNotContain("abcdef123", trace, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void AppendExecResult_redacts_escaped_json_and_nested_arguments_json_secrets()
    {
        string tempRoot = CreateTempDirectory();

        try
        {
            string workspaceRoot = Path.Combine(tempRoot, "workspace");
            Directory.CreateDirectory(workspaceRoot);
            CliEnvironmentSnapshot snapshot = CreateSnapshot(
                workspaceRoot: workspaceRoot,
                userProfile: Path.Combine(tempRoot, "home"));
            DiagnosticContext context = new(
                CommandId: "cmd-escaped",
                SessionId: "session-escaped",
                Workspace: workspaceRoot,
                TimestampUtc: DateTimeOffset.Parse("2026-07-10T10:00:00Z"));
            ExecEvent execEvent = new(
                Type: "tool.completed",
                Sequence: 0,
                Timestamp: DateTimeOffset.Parse("2026-07-10T10:00:01Z"),
                Summary: """model returned {\"password\":\"escaped-summary-secret\"} and apiKey: plain-summary-secret""",
                Payload: new Dictionary<string, string>
                {
                    ["argumentsJson"] = """{"apiKey":"plain-argument-secret","nested":{"password":"nested-argument-secret"},"authorization":"Bearer nestedbearer123","github":"ghp_abcdefghijklmnopqrstuvwxyz"}""",
                    ["escapedJson"] = """{\"password\":\"escaped-payload-secret\",\"apiKey\":\"plain-escaped-payload-secret\"}""",
                    ["escapedKeyValue"] = """password:\"escaped-kv-secret\" apiKey=plain-kv-secret"""
                },
                Status: "success");
            ExecResult result = ExecResult.Success("finished with password: plain-result-secret", [execEvent]);

            TraceLogger.AppendExecResult("exec", snapshot, context, result);

            string tracePath = Path.Combine(workspaceRoot, ".caicli", "logs", "2026-07-10.trace.log");
            string trace = File.ReadAllText(tracePath);
            JsonObject first = Assert.IsType<JsonObject>(JsonNode.Parse(File.ReadAllLines(tracePath)[0]));

            Assert.Contains("[redacted]", first["summary"]?.GetValue<string>(), StringComparison.Ordinal);
            Assert.Contains("[redacted]", first["payload"]?["argumentsJson"]?.GetValue<string>(), StringComparison.Ordinal);
            Assert.Contains("[redacted]", first["payload"]?["escapedJson"]?.GetValue<string>(), StringComparison.Ordinal);
            Assert.Contains("[redacted]", first["payload"]?["escapedKeyValue"]?.GetValue<string>(), StringComparison.Ordinal);
            Assert.DoesNotContain("escaped-summary-secret", trace, StringComparison.Ordinal);
            Assert.DoesNotContain("plain-summary-secret", trace, StringComparison.Ordinal);
            Assert.DoesNotContain("plain-argument-secret", trace, StringComparison.Ordinal);
            Assert.DoesNotContain("nested-argument-secret", trace, StringComparison.Ordinal);
            Assert.DoesNotContain("nestedbearer123", trace, StringComparison.Ordinal);
            Assert.DoesNotContain("abcdefghijklmnopqrstuvwxyz", trace, StringComparison.Ordinal);
            Assert.DoesNotContain("escaped-payload-secret", trace, StringComparison.Ordinal);
            Assert.DoesNotContain("plain-escaped-payload-secret", trace, StringComparison.Ordinal);
            Assert.DoesNotContain("escaped-kv-secret", trace, StringComparison.Ordinal);
            Assert.DoesNotContain("plain-kv-secret", trace, StringComparison.Ordinal);
            Assert.DoesNotContain("plain-result-secret", trace, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void AppendExecResult_handles_payload_key_redaction_collisions_without_dropping_trace()
    {
        string tempRoot = CreateTempDirectory();

        try
        {
            string workspaceRoot = Path.Combine(tempRoot, "workspace");
            Directory.CreateDirectory(workspaceRoot);
            CliEnvironmentSnapshot snapshot = CreateSnapshot(
                workspaceRoot: workspaceRoot,
                userProfile: Path.Combine(tempRoot, "home"));
            DiagnosticContext context = new(
                CommandId: "cmd-collision",
                SessionId: "session-collision",
                Workspace: workspaceRoot,
                TimestampUtc: DateTimeOffset.Parse("2026-07-10T11:00:00Z"));
            ExecEvent execEvent = new(
                Type: "tool.completed",
                Sequence: 0,
                Timestamp: DateTimeOffset.Parse("2026-07-10T11:00:01Z"),
                Payload: new Dictionary<string, string>
                {
                    ["apiKey=first-secret"] = "first value",
                    ["apiKey=second-secret"] = "second value"
                });
            ExecResult result = ExecResult.Success("done", [execEvent]);

            TraceLogger.AppendExecResult("exec", snapshot, context, result);

            string tracePath = Path.Combine(workspaceRoot, ".caicli", "logs", "2026-07-10.trace.log");
            JsonObject first = Assert.IsType<JsonObject>(JsonNode.Parse(File.ReadAllLines(tracePath)[0]));
            JsonObject payload = Assert.IsType<JsonObject>(first["payload"]);

            Assert.Equal("first value", payload["apiKey=[redacted]"]?.GetValue<string>());
            Assert.Equal("second value", payload["apiKey=[redacted]_2"]?.GetValue<string>());
            Assert.DoesNotContain("first-secret", File.ReadAllText(tracePath), StringComparison.Ordinal);
            Assert.DoesNotContain("second-secret", File.ReadAllText(tracePath), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void AppendCommandEvent_writes_start_and_complete_records()
    {
        string tempRoot = CreateTempDirectory();

        try
        {
            string workspaceRoot = Path.Combine(tempRoot, "workspace");
            Directory.CreateDirectory(workspaceRoot);
            CliEnvironmentSnapshot snapshot = CreateSnapshot(
                workspaceRoot: workspaceRoot,
                userProfile: Path.Combine(tempRoot, "home"));
            DiagnosticContext context = new(
                CommandId: "cmd-002",
                SessionId: "session-def",
                Workspace: workspaceRoot,
                TimestampUtc: DateTimeOffset.Parse("2026-07-10T09:00:00Z"));

            TraceLogger.AppendCommandEvent("doctor", snapshot, context, "command.start", 0, "started");
            TraceLogger.AppendCommandEvent("doctor", snapshot, context, "command.complete", 1, "success", "doctor complete");

            string tracePath = Path.Combine(workspaceRoot, ".caicli", "logs", "2026-07-10.trace.log");
            string[] lines = File.ReadAllLines(tracePath);
            JsonObject first = Assert.IsType<JsonObject>(JsonNode.Parse(lines[0]));
            JsonObject second = Assert.IsType<JsonObject>(JsonNode.Parse(lines[1]));

            Assert.Equal(2, lines.Length);
            Assert.Equal("doctor", first["command"]?.GetValue<string>());
            Assert.Equal("command.start", first["type"]?.GetValue<string>());
            Assert.Equal("started", first["status"]?.GetValue<string>());
            Assert.Equal("command.complete", second["type"]?.GetValue<string>());
            Assert.Equal("success", second["status"]?.GetValue<string>());
            Assert.Equal("doctor complete", second["summary"]?.GetValue<string>());
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void AppendCommandEvent_uses_event_specific_timestamp_when_provided()
    {
        string tempRoot = CreateTempDirectory();

        try
        {
            string workspaceRoot = Path.Combine(tempRoot, "workspace");
            Directory.CreateDirectory(workspaceRoot);
            CliEnvironmentSnapshot snapshot = CreateSnapshot(
                workspaceRoot: workspaceRoot,
                userProfile: Path.Combine(tempRoot, "home"));
            DiagnosticContext context = new(
                CommandId: "cmd-003",
                SessionId: "session-ghi",
                Workspace: workspaceRoot,
                TimestampUtc: DateTimeOffset.Parse("2026-07-10T09:00:00Z"));

            TraceLogger.AppendCommandEvent(
                "doctor",
                snapshot,
                context,
                "command.complete",
                1,
                "success",
                timestampUtc: DateTimeOffset.Parse("2026-07-10T09:00:03Z"));

            string tracePath = Path.Combine(workspaceRoot, ".caicli", "logs", "2026-07-10.trace.log");
            JsonObject record = Assert.IsType<JsonObject>(JsonNode.Parse(File.ReadAllLines(tracePath)[0]));

            Assert.Equal("2026-07-10T09:00:03.0000000Z", record["timestampUtc"]?.GetValue<string>());
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static CliEnvironmentSnapshot CreateSnapshot(
        string workspaceRoot,
        string userProfile,
        string? apiKey = null,
        string apiKeySource = "missing")
    {
        WorkspaceContext workspace = new(
            RootPath: workspaceRoot,
            ConfigPath: Path.Combine(workspaceRoot, ".caicli", "config.json"),
            Status: WorkspaceStatus.Ready);

        EffectiveConfiguration configuration = new(
            WorkspaceRoot: workspaceRoot,
            UserConfigPath: Path.Combine(userProfile, ".caicli", "config.json"),
            WorkspaceConfigPath: Path.Combine(workspaceRoot, ".caicli", "config.json"),
            Model: "gpt-test",
            ModelSource: "workspace config",
            AgentBackend: "direct",
            AgentBackendSource: "default",
            DisabledTools: new HashSet<string>(StringComparer.Ordinal),
            ApiKey: SecretValue.From(apiKey),
            ApiKeySource: apiKeySource,
            LoadedConfigPaths: [],
            Warnings: [],
            ConfigSources: []);

        return new CliEnvironmentSnapshot(
            Workspace: workspace,
            Configuration: configuration,
            DotnetSdkVersion: "9.0.308",
            DotnetRuntime: ".NET 9.0.0",
            TargetFramework: "net9.0",
            HasGlobalJson: false);
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "caicli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
