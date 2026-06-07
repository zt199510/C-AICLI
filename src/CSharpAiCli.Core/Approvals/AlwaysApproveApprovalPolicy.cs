namespace CSharpAiCli.Core;

public sealed class AlwaysApproveApprovalPolicy : IApprovalPolicy
{
    public ApprovalDecision RequestApproval(ApprovalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ApprovalDecision.Approve("Approved by test policy.");
    }
}
