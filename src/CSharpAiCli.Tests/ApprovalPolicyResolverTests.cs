using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ApprovalPolicyResolverTests
{
    [Theory]
    [InlineData(ApprovalMode.Never)]
    [InlineData(ApprovalMode.OnRequest)]
    [InlineData(ApprovalMode.OnFailure)]
    [InlineData(ApprovalMode.Always)]
    public void Resolve_approves_read_risk_without_requiring_approval(ApprovalMode mode)
    {
        IApprovalPolicy policy = ApprovalPolicyResolver.Resolve(mode);

        ApprovalDecision decision = policy.RequestApproval(CreateRequest(ToolRiskLevel.Read));

        Assert.True(decision.Approved);
        Assert.Equal("not-required", decision.Status);
        Assert.Equal("Approval is not required for read-only operations.", decision.SafeMessage);
    }

    [Theory]
    [InlineData(ToolRiskLevel.Write)]
    [InlineData(ToolRiskLevel.Shell)]
    public void Always_mode_approves_non_dangerous_approval_required_risks(ToolRiskLevel riskLevel)
    {
        IApprovalPolicy policy = ApprovalPolicyResolver.Resolve(ApprovalMode.Always);

        ApprovalDecision decision = policy.RequestApproval(CreateRequest(riskLevel));

        Assert.True(decision.Approved);
        Assert.Equal("approved", decision.Status);
        Assert.Equal("Approved by approval mode.", decision.SafeMessage);
    }

    [Theory]
    [InlineData(ApprovalMode.Never)]
    [InlineData(ApprovalMode.OnRequest)]
    [InlineData(ApprovalMode.OnFailure)]
    [InlineData(ApprovalMode.Always)]
    public void Resolve_denies_dangerous_shell_for_every_mode(ApprovalMode mode)
    {
        IApprovalPolicy policy = ApprovalPolicyResolver.Resolve(mode);

        ApprovalDecision decision = policy.RequestApproval(CreateRequest(ToolRiskLevel.DangerousShell));

        Assert.False(decision.Approved);
        Assert.Equal("dangerous-shell-denied", decision.Status);
        Assert.Equal("Dangerous shell commands are denied by approval policy.", decision.SafeMessage);
    }

    [Theory]
    [InlineData(ToolRiskLevel.Write)]
    [InlineData(ToolRiskLevel.Shell)]
    public void Never_mode_denies_approval_required_risks(ToolRiskLevel riskLevel)
    {
        IApprovalPolicy policy = ApprovalPolicyResolver.Resolve(ApprovalMode.Never);

        ApprovalDecision decision = policy.RequestApproval(CreateRequest(riskLevel));

        Assert.False(decision.Approved);
        Assert.Equal("denied", decision.Status);
        Assert.Equal("Approval is disabled by policy.", decision.SafeMessage);
    }

    [Theory]
    [InlineData(ApprovalMode.OnRequest, ToolRiskLevel.Write, "Approval is required, but this CLI cannot request interactive approval.")]
    [InlineData(ApprovalMode.OnRequest, ToolRiskLevel.Shell, "Approval is required, but this CLI cannot request interactive approval.")]
    [InlineData(ApprovalMode.OnFailure, ToolRiskLevel.Write, "Approval after failure is not available because sandbox retry escalation is not implemented.")]
    [InlineData(ApprovalMode.OnFailure, ToolRiskLevel.Shell, "Approval after failure is not available because sandbox retry escalation is not implemented.")]
    public void Interactive_escalation_modes_report_approval_required_in_non_interactive_cli(
        ApprovalMode mode,
        ToolRiskLevel riskLevel,
        string expectedMessage)
    {
        IApprovalPolicy policy = ApprovalPolicyResolver.Resolve(mode);

        ApprovalDecision decision = policy.RequestApproval(CreateRequest(riskLevel));

        Assert.False(decision.Approved);
        Assert.Equal("approval-required", decision.Status);
        Assert.Equal(expectedMessage, decision.SafeMessage);
    }

    [Fact]
    public void Approval_request_defaults_to_write_risk_for_backward_compatibility()
    {
        ApprovalRequest request = new(
            Operation: "workspace.apply_patch",
            Summary: "Apply a patch.",
            Diff: null,
            IsDirtyWorkspace: false);

        Assert.Equal(ToolRiskLevel.Write, request.RiskLevel);
    }

    [Fact]
    public void Resolve_uses_cli_override_mode_when_supplied()
    {
        IApprovalPolicy policy = ApprovalPolicyResolver.Resolve(
            effectiveMode: ApprovalMode.Never,
            cliOverrideMode: ApprovalMode.Always);

        ApprovalDecision decision = policy.RequestApproval(CreateRequest(ToolRiskLevel.Write));

        Assert.True(decision.Approved);
        Assert.Equal("approved", decision.Status);
    }

    [Fact]
    public void Resolve_uses_effective_mode_when_cli_override_is_not_supplied()
    {
        IApprovalPolicy policy = ApprovalPolicyResolver.Resolve(
            effectiveMode: ApprovalMode.Never,
            cliOverrideMode: null);

        ApprovalDecision decision = policy.RequestApproval(CreateRequest(ToolRiskLevel.Write));

        Assert.False(decision.Approved);
        Assert.Equal("denied", decision.Status);
    }

    [Fact]
    public void Decisions_do_not_echo_raw_request_context()
    {
        IApprovalPolicy policy = ApprovalPolicyResolver.Resolve(ApprovalMode.OnRequest);
        ApprovalRequest request = new(
            Operation: "workspace.run_shell",
            Summary: "Run rm -rf C:\\sensitive-path",
            Diff: null,
            IsDirtyWorkspace: false,
            Metadata: new Dictionary<string, string>
            {
                ["command"] = "rm -rf C:\\sensitive-path"
            },
            RiskLevel: ToolRiskLevel.Shell);

        ApprovalDecision decision = policy.RequestApproval(request);

        Assert.DoesNotContain("rm -rf", decision.SafeMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive-path", decision.SafeMessage, StringComparison.Ordinal);
    }

    private static ApprovalRequest CreateRequest(ToolRiskLevel riskLevel)
    {
        return new ApprovalRequest(
            Operation: "test.operation",
            Summary: "Request approval.",
            Diff: null,
            IsDirtyWorkspace: false,
            RiskLevel: riskLevel);
    }
}
