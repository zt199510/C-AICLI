namespace CSharpAiCli.Core;

public sealed record OpenAiToolResultInput(
    string CallId,
    string ToolName,
    bool Succeeded,
    string Summary,
    string? ErrorCode,
    string ApprovalStatus);
