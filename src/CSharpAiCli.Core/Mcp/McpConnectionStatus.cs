namespace CSharpAiCli.Core;

public sealed record McpConnectionStatus(
    string ServerName,
    string Status,
    string SafeMessage);
