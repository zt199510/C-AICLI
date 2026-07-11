namespace CSharpAiCli.Core;

public static class AgentRetryPolicy
{
    public static AgentRetryDecision Decide(
        AgentRetryDecisionInput input,
        int remainingRetries,
        bool retryDisabled = false)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!IsEligibleForRetry(input, out string reason))
        {
            return new AgentRetryDecision(
                ShouldRetry: false,
                BudgetExhausted: false,
                input.FailureKind,
                input.StopReason,
                input.ErrorCode,
                reason,
                remainingRetries);
        }

        if (retryDisabled)
        {
            return new AgentRetryDecision(
                ShouldRetry: false,
                BudgetExhausted: false,
                input.FailureKind,
                input.StopReason,
                input.ErrorCode,
                "Retry budget is disabled.",
                remainingRetries);
        }

        if (remainingRetries <= 0)
        {
            return new AgentRetryDecision(
                ShouldRetry: false,
                BudgetExhausted: true,
                input.FailureKind,
                input.StopReason,
                input.ErrorCode,
                "Retry budget exhausted.",
                remainingRetries);
        }

        return new AgentRetryDecision(
            ShouldRetry: true,
            BudgetExhausted: false,
            input.FailureKind,
            input.StopReason,
            input.ErrorCode,
            "Failure can be fed back to the model within the retry budget.",
            remainingRetries - 1);
    }

    private static bool IsEligibleForRetry(AgentRetryDecisionInput input, out string reason)
    {
        if (IsApprovalDenied(input))
        {
            reason = "Approval and policy denials are terminal for automatic retry.";
            return false;
        }

        if (IsGuardOrPolicyDenied(input.ErrorCode))
        {
            reason = "Workspace guard, shell policy, disabled tool, and dangerous command denials are terminal.";
            return false;
        }

        if (input.FailureKind is AgentFailureKind.Model or AgentFailureKind.Budget)
        {
            reason = "Model and budget failures are not retried by the agent loop.";
            return false;
        }

        if (input.FailureKind is AgentFailureKind.Tool
            or AgentFailureKind.Patch
            or AgentFailureKind.Shell
            or AgentFailureKind.Verification)
        {
            reason = string.Empty;
            return true;
        }

        if (input.Retryable)
        {
            reason = string.Empty;
            return true;
        }

        reason = "Failure is not model-correctable.";
        return false;
    }

    private static bool IsApprovalDenied(AgentRetryDecisionInput input)
    {
        if (string.Equals(input.ErrorCode, ToolErrorCode.ApprovalDenied, StringComparison.Ordinal))
        {
            return true;
        }

        return input.ApprovalStatus is "denied" or "approval-required" or "dangerous-shell-denied";
    }

    private static bool IsGuardOrPolicyDenied(string? errorCode)
    {
        return errorCode is ToolErrorCode.ToolDisabled
            or ToolErrorCode.WorkspaceUnavailable
            or ToolErrorCode.WorkspaceBoundaryDenied
            or ToolErrorCode.ShellPolicyDenied
            or ToolErrorCode.DangerousCommandDenied
            or ToolErrorCode.PlanningPhaseWriteDenied;
    }
}
