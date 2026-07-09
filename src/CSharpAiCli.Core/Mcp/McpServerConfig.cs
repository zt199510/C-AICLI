namespace CSharpAiCli.Core;

public sealed class McpServerConfig
{
    public bool Enabled { get; init; } = true;

    public string? Transport { get; init; }

    public string? Command { get; init; }

    public string[]? Args { get; init; }

    public string? Cwd { get; init; }

    public int? TimeoutMilliseconds { get; init; }

    public string? Url { get; init; }
}
