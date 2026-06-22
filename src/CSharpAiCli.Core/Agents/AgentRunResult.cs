using System.Collections.ObjectModel;

namespace CSharpAiCli.Core;

public sealed record AgentRunResult
{
    public AgentRunResult(
        string? Text,
        IReadOnlyList<ConversationToolCall> ToolCalls,
        AgentError? Error,
        IReadOnlyList<AgentRunEvent>? Events = null)
    {
        ArgumentNullException.ThrowIfNull(ToolCalls);

        this.Text = Text;
        this.ToolCalls = new ReadOnlyCollection<ConversationToolCall>(ToolCalls.ToArray());
        this.Error = Error;
        this.Events = new ReadOnlyCollection<AgentRunEvent>((Events ?? []).ToArray());
    }

    public string? Text { get; }

    public IReadOnlyList<ConversationToolCall> ToolCalls { get; }

    public AgentError? Error { get; }

    public IReadOnlyList<AgentRunEvent> Events { get; }

    public bool IsSuccess => Error is null;

    public static AgentRunResult Success(
        string text,
        IReadOnlyList<ConversationToolCall> toolCalls,
        IReadOnlyList<AgentRunEvent>? events = null)
    {
        events ??= [];
        return new AgentRunResult(text, toolCalls, Error: null, Events: events);
    }

    public static AgentRunResult Failure(
        AgentError error,
        IReadOnlyList<ConversationToolCall> toolCalls,
        IReadOnlyList<AgentRunEvent>? events = null)
    {
        ArgumentNullException.ThrowIfNull(error);
        events ??= [];
        return new AgentRunResult(Text: null, toolCalls, error, Events: events);
    }
}
