using System.Collections.ObjectModel;

namespace CSharpAiCli.Core;

public sealed record ConversationAgentRun
{
    public ConversationAgentRun(
        DateTimeOffset CompletedAtUtc,
        string Status,
        string StopReason,
        string? ErrorCode,
        string? Summary,
        int EventCount,
        int ToolCallCount,
        string? PlanSummary = null,
        IReadOnlyList<ChangedFileSummary>? ChangedFiles = null,
        IReadOnlyList<VerificationResultSummary>? VerificationResults = null,
        IReadOnlyList<AgentRetryAttempt>? RetryAttempts = null,
        AgentFailureSummary? FailureSummary = null,
        AgentTaskReport? TaskReport = null)
    {
        this.CompletedAtUtc = CompletedAtUtc;
        this.Status = Status;
        this.StopReason = StopReason;
        this.ErrorCode = ErrorCode;
        this.Summary = Summary;
        this.EventCount = EventCount;
        this.ToolCallCount = ToolCallCount;
        this.PlanSummary = PlanSummary;
        this.ChangedFiles = new ReadOnlyCollection<ChangedFileSummary>((ChangedFiles ?? []).ToArray());
        this.VerificationResults = new ReadOnlyCollection<VerificationResultSummary>((VerificationResults ?? []).ToArray());
        this.RetryAttempts = new ReadOnlyCollection<AgentRetryAttempt>((RetryAttempts ?? []).ToArray());
        this.FailureSummary = FailureSummary;
        this.TaskReport = TaskReport;
    }

    public DateTimeOffset CompletedAtUtc { get; }

    public string Status { get; }

    public string StopReason { get; }

    public string? ErrorCode { get; }

    public string? Summary { get; }

    public int EventCount { get; }

    public int ToolCallCount { get; }

    public string? PlanSummary { get; }

    public IReadOnlyList<ChangedFileSummary> ChangedFiles { get; }

    public IReadOnlyList<VerificationResultSummary> VerificationResults { get; }

    public IReadOnlyList<AgentRetryAttempt> RetryAttempts { get; }

    public AgentFailureSummary? FailureSummary { get; }

    public AgentTaskReport? TaskReport { get; }

    public static ConversationAgentRun FromAgentResult(
        AgentRunResult result,
        DateTimeOffset completedAtUtc,
        AgentTaskReport? taskReport = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new ConversationAgentRun(
            CompletedAtUtc: completedAtUtc,
            Status: result.Status,
            StopReason: result.StopReason,
            ErrorCode: result.Error?.LocalErrorCode,
            Summary: result.IsSuccess ? result.Text : result.Error?.SafeMessage,
            EventCount: result.Events.Count,
            ToolCallCount: result.ToolCalls.Count,
            PlanSummary: FindPlanSummary(result.Events),
            ChangedFiles: result.ChangedFiles,
            VerificationResults: result.VerificationResults,
            RetryAttempts: result.RetryAttempts,
            FailureSummary: result.FailureSummary,
            TaskReport: taskReport);
    }

    private static string? FindPlanSummary(IReadOnlyList<AgentRunEvent> events)
    {
        return events.FirstOrDefault(agentEvent =>
            string.Equals(agentEvent.Type, "plan", StringComparison.Ordinal))?.Summary;
    }
}
