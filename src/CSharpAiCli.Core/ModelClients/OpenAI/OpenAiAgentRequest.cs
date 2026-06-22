namespace CSharpAiCli.Core;

public sealed record OpenAiAgentRequest(
    string Model,
    string? Prompt,
    string? PreviousResponseId,
    string? Instructions,
    IReadOnlyList<OpenAiToolDefinition>? Tools = null,
    IReadOnlyList<OpenAiToolResultInput>? ToolResults = null)
{
    public IReadOnlyList<OpenAiToolDefinition> Tools { get; init; } = Tools?.ToArray() ?? [];

    public IReadOnlyList<OpenAiToolResultInput> ToolResults { get; init; } =
        ToolResults?.ToArray() ?? [];
}
