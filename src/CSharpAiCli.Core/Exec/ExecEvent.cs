using System.Collections.ObjectModel;

namespace CSharpAiCli.Core;

public sealed record ExecEvent
{
    public ExecEvent(
        string Type,
        long Sequence,
        DateTimeOffset Timestamp,
        string? Message = null,
        string? Summary = null,
        IReadOnlyDictionary<string, string>? Payload = null,
        string? ErrorCode = null,
        string? ApprovalStatus = null)
    {
        if (string.IsNullOrWhiteSpace(Type))
        {
            throw new ArgumentException("Type is required.", nameof(Type));
        }

        if (Sequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Sequence));
        }

        this.Type = Type;
        this.Sequence = Sequence;
        this.Timestamp = Timestamp;
        this.Message = Message;
        this.Summary = Summary;
        this.Payload = Payload is null
            ? null
            : new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(Payload));
        this.ErrorCode = ErrorCode;
        this.ApprovalStatus = ApprovalStatus;
    }

    public string Type { get; }

    public long Sequence { get; }

    public DateTimeOffset Timestamp { get; }

    public string? Message { get; }

    public string? Summary { get; }

    public IReadOnlyDictionary<string, string>? Payload { get; }

    public string? ErrorCode { get; }

    public string? ApprovalStatus { get; }
}
