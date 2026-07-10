using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ExecDtoTests
{
    [Fact]
    public void Request_defaults_to_text_output_and_no_approval_status()
    {
        ExecRequest request = new(
            Task: "summarize the repo",
            WorkspaceRoot: "C:\\repo");

        Assert.Equal("summarize the repo", request.Task);
        Assert.Equal("C:\\repo", request.WorkspaceRoot);
        Assert.Equal(ExecOutputMode.Text, request.OutputMode);
        Assert.Null(request.ApprovalPolicy);
        Assert.Null(request.MaxTurns);
        Assert.Null(request.MaxToolCalls);
        Assert.Null(request.TimeoutSeconds);
    }

    [Fact]
    public void Request_stores_optional_loop_limits()
    {
        ExecRequest request = new(
            Task: "summarize the repo",
            WorkspaceRoot: "C:\\repo",
            MaxTurns: 3,
            MaxToolCalls: 12,
            TimeoutSeconds: 60);

        Assert.Equal(3, request.MaxTurns);
        Assert.Equal(12, request.MaxToolCalls);
        Assert.Equal(60, request.TimeoutSeconds);
    }

    [Theory]
    [InlineData(null)]
    public void Request_rejects_null_task(string? task)
    {
        Assert.Throws<ArgumentNullException>(() => new ExecRequest(Task: task!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Request_accepts_empty_or_whitespace_task(string task)
    {
        ExecRequest request = new(Task: task);

        Assert.Equal(task, request.Task);
    }

    [Fact]
    public void Event_snapshots_payload_and_carries_optional_metadata()
    {
        DateTimeOffset timestamp = new(2026, 6, 9, 10, 30, 0, TimeSpan.Zero);
        Dictionary<string, string> payload = new()
        {
            ["command"] = "dotnet test"
        };

        ExecEvent execEvent = new(
            Type: "tool.started",
            Sequence: 2,
            Timestamp: timestamp,
            Message: "Running tests.",
            Summary: "Tests started",
            Payload: payload,
            ErrorCode: null,
            ApprovalStatus: "approved",
            Status: "success",
            DurationMs: 42);
        payload["command"] = "dotnet build";
        payload["extra"] = "ignored";

        Assert.Equal("tool.started", execEvent.Type);
        Assert.Equal(2, execEvent.Sequence);
        Assert.Equal(timestamp, execEvent.Timestamp);
        Assert.Equal("Running tests.", execEvent.Message);
        Assert.Equal("Tests started", execEvent.Summary);
        Assert.NotSame(payload, execEvent.Payload);
        Assert.NotNull(execEvent.Payload);
        Assert.Equal("dotnet test", execEvent.Payload["command"]);
        Assert.False(execEvent.Payload.ContainsKey("extra"));
        Assert.Null(execEvent.ErrorCode);
        Assert.Equal("approved", execEvent.ApprovalStatus);
        Assert.Equal("success", execEvent.Status);
        Assert.Equal(42, execEvent.DurationMs);
    }

    [Fact]
    public void Event_snapshot_is_immune_to_caller_mutation()
    {
        Dictionary<string, string> payload = new()
        {
            ["command"] = "dotnet test"
        };

        ExecEvent execEvent = new(
            Type: "tool.started",
            Sequence: 1,
            Timestamp: DateTimeOffset.UnixEpoch,
            Payload: payload);
        payload["command"] = "dotnet build";
        payload["extra"] = "ignored";

        Assert.NotSame(payload, execEvent.Payload);
        Assert.NotNull(execEvent.Payload);
        Assert.Equal("dotnet test", execEvent.Payload["command"]);
        Assert.False(execEvent.Payload.ContainsKey("extra"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Event_rejects_missing_type(string? type)
    {
        Assert.Throws<ArgumentException>(() =>
            new ExecEvent(Type: type!, Sequence: 1, Timestamp: DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Event_rejects_negative_sequence()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ExecEvent(Type: "tool.started", Sequence: -1, Timestamp: DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Result_factories_snapshot_events_and_set_invariants()
    {
        ExecEvent execEvent = new(
            Type: "task.completed",
            Sequence: 1,
            Timestamp: DateTimeOffset.UnixEpoch);
        List<ExecEvent> successEvents = new() { execEvent };
        List<ExecEvent> failureEvents = new() { execEvent };

        ExecResult success = ExecResult.Success(
            Summary: "Done.",
            Events: successEvents,
            ApprovalStatus: "approved");
        successEvents.Clear();

        ExecResult failure = ExecResult.Failure(
            ExitCode: 2,
            Summary: "Failed.",
            ErrorCode: "exec-failed",
            Events: failureEvents,
            ApprovalStatus: "denied");
        failureEvents.Clear();

        Assert.True(success.IsSuccess);
        Assert.Equal(0, success.ExitCode);
        Assert.Equal("Done.", success.Summary);
        Assert.Null(success.ErrorCode);
        Assert.Equal("approved", success.ApprovalStatus);
        Assert.Equal(new[] { execEvent }, success.Events);
        Assert.Single(success.Events);

        Assert.False(failure.IsSuccess);
        Assert.Equal(2, failure.ExitCode);
        Assert.Equal("Failed.", failure.Summary);
        Assert.Equal("exec-failed", failure.ErrorCode);
        Assert.Equal("denied", failure.ApprovalStatus);
        Assert.Equal(new[] { execEvent }, failure.Events);
        Assert.Single(failure.Events);
    }

    [Fact]
    public void Result_success_rejects_null_events()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ExecResult.Success(Summary: "Done.", Events: null!));
    }

    [Fact]
    public void Result_failure_rejects_success_exit_code()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ExecResult.Failure(
                ExitCode: 0,
                Summary: "Failed.",
                ErrorCode: "exec-failed",
                Events: new[] { new ExecEvent(Type: "task.completed", Sequence: 1, Timestamp: DateTimeOffset.UnixEpoch) }));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Result_failure_rejects_missing_error_code(string? errorCode)
    {
        Assert.Throws<ArgumentException>(() =>
            ExecResult.Failure(
                ExitCode: 2,
                Summary: "Failed.",
                ErrorCode: errorCode!,
                Events: new[] { new ExecEvent(Type: "task.completed", Sequence: 1, Timestamp: DateTimeOffset.UnixEpoch) }));
    }

    [Fact]
    public void Result_failure_rejects_null_events()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ExecResult.Failure(
                ExitCode: 2,
                Summary: "Failed.",
                ErrorCode: "exec-failed",
                Events: null!));
    }

    [Fact]
    public void Result_does_not_expose_public_constructors_that_bypass_invariants()
    {
        Assert.Empty(typeof(ExecResult).GetConstructors());
    }
}
