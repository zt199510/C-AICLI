namespace CSharpAiCli.Core;

public sealed record AgentToolCallRequest(
    string CallId,
    string ToolName,
    string ArgumentsJson);
