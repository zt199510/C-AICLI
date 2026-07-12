using System.Collections.ObjectModel;

namespace CSharpAiCli.Core;

public sealed record ExecResult
{
    private ExecResult(
        bool IsSuccess,
        int ExitCode,
        string? Summary,
        string? ErrorCode,
        string? ApprovalStatus,
        string? StopReason,
        IReadOnlyList<ExecEvent> Events,
        IReadOnlyList<ChangedFileSummary>? ChangedFiles = null,
        IReadOnlyList<VerificationResultSummary>? VerificationResults = null,
        IReadOnlyList<AgentRetryAttempt>? RetryAttempts = null,
        AgentFailureSummary? FailureSummary = null,
        AgentTaskReport? TaskReport = null)
    {
        this.IsSuccess = IsSuccess;
        this.ExitCode = ExitCode;
        this.Summary = Summary;
        this.ErrorCode = ErrorCode;
        this.ApprovalStatus = ApprovalStatus;
        this.StopReason = StopReason;
        this.Events = new ReadOnlyCollection<ExecEvent>(Events.ToArray());
        this.ChangedFiles = new ReadOnlyCollection<ChangedFileSummary>((ChangedFiles ?? []).ToArray());
        this.VerificationResults = new ReadOnlyCollection<VerificationResultSummary>((VerificationResults ?? []).ToArray());
        this.RetryAttempts = new ReadOnlyCollection<AgentRetryAttempt>((RetryAttempts ?? []).ToArray());
        this.FailureSummary = FailureSummary;
        this.TaskReport = TaskReport;
    }

    public bool IsSuccess { get; }

    public int ExitCode { get; }

    public string? Summary { get; }

    public string? ErrorCode { get; }

    public string? ApprovalStatus { get; }

    public string? StopReason { get; }

    public IReadOnlyList<ExecEvent> Events { get; }

    public IReadOnlyList<ChangedFileSummary> ChangedFiles { get; }

    public IReadOnlyList<VerificationResultSummary> VerificationResults { get; }

    public IReadOnlyList<AgentRetryAttempt> RetryAttempts { get; }

    public AgentFailureSummary? FailureSummary { get; }

    public AgentTaskReport? TaskReport { get; }

    public static ExecResult Success(
        string? Summary,
        IReadOnlyList<ExecEvent> Events,
        string? ApprovalStatus = null,
        string? StopReason = null,
        IReadOnlyList<ChangedFileSummary>? ChangedFiles = null,
        IReadOnlyList<VerificationResultSummary>? VerificationResults = null,
        IReadOnlyList<AgentRetryAttempt>? RetryAttempts = null,
        AgentTaskReport? TaskReport = null)
    {
        ArgumentNullException.ThrowIfNull(Events);

        return new ExecResult(
            IsSuccess: true,
            ExitCode: 0,
            Summary,
            ErrorCode: null,
            ApprovalStatus,
            StopReason,
            Events,
            ChangedFiles,
            VerificationResults,
            RetryAttempts,
            TaskReport: TaskReport);
    }

    public static ExecResult Failure(
        int ExitCode,
        string? Summary,
        string ErrorCode,
        IReadOnlyList<ExecEvent> Events,
        string? ApprovalStatus = null,
        string? StopReason = null,
        IReadOnlyList<ChangedFileSummary>? ChangedFiles = null,
        IReadOnlyList<VerificationResultSummary>? VerificationResults = null,
        IReadOnlyList<AgentRetryAttempt>? RetryAttempts = null,
        AgentFailureSummary? FailureSummary = null,
        AgentTaskReport? TaskReport = null)
    {
        if (ExitCode == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ExitCode));
        }

        if (string.IsNullOrWhiteSpace(ErrorCode))
        {
            throw new ArgumentException("ErrorCode is required.", nameof(ErrorCode));
        }

        ArgumentNullException.ThrowIfNull(Events);

        return new ExecResult(
            IsSuccess: false,
            ExitCode,
            Summary,
            ErrorCode,
            ApprovalStatus,
            StopReason,
            Events,
            ChangedFiles,
            VerificationResults,
            RetryAttempts,
            FailureSummary,
            TaskReport);
    }

    public ExecResult WithTaskReport(
        AgentTaskReport taskReport,
        DateTimeOffset timestampUtc,
        IReadOnlyList<ExecEvent>? additionalEvents = null)
    {
        ArgumentNullException.ThrowIfNull(taskReport);

        List<ExecEvent> events = Events.ToList();
        if (additionalEvents is not null)
        {
            events.AddRange(additionalEvents);
        }

        long sequence = events.Count == 0
            ? 0
            : events[^1].Sequence + 1;
        AgentRunEvent taskReportEvent = AgentTaskReportBuilder.CreateTaskReportEvent(
            taskReport,
            sequence,
            timestampUtc);
        events.Add(new ExecEvent(
            Type: taskReportEvent.Type,
            Sequence: taskReportEvent.Sequence,
            Timestamp: taskReportEvent.Timestamp,
            Message: taskReportEvent.Message,
            Summary: taskReportEvent.Summary,
            Payload: taskReportEvent.Payload,
            ErrorCode: taskReportEvent.ErrorCode,
            ApprovalStatus: taskReportEvent.ApprovalStatus,
            Status: taskReportEvent.Status,
            DurationMs: taskReportEvent.DurationMs,
            ApprovalDurationMs: taskReportEvent.ApprovalDurationMs,
            StepIndex: taskReportEvent.StepIndex,
            StopReason: taskReportEvent.StopReason));

        return new ExecResult(
            IsSuccess,
            ExitCode,
            Summary,
            ErrorCode,
            ApprovalStatus,
            StopReason,
            events,
            ChangedFiles,
            VerificationResults,
            RetryAttempts,
            FailureSummary,
            taskReport);
    }
}
