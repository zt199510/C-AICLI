namespace CSharpAiCli.Core;

public sealed record McpConfiguration(IReadOnlyList<McpServerDefinition> Servers)
{
    public static McpConfiguration Empty { get; } = new([]);
}
