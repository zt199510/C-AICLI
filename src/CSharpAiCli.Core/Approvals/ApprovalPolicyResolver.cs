namespace CSharpAiCli.Core;

public static class ApprovalPolicyResolver
{
    public static IApprovalPolicy Resolve(ApprovalMode effectiveMode)
    {
        return Resolve(effectiveMode, cliOverrideMode: null);
    }

    public static IApprovalPolicy Resolve(ApprovalMode effectiveMode, ApprovalMode? cliOverrideMode)
    {
        ApprovalMode mode = cliOverrideMode ?? effectiveMode;
        return new RiskAwareApprovalPolicy(mode);
    }

    private sealed class RiskAwareApprovalPolicy(ApprovalMode mode) : IApprovalPolicy
    {
        public ApprovalDecision RequestApproval(ApprovalRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            return request.RiskLevel switch
            {
                ToolRiskLevel.Read => new ApprovalDecision(
                    Approved: true,
                    Status: "not-required",
                    SafeMessage: "Approval is not required for read-only operations."),
                ToolRiskLevel.DangerousShell => new ApprovalDecision(
                    Approved: false,
                    Status: "dangerous-shell-denied",
                    SafeMessage: "Dangerous shell commands are denied by approval policy."),
                ToolRiskLevel.Write or ToolRiskLevel.Shell => DecideApprovalRequiredRisk(),
                _ => throw new ArgumentOutOfRangeException(nameof(request.RiskLevel), request.RiskLevel, "Unknown tool risk level.")
            };
        }

        private ApprovalDecision DecideApprovalRequiredRisk()
        {
            return mode switch
            {
                ApprovalMode.Always => new ApprovalDecision(
                    Approved: true,
                    Status: "approved",
                    SafeMessage: "Approved by approval mode."),
                ApprovalMode.Never => new ApprovalDecision(
                    Approved: false,
                    Status: "denied",
                    SafeMessage: "Approval is disabled by policy."),
                ApprovalMode.OnRequest => new ApprovalDecision(
                    Approved: false,
                    Status: "approval-required",
                    SafeMessage: "Approval is required, but this CLI cannot request interactive approval."),
                ApprovalMode.OnFailure => new ApprovalDecision(
                    Approved: false,
                    Status: "approval-required",
                    SafeMessage: "Approval after failure is not available because sandbox retry escalation is not implemented."),
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown approval mode.")
            };
        }
    }
}
