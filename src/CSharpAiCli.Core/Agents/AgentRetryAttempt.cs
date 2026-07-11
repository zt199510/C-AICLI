using System.Collections.ObjectModel;

namespace CSharpAiCli.Core;

public sealed record AgentRetryAttempt
{
    public AgentRetryAttempt(
        int Index,
        string FailureKind,
        string StopReason,
        string? ErrorCode,
        string? SourceToolCallId,
        string? ToolName,
        string? Summary,
        IReadOnlyList<string>? Commands = null,
        IReadOnlyList<string>? ChangedFiles = null,
        string? VerificationStatus = null)
    {
        if (Index < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(Index), "Retry attempt index must be greater than zero.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(FailureKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(StopReason);

        this.Index = Index;
        this.FailureKind = FailureKind;
        this.StopReason = StopReason;
        this.ErrorCode = ErrorCode;
        this.SourceToolCallId = SourceToolCallId;
        this.ToolName = ToolName;
        this.Summary = Summary;
        this.Commands = new ReadOnlyCollection<string>((Commands ?? []).ToArray());
        this.ChangedFiles = new ReadOnlyCollection<string>((ChangedFiles ?? []).ToArray());
        this.VerificationStatus = VerificationStatus;
    }

    public int Index { get; }

    public string FailureKind { get; }

    public string StopReason { get; }

    public string? ErrorCode { get; }

    public string? SourceToolCallId { get; }

    public string? ToolName { get; }

    public string? Summary { get; }

    public IReadOnlyList<string> Commands { get; }

    public IReadOnlyList<string> ChangedFiles { get; }

    public string? VerificationStatus { get; }
}
