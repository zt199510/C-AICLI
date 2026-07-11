namespace CSharpAiCli.Core;

public static class AgentStopReason
{
    public const string Completed = "completed";
    public const string EmptyTurn = "empty-turn";
    public const string MaxStepsExceeded = "max-steps-exceeded";
    public const string MaxToolCallsExceeded = "max-tool-calls-exceeded";
    public const string OverallTimeout = "overall-timeout";
    public const string ModelTimeout = "model-timeout";
    public const string ToolTimeout = "tool-timeout";
    public const string ToolFailure = "tool-failure";
    public const string VerificationFailure = "verification-failure";
    public const string RetryBudgetExhausted = "retry-budget-exhausted";
    public const string ToolDisabled = "tool-disabled";
    public const string ApprovalDenied = "approval-denied";
    public const string ModelError = "model-error";
    public const string BackendUnavailable = "backend-unavailable";
    public const string Canceled = "canceled";

    public static string FromErrorCode(string? errorCode)
    {
        return errorCode switch
        {
            "agent-loop-limit-reached" => MaxStepsExceeded,
            "agent-tool-call-limit-reached" => MaxToolCallsExceeded,
            "agent-retry-budget-exhausted" => RetryBudgetExhausted,
            "agent-overall-timeout-reached" => OverallTimeout,
            "agent-model-call-timeout-reached" => ModelTimeout,
            "agent-model-call-canceled" => ModelTimeout,
            ToolErrorCode.ApprovalDenied => ApprovalDenied,
            ToolErrorCode.ToolDisabled => ToolDisabled,
            ToolErrorCode.UnknownTool => ToolFailure,
            "agent-backend-unavailable" => BackendUnavailable,
            "missing-model" => BackendUnavailable,
            "missing-openai-api-key" => BackendUnavailable,
            "unsupported-api-key-source" => BackendUnavailable,
            "openai-http-error" => ModelError,
            "openai-client-error" => ModelError,
            "agent-framework-unavailable" => BackendUnavailable,
            "agent-empty-turn" => EmptyTurn,
            null or "" => ModelError,
            _ => ToolFailure
        };
    }

    public static string FromToolResult(ToolExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Succeeded)
        {
            return Completed;
        }

        return result.ErrorCode switch
        {
            ToolErrorCode.ApprovalDenied => ApprovalDenied,
            ToolErrorCode.ToolDisabled => ToolDisabled,
            ToolErrorCode.ShellTimeout => ToolTimeout,
            ToolErrorCode.GitTimeout => ToolTimeout,
            _ => ToolFailure
        };
    }
}
