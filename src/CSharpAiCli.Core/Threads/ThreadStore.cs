using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CSharpAiCli.Core;

public sealed record ThreadStoreLayout(
    string ThreadsRoot,
    string ThreadRoot,
    string ManifestPath,
    string TurnsRoot,
    string TimelineRoot,
    string LockPath);

public sealed record ThreadStoreDiagnostic(string ErrorCode, string SafeMessage, string? ThreadId = null);

public sealed record ThreadAggregate(
    ThreadRecord Record,
    IReadOnlyList<TurnRecord> Turns);

public sealed record ThreadStoreReadResult(
    bool Succeeded,
    ThreadAggregate? Aggregate,
    ThreadStoreDiagnostic? Diagnostic,
    bool RecoveryRequired)
{
    public static ThreadStoreReadResult Success(ThreadAggregate aggregate) =>
        new(true, aggregate, null, aggregate.Record.ActiveTurnId is not null &&
            aggregate.Turns.Any(turn => turn.TurnId == aggregate.Record.ActiveTurnId &&
                turn.Status is TurnStatus.Running or TurnStatus.WaitingForApproval or TurnStatus.Canceling));

    public static ThreadStoreReadResult Failure(string code, string message, string? threadId = null) =>
        new(false, null, new ThreadStoreDiagnostic(code, message, threadId), false);
}

public sealed record ThreadStoreMutationResult(
    bool Succeeded,
    ThreadAggregate? Aggregate,
    ThreadStoreDiagnostic? Diagnostic,
    bool Idempotent = false)
{
    public static ThreadStoreMutationResult Success(ThreadAggregate aggregate, bool idempotent = false) =>
        new(true, aggregate, null, idempotent);

    public static ThreadStoreMutationResult Failure(string code, string message, string? threadId = null) =>
        new(false, null, new ThreadStoreDiagnostic(code, message, threadId));
}

public sealed record ThreadStoreListResult(
    IReadOnlyList<ThreadRecord> Records,
    IReadOnlyList<ThreadStoreDiagnostic> Diagnostics);

public sealed record ThreadTimelinePageResult(
    bool Succeeded,
    IReadOnlyList<TimelineItemRecord> Items,
    long? NextSequence,
    bool Truncated,
    ThreadStoreDiagnostic? Diagnostic)
{
    public static ThreadTimelinePageResult Failure(string code, string message, string? threadId = null) =>
        new(false, [], null, false, new ThreadStoreDiagnostic(code, message, threadId));
}

public sealed class ThreadStore
{
    private const int StableReadAttempts = 4;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 64
    };

    private readonly string threadsRoot;

    public ThreadStore(string threadsRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadsRoot);
        this.threadsRoot = Path.GetFullPath(threadsRoot);
    }

    public string ThreadsRoot => threadsRoot;

    public static ThreadStore Create(CliEnvironmentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        string? stateRoot = Path.GetDirectoryName(snapshot.UserConfigPath);
        stateRoot = string.IsNullOrWhiteSpace(stateRoot)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".caicli")
            : stateRoot;
        return new ThreadStore(Path.Combine(stateRoot, "threads"));
    }

    public ThreadStoreLayout GetLayout(string threadId)
    {
        if (!ThreadIdentity.IsThreadId(threadId))
        {
            throw new ThreadContractException(ThreadErrorCode.ThreadIdInvalid, "Thread id is invalid.");
        }

        string root = Path.GetFullPath(threadsRoot);
        string threadRoot = Path.GetFullPath(Path.Combine(root, threadId));
        EnsureContained(root, threadRoot);
        return new ThreadStoreLayout(
            root,
            threadRoot,
            Path.Combine(threadRoot, "thread.json"),
            Path.Combine(threadRoot, "turns"),
            Path.Combine(threadRoot, "timeline"),
            Path.Combine(threadRoot, ".thread.lock"));
    }

    public ThreadStoreMutationResult Create(ThreadRecord record) => CreateProjection(record, [], []);

    public ThreadStoreMutationResult CreateProjection(
        ThreadRecord record,
        IReadOnlyList<TurnRecord> turns,
        IReadOnlyList<TimelineItemRecord> timeline)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(turns);
        ArgumentNullException.ThrowIfNull(timeline);

        ThreadStoreLayout layout;
        try
        {
            ThreadContractValidator.ValidateThread(record);
            ValidateProjection(record, turns, timeline);
            layout = GetLayout(record.ThreadId);
            EnsureNoReparseInExistingChain(layout.ThreadsRoot);
            Directory.CreateDirectory(layout.ThreadsRoot);
            EnsureNoReparseInExistingChain(layout.ThreadsRoot);
            Directory.CreateDirectory(layout.ThreadRoot);
            EnsureNoReparseInExistingChain(layout.ThreadRoot);
            Directory.CreateDirectory(layout.TurnsRoot);
            Directory.CreateDirectory(layout.TimelineRoot);
            EnsureNoReparseInExistingChain(layout.TurnsRoot);
            EnsureNoReparseInExistingChain(layout.TimelineRoot);
        }
        catch (ThreadContractException exception)
        {
            return ThreadStoreMutationResult.Failure(exception.ErrorCode, exception.Message, record.ThreadId);
        }
        catch (Exception exception) when (IsStoreException(exception))
        {
            return ThreadStoreMutationResult.Failure(ThreadErrorCode.ThreadWriteFailed, "Thread directory could not be created.", record.ThreadId);
        }

        try
        {
            using ThreadMutationLock threadLock = AcquireLock(layout);
            if (File.Exists(layout.ManifestPath))
            {
                return ThreadStoreMutationResult.Failure(ThreadErrorCode.ThreadAlreadyExists, "Thread already exists.", record.ThreadId);
            }

            Dictionary<string, SerializedRecord<TurnRecord>> serializedTurns = turns.ToDictionary(
                turn => turn.TurnId,
                turn => SerializeRecord(turn, ThreadPersistenceLimits.MaxTurnSnapshotBytes, ThreadErrorCode.TurnLimitExceeded),
                StringComparer.Ordinal);
            SerializedRecord<TimelineItemRecord>[] serializedItems = timeline
                .Select(item => SerializeRecord(item, ThreadPersistenceLimits.MaxTimelineItemBytes, ThreadErrorCode.TimelineLimitExceeded))
                .ToArray();

            foreach (TurnRecord turn in turns)
            {
                WriteImmutableJson(GetTurnPath(layout, turn.TurnId, turn.Revision), serializedTurns[turn.TurnId].Json);
            }

            for (int index = 0; index < timeline.Count; index++)
            {
                WriteImmutableJson(GetTimelinePath(layout, timeline[index]), serializedItems[index].Json);
            }

            ThreadRecord committed = BuildCommittedRecord(record, turns, serializedTurns, timeline, serializedItems);
            WriteManifest(layout, committed);
            return ThreadStoreMutationResult.Success(new ThreadAggregate(committed, turns.OrderBy(turn => turn.Ordinal).ToArray()));
        }
        catch (ThreadContractException exception)
        {
            return ThreadStoreMutationResult.Failure(exception.ErrorCode, exception.Message, record.ThreadId);
        }
        catch (IOException exception)
        {
            return ThreadStoreMutationResult.Failure(
                IsSharingViolation(exception) ? ThreadErrorCode.ThreadRevisionConflict : ThreadErrorCode.ThreadWriteFailed,
                IsSharingViolation(exception) ? "Thread is already being modified." : "Thread could not be written.",
                record.ThreadId);
        }
        catch (Exception exception) when (IsStoreException(exception))
        {
            return ThreadStoreMutationResult.Failure(ThreadErrorCode.ThreadWriteFailed, "Thread could not be written.", record.ThreadId);
        }
    }

    public ThreadStoreReadResult Read(string threadId, CancellationToken cancellationToken = default)
    {
        ThreadStoreReadResult? last = null;
        for (int attempt = 0; attempt < StableReadAttempts; attempt++)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                ThreadStoreLayout layout = GetLayout(threadId);
                last = ReadValidated(layout, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ThreadContractException exception)
            {
                last = ThreadStoreReadResult.Failure(exception.ErrorCode, exception.Message, threadId);
            }
            catch (JsonException)
            {
                return ThreadStoreReadResult.Failure(ThreadErrorCode.ThreadRecordCorrupt, "Thread JSON is corrupt.", threadId);
            }
            catch (Exception exception) when (IsStoreException(exception) || exception is InvalidOperationException or ArgumentException)
            {
                last = ThreadStoreReadResult.Failure(ThreadErrorCode.ThreadStoreUnavailable, "Thread could not be read.", threadId);
            }

            if (last.Succeeded || last.Diagnostic?.ErrorCode is not (ThreadErrorCode.ThreadStoreUnavailable or ThreadErrorCode.ThreadReferenceMissing))
            {
                return last;
            }
        }

        return last ?? ThreadStoreReadResult.Failure(ThreadErrorCode.ThreadStoreUnavailable, "Thread could not be read.", threadId);
    }

    public ThreadStoreListResult List(string? workspaceId = null, CancellationToken cancellationToken = default)
    {
        var records = new List<ThreadRecord>();
        var diagnostics = new List<ThreadStoreDiagnostic>();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureNoReparseInExistingChain(threadsRoot);
            if (!Directory.Exists(threadsRoot))
            {
                return new ThreadStoreListResult([], []);
            }

            foreach (string directory in Directory.EnumerateDirectories(threadsRoot, "thread_*", SearchOption.TopDirectoryOnly)
                .Order(StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string threadId = Path.GetFileName(directory);
                if (!ThreadIdentity.IsThreadId(threadId))
                {
                    diagnostics.Add(new ThreadStoreDiagnostic(ThreadErrorCode.ThreadRecordCorrupt, "Thread directory name is invalid."));
                    continue;
                }

                ThreadStoreReadResult read = Read(threadId, cancellationToken);
                if (!read.Succeeded || read.Aggregate is null)
                {
                    diagnostics.Add(read.Diagnostic ?? new ThreadStoreDiagnostic(ThreadErrorCode.ThreadRecordCorrupt, "Thread could not be read.", threadId));
                    continue;
                }

                if (workspaceId is null || string.Equals(read.Aggregate.Record.WorkspaceId, workspaceId, StringComparison.Ordinal))
                {
                    records.Add(read.Aggregate.Record);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ThreadContractException exception)
        {
            diagnostics.Add(new ThreadStoreDiagnostic(exception.ErrorCode, exception.Message));
        }
        catch (Exception exception) when (IsStoreException(exception))
        {
            diagnostics.Add(new ThreadStoreDiagnostic(ThreadErrorCode.ThreadStoreUnavailable, "Thread store could not be listed."));
        }

        return new ThreadStoreListResult(records, diagnostics);
    }

    public ThreadStoreMutationResult Rename(
        string threadId,
        long expectedRevision,
        string title,
        DateTimeOffset updatedAtUtc)
    {
        try
        {
            ThreadContractValidator.ValidateTitle(title);
            ValidateUtcTimestamp(updatedAtUtc);
        }
        catch (ThreadContractException exception)
        {
            return ThreadStoreMutationResult.Failure(exception.ErrorCode, exception.Message, threadId);
        }

        return MutateManifest(threadId, expectedRevision, aggregate =>
        {
            if (updatedAtUtc < aggregate.Record.UpdatedAtUtc)
            {
                throw new ThreadContractException(ThreadErrorCode.ThreadRecordCorrupt, "Thread update timestamp is not monotonic.");
            }

            return aggregate.Record with
            {
                Revision = aggregate.Record.Revision + 1,
                Title = title,
                UpdatedAtUtc = updatedAtUtc
            };
        });
    }

    public ThreadStoreMutationResult Archive(string threadId, long expectedRevision, DateTimeOffset archivedAtUtc)
    {
        try
        {
            ValidateUtcTimestamp(archivedAtUtc);
        }
        catch (ThreadContractException exception)
        {
            return ThreadStoreMutationResult.Failure(exception.ErrorCode, exception.Message, threadId);
        }

        return MutateManifest(threadId, expectedRevision, aggregate =>
        {
            if (aggregate.Record.ActiveTurnId is not null || aggregate.Record.Status is ThreadStatus.Running or ThreadStatus.WaitingForApproval)
            {
                throw new ThreadContractException(ThreadErrorCode.ThreadArchiveActive, "Active thread cannot be archived.");
            }

            if (aggregate.Record.Status == ThreadStatus.Archived)
            {
                throw new ThreadContractException(ThreadErrorCode.ThreadRevisionConflict, "Thread is already archived.");
            }

            if (archivedAtUtc < aggregate.Record.UpdatedAtUtc)
            {
                throw new ThreadContractException(ThreadErrorCode.ThreadRecordCorrupt, "Thread archive timestamp is not monotonic.");
            }

            return aggregate.Record with
            {
                Revision = aggregate.Record.Revision + 1,
                Status = ThreadStatus.Archived,
                UpdatedAtUtc = archivedAtUtc,
                ArchivedAtUtc = archivedAtUtc
            };
        });
    }

    public ThreadStoreMutationResult Delete(string threadId, long expectedRevision, string confirmation)
    {
        if (!string.Equals(threadId, confirmation, StringComparison.Ordinal))
        {
            return ThreadStoreMutationResult.Failure(ThreadErrorCode.ThreadConfirmationInvalid, "Thread delete confirmation is invalid.", threadId);
        }

        ThreadStoreLayout layout;
        try
        {
            layout = GetLayout(threadId);
            EnsureNoReparseInExistingChain(layout.ThreadRoot);
            string? quarantine = null;
            using (ThreadMutationLock threadLock = AcquireLock(layout))
            {
                ThreadStoreReadResult read = ReadValidated(layout);
                if (!read.Succeeded || read.Aggregate is null)
                {
                    return ThreadStoreMutationResult.Failure(
                        read.Diagnostic?.ErrorCode ?? ThreadErrorCode.ThreadRecordCorrupt,
                        read.Diagnostic?.SafeMessage ?? "Thread could not be read.", threadId);
                }

                if (read.Aggregate.Record.Revision != expectedRevision)
                {
                    return ThreadStoreMutationResult.Failure(ThreadErrorCode.ThreadRevisionConflict, "Thread revision changed before delete.", threadId);
                }

                if (read.Aggregate.Record.Status != ThreadStatus.Archived)
                {
                    return ThreadStoreMutationResult.Failure(ThreadErrorCode.ThreadDeleteNotArchived, "Only archived threads can be deleted.", threadId);
                }

                EnsureNoReparseDescendants(layout.ThreadRoot);
                quarantine = Path.Combine(layout.ThreadsRoot, $".deleting-{threadId}-{Guid.NewGuid():N}");
                EnsureContained(layout.ThreadsRoot, quarantine);
                threadLock.ReleaseMarker();
                Directory.Move(layout.ThreadRoot, quarantine);
            }
            EnsureNoReparseDescendants(quarantine!);
            Directory.Delete(quarantine!, recursive: true);
            return ThreadStoreMutationResult.Success(new ThreadAggregate(
                new ThreadRecord
                {
                    ThreadId = threadId,
                    Revision = expectedRevision,
                    WorkspaceId = "deleted",
                    WorkspaceRootIdentity = "deleted",
                    Title = "deleted",
                    Status = ThreadStatus.Archived,
                    CreatedAtUtc = DateTimeOffset.UnixEpoch,
                    UpdatedAtUtc = DateTimeOffset.UnixEpoch,
                    ArchivedAtUtc = DateTimeOffset.UnixEpoch
                }, []));
        }
        catch (ThreadContractException exception)
        {
            return ThreadStoreMutationResult.Failure(exception.ErrorCode, exception.Message, threadId);
        }
        catch (IOException exception)
        {
            return ThreadStoreMutationResult.Failure(
                IsSharingViolation(exception) ? ThreadErrorCode.ThreadRevisionConflict : ThreadErrorCode.ThreadDeleteFailed,
                IsSharingViolation(exception) ? "Thread is already being modified." : "Thread could not be deleted.", threadId);
        }
        catch (Exception exception) when (IsStoreException(exception))
        {
            return ThreadStoreMutationResult.Failure(ThreadErrorCode.ThreadDeleteFailed, "Thread could not be deleted.", threadId);
        }
    }

    public ThreadStoreMutationResult CreateTurn(string threadId, long expectedRevision, TurnRecord turn)
    {
        ArgumentNullException.ThrowIfNull(turn);
        try
        {
            ThreadContractValidator.ValidateTurn(turn);
        }
        catch (ThreadContractException exception)
        {
            return ThreadStoreMutationResult.Failure(exception.ErrorCode, exception.Message, threadId);
        }

        return Mutate(threadId, expectedRevision, (layout, aggregate) =>
        {
            if (aggregate.Record.Status == ThreadStatus.Archived || aggregate.Record.ActiveTurnId is not null)
            {
                throw new ThreadContractException(ThreadErrorCode.ThreadActiveTurnConflict, "Thread already has an active turn or is archived.");
            }

            if (aggregate.Record.Turns.Count >= ThreadPersistenceLimits.MaxTurnsPerThread)
            {
                throw new ThreadContractException(ThreadErrorCode.TurnLimitExceeded, "Thread turn limit was exceeded.");
            }

            if (turn.ThreadId != threadId || turn.Ordinal != aggregate.Record.Turns.Count + 1 || turn.Revision != 0 ||
                turn.Status != TurnStatus.Queued || turn.TimelineItemCount != 0 ||
                turn.CreatedAtUtc < aggregate.Record.UpdatedAtUtc)
            {
                throw new ThreadContractException(ThreadErrorCode.TurnRecordCorrupt, "New turn record is invalid.");
            }

            SerializedRecord<TurnRecord> serialized = SerializeRecord(turn, ThreadPersistenceLimits.MaxTurnSnapshotBytes, ThreadErrorCode.TurnLimitExceeded);
            WriteImmutableJson(GetTurnPath(layout, turn.TurnId, turn.Revision), serialized.Json);
            ThreadTurnReferenceRecord reference = CreateTurnReference(turn, serialized.Sha256);
            ThreadRecord record = aggregate.Record with
            {
                Revision = aggregate.Record.Revision + 1,
                Status = ThreadStatus.Running,
                UpdatedAtUtc = turn.CreatedAtUtc,
                Turns = aggregate.Record.Turns.Append(reference).ToArray(),
                ActiveTurnId = turn.TurnId,
                PersistedByteCount = aggregate.Record.PersistedByteCount + serialized.ByteCount
            };
            WriteManifest(layout, record);
            return new ThreadAggregate(record, aggregate.Turns.Append(turn).ToArray());
        });
    }

    public ThreadStoreMutationResult StartTurnFromIntent(
        string threadId,
        long expectedRevision,
        string mutationId,
        ComposerIntentClaimRecord claim,
        PendingComposerIntentRecord input,
        TurnRecord turn,
        TimelineItemRecord userMessage)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(turn);
        ArgumentNullException.ThrowIfNull(userMessage);
        if (!ThreadContractValidator.IsSafeMutationId(mutationId))
            return ThreadStoreMutationResult.Failure(ThreadErrorCode.TimelineAppendConflict, "Turn start mutation is invalid.", threadId);
        try
        {
            ComposerIntentContractValidator.ValidateClaim(claim);
            ComposerIntentContractValidator.ValidateIntent(input);
            ThreadContractValidator.ValidateTurn(turn);
            ThreadContractValidator.ValidateTimelineItem(userMessage);
        }
        catch (Exception exception) when (exception is ThreadContractException or ComposerIntentContractException)
        {
            string code = exception is ThreadContractException threadException ? threadException.ErrorCode : ((ComposerIntentContractException)exception).ErrorCode;
            return ThreadStoreMutationResult.Failure(code, exception.Message, threadId);
        }

        string payloadHash = HashCanonical(new { claim.IntentId, claim.TurnId, claim.CanonicalInputSha256, Input = input, Turn = turn, UserMessage = userMessage });
        return Mutate(threadId, expectedRevision, (layout, aggregate) =>
        {
            ThreadMutationReceiptRecord? existing = aggregate.Record.MutationReceipts.SingleOrDefault(receipt => receipt.MutationId == mutationId);
            if (existing is not null)
            {
                if (existing.PayloadSha256 != payloadHash)
                    throw new ThreadContractException(ThreadErrorCode.TimelineAppendConflict, "Turn start mutation id was reused with different content.");
                TurnRecord? committed = aggregate.Turns.SingleOrDefault(candidate => candidate.TurnId == claim.TurnId);
                if (committed?.CanonicalInputSha256 != claim.CanonicalInputSha256)
                    throw new ThreadContractException(ThreadErrorCode.ThreadRecordCorrupt, "Turn start receipt does not match its claimed input.");
                return aggregate;
            }
            if (aggregate.Record.Revision != expectedRevision || aggregate.Record.Status == ThreadStatus.Archived || aggregate.Record.ActiveTurnId is not null)
                throw new ThreadContractException(ThreadErrorCode.ThreadActiveTurnConflict, "Thread cannot start the claimed turn.");
            if (aggregate.Record.Turns.Count >= ThreadPersistenceLimits.MaxTurnsPerThread)
                throw new ThreadContractException(ThreadErrorCode.TurnLimitExceeded, "Thread turn limit was exceeded.");
            if (claim.IntentId != input.IntentId || claim.TurnId != turn.TurnId || claim.CanonicalInputSha256 != ComposerIntentContractValidator.ComputeCanonicalInputSha256(input) ||
                turn.ThreadId != threadId || turn.Ordinal != aggregate.Record.Turns.Count + 1 || turn.Revision != 0 || turn.Status != TurnStatus.Queued ||
                turn.ExecutionInput is null || ComposerIntentContractValidator.ComputeCanonicalInputSha256(turn.ExecutionInput) != claim.CanonicalInputSha256 ||
                turn.CanonicalInputSha256 != claim.CanonicalInputSha256 || turn.TimelineItemCount != 0 ||
                userMessage.ThreadId != threadId || userMessage.TurnId != turn.TurnId || userMessage.Type != TimelineItemType.UserMessage ||
                userMessage.Sequence != aggregate.Record.CommittedSequence + 1 || userMessage.TimestampUtc < turn.CreatedAtUtc)
                throw new ThreadContractException(ThreadErrorCode.TurnRecordCorrupt, "Claimed turn start binding is invalid.");

            TurnRecord committedTurn = turn with
            {
                TimelineFirstSequence = userMessage.Sequence,
                TimelineLastSequence = userMessage.Sequence,
                TimelineItemCount = 1
            };
            SerializedRecord<TurnRecord> serializedTurn = SerializeRecord(committedTurn, ThreadPersistenceLimits.MaxTurnSnapshotBytes, ThreadErrorCode.TurnLimitExceeded);
            SerializedRecord<TimelineItemRecord> serializedItem = SerializeRecord(userMessage, ThreadPersistenceLimits.MaxTimelineItemBytes, ThreadErrorCode.TimelineLimitExceeded);
            WriteImmutableJson(GetTurnPath(layout, committedTurn.TurnId, committedTurn.Revision), serializedTurn.Json);
            WriteImmutableJson(GetTimelinePath(layout, userMessage), serializedItem.Json);
            ThreadMutationReceiptRecord receipt = new()
            {
                MutationId = mutationId,
                PayloadSha256 = payloadHash,
                ResultRevision = aggregate.Record.Revision + 1,
                FirstSequence = userMessage.Sequence,
                LastSequence = userMessage.Sequence,
                ItemIds = [userMessage.ItemId]
            };
            ThreadRecord record = aggregate.Record with
            {
                Revision = aggregate.Record.Revision + 1,
                Status = ThreadStatus.Running,
                UpdatedAtUtc = userMessage.TimestampUtc,
                Turns = aggregate.Record.Turns.Append(CreateTurnReference(committedTurn, serializedTurn.Sha256)).ToArray(),
                ActiveTurnId = committedTurn.TurnId,
                CommittedSequence = userMessage.Sequence,
                TimelineItemCount = aggregate.Record.TimelineItemCount + 1,
                PersistedByteCount = aggregate.Record.PersistedByteCount + serializedTurn.ByteCount + serializedItem.ByteCount,
                MutationReceipts = aggregate.Record.MutationReceipts.Append(receipt).ToArray()
            };
            WriteManifest(layout, record);
            return new ThreadAggregate(record, aggregate.Turns.Append(committedTurn).ToArray());
        }, allowIdempotentRevisionMismatch: true, mutationId: mutationId, payloadHash: payloadHash);
    }

    public ThreadStoreMutationResult TransitionTurn(
        string threadId,
        string turnId,
        long expectedRevision,
        string nextStatus,
        DateTimeOffset changedAtUtc,
        string? stopReason = null,
        string? errorCode = null)
    {
        try
        {
            ValidateUtcTimestamp(changedAtUtc);
        }
        catch (ThreadContractException exception)
        {
            return ThreadStoreMutationResult.Failure(exception.ErrorCode, exception.Message, threadId);
        }

        return Mutate(threadId, expectedRevision, (layout, aggregate) =>
        {
            TurnRecord current = aggregate.Turns.SingleOrDefault(turn => turn.TurnId == turnId)
                ?? throw new ThreadContractException(ThreadErrorCode.TurnNotFound, "Turn was not found.");
            if (!ThreadStateMachine.CanTransitionTurn(current.Status, nextStatus))
            {
                throw new ThreadContractException(ThreadErrorCode.TurnTransitionInvalid, "Turn state transition is invalid.");
            }

            DateTimeOffset boundary = current.StartedAtUtc ?? current.CreatedAtUtc;
            if (changedAtUtc < boundary)
            {
                throw new ThreadContractException(ThreadErrorCode.TurnRecordCorrupt, "Turn transition timestamp is not monotonic.");
            }

            TurnRecord updated = current with
            {
                Revision = current.Revision + 1,
                Status = nextStatus,
                StartedAtUtc = nextStatus == TurnStatus.Running && current.StartedAtUtc is null ? changedAtUtc : current.StartedAtUtc,
                CompletedAtUtc = TurnStatus.IsTerminal(nextStatus) ? changedAtUtc : null,
                StopReason = TurnStatus.IsTerminal(nextStatus) ? stopReason : null,
                ErrorCode = nextStatus == TurnStatus.Failed ? errorCode : null,
                ActiveApproval = nextStatus == TurnStatus.WaitingForApproval ? current.ActiveApproval : null,
                RecoveryRequired = nextStatus == TurnStatus.Canceling || current.RecoveryRequired && !TurnStatus.IsTerminal(nextStatus)
            };
            ThreadContractValidator.ValidateTurn(updated);
            return CommitTurnRevision(layout, aggregate, current, updated);
        });
    }

    public ThreadStoreMutationResult RequestApproval(
        string threadId,
        string turnId,
        long expectedRevision,
        string mutationId,
        DurableApprovalRequestRecord request,
        TimelineItemRecord timelineItem)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(timelineItem);
        if (!ThreadContractValidator.IsSafeMutationId(mutationId))
            return ThreadStoreMutationResult.Failure(ThreadErrorCode.TimelineAppendConflict, "Approval request mutation is invalid.", threadId);
        string payloadHash = HashCanonical(new { Request = request, Timeline = timelineItem });
        return Mutate(threadId, expectedRevision, (layout, aggregate) =>
        {
            TurnRecord current = aggregate.Turns.SingleOrDefault(turn => turn.TurnId == turnId)
                ?? throw new ThreadContractException(ThreadErrorCode.TurnNotFound, "Turn was not found.");
            if (current.Status != TurnStatus.Running || current.ActiveApproval is not null)
                throw new ThreadContractException(ThreadErrorCode.TurnTransitionInvalid, "Turn cannot request approval in its current state.");
            DurableApprovalRequestRecord bound = request with
            {
                ThreadId = threadId,
                TurnId = turnId,
                TurnRevision = current.Revision + 1,
                ApprovalRevision = current.ApprovalRevision + 1
            };
            TurnRecord updated = current with
            {
                Revision = current.Revision + 1,
                Status = TurnStatus.WaitingForApproval,
                ActiveApproval = bound,
                ApprovalRevision = bound.ApprovalRevision
            };
            return CommitTurnRevisionWithTimeline(layout, aggregate, current, updated, timelineItem, mutationId, payloadHash);
        }, allowIdempotentRevisionMismatch: true, mutationId: mutationId, payloadHash: payloadHash);
    }

    public ThreadStoreMutationResult ResolveApproval(
        string threadId,
        string turnId,
        long expectedRevision,
        string requestId,
        string decision,
        long expectedTurnRevision,
        long expectedApprovalRevision,
        string mutationId,
        DateTimeOffset decidedAtUtc,
        TimelineItemRecord timelineItem)
    {
        if (decision is not ("approve" or "deny") || !ThreadContractValidator.IsSafeMutationId(mutationId))
            return ThreadStoreMutationResult.Failure(ThreadErrorCode.TurnTransitionInvalid, "Approval decision is invalid.", threadId);
        ArgumentNullException.ThrowIfNull(timelineItem);
        string payloadHash = HashCanonical(new { requestId, decision, expectedTurnRevision, expectedApprovalRevision, Timeline = timelineItem });
        return Mutate(threadId, expectedRevision, (layout, aggregate) =>
        {
            TurnRecord current = aggregate.Turns.SingleOrDefault(turn => turn.TurnId == turnId)
                ?? throw new ThreadContractException(ThreadErrorCode.TurnNotFound, "Turn was not found.");
            DurableApprovalRequestRecord approval = current.ActiveApproval ??
                throw new ThreadContractException(ThreadErrorCode.TurnTransitionInvalid, "Approval request is no longer active.");
            if (current.Status != TurnStatus.WaitingForApproval || current.Revision != expectedTurnRevision ||
                current.ApprovalRevision != expectedApprovalRevision || approval.RequestId != requestId || decidedAtUtc >= approval.ExpiresAtUtc)
                throw new ThreadContractException(ThreadErrorCode.ThreadRevisionConflict, "Approval request is stale or expired.");
            TurnRecord updated = current with
            {
                Revision = current.Revision + 1,
                Status = TurnStatus.Running,
                ActiveApproval = null
            };
            return CommitTurnRevisionWithTimeline(layout, aggregate, current, updated, timelineItem, mutationId, payloadHash);
        }, allowIdempotentRevisionMismatch: true, mutationId: mutationId, payloadHash: payloadHash);
    }

    public ThreadStoreMutationResult RequestCancel(
        string threadId,
        string turnId,
        long expectedRevision,
        long expectedTurnRevision,
        string mutationId,
        DateTimeOffset requestedAtUtc,
        TimelineItemRecord timelineItem)
    {
        if (!ThreadContractValidator.IsSafeMutationId(mutationId))
            return ThreadStoreMutationResult.Failure(ThreadErrorCode.TurnTransitionInvalid, "Cancel mutation is invalid.", threadId);
        ArgumentNullException.ThrowIfNull(timelineItem);
        string payloadHash = HashCanonical(new { turnId, expectedTurnRevision, Timeline = timelineItem });
        return Mutate(threadId, expectedRevision, (layout, aggregate) =>
        {
            TurnRecord current = aggregate.Turns.SingleOrDefault(turn => turn.TurnId == turnId)
                ?? throw new ThreadContractException(ThreadErrorCode.TurnNotFound, "Turn was not found.");
            if (current.Revision != expectedTurnRevision || current.Status is not (TurnStatus.Queued or TurnStatus.Running or TurnStatus.WaitingForApproval))
                throw new ThreadContractException(ThreadErrorCode.ThreadRevisionConflict, "Turn cancel request is stale.");
            TurnRecord updated = current with
            {
                Revision = current.Revision + 1,
                Status = TurnStatus.Canceling,
                StartedAtUtc = current.StartedAtUtc ?? requestedAtUtc,
                ActiveApproval = null,
                RecoveryRequired = true
            };
            return CommitTurnRevisionWithTimeline(layout, aggregate, current, updated, timelineItem, mutationId, payloadHash);
        }, allowIdempotentRevisionMismatch: true, mutationId: mutationId, payloadHash: payloadHash);
    }

    public ThreadStoreMutationResult AppendTimeline(
        string threadId,
        string turnId,
        long expectedRevision,
        long expectedNextSequence,
        string mutationId,
        IReadOnlyList<TimelineItemRecord> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (!ThreadContractValidator.IsSafeMutationId(mutationId) || items.Count == 0)
        {
            return ThreadStoreMutationResult.Failure(ThreadErrorCode.TimelineAppendConflict, "Timeline mutation is invalid.", threadId);
        }

        string payloadHash;
        try
        {
            foreach (TimelineItemRecord item in items)
            {
                ThreadContractValidator.ValidateTimelineItem(item);
            }

            payloadHash = HashCanonical(items);
        }
        catch (ThreadContractException exception)
        {
            return ThreadStoreMutationResult.Failure(exception.ErrorCode, exception.Message, threadId);
        }

        return Mutate(threadId, expectedRevision, (layout, aggregate) =>
        {
            ThreadMutationReceiptRecord? existing = aggregate.Record.MutationReceipts
                .SingleOrDefault(receipt => receipt.MutationId == mutationId);
            if (existing is not null)
            {
                if (existing.PayloadSha256 != payloadHash)
                {
                    throw new ThreadContractException(ThreadErrorCode.TimelineAppendConflict, "Timeline mutation id was reused with different content.");
                }

                return new ThreadAggregate(aggregate.Record, aggregate.Turns);
            }

            if (aggregate.Record.Revision != expectedRevision || expectedNextSequence != aggregate.Record.CommittedSequence + 1)
            {
                throw new ThreadContractException(ThreadErrorCode.ThreadRevisionConflict, "Thread revision or next sequence changed before append.");
            }

            TurnRecord current = aggregate.Turns.SingleOrDefault(turn => turn.TurnId == turnId)
                ?? throw new ThreadContractException(ThreadErrorCode.TurnNotFound, "Turn was not found.");
            if (items.Count > ThreadPersistenceLimits.MaxTimelineItemsPerThread - aggregate.Record.TimelineItemCount)
            {
                throw new ThreadContractException(ThreadErrorCode.TimelineLimitExceeded, "Thread timeline item limit was exceeded.");
            }

            long sequence = expectedNextSequence;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            IReadOnlyList<TimelineItemRecord> committed = ReadCommittedTimeline(layout, aggregate.Record, aggregate.Turns);
            DateTimeOffset previousTimestamp = committed.LastOrDefault()?.TimestampUtc ?? current.CreatedAtUtc;
            foreach (TimelineItemRecord item in items)
            {
                if (item.ThreadId != threadId || item.TurnId != turnId || item.Sequence != sequence++ || !ids.Add(item.ItemId) ||
                    item.TimestampUtc < previousTimestamp)
                {
                    throw new ThreadContractException(ThreadErrorCode.TimelineAppendConflict, "Timeline sequence or binding is invalid.");
                }

                previousTimestamp = item.TimestampUtc;
            }

            SerializedRecord<TimelineItemRecord>[] serializedItems = items
                .Select(item => SerializeRecord(item, ThreadPersistenceLimits.MaxTimelineItemBytes, ThreadErrorCode.TimelineLimitExceeded))
                .ToArray();
            for (int index = 0; index < items.Count; index++)
            {
                WriteImmutableJson(GetTimelinePath(layout, items[index]), serializedItems[index].Json);
            }

            int newCount = current.TimelineItemCount + items.Count;
            TurnRecord updatedTurn = current with
            {
                Revision = current.Revision + 1,
                TimelineFirstSequence = current.TimelineFirstSequence ?? items[0].Sequence,
                TimelineLastSequence = items[^1].Sequence,
                TimelineItemCount = newCount
            };
            SerializedRecord<TurnRecord> serializedTurn = SerializeRecord(updatedTurn, ThreadPersistenceLimits.MaxTurnSnapshotBytes, ThreadErrorCode.TurnLimitExceeded);
            WriteImmutableJson(GetTurnPath(layout, updatedTurn.TurnId, updatedTurn.Revision), serializedTurn.Json);
            ThreadMutationReceiptRecord receipt = new()
            {
                MutationId = mutationId,
                PayloadSha256 = payloadHash,
                ResultRevision = aggregate.Record.Revision + 1,
                FirstSequence = items[0].Sequence,
                LastSequence = items[^1].Sequence,
                ItemIds = items.Select(item => item.ItemId).ToArray()
            };
            long itemBytes = serializedItems.Sum(item => (long)item.ByteCount);
            long oldTurnBytes = new FileInfo(GetTurnPath(layout, current.TurnId, current.Revision)).Length;
            ThreadRecord record = UpdateTurnManifest(aggregate.Record, updatedTurn, serializedTurn.Sha256) with
            {
                Revision = aggregate.Record.Revision + 1,
                UpdatedAtUtc = Max(aggregate.Record.UpdatedAtUtc, items.Max(item => item.TimestampUtc)),
                CommittedSequence = items[^1].Sequence,
                TimelineItemCount = aggregate.Record.TimelineItemCount + items.Count,
                PersistedByteCount = aggregate.Record.PersistedByteCount - oldTurnBytes + serializedTurn.ByteCount + itemBytes,
                MutationReceipts = aggregate.Record.MutationReceipts.Append(receipt).ToArray()
            };
            WriteManifest(layout, record);
            TryDeleteFile(GetTurnPath(layout, current.TurnId, current.Revision));
            return new ThreadAggregate(record, ReplaceTurn(aggregate.Turns, updatedTurn));
        }, allowIdempotentRevisionMismatch: true, mutationId: mutationId, payloadHash: payloadHash);
    }

    public ThreadStoreMutationResult RecoverInterrupted(
        string threadId,
        long expectedRevision,
        DateTimeOffset recoveredAtUtc,
        string mutationId)
    {
        try
        {
            ValidateUtcTimestamp(recoveredAtUtc);
        }
        catch (ThreadContractException exception)
        {
            return ThreadStoreMutationResult.Failure(exception.ErrorCode, exception.Message, threadId);
        }

        return Mutate(threadId, expectedRevision, (layout, aggregate) =>
        {
            if (aggregate.Record.ActiveTurnId is null)
            {
                throw new ThreadContractException(ThreadErrorCode.TurnTransitionInvalid, "Thread has no interrupted turn.");
            }

            TurnRecord current = aggregate.Turns.Single(turn => turn.TurnId == aggregate.Record.ActiveTurnId);
            if (current.Status is not (TurnStatus.Running or TurnStatus.WaitingForApproval or TurnStatus.Canceling))
            {
                throw new ThreadContractException(ThreadErrorCode.TurnTransitionInvalid, "Turn does not require interrupted recovery.");
            }

            if (recoveredAtUtc < aggregate.Record.UpdatedAtUtc)
            {
                throw new ThreadContractException(ThreadErrorCode.ThreadRecordCorrupt, "Recovery timestamp is not monotonic.");
            }

            long firstSequence = aggregate.Record.CommittedSequence + 1;
            string seed = threadId + ":" + mutationId;
            ThreadSourcePointerRecord? source = current.SourcePointers.FirstOrDefault();
            TimelineItemRecord warning = new()
            {
                ItemId = ThreadIdentity.CreateDeterministicItemId(seed, "recovery-warning", 0),
                ThreadId = threadId,
                TurnId = current.TurnId,
                Sequence = firstSequence,
                TimestampUtc = recoveredAtUtc,
                Type = TimelineItemType.WarningRaised,
                Source = source,
                Status = "failed",
                Summary = "Turn was interrupted before completion.",
                Payload = new TimelinePayloadRecord { Warning = new TimelineWarningPayloadRecord("interrupted") },
                Redaction = new TimelineRedactionRecord { Applied = false }
            };
            TimelineItemRecord completed = new()
            {
                ItemId = ThreadIdentity.CreateDeterministicItemId(seed, "recovery-completed", 1),
                ThreadId = threadId,
                TurnId = current.TurnId,
                Sequence = firstSequence + 1,
                TimestampUtc = recoveredAtUtc,
                Type = TimelineItemType.TurnCompleted,
                Source = source,
                Status = "failed",
                Summary = "Turn failed because execution was interrupted.",
                Payload = new TimelinePayloadRecord { TurnCompleted = new TimelineTurnCompletedPayloadRecord("interrupted", "interrupted") },
                Redaction = new TimelineRedactionRecord { Applied = false }
            };
            return CommitRecovery(layout, aggregate, current, recoveredAtUtc, mutationId, [warning, completed]);
        });
    }

    public ThreadTimelinePageResult ReadTimelinePage(
        string threadId,
        long afterSequence = 0,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (afterSequence < 0 || limit is < 1 or > 100)
        {
            return ThreadTimelinePageResult.Failure(ThreadErrorCode.TimelineSequenceInvalid, "Timeline cursor or limit is invalid.", threadId);
        }

        cancellationToken.ThrowIfCancellationRequested();
        ThreadStoreReadResult read = Read(threadId, cancellationToken);
        if (!read.Succeeded || read.Aggregate is null)
        {
            return ThreadTimelinePageResult.Failure(
                read.Diagnostic?.ErrorCode ?? ThreadErrorCode.ThreadRecordCorrupt,
                read.Diagnostic?.SafeMessage ?? "Thread could not be read.", threadId);
        }

        try
        {
            ThreadStoreLayout layout = GetLayout(threadId);
            IReadOnlyList<TimelineItemRecord> all = ReadCommittedTimeline(
                layout, read.Aggregate.Record, read.Aggregate.Turns, cancellationToken);
            TimelineItemRecord[] page = all.Where(item => item.Sequence > afterSequence).Take(limit + 1).ToArray();
            bool truncated = page.Length > limit;
            TimelineItemRecord[] items = page.Take(limit).ToArray();
            return new ThreadTimelinePageResult(
                true,
                items,
                truncated && items.Length > 0 ? items[^1].Sequence : null,
                truncated,
                null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ThreadContractException exception)
        {
            return ThreadTimelinePageResult.Failure(exception.ErrorCode, exception.Message, threadId);
        }
        catch (JsonException)
        {
            return ThreadTimelinePageResult.Failure(ThreadErrorCode.TimelineRecordCorrupt, "Timeline JSON is corrupt.", threadId);
        }
        catch (Exception exception) when (IsStoreException(exception) || exception is InvalidOperationException or ArgumentException)
        {
            return ThreadTimelinePageResult.Failure(ThreadErrorCode.ThreadStoreUnavailable, "Timeline could not be read.", threadId);
        }
    }

    private ThreadStoreMutationResult MutateManifest(
        string threadId,
        long expectedRevision,
        Func<ThreadAggregate, ThreadRecord> mutation) =>
        Mutate(threadId, expectedRevision, (layout, aggregate) =>
        {
            ThreadRecord record = mutation(aggregate);
            WriteManifest(layout, record);
            return new ThreadAggregate(record, aggregate.Turns);
        });

    private ThreadStoreMutationResult Mutate(
        string threadId,
        long expectedRevision,
        Func<ThreadStoreLayout, ThreadAggregate, ThreadAggregate> mutation,
        bool allowIdempotentRevisionMismatch = false,
        string? mutationId = null,
        string? payloadHash = null)
    {
        try
        {
            ThreadStoreLayout layout = GetLayout(threadId);
            EnsureNoReparseInExistingChain(layout.ThreadRoot);
            using ThreadMutationLock threadLock = AcquireLock(layout);
            ThreadStoreReadResult read = ReadValidated(layout);
            if (!read.Succeeded || read.Aggregate is null)
            {
                return ThreadStoreMutationResult.Failure(
                    read.Diagnostic?.ErrorCode ?? ThreadErrorCode.ThreadRecordCorrupt,
                    read.Diagnostic?.SafeMessage ?? "Thread could not be read.", threadId);
            }

            if (allowIdempotentRevisionMismatch && mutationId is not null)
            {
                ThreadMutationReceiptRecord? receipt = read.Aggregate.Record.MutationReceipts
                    .SingleOrDefault(candidate => candidate.MutationId == mutationId);
                if (receipt is not null)
                {
                    if (receipt.PayloadSha256 == payloadHash)
                    {
                        return ThreadStoreMutationResult.Success(read.Aggregate, idempotent: true);
                    }

                    return ThreadStoreMutationResult.Failure(ThreadErrorCode.TimelineAppendConflict, "Mutation id was reused with different content.", threadId);
                }
            }

            if (read.Aggregate.Record.Revision != expectedRevision)
            {
                return ThreadStoreMutationResult.Failure(ThreadErrorCode.ThreadRevisionConflict, "Thread revision changed before update.", threadId);
            }

            ThreadAggregate updated = mutation(layout, read.Aggregate);
            return ThreadStoreMutationResult.Success(updated);
        }
        catch (ThreadContractException exception)
        {
            return ThreadStoreMutationResult.Failure(exception.ErrorCode, exception.Message, threadId);
        }
        catch (JsonException)
        {
            return ThreadStoreMutationResult.Failure(ThreadErrorCode.ThreadRecordCorrupt, "Thread JSON is corrupt.", threadId);
        }
        catch (IOException exception)
        {
            return ThreadStoreMutationResult.Failure(
                IsSharingViolation(exception) ? ThreadErrorCode.ThreadRevisionConflict : ThreadErrorCode.ThreadWriteFailed,
                IsSharingViolation(exception) ? "Thread is already being modified." : "Thread could not be updated.", threadId);
        }
        catch (Exception exception) when (IsStoreException(exception) || exception is InvalidOperationException or ArgumentException)
        {
            return ThreadStoreMutationResult.Failure(ThreadErrorCode.ThreadStoreUnavailable, "Thread could not be updated.", threadId);
        }
    }

    private ThreadStoreReadResult ReadValidated(
        ThreadStoreLayout layout,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(layout.ThreadRoot))
        {
            return ThreadStoreReadResult.Failure(ThreadErrorCode.ThreadNotFound, "Thread was not found.", Path.GetFileName(layout.ThreadRoot));
        }

        EnsureNoReparseInExistingChain(layout.ThreadRoot);
        if (!File.Exists(layout.ManifestPath))
        {
            return ThreadStoreReadResult.Failure(ThreadErrorCode.ThreadReferenceMissing, "Thread manifest is missing.", Path.GetFileName(layout.ThreadRoot));
        }

        ThreadRecord record = ReadRecord<ThreadRecord>(layout.ManifestPath, ThreadPersistenceLimits.MaxThreadManifestBytes,
            ThreadErrorCode.ThreadRecordCorrupt, ThreadErrorCode.ThreadSchemaUnsupported, ThreadRecord.CurrentSchemaVersion);
        if (record.ThreadId != Path.GetFileName(layout.ThreadRoot))
        {
            return ThreadStoreReadResult.Failure(ThreadErrorCode.ThreadRecordCorrupt, "Thread manifest path binding is invalid.", record.ThreadId);
        }

        ThreadContractValidator.ValidateThread(record);
        var turns = new List<TurnRecord>(record.Turns.Count);
        long persistedBytes = 0;
        foreach (ThreadTurnReferenceRecord reference in record.Turns)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = GetTurnPath(layout, reference.TurnId, reference.Revision);
            if (!File.Exists(path))
            {
                return ThreadStoreReadResult.Failure(ThreadErrorCode.ThreadReferenceMissing, "Committed turn snapshot is missing.", record.ThreadId);
            }

            TurnRecord turn = ReadRecord<TurnRecord>(path, ThreadPersistenceLimits.MaxTurnSnapshotBytes,
                ThreadErrorCode.TurnRecordCorrupt, ThreadErrorCode.ThreadSchemaUnsupported, TurnRecord.CurrentSchemaVersion);
            ThreadContractValidator.ValidateTurn(turn);
            string sha = HashFile(path);
            if (turn.ThreadId != record.ThreadId || turn.TurnId != reference.TurnId || turn.Ordinal != reference.Ordinal ||
                turn.Revision != reference.Revision || turn.Status != reference.Status || sha != reference.SnapshotSha256)
            {
                return ThreadStoreReadResult.Failure(ThreadErrorCode.TurnRecordCorrupt, "Committed turn snapshot binding or hash is invalid.", record.ThreadId);
            }

            persistedBytes += new FileInfo(path).Length;
            turns.Add(turn);
        }

        IReadOnlyList<TimelineItemRecord> timeline = ReadCommittedTimeline(
            layout, record, turns, cancellationToken);
        foreach (TimelineItemRecord item in timeline)
        {
            persistedBytes += new FileInfo(GetTimelinePath(layout, item)).Length;
        }

        if (persistedBytes != record.PersistedByteCount)
        {
            return ThreadStoreReadResult.Failure(ThreadErrorCode.ThreadRecordCorrupt, "Thread persisted byte count is inconsistent.", record.ThreadId);
        }

        string derived = ThreadStateMachine.DeriveThreadStatus(turns, record.ArchivedAtUtc is not null);
        if (record.Status != derived)
        {
            return ThreadStoreReadResult.Failure(ThreadErrorCode.ThreadRecordCorrupt, "Thread status is inconsistent with its turns.", record.ThreadId);
        }

        return ThreadStoreReadResult.Success(new ThreadAggregate(record, turns));
    }

    private static IReadOnlyList<TimelineItemRecord> ReadCommittedTimeline(
        ThreadStoreLayout layout,
        ThreadRecord record,
        IReadOnlyList<TurnRecord> turns,
        CancellationToken cancellationToken = default)
    {
        if (record.CommittedSequence == 0)
        {
            if (turns.Any(turn => turn.TimelineItemCount != 0 ||
                turn.TimelineFirstSequence is not null || turn.TimelineLastSequence is not null))
            {
                throw new ThreadContractException(
                    ThreadErrorCode.TurnRecordCorrupt,
                    "Turn timeline range does not match the empty committed timeline.");
            }

            return [];
        }

        if (!Directory.Exists(layout.TimelineRoot))
        {
            throw new ThreadContractException(ThreadErrorCode.ThreadReferenceMissing, "Committed timeline directory is missing.");
        }

        var committedPaths = new Dictionary<long, string>();
        foreach (string path in Directory.EnumerateFiles(layout.TimelineRoot, "*.timeline.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string fileName = Path.GetFileName(path);
            int separator = fileName.IndexOf('-');
            if (separator <= 0 || !long.TryParse(fileName.AsSpan(0, separator), out long sequence))
            {
                continue;
            }

            if (sequence <= record.CommittedSequence && !committedPaths.TryAdd(sequence, path))
            {
                throw new ThreadContractException(ThreadErrorCode.TimelineRecordCorrupt, "Committed timeline sequence is duplicated.");
            }
        }

        var turnIds = turns.Select(turn => turn.TurnId).ToHashSet(StringComparer.Ordinal);
        Dictionary<string, TurnRecord> turnsById = turns.ToDictionary(turn => turn.TurnId, StringComparer.Ordinal);
        var items = new List<TimelineItemRecord>(record.TimelineItemCount);
        DateTimeOffset? previousTimestamp = null;
        for (long sequence = 1; sequence <= record.CommittedSequence; sequence++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!committedPaths.TryGetValue(sequence, out string? path))
            {
                throw new ThreadContractException(ThreadErrorCode.ThreadReferenceMissing, "Committed timeline item is missing.");
            }

            TimelineItemRecord item = ReadRecord<TimelineItemRecord>(path, ThreadPersistenceLimits.MaxTimelineItemBytes,
                ThreadErrorCode.TimelineRecordCorrupt, ThreadErrorCode.ThreadSchemaUnsupported, TimelineItemRecord.CurrentSchemaVersion);
            ThreadContractValidator.ValidateTimelineItem(item);
            string expectedFileName = Path.GetFileName(GetTimelinePath(layout, item));
            if (item.ThreadId != record.ThreadId || item.Sequence != sequence || !turnIds.Contains(item.TurnId) ||
                !string.Equals(Path.GetFileName(path), expectedFileName, StringComparison.Ordinal) ||
                item.TimestampUtc < turnsById[item.TurnId].CreatedAtUtc ||
                (previousTimestamp is not null && item.TimestampUtc < previousTimestamp))
            {
                throw new ThreadContractException(ThreadErrorCode.TimelineRecordCorrupt, "Committed timeline binding is invalid.");
            }

            items.Add(item);
            previousTimestamp = item.TimestampUtc;
        }

        foreach (TurnRecord turn in turns)
        {
            TimelineItemRecord[] owned = items.Where(item => item.TurnId == turn.TurnId).ToArray();
            if (owned.Length != turn.TimelineItemCount ||
                (owned.Length > 0 && (owned[0].Sequence != turn.TimelineFirstSequence || owned[^1].Sequence != turn.TimelineLastSequence)))
            {
                throw new ThreadContractException(ThreadErrorCode.TurnRecordCorrupt, "Turn timeline range does not match committed items.");
            }
        }

        return items;
    }

    private static ThreadAggregate CommitTurnRevision(
        ThreadStoreLayout layout,
        ThreadAggregate aggregate,
        TurnRecord current,
        TurnRecord updated,
        ThreadMutationReceiptRecord? receipt = null)
    {
        SerializedRecord<TurnRecord> serialized = SerializeRecord(updated, ThreadPersistenceLimits.MaxTurnSnapshotBytes, ThreadErrorCode.TurnLimitExceeded);
        WriteImmutableJson(GetTurnPath(layout, updated.TurnId, updated.Revision), serialized.Json);
        long oldBytes = new FileInfo(GetTurnPath(layout, current.TurnId, current.Revision)).Length;
        TurnRecord[] turns = ReplaceTurn(aggregate.Turns, updated);
        ThreadRecord record = UpdateTurnManifest(aggregate.Record, updated, serialized.Sha256) with
        {
            Revision = aggregate.Record.Revision + 1,
            UpdatedAtUtc = Max(aggregate.Record.UpdatedAtUtc, updated.CompletedAtUtc ?? updated.StartedAtUtc ?? updated.CreatedAtUtc),
            Status = ThreadStateMachine.DeriveThreadStatus(turns, archived: false),
            ActiveTurnId = TurnStatus.IsActive(updated.Status) ? updated.TurnId : null,
            PersistedByteCount = aggregate.Record.PersistedByteCount - oldBytes + serialized.ByteCount,
            MutationReceipts = receipt is null ? aggregate.Record.MutationReceipts : aggregate.Record.MutationReceipts.Append(receipt).ToArray()
        };
        WriteManifest(layout, record);
        TryDeleteFile(GetTurnPath(layout, current.TurnId, current.Revision));
        return new ThreadAggregate(record, turns);
    }

    private static ThreadAggregate CommitTurnRevisionWithTimeline(
        ThreadStoreLayout layout,
        ThreadAggregate aggregate,
        TurnRecord current,
        TurnRecord updated,
        TimelineItemRecord item,
        string mutationId,
        string payloadHash)
    {
        long nextSequence = aggregate.Record.CommittedSequence + 1;
        if (item.ThreadId != aggregate.Record.ThreadId || item.TurnId != current.TurnId || item.Sequence != nextSequence ||
            item.TimestampUtc < aggregate.Record.UpdatedAtUtc)
            throw new ThreadContractException(ThreadErrorCode.TimelineAppendConflict, "Turn lifecycle timeline binding is invalid.");
        ThreadContractValidator.ValidateTimelineItem(item);
        TurnRecord committedTurn = updated with
        {
            TimelineFirstSequence = current.TimelineFirstSequence ?? item.Sequence,
            TimelineLastSequence = item.Sequence,
            TimelineItemCount = current.TimelineItemCount + 1
        };
        ThreadContractValidator.ValidateTurn(committedTurn);
        SerializedRecord<TurnRecord> serializedTurn = SerializeRecord(committedTurn, ThreadPersistenceLimits.MaxTurnSnapshotBytes, ThreadErrorCode.TurnLimitExceeded);
        SerializedRecord<TimelineItemRecord> serializedItem = SerializeRecord(item, ThreadPersistenceLimits.MaxTimelineItemBytes, ThreadErrorCode.TimelineLimitExceeded);
        WriteImmutableJson(GetTurnPath(layout, committedTurn.TurnId, committedTurn.Revision), serializedTurn.Json);
        WriteImmutableJson(GetTimelinePath(layout, item), serializedItem.Json);
        long oldTurnBytes = new FileInfo(GetTurnPath(layout, current.TurnId, current.Revision)).Length;
        ThreadMutationReceiptRecord receipt = new()
        {
            MutationId = mutationId,
            PayloadSha256 = payloadHash,
            ResultRevision = aggregate.Record.Revision + 1,
            FirstSequence = item.Sequence,
            LastSequence = item.Sequence,
            ItemIds = [item.ItemId]
        };
        TurnRecord[] turns = ReplaceTurn(aggregate.Turns, committedTurn);
        ThreadRecord record = UpdateTurnManifest(aggregate.Record, committedTurn, serializedTurn.Sha256) with
        {
            Revision = aggregate.Record.Revision + 1,
            UpdatedAtUtc = item.TimestampUtc,
            Status = ThreadStateMachine.DeriveThreadStatus(turns, archived: false),
            ActiveTurnId = TurnStatus.IsActive(committedTurn.Status) ? committedTurn.TurnId : null,
            CommittedSequence = item.Sequence,
            TimelineItemCount = aggregate.Record.TimelineItemCount + 1,
            PersistedByteCount = aggregate.Record.PersistedByteCount - oldTurnBytes + serializedTurn.ByteCount + serializedItem.ByteCount,
            MutationReceipts = aggregate.Record.MutationReceipts.Append(receipt).ToArray()
        };
        WriteManifest(layout, record);
        TryDeleteFile(GetTurnPath(layout, current.TurnId, current.Revision));
        return new ThreadAggregate(record, turns);
    }

    private static ThreadAggregate CommitRecovery(
        ThreadStoreLayout layout,
        ThreadAggregate aggregate,
        TurnRecord current,
        DateTimeOffset recoveredAtUtc,
        string mutationId,
        IReadOnlyList<TimelineItemRecord> items)
    {
        foreach (TimelineItemRecord item in items)
        {
            ThreadContractValidator.ValidateTimelineItem(item);
        }

        string payloadHash = HashCanonical(items);
        ThreadMutationReceiptRecord? existing = aggregate.Record.MutationReceipts.SingleOrDefault(receipt => receipt.MutationId == mutationId);
        if (existing is not null)
        {
            if (existing.PayloadSha256 != payloadHash)
            {
                throw new ThreadContractException(ThreadErrorCode.TimelineAppendConflict, "Recovery mutation id was reused with different content.");
            }

            return aggregate;
        }

        SerializedRecord<TimelineItemRecord>[] serializedItems = items
            .Select(item => SerializeRecord(item, ThreadPersistenceLimits.MaxTimelineItemBytes, ThreadErrorCode.TimelineLimitExceeded))
            .ToArray();
        for (int index = 0; index < items.Count; index++)
        {
            WriteImmutableJson(GetTimelinePath(layout, items[index]), serializedItems[index].Json);
        }

        TurnRecord updated = current with
        {
            Revision = current.Revision + 1,
            Status = TurnStatus.Failed,
            CompletedAtUtc = recoveredAtUtc,
            StopReason = "interrupted",
            ErrorCode = "interrupted",
            RecoveryRequired = true,
            ActiveApproval = null,
            TimelineFirstSequence = current.TimelineFirstSequence ?? items[0].Sequence,
            TimelineLastSequence = items[^1].Sequence,
            TimelineItemCount = current.TimelineItemCount + items.Count
        };
        SerializedRecord<TurnRecord> serializedTurn = SerializeRecord(updated, ThreadPersistenceLimits.MaxTurnSnapshotBytes, ThreadErrorCode.TurnLimitExceeded);
        WriteImmutableJson(GetTurnPath(layout, updated.TurnId, updated.Revision), serializedTurn.Json);
        long oldTurnBytes = new FileInfo(GetTurnPath(layout, current.TurnId, current.Revision)).Length;
        long itemBytes = serializedItems.Sum(item => (long)item.ByteCount);
        ThreadMutationReceiptRecord receipt = new()
        {
            MutationId = mutationId,
            PayloadSha256 = payloadHash,
            ResultRevision = aggregate.Record.Revision + 1,
            FirstSequence = items[0].Sequence,
            LastSequence = items[^1].Sequence,
            ItemIds = items.Select(item => item.ItemId).ToArray()
        };
        ThreadRecord record = UpdateTurnManifest(aggregate.Record, updated, serializedTurn.Sha256) with
        {
            Revision = aggregate.Record.Revision + 1,
            Status = ThreadStatus.Failed,
            UpdatedAtUtc = recoveredAtUtc,
            ActiveTurnId = null,
            CommittedSequence = items[^1].Sequence,
            TimelineItemCount = aggregate.Record.TimelineItemCount + items.Count,
            PersistedByteCount = aggregate.Record.PersistedByteCount - oldTurnBytes + serializedTurn.ByteCount + itemBytes,
            MutationReceipts = aggregate.Record.MutationReceipts.Append(receipt).ToArray()
        };
        WriteManifest(layout, record);
        TryDeleteFile(GetTurnPath(layout, current.TurnId, current.Revision));
        return new ThreadAggregate(record, ReplaceTurn(aggregate.Turns, updated));
    }

    private static ThreadRecord BuildCommittedRecord(
        ThreadRecord template,
        IReadOnlyList<TurnRecord> turns,
        IReadOnlyDictionary<string, SerializedRecord<TurnRecord>> serializedTurns,
        IReadOnlyList<TimelineItemRecord> items,
        IReadOnlyList<SerializedRecord<TimelineItemRecord>> serializedItems)
    {
        ThreadTurnReferenceRecord[] references = turns.OrderBy(turn => turn.Ordinal)
            .Select(turn => CreateTurnReference(turn, serializedTurns[turn.TurnId].Sha256))
            .ToArray();
        long persistedBytes = serializedTurns.Values.Sum(value => (long)value.ByteCount) + serializedItems.Sum(value => (long)value.ByteCount);
        TurnRecord? active = turns.SingleOrDefault(turn => TurnStatus.IsActive(turn.Status));
        ThreadRecord committed = template with
        {
            Turns = references,
            Status = ThreadStateMachine.DeriveThreadStatus(turns, template.ArchivedAtUtc is not null),
            ActiveTurnId = active?.TurnId,
            CommittedSequence = items.Count,
            TimelineItemCount = items.Count,
            PersistedByteCount = persistedBytes
        };
        ThreadContractValidator.ValidateThread(committed);
        return committed;
    }

    private static void ValidateProjection(
        ThreadRecord record,
        IReadOnlyList<TurnRecord> turns,
        IReadOnlyList<TimelineItemRecord> timeline)
    {
        if (turns.Count > ThreadPersistenceLimits.MaxTurnsPerThread || timeline.Count > ThreadPersistenceLimits.MaxTimelineItemsPerThread)
        {
            throw new ThreadContractException(ThreadErrorCode.ThreadLimitExceeded, "Thread projection exceeds persisted limits.");
        }

        var turnIds = new HashSet<string>(StringComparer.Ordinal);
        int ordinal = 1;
        foreach (TurnRecord turn in turns.OrderBy(turn => turn.Ordinal))
        {
            ThreadContractValidator.ValidateTurn(turn);
            if (turn.ThreadId != record.ThreadId || turn.Ordinal != ordinal++ || !turnIds.Add(turn.TurnId))
            {
                throw new ThreadContractException(ThreadErrorCode.TurnRecordCorrupt, "Projected turn binding is invalid.");
            }
        }

        if (turns.Count(turn => TurnStatus.IsActive(turn.Status)) > 1)
        {
            throw new ThreadContractException(
                ThreadErrorCode.ThreadActiveTurnConflict,
                "Projected thread has more than one active turn.");
        }

        long sequence = 1;
        DateTimeOffset? previousTimestamp = null;
        Dictionary<string, TurnRecord> turnsById = turns.ToDictionary(turn => turn.TurnId, StringComparer.Ordinal);
        foreach (TimelineItemRecord item in timeline.OrderBy(item => item.Sequence))
        {
            ThreadContractValidator.ValidateTimelineItem(item);
            if (item.ThreadId != record.ThreadId || item.Sequence != sequence++ || !turnIds.Contains(item.TurnId) ||
                item.TimestampUtc < turnsById[item.TurnId].CreatedAtUtc ||
                (previousTimestamp is not null && item.TimestampUtc < previousTimestamp))
            {
                throw new ThreadContractException(ThreadErrorCode.TimelineAppendConflict, "Projected timeline binding is invalid.");
            }

            previousTimestamp = item.TimestampUtc;
        }

        foreach (TurnRecord turn in turns)
        {
            TimelineItemRecord[] owned = timeline.Where(item => item.TurnId == turn.TurnId).OrderBy(item => item.Sequence).ToArray();
            if (owned.Length != turn.TimelineItemCount ||
                (owned.Length > 0 && (owned[0].Sequence != turn.TimelineFirstSequence || owned[^1].Sequence != turn.TimelineLastSequence)))
            {
                throw new ThreadContractException(ThreadErrorCode.TurnRecordCorrupt, "Projected turn timeline range is invalid.");
            }
        }
    }

    private static ThreadRecord UpdateTurnManifest(ThreadRecord record, TurnRecord updated, string sha256)
    {
        ThreadTurnReferenceRecord[] references = record.Turns
            .Select(reference => reference.TurnId == updated.TurnId ? CreateTurnReference(updated, sha256) : reference)
            .ToArray();
        return record with { Turns = references };
    }

    private static ThreadTurnReferenceRecord CreateTurnReference(TurnRecord turn, string sha256) => new()
    {
        TurnId = turn.TurnId,
        Ordinal = turn.Ordinal,
        Revision = turn.Revision,
        Status = turn.Status,
        SnapshotSha256 = sha256,
        SourcePointers = turn.SourcePointers
    };

    private static TurnRecord[] ReplaceTurn(IReadOnlyList<TurnRecord> turns, TurnRecord updated) =>
        turns.Select(turn => turn.TurnId == updated.TurnId ? updated : turn).OrderBy(turn => turn.Ordinal).ToArray();

    private static void WriteManifest(ThreadStoreLayout layout, ThreadRecord record)
    {
        ThreadContractValidator.ValidateThread(record);
        SerializedRecord<ThreadRecord> serialized = SerializeRecord(record, ThreadPersistenceLimits.MaxThreadManifestBytes, ThreadErrorCode.ThreadLimitExceeded);
        WriteTextAtomically(layout.ManifestPath, serialized.Json, overwrite: true);
    }

    private static void WriteImmutableJson(string path, string json)
    {
        if (File.Exists(path))
        {
            string existing = ReadTextBounded(path, Encoding.UTF8.GetByteCount(json));
            if (existing == json)
            {
                return;
            }

            throw new ThreadContractException(ThreadErrorCode.TimelineAppendConflict, "Immutable thread child already exists with different content.");
        }

        WriteTextAtomically(path, json, overwrite: false);
    }

    private static SerializedRecord<T> SerializeRecord<T>(T value, int maxBytes, string limitErrorCode)
    {
        string json = JsonSerializer.Serialize(value, JsonOptions);
        int bytes = Encoding.UTF8.GetByteCount(json);
        if (bytes <= 0 || bytes > maxBytes)
        {
            throw new ThreadContractException(limitErrorCode, "Persisted thread record exceeds its byte limit.");
        }

        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        return new SerializedRecord<T>(json, bytes, hash);
    }

    private static T ReadRecord<T>(string path, int maxBytes, string corruptCode, string schemaCode, int currentSchema)
    {
        string json = ReadTextBounded(path, maxBytes);
        int schema = ReadSchemaVersion(json);
        if (schema != currentSchema)
        {
            throw new ThreadContractException(schemaCode, "Persisted thread record uses an unsupported schema.");
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions)
                ?? throw new ThreadContractException(corruptCode, "Persisted thread record is empty.");
        }
        catch (JsonException)
        {
            throw new ThreadContractException(corruptCode, "Persisted thread record is corrupt.");
        }
    }

    private static int ReadSchemaVersion(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
            return document.RootElement.TryGetProperty("schemaVersion", out JsonElement element) &&
                element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out int version)
                    ? version
                    : throw new ThreadContractException(ThreadErrorCode.ThreadRecordCorrupt, "schemaVersion is missing.");
        }
        catch (JsonException)
        {
            throw new ThreadContractException(ThreadErrorCode.ThreadRecordCorrupt, "Persisted thread JSON is corrupt.");
        }
    }

    private static ThreadMutationLock AcquireLock(ThreadStoreLayout layout)
    {
        EnsureNoReparseInExistingChain(layout.ThreadRoot);
        string lockIdentity = Path.GetFullPath(layout.ThreadRoot);
        if (OperatingSystem.IsWindows())
        {
            lockIdentity = lockIdentity.ToUpperInvariant();
        }

        string lockHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(lockIdentity))).ToLowerInvariant();
        string mutexName = (OperatingSystem.IsWindows() ? "Local\\" : string.Empty) +
            "CSharpAiCli.Thread." + lockHash[..32];
        var mutex = new Mutex(initiallyOwned: false, mutexName);
        bool acquired;
        try
        {
            acquired = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            acquired = true;
        }

        if (!acquired)
        {
            mutex.Dispose();
            throw new IOException("Thread is already being modified.", unchecked((int)0x80070020));
        }

        try
        {
            FileStream marker = new(
                layout.LockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.ReadWrite | FileShare.Delete,
                1,
                FileOptions.WriteThrough);
            return new ThreadMutationLock(mutex, marker);
        }
        catch
        {
            mutex.ReleaseMutex();
            mutex.Dispose();
            throw;
        }
    }

    internal static void WriteTextAtomically(string path, string value, bool overwrite)
    {
        string directory = Path.GetDirectoryName(path) ?? throw new IOException("Destination directory is unavailable.");
        string temporaryPath = Path.Combine(directory, Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            using (StreamWriter writer = new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(value);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, path, overwrite);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception exception) when (IsStoreException(exception))
            {
            }
        }
    }

    internal static string ReadTextBounded(string path, int maxBytes)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        if (stream.Length <= 0 || stream.Length > maxBytes)
        {
            throw new ThreadContractException(ThreadErrorCode.ThreadLimitExceeded, "Persisted thread record exceeds its read bound.");
        }

        using StreamReader reader = new(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
        string value = reader.ReadToEnd();
        if (stream.Length > maxBytes)
        {
            throw new ThreadContractException(ThreadErrorCode.ThreadLimitExceeded, "Persisted thread record changed beyond its read bound.");
        }

        return value;
    }

    public static void EnsureNoReparseInExistingChain(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string? root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ThreadContractException(ThreadErrorCode.ThreadPathUnsafe, "Thread path could not be rooted.");
        }

        string current = root;
        string relative = Path.GetRelativePath(root, fullPath);
        foreach (string segment in relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!Directory.Exists(current) && !File.Exists(current))
            {
                continue;
            }

            if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
            {
                throw new ThreadContractException(ThreadErrorCode.ThreadReparsePoint, "Thread paths containing reparse points are not supported.");
            }
        }
    }

    private static void EnsureNoReparseDescendants(string root)
    {
        EnsureNoReparseInExistingChain(root);
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            foreach (string path in Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.TopDirectoryOnly))
            {
                FileAttributes attributes = File.GetAttributes(path);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new ThreadContractException(ThreadErrorCode.ThreadReparsePoint, "Thread contains a reparse point.");
                }

                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    pending.Push(path);
                }
            }
        }
    }

    private static string GetTurnPath(ThreadStoreLayout layout, string turnId, long revision)
    {
        if (!ThreadIdentity.IsTurnId(turnId) || revision < 0)
        {
            throw new ThreadContractException(ThreadErrorCode.TurnIdInvalid, "Turn snapshot identity is invalid.");
        }

        string path = Path.GetFullPath(Path.Combine(layout.TurnsRoot, $"{turnId}.r{revision}.turn.json"));
        EnsureContained(layout.TurnsRoot, path);
        return path;
    }

    private static string GetTimelinePath(ThreadStoreLayout layout, TimelineItemRecord item)
    {
        if (!ThreadIdentity.IsItemId(item.ItemId) || item.Sequence <= 0)
        {
            throw new ThreadContractException(ThreadErrorCode.ItemIdInvalid, "Timeline item identity is invalid.");
        }

        string path = Path.GetFullPath(Path.Combine(layout.TimelineRoot, $"{item.Sequence:D12}-{item.ItemId}.timeline.json"));
        EnsureContained(layout.TimelineRoot, path);
        return path;
    }

    private static string HashFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string HashCanonical<T>(T value)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(json)).ToLowerInvariant();
    }

    private static void EnsureContained(string root, string candidate)
    {
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string normalizedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        string rooted = normalizedRoot + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!normalizedCandidate.StartsWith(rooted, comparison))
        {
            throw new ThreadContractException(ThreadErrorCode.ThreadPathUnsafe, "Thread path escaped the store root.");
        }
    }

    private static void ValidateUtcTimestamp(DateTimeOffset timestamp)
    {
        if (timestamp == default || timestamp.Offset != TimeSpan.Zero)
        {
            throw new ThreadContractException(ThreadErrorCode.ThreadRecordCorrupt, "Thread timestamp must be UTC.");
        }
    }

    private static DateTimeOffset Max(DateTimeOffset first, DateTimeOffset second) => first >= second ? first : second;

    private static bool IsSharingViolation(IOException exception) => (exception.HResult & 0xFFFF) is 32 or 33;

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (IsStoreException(exception))
        {
        }
    }

    private static bool IsStoreException(Exception exception) => exception is IOException
        or UnauthorizedAccessException
        or DirectoryNotFoundException
        or FileNotFoundException
        or NotSupportedException
        or PathTooLongException
        or DecoderFallbackException;

    private sealed record SerializedRecord<T>(string Json, int ByteCount, string Sha256);

    private sealed class ThreadMutationLock(Mutex mutex, FileStream marker) : IDisposable
    {
        private FileStream? marker = marker;
        private bool disposed;

        public void ReleaseMarker()
        {
            marker?.Dispose();
            marker = null;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            ReleaseMarker();
            try
            {
                mutex.ReleaseMutex();
            }
            finally
            {
                mutex.Dispose();
            }
        }
    }
}
