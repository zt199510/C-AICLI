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
                    DurationMs: 35)
            ]);

        ExecResult result = AgentExecResultAdapter.FromAgentResult(agentResult);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("done", result.Summary);
        Assert.Equal("not-required", result.ApprovalStatus);
        ExecEvent execEvent = Assert.Single(result.Events);
        Assert.Equal("tool.result", execEvent.Type);
        Assert.Equal(2, execEvent.Sequence);
        Assert.Equal("Tool completed.", execEvent.Message);
        Assert.Equal("read README", execEvent.Summary);
        Assert.Equal("workspace.read_text", execEvent.Payload?["toolName"]);
        Assert.Equal("not-required", execEvent.ApprovalStatus);
        Assert.Equal("success", execEvent.Status);
        Assert.Equal(35, execEvent.DurationMs);
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
                    ErrorCode: "agent-loop-limit-reached")
            ]);

        ExecResult result = AgentExecResultAdapter.FromAgentResult(agentResult);

        Assert.False(result.IsSuccess);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("agent-loop-limit-reached", result.ErrorCode);
        Assert.Equal("Agent loop reached the maximum iteration limit.", result.Summary);
        Assert.Equal("agent-loop-limit-reached", Assert.Single(result.Events).ErrorCode);
    }
}
