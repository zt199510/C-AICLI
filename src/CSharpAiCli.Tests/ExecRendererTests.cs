using System.Text.Json;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ExecRendererTests
{
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
            ApprovalStatus: "denied"));

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
        Assert.Equal("failure", result.GetProperty("payload").GetProperty("status").GetString());
        Assert.Equal(1, result.GetProperty("payload").GetProperty("exitCode").GetInt32());
        Assert.Equal(2, result.GetProperty("payload").GetProperty("eventCount").GetInt32());
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
}
