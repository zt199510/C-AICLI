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
        AgentFailureSummary? FailureSummary = null)
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

    public static ExecResult Success(
        string? Summary,
        IReadOnlyList<ExecEvent> Events,
        string? ApprovalStatus = null,
        string? StopReason = null,
        IReadOnlyList<ChangedFileSummary>? ChangedFiles = null,
        IReadOnlyList<VerificationResultSummary>? VerificationResults = null,
        IReadOnlyList<AgentRetryAttempt>? RetryAttempts = null)
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
            RetryAttempts);
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
        AgentFailureSummary? FailureSummary = null)
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
            FailureSummary);
    }
}
