namespace CSharpAiCli.Core;

public sealed class DefaultDenyApprovalPolicy : IApprovalPolicy
{
    public ApprovalDecision RequestApproval(ApprovalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ApprovalDecision.Deny("File edit approval is required before applying patches.");
    }
}
