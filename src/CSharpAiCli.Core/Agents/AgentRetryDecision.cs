using System.Text.Json;

namespace CSharpAiCli.Core;

public sealed record AgentRetryDecisionInput(
    string FailureKind,
    string StopReason,
    string? ErrorCode,
    string Summary,
    bool Retryable,
    string ApprovalStatus,
    string? SourceToolCallId = null,
    string? ToolName = null,
    IReadOnlyDictionary<string, JsonElement>? StructuredPayload = null,
    VerificationResultSummary? Verification = null);

public sealed record AgentRetryDecision(
    bool ShouldRetry,
    bool BudgetExhausted,
    string FailureKind,
    string StopReason,
    string? ErrorCode,
    string Reason,
    int RemainingRetries);
