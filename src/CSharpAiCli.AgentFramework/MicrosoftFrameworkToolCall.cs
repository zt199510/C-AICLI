namespace CSharpAiCli.AgentFramework;

public sealed record MicrosoftFrameworkToolCall(
    string CallId,
    string ToolName,
    string ArgumentsJson);
