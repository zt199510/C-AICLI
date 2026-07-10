namespace CSharpAiCli.Core;

public sealed record AgentStep(
    int Index,
    DateTimeOffset StartedAtUtc,
    string Status,
    DateTimeOffset? CompletedAtUtc = null,
    int ToolCallCount = 0,
    string? StopReason = null);
