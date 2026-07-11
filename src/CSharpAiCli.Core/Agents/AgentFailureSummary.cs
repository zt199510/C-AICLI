using System.Collections.ObjectModel;

namespace CSharpAiCli.Core;

public sealed record AgentFailureSummary
{
    public AgentFailureSummary(
        string FailureKind,
        string StopReason,
        string? ErrorCode,
        string Message,
        int RetryBudget,
        int RetryCount,
        int RemainingRetries,
        string RemainingRisk,
        IReadOnlyList<AgentRetryAttempt>? RetryAttempts = null,
        IReadOnlyList<string>? Commands = null,
        IReadOnlyList<string>? ChangedFiles = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(FailureKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(StopReason);

        if (RetryBudget < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(RetryBudget));
        }

        if (RetryCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(RetryCount));
        }

        if (RemainingRetries < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(RemainingRetries));
        }

        this.FailureKind = FailureKind;
        this.StopReason = StopReason;
        this.ErrorCode = ErrorCode;
        this.Message = Message ?? string.Empty;
        this.RetryBudget = RetryBudget;
        this.RetryCount = RetryCount;
        this.RemainingRetries = RemainingRetries;
        this.RemainingRisk = RemainingRisk ?? string.Empty;
        this.RetryAttempts = new ReadOnlyCollection<AgentRetryAttempt>((RetryAttempts ?? []).ToArray());
        this.Commands = new ReadOnlyCollection<string>((Commands ?? []).ToArray());
        this.ChangedFiles = new ReadOnlyCollection<string>((ChangedFiles ?? []).ToArray());
    }

    public string FailureKind { get; }

    public string StopReason { get; }

    public string? ErrorCode { get; }

    public string Message { get; }

    public int RetryBudget { get; }

    public int RetryCount { get; }

    public int RemainingRetries { get; }

    public string RemainingRisk { get; }

    public IReadOnlyList<AgentRetryAttempt> RetryAttempts { get; }

    public IReadOnlyList<string> Commands { get; }

    public IReadOnlyList<string> ChangedFiles { get; }
}
