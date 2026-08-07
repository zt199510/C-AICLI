using System.Security.Cryptography;
using System.Text;

namespace CSharpAiCli.Core;

public static class ThreadErrorCode
{
    public const string ThreadIdInvalid = "thread-id-invalid";
    public const string TurnIdInvalid = "turn-id-invalid";
    public const string ItemIdInvalid = "timeline-item-id-invalid";
    public const string TimelineSequenceInvalid = "timeline-sequence-invalid";
    public const string ThreadTitleInvalid = "thread-title-invalid";
    public const string ThreadConfirmationInvalid = "thread-confirmation-invalid";
    public const string ThreadNotFound = "thread-not-found";
    public const string TurnNotFound = "turn-not-found";
    public const string ThreadRevisionConflict = "thread-revision-conflict";
    public const string ThreadActiveTurnConflict = "thread-active-turn-conflict";
    public const string TimelineAppendConflict = "timeline-append-conflict";
    public const string SessionImportSourceChanged = "session-import-source-changed";
    public const string ThreadAlreadyExists = "thread-already-exists";
    public const string ThreadPathUnsafe = "thread-path-unsafe";
    public const string ThreadReparsePoint = "thread-reparse-point";
    public const string ThreadDeleteNotArchived = "thread-delete-not-archived";
    public const string ThreadArchiveActive = "thread-archive-active";
    public const string ThreadRecordCorrupt = "thread-record-corrupt";
    public const string ThreadSchemaUnsupported = "thread-schema-unsupported";
    public const string TurnRecordCorrupt = "turn-record-corrupt";
    public const string TimelineRecordCorrupt = "timeline-record-corrupt";
    public const string ThreadReferenceMissing = "thread-reference-missing";
    public const string ThreadLimitExceeded = "thread-limit-exceeded";
    public const string TurnLimitExceeded = "turn-limit-exceeded";
    public const string TimelineLimitExceeded = "timeline-limit-exceeded";
    public const string SessionImportLimitExceeded = "session-import-limit-exceeded";
    public const string ThreadStoreUnavailable = "thread-store-unavailable";
    public const string ThreadWriteFailed = "thread-write-failed";
    public const string ThreadDeleteFailed = "thread-delete-failed";
    public const string RecoveryRequired = "recovery-required";
    public const string ThreadTransitionInvalid = "thread-transition-invalid";
    public const string TurnTransitionInvalid = "turn-transition-invalid";
}

public static class ThreadPersistenceLimits
{
    public const int MaxThreadManifestBytes = 1024 * 1024;
    public const int MaxTurnSnapshotBytes = 256 * 1024;
    public const int MaxTimelineItemBytes = 32 * 1024;
    public const int MaxTitleBytes = 512;
    public const int MaxTaskSummaryBytes = 4 * 1024;
    public const int MaxTimelineSummaryBytes = 16 * 1024;
    public const int MaxPointerValueBytes = 4 * 1024;
    public const int MaxTurnsPerThread = 1_000;
    public const int MaxTimelineItemsPerThread = 10_000;
    public const long MaxPersistedBytesPerThread = 64L * 1024 * 1024;
    public const int MaxPointersPerKind = 200;
    public const int MaxMutationReceipts = MaxTimelineItemsPerThread + MaxTurnsPerThread + 100;
    public const int MaxSessionImportBytes = 4 * 1024 * 1024;
    public const int MaxSessionImportRecords = 10_000;
}

public static class ThreadStatus
{
    public const string Idle = "idle";
    public const string Running = "running";
    public const string WaitingForApproval = "waiting-for-approval";
    public const string Canceling = "canceling";
    public const string Canceled = "canceled";
    public const string Failed = "failed";
    public const string Completed = "completed";
    public const string Archived = "archived";

    public static bool IsKnown(string? value) => value is Idle or Running or WaitingForApproval or Canceling or Canceled or Failed or Completed or Archived;
}

public static class TurnStatus
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string WaitingForApproval = "waiting-for-approval";
    public const string Canceling = "canceling";
    public const string Canceled = "canceled";
    public const string Failed = "failed";
    public const string Completed = "completed";

    public static bool IsKnown(string? value) => value is Queued or Running or WaitingForApproval or Canceling or Canceled or Failed or Completed;

    public static bool IsTerminal(string? value) => value is Canceled or Failed or Completed;

    public static bool IsActive(string? value) => value is Queued or Running or WaitingForApproval or Canceling;
}

public static class TimelineItemType
{
    public const string UserMessage = "user.message";
    public const string AssistantMessage = "assistant.message";
    public const string ProviderAttempt = "provider.attempt";
    public const string PlanUpdated = "plan.updated";
    public const string ToolStarted = "tool.started";
    public const string ToolCompleted = "tool.completed";
    public const string CommandStarted = "command.started";
    public const string CommandCompleted = "command.completed";
    public const string ApprovalRequested = "approval.requested";
    public const string ApprovalResolved = "approval.resolved";
    public const string VerificationCompleted = "verification.completed";
    public const string AssistantFinal = "assistant.final";
    public const string ChangesUpdated = "changes.updated";
    public const string ReportAvailable = "report.available";
    public const string ArtifactAvailable = "artifact.available";
    public const string WarningRaised = "warning.raised";
    public const string TurnCompleted = "turn.completed";

    public static bool IsKnown(string? value) => value is UserMessage or AssistantMessage or ProviderAttempt or PlanUpdated
        or ToolStarted or ToolCompleted or CommandStarted or CommandCompleted
        or ApprovalRequested or ApprovalResolved or VerificationCompleted or AssistantFinal or ChangesUpdated or ReportAvailable
        or ArtifactAvailable or WarningRaised or TurnCompleted;
}

public static class ThreadSourceKind
{
    public const string Session = "session";
    public const string Job = "job";
    public const string Queue = "queue";
    public const string Report = "report";
    public const string Trace = "trace";
    public const string Run = "run";
    public const string Artifact = "artifact";
    public const string ThreadMessage = "thread-message";

    public static bool IsKnown(string? value) => value is Session or Job or Queue or Report or Trace or Run or Artifact or ThreadMessage;
}

public static class ThreadSourceAvailability
{
    public const string Available = "available";
    public const string Missing = "missing";
    public const string Corrupt = "corrupt";
    public const string Stale = "stale";
    public const string Unknown = "unknown";

    public static bool IsKnown(string? value) => value is Available or Missing or Corrupt or Stale or Unknown;
}

public static class ThreadIdentity
{
    public static string CreateThreadId() => CreateRandom("thread");

    public static string CreateTurnId() => CreateRandom("turn");

    public static string CreateItemId() => CreateRandom("item");

    public static string CreateDeterministicThreadId(string sourceKind, string sourceIdentity, string fingerprint) =>
        CreateDeterministic("thread", sourceKind, sourceIdentity, fingerprint);

    public static string CreateDeterministicTurnId(string seed, int ordinal) =>
        CreateDeterministic("turn", seed, ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public static string CreateDeterministicItemId(string seed, string collectionKind, int sourceIndex) =>
        CreateDeterministic("item", seed, collectionKind, sourceIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public static bool IsThreadId(string? value) => IsTypedId(value, "thread");

    public static bool IsTurnId(string? value) => IsTypedId(value, "turn");

    public static bool IsItemId(string? value) => IsTypedId(value, "item");

    private static string CreateRandom(string prefix) => $"{prefix}_{Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant()}";

    private static string CreateDeterministic(string prefix, params string[] parts)
    {
        string canonical = string.Join('\0', parts.Select(part => part ?? string.Empty));
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return $"{prefix}_{Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant()}";
    }

    private static bool IsTypedId(string? value, string prefix)
    {
        if (value is null || value.Length != prefix.Length + 25 || !value.StartsWith(prefix + "_", StringComparison.Ordinal))
        {
            return false;
        }

        return value.AsSpan(prefix.Length + 1).IndexOfAnyExcept("0123456789abcdef") < 0;
    }
}

public sealed record ThreadSourcePointerRecord
{
    public string Kind { get; init; } = string.Empty;
    public string SourceId { get; init; } = string.Empty;
    public string? SourceRevision { get; init; }
    public string? SourceFingerprint { get; init; }
    public string Availability { get; init; } = ThreadSourceAvailability.Unknown;
}

public sealed record ThreadOriginRecord
{
    public string Kind { get; init; } = "native";
    public string? SourceKind { get; init; }
    public string? SourceId { get; init; }
    public string? SourceFingerprint { get; init; }
}

public sealed record ThreadPersistencePolicyRecord
{
    public string PolicyVersion { get; init; } = "ui-safe-v1";
    public bool RawSecretsStored { get; init; }
    public bool ApprovalMaterialStored { get; init; }
    public bool ArtifactContentStored { get; init; }
    public bool FullCommandOutputStored { get; init; }
}

public sealed record ThreadTurnReferenceRecord
{
    public string TurnId { get; init; } = string.Empty;
    public int Ordinal { get; init; }
    public long Revision { get; init; }
    public string Status { get; init; } = string.Empty;
    public string SnapshotSha256 { get; init; } = string.Empty;
    public IReadOnlyList<ThreadSourcePointerRecord> SourcePointers { get; init; } = [];
}

public sealed record ThreadMutationReceiptRecord
{
    public string MutationId { get; init; } = string.Empty;
    public string PayloadSha256 { get; init; } = string.Empty;
    public long ResultRevision { get; init; }
    public long FirstSequence { get; init; }
    public long LastSequence { get; init; }
    public IReadOnlyList<string> ItemIds { get; init; } = [];
}

public sealed record ThreadRecord
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string ThreadId { get; init; } = string.Empty;
    public long Revision { get; init; }
    public string WorkspaceId { get; init; } = string.Empty;
    public string WorkspaceRootIdentity { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Status { get; init; } = ThreadStatus.Idle;
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
    public DateTimeOffset? ArchivedAtUtc { get; init; }
    public IReadOnlyList<ThreadTurnReferenceRecord> Turns { get; init; } = [];
    public long CommittedSequence { get; init; }
    public int TimelineItemCount { get; init; }
    public long PersistedByteCount { get; init; }
    public string? ActiveTurnId { get; init; }
    public ThreadOriginRecord Origin { get; init; } = new();
    public ThreadPersistencePolicyRecord PersistencePolicy { get; init; } = new();
    public IReadOnlyList<ThreadMutationReceiptRecord> MutationReceipts { get; init; } = [];
}

public sealed record TurnRecord
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string TurnId { get; init; } = string.Empty;
    public string ThreadId { get; init; } = string.Empty;
    public int Ordinal { get; init; }
    public long Revision { get; init; }
    public string Status { get; init; } = TurnStatus.Queued;
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? StartedAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; init; }
    public string TaskSummary { get; init; } = string.Empty;
    public string? StopReason { get; init; }
    public string? ErrorCode { get; init; }
    public string Mode { get; init; } = "projection";
    public string? SourceCorrelation { get; init; }
    public PendingComposerIntentRecord? ExecutionInput { get; init; }
    public string? CanonicalInputSha256 { get; init; }
    public bool RecoveryRequired { get; init; }
    public TurnCheckpointRecord? Checkpoint { get; init; }
    public DurableApprovalRequestRecord? ActiveApproval { get; init; }
    public long ApprovalRevision { get; init; }
    public ProviderProgressRecord ProviderProgress { get; init; } = new();
    public IReadOnlyList<ThreadSourcePointerRecord> SourcePointers { get; init; } = [];
    public long? TimelineFirstSequence { get; init; }
    public long? TimelineLastSequence { get; init; }
    public int TimelineItemCount { get; init; }
}

public sealed record ProviderProgressRecord
{
    public string Phase { get; init; } = ProviderAttemptPhase.Connecting;
    public int Attempt { get; init; }
    public int MaxAdditionalRetries { get; init; } = ProviderRequestRetryLimits.MaxAdditionalRetries;
    public bool AttemptHasStreamContent { get; init; }
    public string? AssistantMessageId { get; init; }
    public string? ErrorCategory { get; init; }
    public bool? Retryable { get; init; }
    public string? SafeErrorMessage { get; init; }
    public bool RetryExhausted { get; init; }
}

public sealed record TurnCheckpointRecord
{
    public string CheckpointId { get; init; } = string.Empty;
    public string CanonicalInputSha256 { get; init; } = string.Empty;
    public long EventSequence { get; init; }
    public string WorkspaceRootIdentity { get; init; } = string.Empty;
    public bool UnknownWriteBoundary { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed record DurableApprovalRequestRecord
{
    public string RequestId { get; init; } = string.Empty;
    public string WorkspaceId { get; init; } = string.Empty;
    public string ThreadId { get; init; } = string.Empty;
    public string TurnId { get; init; } = string.Empty;
    public long TurnRevision { get; init; }
    public long ApprovalRevision { get; init; }
    public string PolicyIdentity { get; init; } = string.Empty;
    public string PolicyRevision { get; init; } = string.Empty;
    public string Risk { get; init; } = string.Empty;
    public string Operation { get; init; } = string.Empty;
    public string TargetClass { get; init; } = string.Empty;
    public string CanonicalActionSha256 { get; init; } = string.Empty;
    public string SafeSummary { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset ExpiresAtUtc { get; init; }
}

public sealed record TimelineMessagePayloadRecord(
    string Preview,
    string? AssistantMessageId = null,
    int? Attempt = null);
public sealed record TimelineProviderPayloadRecord(
    string Phase,
    int Attempt,
    int MaxAdditionalRetries,
    bool AttemptHasStreamContent,
    string AssistantMessageId,
    string? ErrorCategory,
    bool? Retryable,
    string? SafeErrorMessage,
    bool RetryExhausted);
public sealed record TimelinePlanPayloadRecord(string Summary);
public sealed record TimelineOperationPayloadRecord(string Name, bool? Succeeded, string? ErrorCode);
public sealed record TimelineApprovalPayloadRecord(string Status);
public sealed record TimelineChangesPayloadRecord(int ChangedFileCount);
public sealed record TimelineReferencePayloadRecord(string ReferenceId);
public sealed record TimelineWarningPayloadRecord(string Code);
public sealed record TimelineTurnCompletedPayloadRecord(string StopReason, string? ErrorCode);

public sealed record TimelinePayloadRecord
{
    public TimelineMessagePayloadRecord? Message { get; init; }
    public TimelineProviderPayloadRecord? Provider { get; init; }
    public TimelinePlanPayloadRecord? Plan { get; init; }
    public TimelineOperationPayloadRecord? Operation { get; init; }
    public TimelineApprovalPayloadRecord? Approval { get; init; }
    public TimelineChangesPayloadRecord? Changes { get; init; }
    public TimelineReferencePayloadRecord? Reference { get; init; }
    public TimelineWarningPayloadRecord? Warning { get; init; }
    public TimelineTurnCompletedPayloadRecord? TurnCompleted { get; init; }
}

public sealed record TimelineRedactionRecord
{
    public bool Applied { get; init; }
    public string Policy { get; init; } = "ui-safe-v1";
}

public sealed record TimelineItemRecord
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string ItemId { get; init; } = string.Empty;
    public string ThreadId { get; init; } = string.Empty;
    public string TurnId { get; init; } = string.Empty;
    public long Sequence { get; init; }
    public DateTimeOffset TimestampUtc { get; init; }
    public string Type { get; init; } = string.Empty;
    public ThreadSourcePointerRecord? Source { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public TimelinePayloadRecord Payload { get; init; } = new();
    public TimelineRedactionRecord Redaction { get; init; } = new();
}

public sealed class ThreadContractException : Exception
{
    public ThreadContractException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}

public static class ThreadStateMachine
{
    public static bool CanTransitionTurn(string current, string next) => (current, next) switch
    {
        (TurnStatus.Queued, TurnStatus.Running or TurnStatus.Canceling or TurnStatus.Canceled) => true,
        (TurnStatus.Running, TurnStatus.WaitingForApproval or TurnStatus.Canceling or TurnStatus.Failed or TurnStatus.Completed) => true,
        (TurnStatus.WaitingForApproval, TurnStatus.Running or TurnStatus.Canceling or TurnStatus.Failed) => true,
        (TurnStatus.Canceling, TurnStatus.Canceled or TurnStatus.Failed) => true,
        _ => false
    };

    public static string DeriveThreadStatus(IReadOnlyList<TurnRecord> turns, bool archived)
    {
        if (archived)
        {
            return ThreadStatus.Archived;
        }

        TurnRecord? latest = turns.OrderBy(turn => turn.Ordinal).LastOrDefault();
        return latest?.Status switch
        {
            null => ThreadStatus.Idle,
            TurnStatus.WaitingForApproval => ThreadStatus.WaitingForApproval,
            TurnStatus.Canceling => ThreadStatus.Canceling,
            TurnStatus.Canceled => ThreadStatus.Canceled,
            TurnStatus.Failed => ThreadStatus.Failed,
            TurnStatus.Completed => ThreadStatus.Completed,
            _ => ThreadStatus.Running
        };
    }
}

public static class ThreadContractValidator
{
    public static void ValidateThread(ThreadRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.SchemaVersion != ThreadRecord.CurrentSchemaVersion)
        {
            throw new ThreadContractException(ThreadErrorCode.ThreadSchemaUnsupported, "Thread record uses an unsupported schema.");
        }

        if (!ThreadIdentity.IsThreadId(record.ThreadId))
        {
            throw new ThreadContractException(ThreadErrorCode.ThreadIdInvalid, "Thread id is invalid.");
        }

        if (record.Revision < 0 || string.IsNullOrWhiteSpace(record.WorkspaceId) ||
            string.IsNullOrWhiteSpace(record.WorkspaceRootIdentity) || Utf8Bytes(record.WorkspaceRootIdentity) > ThreadPersistenceLimits.MaxPointerValueBytes)
        {
            throw Corrupt("Thread identity or revision is invalid.");
        }

        ValidateTitle(record.Title);
        ValidateUtc(record.CreatedAtUtc, "createdAtUtc");
        ValidateUtc(record.UpdatedAtUtc, "updatedAtUtc");
        if (record.UpdatedAtUtc < record.CreatedAtUtc)
        {
            throw Corrupt("Thread timestamps are not monotonic.");
        }

        if (!ThreadStatus.IsKnown(record.Status))
        {
            throw Corrupt("Thread status is invalid.");
        }

        if (record.ArchivedAtUtc is { } archivedAt)
        {
            ValidateUtc(archivedAt, "archivedAtUtc");
            if (archivedAt < record.CreatedAtUtc || record.Status != ThreadStatus.Archived)
            {
                throw Corrupt("Thread archive state is invalid.");
            }
        }
        else if (record.Status == ThreadStatus.Archived)
        {
            throw Corrupt("Archived thread timestamp is missing.");
        }

        if (record.Turns.Count > ThreadPersistenceLimits.MaxTurnsPerThread)
        {
            throw new ThreadContractException(ThreadErrorCode.TurnLimitExceeded, "Thread turn limit was exceeded.");
        }

        int expectedOrdinal = 1;
        var turnIds = new HashSet<string>(StringComparer.Ordinal);
        int activeCount = 0;
        foreach (ThreadTurnReferenceRecord turn in record.Turns)
        {
            if (!ThreadIdentity.IsTurnId(turn.TurnId) || turn.Ordinal != expectedOrdinal++ || turn.Revision < 0 ||
                !TurnStatus.IsKnown(turn.Status) || !IsSha256(turn.SnapshotSha256) || !turnIds.Add(turn.TurnId))
            {
                throw Corrupt("Thread turn reference is invalid.");
            }

            ValidatePointers(turn.SourcePointers);
            if (TurnStatus.IsActive(turn.Status))
            {
                activeCount++;
            }
        }

        if (activeCount > 1 || (activeCount == 0) != (record.ActiveTurnId is null) ||
            (record.ActiveTurnId is not null && !record.Turns.Any(turn => turn.TurnId == record.ActiveTurnId && TurnStatus.IsActive(turn.Status))))
        {
            throw new ThreadContractException(ThreadErrorCode.ThreadActiveTurnConflict, "Thread active turn reference is inconsistent.");
        }

        if (record.CommittedSequence < 0 || record.TimelineItemCount < 0 ||
            record.CommittedSequence != record.TimelineItemCount ||
            record.TimelineItemCount > ThreadPersistenceLimits.MaxTimelineItemsPerThread ||
            record.PersistedByteCount < 0 || record.PersistedByteCount > ThreadPersistenceLimits.MaxPersistedBytesPerThread)
        {
            throw new ThreadContractException(ThreadErrorCode.ThreadLimitExceeded, "Thread persisted limits are invalid.");
        }

        ValidateOrigin(record.Origin);
        if (record.PersistencePolicy.RawSecretsStored || record.PersistencePolicy.ApprovalMaterialStored ||
            record.PersistencePolicy.ArtifactContentStored || record.PersistencePolicy.FullCommandOutputStored ||
            string.IsNullOrWhiteSpace(record.PersistencePolicy.PolicyVersion))
        {
            throw Corrupt("Thread persistence policy is unsafe.");
        }

        if (record.MutationReceipts.Count > ThreadPersistenceLimits.MaxMutationReceipts)
        {
            throw new ThreadContractException(ThreadErrorCode.ThreadLimitExceeded, "Thread mutation receipt limit was exceeded.");
        }

        var mutations = new HashSet<string>(StringComparer.Ordinal);
        foreach (ThreadMutationReceiptRecord receipt in record.MutationReceipts)
        {
            if (!IsSafeMutationId(receipt.MutationId) || !mutations.Add(receipt.MutationId) || !IsSha256(receipt.PayloadSha256) ||
                receipt.ResultRevision < 0 || receipt.FirstSequence < 0 || receipt.LastSequence < receipt.FirstSequence ||
                receipt.ItemIds.Any(itemId => !ThreadIdentity.IsItemId(itemId)))
            {
                throw Corrupt("Thread mutation receipt is invalid.");
            }
        }
    }

    public static void ValidateTurn(TurnRecord turn)
    {
        ArgumentNullException.ThrowIfNull(turn);
        if (turn.SchemaVersion != TurnRecord.CurrentSchemaVersion)
        {
            throw new ThreadContractException(ThreadErrorCode.ThreadSchemaUnsupported, "Turn record uses an unsupported schema.");
        }

        if (!ThreadIdentity.IsTurnId(turn.TurnId))
        {
            throw new ThreadContractException(ThreadErrorCode.TurnIdInvalid, "Turn id is invalid.");
        }

        if (!ThreadIdentity.IsThreadId(turn.ThreadId) || turn.Ordinal <= 0 || turn.Revision < 0 || !TurnStatus.IsKnown(turn.Status))
        {
            throw new ThreadContractException(ThreadErrorCode.TurnRecordCorrupt, "Turn identity or state is invalid.");
        }

        ValidateUtc(turn.CreatedAtUtc, "createdAtUtc");
        if (turn.StartedAtUtc is { } started)
        {
            ValidateUtc(started, "startedAtUtc");
            if (started < turn.CreatedAtUtc)
            {
                throw TurnCorrupt("Turn timestamps are not monotonic.");
            }
        }

        if (turn.CompletedAtUtc is { } completed)
        {
            ValidateUtc(completed, "completedAtUtc");
            if (completed < (turn.StartedAtUtc ?? turn.CreatedAtUtc))
            {
                throw TurnCorrupt("Turn timestamps are not monotonic.");
            }
        }

        if ((turn.Status == TurnStatus.Queued && turn.StartedAtUtc is not null) ||
            (turn.Status != TurnStatus.Queued && turn.StartedAtUtc is null) ||
            TurnStatus.IsTerminal(turn.Status) != turn.CompletedAtUtc.HasValue ||
            Utf8Bytes(turn.TaskSummary) > ThreadPersistenceLimits.MaxTaskSummaryBytes ||
            Utf8Bytes(turn.Mode) > 256 || Utf8Bytes(turn.SourceCorrelation) > ThreadPersistenceLimits.MaxPointerValueBytes ||
            turn.ApprovalRevision < 0)
        {
            throw TurnCorrupt("Turn metadata is invalid.");
        }


        if ((turn.ExecutionInput is null) != (turn.CanonicalInputSha256 is null))
        {
            throw TurnCorrupt("Turn execution input identity is incomplete.");
        }
        if (turn.ExecutionInput is not null)
        {
            ComposerIntentContractValidator.ValidateIntent(turn.ExecutionInput);
            if (turn.ExecutionInput.ThreadId != turn.ThreadId ||
                ComposerIntentContractValidator.ComputeCanonicalInputSha256(turn.ExecutionInput) != turn.CanonicalInputSha256)
                throw TurnCorrupt("Turn execution input hash is invalid.");
        }
        ValidateProviderProgress(turn.ProviderProgress);
        ValidateCheckpoint(turn.Checkpoint, turn);
        ValidateApproval(turn.ActiveApproval, turn);

        ValidatePointers(turn.SourcePointers);
        if (turn.TimelineItemCount < 0 || turn.TimelineItemCount > ThreadPersistenceLimits.MaxTimelineItemsPerThread ||
            (turn.TimelineItemCount == 0 && (turn.TimelineFirstSequence is not null || turn.TimelineLastSequence is not null)) ||
            (turn.TimelineItemCount > 0 && (turn.TimelineFirstSequence is null || turn.TimelineLastSequence is null ||
                turn.TimelineFirstSequence <= 0 || turn.TimelineLastSequence < turn.TimelineFirstSequence ||
                turn.TimelineLastSequence - turn.TimelineFirstSequence + 1 != turn.TimelineItemCount)))
        {
            throw TurnCorrupt("Turn timeline range is invalid.");
        }
    }

    private static void ValidateProviderProgress(ProviderProgressRecord progress)
    {
        if (!ProviderAttemptPhase.IsKnown(progress.Phase) ||
            progress.Attempt < 0 ||
            progress.Attempt > ProviderRequestRetryLimits.MaxAttempts ||
            progress.MaxAdditionalRetries < 0 ||
            progress.MaxAdditionalRetries > ProviderRequestRetryLimits.MaxAdditionalRetries ||
            Utf8Bytes(progress.AssistantMessageId) > 128 ||
            Utf8Bytes(progress.ErrorCategory) > 64 ||
            Utf8Bytes(progress.SafeErrorMessage) > ThreadPersistenceLimits.MaxTimelineSummaryBytes)
        {
            throw TurnCorrupt("Turn provider progress is invalid.");
        }
    }

    private static void ValidateCheckpoint(TurnCheckpointRecord? checkpoint, TurnRecord turn)
    {
        if (checkpoint is null) return;
        if (string.IsNullOrWhiteSpace(checkpoint.CheckpointId) || Utf8Bytes(checkpoint.CheckpointId) > 128 ||
            !IsSha256(checkpoint.CanonicalInputSha256) || checkpoint.CanonicalInputSha256 != turn.CanonicalInputSha256 ||
            checkpoint.EventSequence < 0 || string.IsNullOrWhiteSpace(checkpoint.WorkspaceRootIdentity) ||
            Utf8Bytes(checkpoint.WorkspaceRootIdentity) > ThreadPersistenceLimits.MaxPointerValueBytes)
            throw TurnCorrupt("Turn checkpoint is invalid.");
        ValidateUtc(checkpoint.CreatedAtUtc, "checkpoint.createdAtUtc");
    }

    private static void ValidateApproval(DurableApprovalRequestRecord? approval, TurnRecord turn)
    {
        if (approval is null) return;
        if (turn.Status != TurnStatus.WaitingForApproval || approval.ThreadId != turn.ThreadId || approval.TurnId != turn.TurnId ||
            approval.TurnRevision != turn.Revision || approval.ApprovalRevision != turn.ApprovalRevision ||
            string.IsNullOrWhiteSpace(approval.RequestId) || Utf8Bytes(approval.RequestId) > 128 ||
            string.IsNullOrWhiteSpace(approval.WorkspaceId) || string.IsNullOrWhiteSpace(approval.PolicyIdentity) ||
            Utf8Bytes(approval.PolicyIdentity) > 256 || Utf8Bytes(approval.PolicyRevision) > 256 ||
            Utf8Bytes(approval.Risk) > 64 || Utf8Bytes(approval.Operation) > 1024 || Utf8Bytes(approval.TargetClass) > 1024 ||
            !IsSha256(approval.CanonicalActionSha256) || Utf8Bytes(approval.SafeSummary) > 4 * 1024)
            throw TurnCorrupt("Turn approval request is invalid.");
        ValidateUtc(approval.CreatedAtUtc, "approval.createdAtUtc");
        ValidateUtc(approval.ExpiresAtUtc, "approval.expiresAtUtc");
        if (approval.ExpiresAtUtc <= approval.CreatedAtUtc) throw TurnCorrupt("Turn approval expiry is invalid.");
    }

    public static void ValidateTimelineItem(TimelineItemRecord item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.SchemaVersion != TimelineItemRecord.CurrentSchemaVersion)
        {
            throw new ThreadContractException(ThreadErrorCode.ThreadSchemaUnsupported, "Timeline item uses an unsupported schema.");
        }

        if (!ThreadIdentity.IsItemId(item.ItemId))
        {
            throw new ThreadContractException(ThreadErrorCode.ItemIdInvalid, "Timeline item id is invalid.");
        }

        if (!ThreadIdentity.IsThreadId(item.ThreadId) || !ThreadIdentity.IsTurnId(item.TurnId) || item.Sequence <= 0 ||
            !TimelineItemType.IsKnown(item.Type) || Utf8Bytes(item.Status) > 256 ||
            Utf8Bytes(item.Summary) > ThreadPersistenceLimits.MaxTimelineSummaryBytes)
        {
            throw new ThreadContractException(ThreadErrorCode.TimelineRecordCorrupt, "Timeline item envelope is invalid.");
        }

        ValidateUtc(item.TimestampUtc, "timestampUtc");
        if (item.Source is not null)
        {
            ValidatePointer(item.Source);
        }

        if (item.Redaction.Policy != "ui-safe-v1")
        {
            throw new ThreadContractException(ThreadErrorCode.TimelineRecordCorrupt, "Timeline redaction policy is invalid.");
        }

        ValidatePayload(item.Type, item.Payload);
    }

    public static void ValidateTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title) || !string.Equals(title, title.Trim(), StringComparison.Ordinal) ||
            Utf8Bytes(title) > ThreadPersistenceLimits.MaxTitleBytes)
        {
            throw new ThreadContractException(ThreadErrorCode.ThreadTitleInvalid, "Thread title is invalid.");
        }
    }

    public static bool IsSafeMutationId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
        {
            return false;
        }

        return value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.');
    }

    private static void ValidateOrigin(ThreadOriginRecord origin)
    {
        if (origin.Kind == "native")
        {
            if (origin.SourceKind is not null || origin.SourceId is not null || origin.SourceFingerprint is not null)
            {
                throw Corrupt("Native thread origin is invalid.");
            }

            return;
        }

        if (origin.Kind != "session-import" || origin.SourceKind != ThreadSourceKind.Session ||
            string.IsNullOrWhiteSpace(origin.SourceId) || Utf8Bytes(origin.SourceId) > 256 || !IsSha256(origin.SourceFingerprint))
        {
            throw Corrupt("Imported thread origin is invalid.");
        }
    }

    private static void ValidatePointers(IReadOnlyList<ThreadSourcePointerRecord> pointers)
    {
        foreach (IGrouping<string, ThreadSourcePointerRecord> group in pointers.GroupBy(pointer => pointer.Kind, StringComparer.Ordinal))
        {
            if (group.Count() > ThreadPersistenceLimits.MaxPointersPerKind)
            {
                throw new ThreadContractException(ThreadErrorCode.TurnLimitExceeded, "Turn source pointer limit was exceeded.");
            }
        }

        foreach (ThreadSourcePointerRecord pointer in pointers)
        {
            ValidatePointer(pointer);
        }
    }

    private static void ValidatePointer(ThreadSourcePointerRecord pointer)
    {
        if (!ThreadSourceKind.IsKnown(pointer.Kind) || !ThreadSourceAvailability.IsKnown(pointer.Availability) ||
            string.IsNullOrWhiteSpace(pointer.SourceId) || Utf8Bytes(pointer.SourceId) > ThreadPersistenceLimits.MaxPointerValueBytes ||
            Utf8Bytes(pointer.SourceRevision) > ThreadPersistenceLimits.MaxPointerValueBytes ||
            Utf8Bytes(pointer.SourceFingerprint) > ThreadPersistenceLimits.MaxPointerValueBytes)
        {
            throw new ThreadContractException(ThreadErrorCode.TimelineRecordCorrupt, "Source pointer is invalid.");
        }
    }

    private static void ValidatePayload(string type, TimelinePayloadRecord payload)
    {
        int count = new object?[] { payload.Message, payload.Provider, payload.Plan, payload.Operation, payload.Approval, payload.Changes,
            payload.Reference, payload.Warning, payload.TurnCompleted }.Count(value => value is not null);
        bool matching = type switch
        {
            TimelineItemType.UserMessage or TimelineItemType.AssistantMessage or TimelineItemType.AssistantFinal => payload.Message is not null,
            TimelineItemType.ProviderAttempt => payload.Provider is not null &&
                ProviderAttemptPhase.IsKnown(payload.Provider.Phase) &&
                payload.Provider.Attempt >= 1 &&
                payload.Provider.Attempt <= ProviderRequestRetryLimits.MaxAttempts &&
                payload.Provider.MaxAdditionalRetries >= 0 &&
                payload.Provider.MaxAdditionalRetries <= ProviderRequestRetryLimits.MaxAdditionalRetries &&
                !string.IsNullOrWhiteSpace(payload.Provider.AssistantMessageId),
            TimelineItemType.PlanUpdated => payload.Plan is not null,
            TimelineItemType.ToolStarted or TimelineItemType.ToolCompleted or TimelineItemType.CommandStarted or TimelineItemType.CommandCompleted or TimelineItemType.VerificationCompleted => payload.Operation is not null,
            TimelineItemType.ApprovalRequested or TimelineItemType.ApprovalResolved => payload.Approval is not null,
            TimelineItemType.ChangesUpdated => payload.Changes is not null && payload.Changes.ChangedFileCount >= 0,
            TimelineItemType.ReportAvailable or TimelineItemType.ArtifactAvailable => payload.Reference is not null,
            TimelineItemType.WarningRaised => payload.Warning is not null,
            TimelineItemType.TurnCompleted => payload.TurnCompleted is not null,
            _ => false
        };
        string?[] values =
        [
            payload.Message?.Preview,
            payload.Message?.AssistantMessageId,
            payload.Provider?.AssistantMessageId,
            payload.Provider?.ErrorCategory,
            payload.Provider?.SafeErrorMessage,
            payload.Plan?.Summary,
            payload.Operation?.Name,
            payload.Operation?.ErrorCode,
            payload.Approval?.Status,
            payload.Reference?.ReferenceId,
            payload.Warning?.Code,
            payload.TurnCompleted?.StopReason,
            payload.TurnCompleted?.ErrorCode
        ];
        if (count != 1 || !matching || values.Any(value => Utf8Bytes(value) > ThreadPersistenceLimits.MaxTimelineSummaryBytes))
        {
            throw new ThreadContractException(ThreadErrorCode.TimelineRecordCorrupt, "Timeline typed payload is invalid.");
        }
    }

    private static void ValidateUtc(DateTimeOffset value, string name)
    {
        if (value == default || value.Offset != TimeSpan.Zero)
        {
            throw new ThreadContractException(ThreadErrorCode.ThreadRecordCorrupt, $"{name} must be a UTC timestamp.");
        }
    }

    private static int Utf8Bytes(string? value) => Encoding.UTF8.GetByteCount(value ?? string.Empty);

    private static bool IsSha256(string? value) => value is { Length: 64 } && value.AsSpan().IndexOfAnyExcept("0123456789abcdef") < 0;

    private static ThreadContractException Corrupt(string message) => new(ThreadErrorCode.ThreadRecordCorrupt, message);

    private static ThreadContractException TurnCorrupt(string message) => new(ThreadErrorCode.TurnRecordCorrupt, message);
}
