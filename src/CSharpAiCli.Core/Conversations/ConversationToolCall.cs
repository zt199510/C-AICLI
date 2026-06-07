namespace CSharpAiCli.Core;

public sealed record ConversationToolCall(
    DateTimeOffset CreatedAtUtc,
    string CallId,
    string ToolName,
    string ArgumentsJson,
    string ApprovalStatus,
    DateTimeOffset CompletedAtUtc,
    bool Succeeded,
    string? OutputSummary,
    string? FailureReason,
    string? ErrorCode,
    bool Retryable)
{
    public static ConversationToolCall FromExecution(
        string callId,
        string toolName,
        string argumentsJson,
        ToolExecutionResult result,
        DateTimeOffset completedAtUtc,
        string? approvalStatus = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(callId);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        string effectiveApprovalStatus = string.IsNullOrWhiteSpace(approvalStatus)
            ? result.ApprovalStatus
            : approvalStatus;

        return new ConversationToolCall(
            CreatedAtUtc: completedAtUtc,
            CallId: callId,
            ToolName: toolName,
            ArgumentsJson: argumentsJson ?? "{}",
            ApprovalStatus: effectiveApprovalStatus,
            CompletedAtUtc: completedAtUtc,
            Succeeded: result.Succeeded,
            OutputSummary: result.Succeeded ? result.Summary : null,
            FailureReason: result.Succeeded ? null : result.Summary,
            ErrorCode: result.ErrorCode,
            Retryable: result.Retryable);
    }
}
