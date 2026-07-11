using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class AgentExecResultAdapterTests
{
    [Fact]
    public void FromAgentResult_preserves_success_summary_and_event_fields()
    {
        AgentRunResult agentResult = AgentRunResult.Success(
            "done",
            [],
            [
                new AgentRunEvent(
                    Type: "tool.result",
                    Sequence: 2,
                    Timestamp: DateTimeOffset.Parse("2024-01-01T00:00:02Z"),
                    Message: "Tool completed.",
                    Summary: "read README",
                    Payload: new Dictionary<string, string>
                    {
                        ["toolName"] = "workspace.read_text"
                    },
                    ErrorCode: null,
                    ApprovalStatus: "not-required",
                    Status: "success",
                    DurationMs: 35,
                    StepIndex: 1)
            ],
            stopReason: "completed");

        ExecResult result = AgentExecResultAdapter.FromAgentResult(agentResult);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("done", result.Summary);
        Assert.Equal("not-required", result.ApprovalStatus);
        Assert.Equal("completed", result.StopReason);
        ExecEvent execEvent = Assert.Single(result.Events);
        Assert.Equal("tool.result", execEvent.Type);
        Assert.Equal(2, execEvent.Sequence);
        Assert.Equal("Tool completed.", execEvent.Message);
        Assert.Equal("read README", execEvent.Summary);
        Assert.Equal("workspace.read_text", execEvent.Payload?["toolName"]);
        Assert.Equal("not-required", execEvent.ApprovalStatus);
        Assert.Equal("success", execEvent.Status);
        Assert.Equal(35, execEvent.DurationMs);
        Assert.Equal(1, execEvent.StepIndex);
    }

    [Fact]
    public void FromAgentResult_preserves_changed_files_and_verification_results()
    {
        AgentRunResult agentResult = AgentRunResult.Success(
            "done",
            [],
            [],
            changedFiles:
            [
                new ChangedFileSummary(
                    Path: "src/App.cs",
                    Status: "modified",
                    SourceToolCallId: "call_patch")
            ],
            verificationResults:
            [
                new VerificationResultSummary(
                    Status: "success",
                    Source: "project-instructions",
                    Command: "dotnet test",
                    WorkingDirectory: ".",
                    Succeeded: true,
                    ApprovalStatus: "approved",
                    ErrorCode: null,
                    ExitCode: 0,
                    TimedOut: false,
                    StdoutTruncated: false,
                    StderrTruncated: false,
                    Stdout: "passed",
                    Stderr: "",
                    Summary: "Shell command completed.")
            ],
            stopReason: "completed");

        ExecResult result = AgentExecResultAdapter.FromAgentResult(agentResult);

        ChangedFileSummary changedFile = Assert.Single(result.ChangedFiles);
        Assert.Equal("src/App.cs", changedFile.Path);
        VerificationResultSummary verification = Assert.Single(result.VerificationResults);
        Assert.Equal("success", verification.Status);
        Assert.Equal("dotnet test", verification.Command);
    }

    [Fact]
    public void FromAgentResult_maps_failure_to_exit_one_and_agent_error_code()
    {
        AgentRunResult agentResult = AgentRunResult.Failure(
            new AgentError(
                "agent-loop-limit-reached",
                "Agent loop reached the maximum iteration limit.",
                Retryable: false),
            [],
            [
                new AgentRunEvent(
                    Type: "agent.error",
                    Sequence: 0,
                    Timestamp: DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                    Message: "Agent loop reached the maximum iteration limit.",
                    ErrorCode: "agent-loop-limit-reached",
                    StopReason: "max-steps-exceeded")
            ],
            stopReason: "max-steps-exceeded");

        ExecResult result = AgentExecResultAdapter.FromAgentResult(agentResult);

        Assert.False(result.IsSuccess);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("agent-loop-limit-reached", result.ErrorCode);
        Assert.Equal("max-steps-exceeded", result.StopReason);
        Assert.Equal("Agent loop reached the maximum iteration limit.", result.Summary);
        ExecEvent execEvent = Assert.Single(result.Events);
        Assert.Equal("agent-loop-limit-reached", execEvent.ErrorCode);
        Assert.Equal("max-steps-exceeded", execEvent.StopReason);
    }

    [Fact]
    public void FromAgentResult_preserves_retry_attempts_and_failure_summary()
    {
        AgentRetryAttempt attempt = new(
            Index: 1,
            FailureKind: AgentFailureKind.Verification,
            StopReason: AgentStopReason.VerificationFailure,
            ErrorCode: ToolErrorCode.ShellExitCode,
            SourceToolCallId: "call_patch",
            ToolName: "workspace.apply_patch",
            Summary: "tests failed",
            Commands: ["dotnet test"],
            ChangedFiles: ["src/App.cs"],
            VerificationStatus: "failure");
        AgentFailureSummary failureSummary = new(
            FailureKind: AgentFailureKind.Verification,
            StopReason: AgentStopReason.RetryBudgetExhausted,
            ErrorCode: "agent-retry-budget-exhausted",
            Message: "Agent retry budget was exhausted.",
            RetryBudget: 1,
            RetryCount: 1,
            RemainingRetries: 0,
            RemainingRisk: "Review changed files.",
            RetryAttempts: [attempt],
            Commands: ["dotnet test"],
            ChangedFiles: ["src/App.cs"]);
        AgentRunResult agentResult = AgentRunResult.Failure(
            new AgentError(
                "agent-retry-budget-exhausted",
                "Agent retry budget was exhausted.",
                Retryable: false),
            [],
            [],
            retryAttempts: [attempt],
            failureSummary: failureSummary,
            stopReason: AgentStopReason.RetryBudgetExhausted);

        ExecResult result = AgentExecResultAdapter.FromAgentResult(agentResult);

        AgentRetryAttempt mappedAttempt = Assert.Single(result.RetryAttempts);
        Assert.Equal("verification", mappedAttempt.FailureKind);
        Assert.Equal("dotnet test", Assert.Single(mappedAttempt.Commands));
        Assert.Same(failureSummary, result.FailureSummary);
    }
}
