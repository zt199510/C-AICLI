namespace CSharpAiCli.Core;

public sealed record PatchApplyResult(
    bool Succeeded,
    string Summary,
    string? ErrorCode,
    string ApprovalStatus,
    string? Diff)
{
    public static PatchApplyResult Success(string summary, string approvalStatus, string? diff)
    {
        return new PatchApplyResult(true, summary, null, approvalStatus, diff);
    }

    public static PatchApplyResult Failure(
        string errorCode,
        string safeMessage,
        string approvalStatus = "not-required",
        string? diff = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        return new PatchApplyResult(false, safeMessage, errorCode, approvalStatus, diff);
    }
}
