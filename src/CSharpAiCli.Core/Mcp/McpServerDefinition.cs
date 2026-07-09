namespace CSharpAiCli.Core;

public sealed record McpServerDefinition(
    string Name,
    bool Enabled,
    string Status,
    string TransportSummary,
    string Source)
{
    public string? Transport { get; init; }

    public string? Command { get; init; }

    public IReadOnlyList<string> Args { get; init; } = [];

    public string? Cwd { get; init; }

    public int? TimeoutMilliseconds { get; init; }

    public string? Url { get; init; }
}
