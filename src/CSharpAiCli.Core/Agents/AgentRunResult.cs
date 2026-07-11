using System.Collections.ObjectModel;

namespace CSharpAiCli.Core;

public sealed record AgentRunResult
{
    public AgentRunResult(
        string? Text,
        IReadOnlyList<ConversationToolCall> ToolCalls,
        AgentError? Error,
        IReadOnlyList<AgentRunEvent>? Events = null,
        string? Status = null,
        string? StopReason = null,
        IReadOnlyList<AgentStep>? Steps = null,
        IReadOnlyList<ChangedFileSummary>? ChangedFiles = null,
        IReadOnlyList<VerificationResultSummary>? VerificationResults = null,
        IReadOnlyList<AgentRetryAttempt>? RetryAttempts = null,
        AgentFailureSummary? FailureSummary = null)
    {
        ArgumentNullException.ThrowIfNull(ToolCalls);

        this.Text = Text;
        this.ToolCalls = new ReadOnlyCollection<ConversationToolCall>(ToolCalls.ToArray());
        this.Error = Error;
        this.Events = new ReadOnlyCollection<AgentRunEvent>((Events ?? []).ToArray());
        this.Status = string.IsNullOrWhiteSpace(Status)
            ? Error is null ? DiagnosticEventStatus.Success : DiagnosticEventStatus.Failure
            : Status;
        this.StopReason = string.IsNullOrWhiteSpace(StopReason)
            ? Error is null ? AgentStopReason.Completed : AgentStopReason.FromErrorCode(Error.LocalErrorCode)
            : StopReason;
        this.Steps = new ReadOnlyCollection<AgentStep>((Steps ?? []).ToArray());
        this.ChangedFiles = new ReadOnlyCollection<ChangedFileSummary>((ChangedFiles ?? []).ToArray());
        this.VerificationResults = new ReadOnlyCollection<VerificationResultSummary>((VerificationResults ?? []).ToArray());
        this.RetryAttempts = new ReadOnlyCollection<AgentRetryAttempt>((RetryAttempts ?? []).ToArray());
        this.FailureSummary = FailureSummary;
    }

    public string? Text { get; }

    public IReadOnlyList<ConversationToolCall> ToolCalls { get; }

    public AgentError? Error { get; }

    public IReadOnlyList<AgentRunEvent> Events { get; }

    public string Status { get; }

    public string StopReason { get; }

    public IReadOnlyList<AgentStep> Steps { get; }

    public IReadOnlyList<ChangedFileSummary> ChangedFiles { get; }

    public IReadOnlyList<VerificationResultSummary> VerificationResults { get; }

    public IReadOnlyList<AgentRetryAttempt> RetryAttempts { get; }

    public AgentFailureSummary? FailureSummary { get; }

    public bool IsSuccess => Error is null;

    public static AgentRunResult Success(
        string text,
        IReadOnlyList<ConversationToolCall> toolCalls,
        IReadOnlyList<AgentRunEvent>? events = null,
        IReadOnlyList<AgentStep>? steps = null,
        IReadOnlyList<ChangedFileSummary>? changedFiles = null,
        IReadOnlyList<VerificationResultSummary>? verificationResults = null,
        IReadOnlyList<AgentRetryAttempt>? retryAttempts = null,
        string stopReason = AgentStopReason.Completed)
    {
        events ??= [];
        return new AgentRunResult(
            text,
            toolCalls,
            Error: null,
            Events: events,
            Status: DiagnosticEventStatus.Success,
            StopReason: stopReason,
            Steps: steps,
            ChangedFiles: changedFiles,
            VerificationResults: verificationResults,
            RetryAttempts: retryAttempts);
    }

    public static AgentRunResult Failure(
        AgentError error,
        IReadOnlyList<ConversationToolCall> toolCalls,
        IReadOnlyList<AgentRunEvent>? events = null,
        IReadOnlyList<AgentStep>? steps = null,
        IReadOnlyList<ChangedFileSummary>? changedFiles = null,
        IReadOnlyList<VerificationResultSummary>? verificationResults = null,
        IReadOnlyList<AgentRetryAttempt>? retryAttempts = null,
        AgentFailureSummary? failureSummary = null,
        string? stopReason = null,
        string status = DiagnosticEventStatus.Failure)
    {
        ArgumentNullException.ThrowIfNull(error);
        events ??= [];
        return new AgentRunResult(
            Text: null,
            toolCalls,
            error,
            Events: events,
            Status: status,
            StopReason: stopReason ?? AgentStopReason.FromErrorCode(error.LocalErrorCode),
            Steps: steps,
            ChangedFiles: changedFiles,
            VerificationResults: verificationResults,
            RetryAttempts: retryAttempts,
            FailureSummary: failureSummary);
    }
}
