namespace CSharpAiCli.Core;

public sealed record OpenAiResponseEnvelope(
    string ResponseId,
    string Model,
    string Text,
    IReadOnlyList<OpenAiToolCall>? ToolCalls = null)
{
    public IReadOnlyList<OpenAiToolCall> ToolCalls { get; init; } = ToolCalls ?? [];
}
