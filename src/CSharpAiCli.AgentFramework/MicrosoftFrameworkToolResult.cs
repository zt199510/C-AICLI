namespace CSharpAiCli.AgentFramework;

public sealed record MicrosoftFrameworkToolResult(
    string CallId,
    string ToolName,
    bool Succeeded,
    string Summary,
    string? ErrorCode,
    bool Retryable,
    string ApprovalStatus);
