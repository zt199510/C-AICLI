namespace CSharpAiCli.Core;

public sealed record ConversationAgentRun(
    DateTimeOffset CompletedAtUtc,
    string Status,
    string StopReason,
    string? ErrorCode,
    string? Summary,
    int EventCount,
    int ToolCallCount)
{
    public static ConversationAgentRun FromAgentResult(
        AgentRunResult result,
        DateTimeOffset completedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new ConversationAgentRun(
            CompletedAtUtc: completedAtUtc,
            Status: result.Status,
            StopReason: result.StopReason,
            ErrorCode: result.Error?.LocalErrorCode,
            Summary: result.IsSuccess ? result.Text : result.Error?.SafeMessage,
            EventCount: result.Events.Count,
            ToolCallCount: result.ToolCalls.Count);
    }
}
