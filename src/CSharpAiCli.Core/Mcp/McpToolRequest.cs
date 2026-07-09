namespace CSharpAiCli.Core;

public sealed record McpToolRequest(
    McpServerDefinition Server,
    string RemoteToolName,
    string ArgumentsJson)
{
    public string ServerName => Server.Name;
}
