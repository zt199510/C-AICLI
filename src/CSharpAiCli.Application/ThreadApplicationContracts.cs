using System.Collections.ObjectModel;
using CSharpAiCli.Core;

namespace CSharpAiCli.Application;

public sealed record ThreadCreateRequest(CliEnvironmentSnapshot Snapshot, string Title);

public sealed record ThreadListRequest(
    CliEnvironmentSnapshot Snapshot,
    string WorkspaceId,
    int PageSize = ApplicationLimits.DefaultPageSize);

public sealed record ThreadGetRequest(
    CliEnvironmentSnapshot Snapshot,
    string ThreadId,
    long AfterSequence = 0,
    int TimelinePageSize = ApplicationLimits.DefaultPageSize);

public sealed record ThreadRenameRequest(
    CliEnvironmentSnapshot Snapshot,
    string ThreadId,
    long ExpectedRevision,
    string Title);

public sealed record ThreadArchiveRequest(
    CliEnvironmentSnapshot Snapshot,
    string ThreadId,
    long ExpectedRevision);

public sealed record ThreadDeleteRequest(
    CliEnvironmentSnapshot Snapshot,
    string ThreadId,
    long ExpectedRevision,
    string Confirmation);

public sealed record SessionImportPreviewRequest(CliEnvironmentSnapshot Snapshot, string SessionName);

public sealed record SessionImportRequest(
    CliEnvironmentSnapshot Snapshot,
    string SessionName,
    string PreviewFingerprint);

public sealed record ThreadOriginProjection(
    string Kind,
    string? SourceKind,
    string? SourceId,
    string? SourceFingerprint);

public sealed record ThreadSummaryProjection(
    string ThreadId,
    long Revision,
    string WorkspaceId,
    string Title,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ArchivedAtUtc,
    int TurnCount,
    int TimelineItemCount,
    string? ActiveTurnId,
    ThreadOriginProjection Origin);

public sealed record ThreadListProjection
{
    public ThreadListProjection(IReadOnlyList<ThreadSummaryProjection>? Threads, bool Truncated)
    {
        this.Threads = new ReadOnlyCollection<ThreadSummaryProjection>((Threads ?? []).ToArray());
        this.Truncated = Truncated;
    }

    public IReadOnlyList<ThreadSummaryProjection> Threads { get; }
    public bool Truncated { get; }
}

public sealed record ThreadSourcePointerProjection(
    string Kind,
    string SourceId,
    string? SourceRevision,
    string? SourceFingerprint,
    string Availability);

public sealed record TurnSummaryProjection(
    string TurnId,
    int Ordinal,
    long Revision,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string TaskSummary,
    string? StopReason,
    string? ErrorCode,
    IReadOnlyList<ThreadSourcePointerProjection> SourcePointers,
    long? TimelineFirstSequence,
    long? TimelineLastSequence,
    int TimelineItemCount,
    bool RecoveryRequired,
    DurableApprovalProjection? Approval);

public sealed record TimelinePayloadProjection(
    string Kind,
    string? Text = null,
    string? Name = null,
    bool? Succeeded = null,
    string? ErrorCode = null,
    int? Count = null,
    string? ReferenceId = null,
    string? StopReason = null);

public sealed record TimelineItemProjection(
    string ItemId,
    string TurnId,
    long Sequence,
    DateTimeOffset TimestampUtc,
    string Type,
    ThreadSourcePointerProjection? Source,
    string Status,
    string Summary,
    TimelinePayloadProjection Payload,
    bool Redacted);

public sealed record ThreadDetailProjection(
    ThreadSummaryProjection Thread,
    IReadOnlyList<TurnSummaryProjection> Turns,
    IReadOnlyList<TimelineItemProjection> Timeline,
    long? NextSequence,
    bool TimelineTruncated,
    bool RecoveryRequired);

public sealed record ThreadDeleteProjection(string ThreadId, bool Deleted);

public sealed record SessionImportPreviewProjection(
    string ThreadId,
    string SourceKind,
    string SourceId,
    string Fingerprint,
    string Title,
    int TurnCount,
    int TimelineItemCount,
    long SourceByteCount,
    int SourceRecordCount,
    IReadOnlyList<string> Warnings,
    bool Truncated);

public sealed record SessionImportProjection(
    ThreadSummaryProjection Thread,
    string SourceId,
    string Fingerprint,
    bool Idempotent);
