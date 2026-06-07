namespace CSharpAiCli.Core;

public sealed record AgentRunResult(
    string? Text,
    IReadOnlyList<ConversationToolCall> ToolCalls,
    AgentError? Error)
{
    public bool IsSuccess => Error is null;

    public static AgentRunResult Success(string text, IReadOnlyList<ConversationToolCall> toolCalls)
    {
        return new AgentRunResult(text, toolCalls, Error: null);
    }

    public static AgentRunResult Failure(AgentError error, IReadOnlyList<ConversationToolCall> toolCalls)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new AgentRunResult(Text: null, toolCalls, error);
    }
}
