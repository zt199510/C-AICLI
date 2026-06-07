namespace CSharpAiCli.Core;

public interface IApprovalPolicy
{
    ApprovalDecision RequestApproval(ApprovalRequest request);
}
