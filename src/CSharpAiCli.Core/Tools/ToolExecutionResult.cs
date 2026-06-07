namespace CSharpAiCli.Core;

public sealed record ToolExecutionResult(
    bool Succeeded,
    string Summary,
    string? ErrorCode,
    bool Retryable,
    string ApprovalStatus = "not-required")
{
    public static ToolExecutionResult Success(string summary, string approvalStatus = "not-required")
    {
        return new ToolExecutionResult(
            Succeeded: true,
            Summary: summary ?? string.Empty,
            ErrorCode: null,
            Retryable: false,
            ApprovalStatus: approvalStatus);
    }

    public static ToolExecutionResult Failure(
        string errorCode,
        string safeMessage,
        bool retryable = false,
        string approvalStatus = "not-required")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);

        return new ToolExecutionResult(
            Succeeded: false,
            Summary: safeMessage ?? string.Empty,
            ErrorCode: errorCode,
            Retryable: retryable,
            ApprovalStatus: approvalStatus);
    }
}
