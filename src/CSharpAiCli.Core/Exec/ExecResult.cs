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
        IReadOnlyList<ExecEvent> Events)
    {
        this.IsSuccess = IsSuccess;
        this.ExitCode = ExitCode;
        this.Summary = Summary;
        this.ErrorCode = ErrorCode;
        this.ApprovalStatus = ApprovalStatus;
        this.StopReason = StopReason;
        this.Events = new ReadOnlyCollection<ExecEvent>(Events.ToArray());
    }

    public bool IsSuccess { get; }

    public int ExitCode { get; }

    public string? Summary { get; }

    public string? ErrorCode { get; }

    public string? ApprovalStatus { get; }

    public string? StopReason { get; }

    public IReadOnlyList<ExecEvent> Events { get; }

    public static ExecResult Success(
        string? Summary,
        IReadOnlyList<ExecEvent> Events,
        string? ApprovalStatus = null,
        string? StopReason = null)
    {
        ArgumentNullException.ThrowIfNull(Events);

        return new ExecResult(
            IsSuccess: true,
            ExitCode: 0,
            Summary,
            ErrorCode: null,
            ApprovalStatus,
            StopReason,
            Events);
    }

    public static ExecResult Failure(
        int ExitCode,
        string? Summary,
        string ErrorCode,
        IReadOnlyList<ExecEvent> Events,
        string? ApprovalStatus = null,
        string? StopReason = null)
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
            Events);
    }
}
