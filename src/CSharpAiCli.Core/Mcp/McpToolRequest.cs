namespace CSharpAiCli.Core;

public sealed record McpToolRequest(
    string ServerName,
    string ArgumentsJson);
