using System.Text.Json;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ExecRendererTests
{
    private static readonly string[] RendererRawSecrets =
    [
        "json-message-password",
        "json-message-auth-secret",
        "json-summary-secret",
        "json-result-secret",
        "text-message-password",
        "text-message-auth-secret",
        "text-summary-secret",
        "text-result-secret",
        "sk-argument-api-secret",
        "argument-password-secret",
        "argument-bearer-secret",
        "argument-basic-secret",
        "argument-secret-key-secret",
        "argument-private-key-secret",
        "escaped-secret-key-secret",
        "escaped-auth-secret",
        "ghp_argumentsecret1234567890",
        "github_pat_argument_secret_1234567890",
        "payload-api-key-secret",
        "payload-password-secret",
        "payload-authorization-secret",
        "payload-secret-key-secret",
        "payload-private-key-secret",
        "sk-json-key-one",
        "sk-json-key-two",
        "sk-text-key-one",
        "sk-text-key-two"
    ];

    [Fact]
    public void Text_renderer_writes_stable_human_readable_event_and_result_output()
    {
        using StringWriter writer = new();
        ExecTextRenderer renderer = new(writer);
        ExecEvent started = new(
            Type: "tool.started",
            Sequence: 1,
            Timestamp: new DateTimeOffset(2026, 6, 9, 10, 30, 0, TimeSpan.Zero),
            Message: "Running tests.",
            Payload: new Dictionary<string, string>
            {
                ["command"] = "dotnet test"
            },
            ApprovalStatus: "approved",
            Status: "started");
        ExecEvent completed = new(
            Type: "task.completed",
            Sequence: 2,
            Timestamp: new DateTimeOffset(2026, 6, 9, 10, 31, 0, TimeSpan.Zero),
            Summary: "Done.",
            Status: "success",
            DurationMs: 60000);

        renderer.WriteEvent(started);
        renderer.WriteEvent(completed);
        renderer.WriteResult(ExecResult.Success(
            Summary: "All good.",
            Events: new[] { started, completed },
            ApprovalStatus: "approved"));

        string[] lines = writer.ToString().TrimEnd().Split(Environment.NewLine);

        Assert.Equal("event: tool.started seq=1 ts=2026-06-09T10:30:00.0000000+00:00 status=started message=Running tests. approvalStatus=approved payload.command=dotnet test", lines[0]);
        Assert.Equal("event: task.completed seq=2 ts=2026-06-09T10:31:00.0000000+00:00 status=success durationMs=60000 summary=Done.", lines[1]);
        Assert.Equal("result: success exitCode=0 summary=All good. approvalStatus=approved events=2", lines[2]);
    }

    [Fact]
    public void Json_renderer_writes_ndjson_for_events_and_result()
    {
        using StringWriter writer = new();
        ExecJsonRenderer renderer = new(writer);
        ExecEvent eventWithPayload = new(
            Type: "tool.started",
            Sequence: 1,
            Timestamp: new DateTimeOffset(2026, 6, 9, 10, 30, 0, TimeSpan.Zero),
            Message: "Running tests.",
            Summary: "Started",
            Payload: new Dictionary<string, string>
            {
                ["command"] = "dotnet test"
            },
            ErrorCode: "none",
            ApprovalStatus: "approved",
            ApprovalDurationMs: 12,
            Status: "started");
        ExecEvent plainEvent = new(
            Type: "task.completed",
            Sequence: 2,
            Timestamp: new DateTimeOffset(2026, 6, 9, 10, 31, 0, TimeSpan.Zero),
            Status: "failure",
            DurationMs: 60000);

        renderer.WriteEvent(eventWithPayload);
        renderer.WriteEvent(plainEvent);
        renderer.WriteResult(ExecResult.Failure(
            ExitCode: 1,
            Summary: "Failed.",
            ErrorCode: "exec-failed",
            Events: new[] { eventWithPayload, plainEvent },
            ApprovalStatus: "denied",
            StopReason: "approval-denied"));

        string[] lines = writer.ToString().TrimEnd().Split(Environment.NewLine);
        Assert.Equal(3, lines.Length);

        JsonElement first = JsonSerializer.Deserialize<JsonElement>(lines[0]);
        Assert.Equal("tool.started", first.GetProperty("type").GetString());
        Assert.Equal(1, first.GetProperty("sequence").GetInt64());
        Assert.Equal("2026-06-09T10:30:00.0000000+00:00", first.GetProperty("timestamp").GetString());
        Assert.Equal("Running tests.", first.GetProperty("message").GetString());
        Assert.Equal("Started", first.GetProperty("summary").GetString());
        Assert.Equal("dotnet test", first.GetProperty("payload").GetProperty("command").GetString());
        Assert.Equal("none", first.GetProperty("errorCode").GetString());
        Assert.Equal("approved", first.GetProperty("approvalStatus").GetString());
        Assert.Equal(12, first.GetProperty("approvalDurationMs").GetInt64());
        Assert.Equal("started", first.GetProperty("status").GetString());

        JsonElement second = JsonSerializer.Deserialize<JsonElement>(lines[1]);
        Assert.Equal("failure", second.GetProperty("status").GetString());
        Assert.Equal(60000, second.GetProperty("durationMs").GetInt64());

        JsonElement result = JsonSerializer.Deserialize<JsonElement>(lines[2]);
        Assert.Equal("exec.result", result.GetProperty("type").GetString());
        Assert.Equal(3, result.GetProperty("sequence").GetInt64());
        Assert.Equal("2026-06-09T10:31:00.0000000+00:00", result.GetProperty("timestamp").GetString());
        Assert.Equal("Failed.", result.GetProperty("summary").GetString());
        Assert.Equal("exec-failed", result.GetProperty("errorCode").GetString());
        Assert.Equal("denied", result.GetProperty("approvalStatus").GetString());
        Assert.Equal("approval-denied", result.GetProperty("stopReason").GetString());
        Assert.Equal("failure", result.GetProperty("payload").GetProperty("status").GetString());
        Assert.Equal(1, result.GetProperty("payload").GetProperty("exitCode").GetInt32());
        Assert.Equal(2, result.GetProperty("payload").GetProperty("eventCount").GetInt32());
    }

    [Fact]
    public void Json_renderer_redacts_secret_bearing_event_and_result_output()
    {
        using StringWriter writer = new();
        ExecJsonRenderer renderer = new(writer);
        ExecEvent secretEvent = CreateSecretBearingEvent(
            messagePassword: "json-message-password",
            messageAuthorization: "json-message-auth-secret",
            summarySecret: "json-summary-secret",
            firstSecretKey: "sk-json-key-one",
            secondSecretKey: "sk-json-key-two");

        renderer.WriteEvent(secretEvent);
        renderer.WriteResult(ExecResult.Success(
            Summary: "result apiKey=json-result-secret",
            Events: new[] { secretEvent },
            ApprovalStatus: "approved"));

        string output = writer.ToString();
        string[] lines = output.TrimEnd().Split(Environment.NewLine);
        Assert.Equal(2, lines.Length);

        foreach (string line in lines)
        {
            _ = JsonSerializer.Deserialize<JsonElement>(line);
        }

        JsonElement eventJson = JsonSerializer.Deserialize<JsonElement>(lines[0]);
        JsonElement payload = eventJson.GetProperty("payload");
        Assert.Contains("[redacted]", output, StringComparison.Ordinal);
        Assert.Contains("note.txt", output, StringComparison.Ordinal);
        Assert.Contains("workspace.search", output, StringComparison.Ordinal);
        Assert.Contains("[redacted]", eventJson.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains("[redacted]", eventJson.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Equal("note.txt", payload.GetProperty("path").GetString());
        Assert.Contains("[redacted]", payload.GetProperty("argumentsJson").GetString(), StringComparison.Ordinal);
        Assert.Equal("[redacted]", payload.GetProperty("apiKey").GetString());
        Assert.Equal("[redacted]", payload.GetProperty("password").GetString());
        Assert.Equal("[redacted]", payload.GetProperty("authorization").GetString());
        Assert.Equal("[redacted]", payload.GetProperty("secretKey").GetString());
        Assert.Equal("[redacted]", payload.GetProperty("privateKey").GetString());

        JsonElement resultJson = JsonSerializer.Deserialize<JsonElement>(lines[1]);
        Assert.Contains("[redacted]", resultJson.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Equal("approved", resultJson.GetProperty("approvalStatus").GetString());
        Assert.Equal("success", resultJson.GetProperty("payload").GetProperty("status").GetString());
        Assert.Equal(0, resultJson.GetProperty("payload").GetProperty("exitCode").GetInt32());
        Assert.Equal(1, resultJson.GetProperty("payload").GetProperty("eventCount").GetInt32());
        Assert.DoesNotContain(RendererRawSecrets, secret => output.Contains(secret, StringComparison.Ordinal));
    }

    [Fact]
    public void Text_renderer_redacts_secret_bearing_event_and_result_output()
    {
        using StringWriter writer = new();
        ExecTextRenderer renderer = new(writer);
        ExecEvent secretEvent = CreateSecretBearingEvent(
            messagePassword: "text-message-password",
            messageAuthorization: "text-message-auth-secret",
            summarySecret: "text-summary-secret",
            firstSecretKey: "sk-text-key-one",
            secondSecretKey: "sk-text-key-two");

        renderer.WriteEvent(secretEvent);
        renderer.WriteResult(ExecResult.Success(
            Summary: "result apiKey=text-result-secret",
            Events: new[] { secretEvent },
            ApprovalStatus: "approved"));

        string output = writer.ToString();
        Assert.Contains("[redacted]", output, StringComparison.Ordinal);
        Assert.Contains("payload.toolName=workspace.search", output, StringComparison.Ordinal);
        Assert.Contains("payload.path=note.txt", output, StringComparison.Ordinal);
        Assert.Contains("message=message password=[redacted] authorization=Bearer [redacted]", output, StringComparison.Ordinal);
        Assert.Contains("summary=summary apiKey=[redacted]", output, StringComparison.Ordinal);
        Assert.Contains("result: success exitCode=0 summary=result apiKey=[redacted] approvalStatus=approved events=1", output, StringComparison.Ordinal);
        Assert.DoesNotContain(RendererRawSecrets, secret => output.Contains(secret, StringComparison.Ordinal));
    }

    [Fact]
    public void Json_renderer_omits_absent_result_optionals_and_uses_payload_for_result_data()
    {
        using StringWriter writer = new();
        ExecJsonRenderer renderer = new(writer);
        ExecEvent eventWithoutOptionals = new(
            Type: "task.completed",
            Sequence: 2,
            Timestamp: new DateTimeOffset(2026, 6, 9, 10, 31, 0, TimeSpan.Zero));

        renderer.WriteEvent(eventWithoutOptionals);
        renderer.WriteResult(ExecResult.Success(
            Summary: null,
            Events: new[] { eventWithoutOptionals }));

        string[] lines = writer.ToString().TrimEnd().Split(Environment.NewLine);
        Assert.Equal(2, lines.Length);

        JsonElement result = JsonSerializer.Deserialize<JsonElement>(lines[1]);
        Assert.Equal("exec.result", result.GetProperty("type").GetString());
        Assert.Equal(3, result.GetProperty("sequence").GetInt64());
        Assert.Equal("2026-06-09T10:31:00.0000000+00:00", result.GetProperty("timestamp").GetString());
        Assert.False(result.TryGetProperty("summary", out _));
        Assert.False(result.TryGetProperty("errorCode", out _));
        Assert.False(result.TryGetProperty("approvalStatus", out _));
        Assert.False(result.TryGetProperty("isSuccess", out _));
        Assert.False(result.TryGetProperty("exitCode", out _));
        Assert.False(result.TryGetProperty("eventCount", out _));
        Assert.Equal("success", result.GetProperty("payload").GetProperty("status").GetString());
        Assert.Equal(0, result.GetProperty("payload").GetProperty("exitCode").GetInt32());
        Assert.Equal(1, result.GetProperty("payload").GetProperty("eventCount").GetInt32());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Renderers_reject_null_writer(bool useJson)
    {
        if (useJson)
        {
            Assert.Throws<ArgumentNullException>(() => new ExecJsonRenderer(null!));
            return;
        }

        Assert.Throws<ArgumentNullException>(() => new ExecTextRenderer(null!));
    }

    private static ExecEvent CreateSecretBearingEvent(
        string messagePassword,
        string messageAuthorization,
        string summarySecret,
        string firstSecretKey,
        string secondSecretKey)
    {
        return new ExecEvent(
            Type: "tool.call",
            Sequence: 1,
            Timestamp: new DateTimeOffset(2026, 6, 9, 10, 30, 0, TimeSpan.Zero),
            Message: $"message password={messagePassword} authorization=Bearer {messageAuthorization}",
            Summary: $"summary apiKey={summarySecret}",
            Payload: new Dictionary<string, string>
            {
                ["argumentsJson"] = """
                {"apiKey":"sk-argument-api-secret","nested":{"password":"argument-password-secret","authorization":"Bearer argument-bearer-secret","Authorization":"Basic argument-basic-secret","secretKey":"argument-secret-key-secret","privateKey":"argument-private-key-secret","github":"ghp_argumentsecret1234567890","githubPat":"github_pat_argument_secret_1234567890","escaped":"{\"secretKey\":\"escaped-secret-key-secret\",\"authorization\":\"Bearer escaped-auth-secret\"}"},"path":"note.txt"}
                """,
                ["toolName"] = "workspace.search",
                ["path"] = "note.txt",
                ["apiKey"] = "payload-api-key-secret",
                ["password"] = "payload-password-secret",
                ["authorization"] = "Bearer payload-authorization-secret",
                ["secretKey"] = "payload-secret-key-secret",
                ["privateKey"] = "payload-private-key-secret",
                [firstSecretKey] = "first key should keep its value",
                [secondSecretKey] = "second key should keep its value"
            },
            ApprovalStatus: "approved",
            Status: "started",
            DurationMs: 42,
            ApprovalDurationMs: 5);
    }
}
