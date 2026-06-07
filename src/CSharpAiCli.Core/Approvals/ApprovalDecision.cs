namespace CSharpAiCli.Core;

public sealed record ApprovalDecision(
    bool Approved,
    string Status,
    string SafeMessage)
{
    public static ApprovalDecision Approve(string safeMessage = "Approved.")
    {
        return new ApprovalDecision(true, "approved", safeMessage);
    }

    public static ApprovalDecision Deny(string safeMessage = "Denied.")
    {
        return new ApprovalDecision(false, "denied", safeMessage);
    }
}
