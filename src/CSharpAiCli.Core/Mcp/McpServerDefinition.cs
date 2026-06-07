namespace CSharpAiCli.Core;

public sealed record McpServerDefinition(
    string Name,
    bool Enabled,
    string Status,
    string TransportSummary,
    string Source);
