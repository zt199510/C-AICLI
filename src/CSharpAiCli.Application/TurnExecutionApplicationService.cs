using System.Security.Cryptography;
using System.Text;
using CSharpAiCli.Core;

namespace CSharpAiCli.Application;

public sealed class TurnExecutionApplicationService
{
    private readonly Func<CliEnvironmentSnapshot, ThreadStore> threadStoreFactory;
    private readonly Func<CliEnvironmentSnapshot, ComposerIntentStore> composerStoreFactory;
    private readonly Func<DateTimeOffset> clock;
    private readonly WorkspaceApplicationService workspaceService;

    public TurnExecutionApplicationService()
        : this(ThreadStore.Create, ComposerIntentStore.Create, () => DateTimeOffset.UtcNow, new WorkspaceApplicationService()) { }

    internal TurnExecutionApplicationService(
        Func<CliEnvironmentSnapshot, ThreadStore> threadStoreFactory,
        Func<CliEnvironmentSnapshot, ComposerIntentStore> composerStoreFactory,
        Func<DateTimeOffset> clock,
        WorkspaceApplicationService workspaceService)
    {
        this.threadStoreFactory = threadStoreFactory;
        this.composerStoreFactory = composerStoreFactory;
        this.clock = clock;
        this.workspaceService = workspaceService;
    }

    public ApplicationResult<TurnExecutionStateProjection> Start(TurnStartRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!ThreadIdentity.IsThreadId(request.ThreadId) || !ComposerIntentContractValidator.IsSafeMutationId(request.ClientMutationId))
            return Failure("turn-start-invalid", ApplicationErrorCategory.Validation, "Turn start request is invalid.");
        ApplicationResult<WorkspaceSnapshotProjection> workspace = workspaceService.Snapshot(request.Snapshot, cancellationToken);
        if (!workspace.Succeeded || workspace.Data is null)
            return ApplicationResult<TurnExecutionStateProjection>.Failure(workspace.Error!, workspace.Diagnostics);
        ThreadStore threads = threadStoreFactory(request.Snapshot);
        ComposerIntentStore composer = composerStoreFactory(request.Snapshot);
        ThreadStoreReadResult read = threads.Read(request.ThreadId, cancellationToken);
        if (!read.Succeeded || read.Aggregate is null) return ThreadFailure(read.Diagnostic);
        if (read.Aggregate.Record.WorkspaceId != workspace.Data.WorkspaceId || read.Aggregate.Record.WorkspaceRootIdentity != workspace.Data.RootPath)
            return Failure("workspace-changed", ApplicationErrorCategory.Workspace, "Thread belongs to a different workspace identity.");

        ComposerIntentStoreResult queueRead = composer.Get(workspace.Data.WorkspaceId, workspace.Data.RootPath, request.ThreadId, cancellationToken);
        if (!queueRead.Succeeded || queueRead.Queue is null) return ComposerFailure(queueRead.Diagnostic);
        string finalizeMutationId = request.ClientMutationId + ".finalize";
        if (queueRead.Queue.PendingIntent is null)
        {
            ComposerMutationReceiptRecord? completed = queueRead.Queue.MutationReceipts.SingleOrDefault(receipt => receipt.MutationId == finalizeMutationId && receipt.Operation == "finalize");
            TurnRecord? prior = completed?.TurnId is null ? null : read.Aggregate.Turns.SingleOrDefault(turn => turn.TurnId == completed.TurnId);
            return prior is not null && prior.CanonicalInputSha256 == completed!.CanonicalInputSha256
                ? ApplicationResult<TurnExecutionStateProjection>.Success(Project(read.Aggregate, prior, true))
                : Failure("composer-intent-not-found", ApplicationErrorCategory.NotFound, "Thread has no pending composer intent.");
        }
        if (queueRead.Queue.Revision != request.ExpectedQueueRevision && queueRead.Queue.Claim is null)
            return Failure("composer-queue-revision-conflict", ApplicationErrorCategory.Conflict, "Composer queue revision changed.");
        if (read.Aggregate.Record.Revision != request.ExpectedThreadRevision)
            return Failure("thread-revision-conflict", ApplicationErrorCategory.Conflict, "Thread revision changed before start.");
        if (read.Aggregate.Record.ActiveTurnId is not null)
            return Failure("write-execution-busy", ApplicationErrorCategory.Conflict, "The pending input remains queued until the active turn is terminal.");

        PendingComposerIntentRecord intent = queueRead.Queue.PendingIntent;
        if (intent.WorkspaceId != workspace.Data.WorkspaceId || intent.WorkspaceRootIdentity != workspace.Data.RootPath ||
            intent.EffectiveModel != request.Snapshot.Configuration.Model || intent.ApprovalMode != request.Snapshot.Configuration.ApprovalMode.ToString())
            return Failure("turn-input-stale", ApplicationErrorCategory.Conflict, "Composer input authority changed before start.");
        ApplicationError? contextError = RevalidateContext(intent, workspace.Data.RootPath);
        if (contextError is not null) return ApplicationResult<TurnExecutionStateProjection>.Failure(contextError);

        ComposerIntentStoreResult claimed = composer.Claim(workspace.Data.WorkspaceId, workspace.Data.RootPath, request.ThreadId,
            request.ExpectedQueueRevision, request.ClientMutationId + ".claim", request.ClientMutationId, NowAtLeast(read.Aggregate.Record.UpdatedAtUtc));
        if (!claimed.Succeeded || claimed.Queue?.Claim is null) return ComposerFailure(claimed.Diagnostic);
        ComposerIntentClaimRecord claim = claimed.Queue.Claim;

        DateTimeOffset createdAt = NowAtLeast(read.Aggregate.Record.UpdatedAtUtc);
        TurnRecord turn = new()
        {
            TurnId = claim.TurnId,
            ThreadId = request.ThreadId,
            Ordinal = read.Aggregate.Record.Turns.Count + 1,
            CreatedAtUtc = createdAt,
            TaskSummary = ApplicationProjection.Safe(intent.Prompt, ThreadPersistenceLimits.MaxTaskSummaryBytes),
            Mode = "desktop-write",
            SourceCorrelation = intent.IntentId,
            ExecutionInput = intent,
            CanonicalInputSha256 = claim.CanonicalInputSha256
        };
        string prompt = ApplicationProjection.Safe(intent.Prompt, ThreadPersistenceLimits.MaxTimelineSummaryBytes);
        TimelineItemRecord message = Item(read.Aggregate.Record, turn.TurnId, "turn-start-user", TimelineItemType.UserMessage,
            "committed", prompt, createdAt, new TimelinePayloadRecord { Message = new TimelineMessagePayloadRecord(prompt) });
        ThreadStoreMutationResult started = threads.StartTurnFromIntent(request.ThreadId, request.ExpectedThreadRevision,
            request.ClientMutationId, claim, intent, turn, message);
        if (!started.Succeeded || started.Aggregate is null) return ThreadFailure(started.Diagnostic);
        ComposerIntentStoreResult finalized = composer.FinalizeClaim(workspace.Data.WorkspaceId, workspace.Data.RootPath, request.ThreadId,
            claimed.Queue.Revision, finalizeMutationId, claim.ClaimId, claim.TurnId, claim.CanonicalInputSha256);
        if (!finalized.Succeeded)
            return Failure("turn-start-recovery-required", ApplicationErrorCategory.CorruptState, "Turn was committed but composer consumption requires recovery.");
        TurnRecord committed = started.Aggregate.Turns.Single(candidate => candidate.TurnId == claim.TurnId);
        return ApplicationResult<TurnExecutionStateProjection>.Success(Project(started.Aggregate, committed, started.Idempotent));
    }

    public async Task<ApplicationResult<TurnExecutionStateProjection>> ExecuteAsync(
        CliEnvironmentSnapshot snapshot,
        string threadId,
        string turnId,
        ITurnExecutionRuntime runtime,
        IInteractiveApprovalWaiter approvalWaiter,
        Func<TurnExecutionStateProjection, ValueTask>? committed = null,
        CancellationToken cancellationToken = default)
    {
        ThreadStore store = threadStoreFactory(snapshot);
        ThreadStoreReadResult read = store.Read(threadId, cancellationToken);
        if (!read.Succeeded || read.Aggregate is null) return ThreadFailure(read.Diagnostic);
        TurnRecord? turn = read.Aggregate.Turns.SingleOrDefault(candidate => candidate.TurnId == turnId);
        if (turn?.ExecutionInput is null || turn.CanonicalInputSha256 is null)
            return Failure("turn-input-missing", ApplicationErrorCategory.CorruptState, "Turn execution input is unavailable.");
        if (turn.Status == TurnStatus.Queued)
        {
            ThreadStoreMutationResult running = store.TransitionTurn(threadId, turnId, read.Aggregate.Record.Revision,
                TurnStatus.Running, NowAtLeast(read.Aggregate.Record.UpdatedAtUtc));
            if (!running.Succeeded || running.Aggregate is null) return ThreadFailure(running.Diagnostic);
            read = ThreadStoreReadResult.Success(running.Aggregate);
            turn = running.Aggregate.Turns.Single(candidate => candidate.TurnId == turnId);
            if (committed is not null) await committed(Project(running.Aggregate, turn, false)).ConfigureAwait(false);
        }

        ThreadAggregate executionAggregate = read.Aggregate ?? throw new InvalidOperationException("Turn aggregate disappeared.");
        PendingComposerIntentRecord executionInput = turn.ExecutionInput ?? throw new InvalidOperationException("Turn input disappeared.");
        string executionInputHash = turn.CanonicalInputSha256 ?? throw new InvalidOperationException("Turn input hash disappeared.");
        var sink = new PersistedEventSink(store, threadId, turnId, committed);
        var approval = new PersistedApprovalGateway(store, executionAggregate.Record.WorkspaceId, threadId, turnId, approvalWaiter, committed);
        try
        {
            TurnRuntimeResult result = await runtime.ExecuteAsync(new TurnExecutionInput(
                snapshot, executionAggregate.Record.WorkspaceId, executionAggregate.Record.WorkspaceRootIdentity, threadId, turnId,
                executionInputHash, executionInput), sink, approval, cancellationToken).ConfigureAwait(false);
            ThreadStoreReadResult beforeTerminal = store.Read(threadId, CancellationToken.None);
            if (!beforeTerminal.Succeeded || beforeTerminal.Aggregate is null) return ThreadFailure(beforeTerminal.Diagnostic);
            string terminal = result.Status == "completed" ? TurnStatus.Completed : TurnStatus.Failed;
            ThreadStoreMutationResult terminalized = store.TransitionTurn(threadId, turnId, beforeTerminal.Aggregate.Record.Revision,
                terminal, NowAtLeast(beforeTerminal.Aggregate.Record.UpdatedAtUtc), result.StopReason, result.ErrorCode);
            if (!terminalized.Succeeded || terminalized.Aggregate is null) return ThreadFailure(terminalized.Diagnostic);
            TurnRecord finalTurn = terminalized.Aggregate.Turns.Single(candidate => candidate.TurnId == turnId);
            TurnExecutionStateProjection projection = Project(terminalized.Aggregate, finalTurn, false);
            if (committed is not null) await committed(projection).ConfigureAwait(false);
            return ApplicationResult<TurnExecutionStateProjection>.Success(projection);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ThreadStoreReadResult cancelRead = store.Read(threadId, CancellationToken.None);
            if (!cancelRead.Succeeded || cancelRead.Aggregate is null) return ThreadFailure(cancelRead.Diagnostic);
            TurnRecord current = cancelRead.Aggregate.Turns.Single(candidate => candidate.TurnId == turnId);
            if (current.Status != TurnStatus.Canceling)
            {
                ThreadStoreMutationResult interrupted = store.TransitionTurn(
                    threadId,
                    turnId,
                    cancelRead.Aggregate.Record.Revision,
                    TurnStatus.Failed,
                    NowAtLeast(cancelRead.Aggregate.Record.UpdatedAtUtc),
                    "interrupted",
                    "turn-disconnected");
                if (!interrupted.Succeeded || interrupted.Aggregate is null)
                    return Failure("turn-cancel-recovery-required", ApplicationErrorCategory.Conflict,
                        "Execution stopped without a durable terminal state.");
                TurnRecord interruptedTurn = interrupted.Aggregate.Turns.Single(candidate => candidate.TurnId == turnId);
                TurnExecutionStateProjection interruptedProjection = Project(
                    interrupted.Aggregate,
                    interruptedTurn,
                    false);
                if (committed is not null) await committed(interruptedProjection).ConfigureAwait(false);
                return ApplicationResult<TurnExecutionStateProjection>.Success(interruptedProjection);
            }
            ThreadStoreMutationResult canceled = store.TransitionTurn(threadId, turnId, cancelRead.Aggregate.Record.Revision,
                TurnStatus.Canceled, NowAtLeast(cancelRead.Aggregate.Record.UpdatedAtUtc), "canceled");
            if (!canceled.Succeeded || canceled.Aggregate is null) return ThreadFailure(canceled.Diagnostic);
            TurnRecord canceledTurn = canceled.Aggregate.Turns.Single(candidate => candidate.TurnId == turnId);
            TurnExecutionStateProjection projection = Project(canceled.Aggregate, canceledTurn, false);
            if (committed is not null) await committed(projection).ConfigureAwait(false);
            return ApplicationResult<TurnExecutionStateProjection>.Success(projection);
        }
        catch (Exception)
        {
            ThreadStoreReadResult failureRead = store.Read(threadId, CancellationToken.None);
            if (!failureRead.Succeeded || failureRead.Aggregate is null)
                return Failure("turn-runtime-recovery-required", ApplicationErrorCategory.CorruptState,
                    "Turn execution failed and durable state requires recovery.");
            TurnRecord current = failureRead.Aggregate.Turns.Single(candidate => candidate.TurnId == turnId);
            if (TurnStatus.IsTerminal(current.Status))
                return ApplicationResult<TurnExecutionStateProjection>.Success(
                    Project(failureRead.Aggregate, current, false));
            ThreadStoreMutationResult failed = store.TransitionTurn(
                threadId,
                turnId,
                failureRead.Aggregate.Record.Revision,
                TurnStatus.Failed,
                NowAtLeast(failureRead.Aggregate.Record.UpdatedAtUtc),
                "runtime-failure",
                "turn-runtime-failed");
            if (!failed.Succeeded || failed.Aggregate is null)
                return Failure("turn-runtime-recovery-required", ApplicationErrorCategory.CorruptState,
                    "Turn execution failed and durable state requires recovery.");
            TurnRecord failedTurn = failed.Aggregate.Turns.Single(candidate => candidate.TurnId == turnId);
            TurnExecutionStateProjection projection = Project(failed.Aggregate, failedTurn, false);
            if (committed is not null) await committed(projection).ConfigureAwait(false);
            return ApplicationResult<TurnExecutionStateProjection>.Success(projection);
        }
    }

    public ApplicationResult<TurnExecutionStateProjection> Cancel(TurnCancelRequest request)
    {
        if (!ComposerIntentContractValidator.IsSafeMutationId(request.ClientMutationId))
            return Failure("turn-cancel-invalid", ApplicationErrorCategory.Validation, "Turn cancel request is invalid.");
        ThreadStore store = threadStoreFactory(request.Snapshot);
        ThreadStoreReadResult read = store.Read(request.ThreadId);
        if (!read.Succeeded || read.Aggregate is null) return ThreadFailure(read.Diagnostic);
        DateTimeOffset now = NowAtLeast(read.Aggregate.Record.UpdatedAtUtc);
        TimelineItemRecord item = Item(read.Aggregate.Record, request.TurnId, "cancel-" + request.ClientMutationId,
            TimelineItemType.WarningRaised, "canceling", "Cancellation was requested.", now,
            new TimelinePayloadRecord { Warning = new TimelineWarningPayloadRecord("cancel-requested") });
        ThreadStoreMutationResult result = store.RequestCancel(request.ThreadId, request.TurnId, request.ExpectedThreadRevision,
            request.ExpectedTurnRevision, request.ClientMutationId, now, item);
        if (!result.Succeeded || result.Aggregate is null) return ThreadFailure(result.Diagnostic);
        return ApplicationResult<TurnExecutionStateProjection>.Success(Project(result.Aggregate,
            result.Aggregate.Turns.Single(turn => turn.TurnId == request.TurnId), result.Idempotent));
    }

    public ApplicationResult<TurnExecutionStateProjection> ResolveApproval(ApprovalResolveRequest request)
    {
        if (!ComposerIntentContractValidator.IsSafeMutationId(request.ClientMutationId) || request.Decision is not ("approve" or "deny"))
            return Failure("approval-decision-invalid", ApplicationErrorCategory.Validation, "Approval decision is invalid.");
        ThreadStore store = threadStoreFactory(request.Snapshot);
        ThreadStoreReadResult read = store.Read(request.ThreadId);
        if (!read.Succeeded || read.Aggregate is null) return ThreadFailure(read.Diagnostic);
        TurnRecord? turn = read.Aggregate.Turns.SingleOrDefault(candidate => candidate.TurnId == request.TurnId);
        if (turn?.ActiveApproval is null || turn.ActiveApproval.RequestId != request.RequestId)
            return Failure("approval-stale", ApplicationErrorCategory.Conflict, "Approval request is no longer active.");
        DateTimeOffset now = NowAtLeast(read.Aggregate.Record.UpdatedAtUtc);
        TimelineItemRecord item = Item(read.Aggregate.Record, request.TurnId, "approval-" + request.ClientMutationId,
            TimelineItemType.ApprovalResolved, "resolved", request.Decision == "approve" ? "Approval granted." : "Approval denied.", now,
            new TimelinePayloadRecord { Approval = new TimelineApprovalPayloadRecord(request.Decision) });
        ThreadStoreMutationResult result = store.ResolveApproval(request.ThreadId, request.TurnId, request.ExpectedThreadRevision,
            request.RequestId, request.Decision, request.ExpectedTurnRevision, request.ExpectedApprovalRevision,
            request.ClientMutationId, now, item);
        if (!result.Succeeded || result.Aggregate is null) return ThreadFailure(result.Diagnostic);
        TurnRecord resolved = result.Aggregate.Turns.Single(candidate => candidate.TurnId == request.TurnId);
        return ApplicationResult<TurnExecutionStateProjection>.Success(Project(result.Aggregate, resolved, result.Idempotent));
    }

    public ApplicationResult<TurnExecutionStateProjection> Restart(TurnRestartRequest request)
    {
        if (!request.Confirmed || !ComposerIntentContractValidator.IsSafeMutationId(request.ClientMutationId))
            return Failure("restart-confirmation-required", ApplicationErrorCategory.Validation, "Restart requires explicit confirmation.");
        ThreadStore store = threadStoreFactory(request.Snapshot);
        ThreadStoreReadResult read = store.Read(request.ThreadId);
        if (!read.Succeeded || read.Aggregate is null) return ThreadFailure(read.Diagnostic);
        ThreadMutationReceiptRecord? existingRestartReceipt = read.Aggregate.Record.MutationReceipts
            .SingleOrDefault(receipt => receipt.MutationId == request.ClientMutationId);
        TurnRecord? existingRestart = existingRestartReceipt is null ? null : read.Aggregate.Turns
            .SingleOrDefault(turn => turn.Mode == "desktop-restart" && turn.SourceCorrelation == request.SourceTurnId);
        if (existingRestart is not null)
            return ApplicationResult<TurnExecutionStateProjection>.Success(Project(read.Aggregate, existingRestart, true));

        TurnRecord? source = read.Aggregate.Turns.SingleOrDefault(turn => turn.TurnId == request.SourceTurnId);
        string recoveryMutationId = RecoveryMutationId(request.ClientMutationId);
        bool recoveryAlreadyCommitted = read.Aggregate.Record.MutationReceipts.Any(
            receipt => receipt.MutationId == recoveryMutationId);
        bool staleActiveSource = source is not null &&
            read.RecoveryRequired &&
            read.Aggregate.Record.ActiveTurnId == source.TurnId &&
            source.Status is TurnStatus.Running or TurnStatus.WaitingForApproval or TurnStatus.Canceling;
        if (source?.ExecutionInput is null ||
            (!recoveryAlreadyCommitted && (source.Revision != request.ExpectedSourceTurnRevision ||
                read.Aggregate.Record.Revision != request.ExpectedThreadRevision)) ||
            (!TurnStatus.IsTerminal(source.Status) && !staleActiveSource && !source.RecoveryRequired) ||
            (read.Aggregate.Record.ActiveTurnId is not null && !staleActiveSource))
            return Failure("restart-source-invalid", ApplicationErrorCategory.Conflict, "Source turn cannot be restarted safely.");

        if (staleActiveSource)
        {
            ThreadStoreMutationResult recovered = store.RecoverInterrupted(
                request.ThreadId,
                read.Aggregate.Record.Revision,
                NowAtLeast(read.Aggregate.Record.UpdatedAtUtc),
                recoveryMutationId);
            if (!recovered.Succeeded || recovered.Aggregate is null) return ThreadFailure(recovered.Diagnostic);
            read = ThreadStoreReadResult.Success(recovered.Aggregate);
            source = recovered.Aggregate.Turns.Single(turn => turn.TurnId == request.SourceTurnId);
        }

        if (read.Aggregate is not ThreadAggregate restartAggregate ||
            restartAggregate.Record.ActiveTurnId is not null || source is null || !TurnStatus.IsTerminal(source.Status))
            return Failure("restart-source-invalid", ApplicationErrorCategory.Conflict, "Source turn cannot be restarted safely.");
        TurnRecord restartSource = source;
        DateTimeOffset now = NowAtLeast(restartAggregate.Record.UpdatedAtUtc);
        string intentId = "intent_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.ClientMutationId + "\0" + restartSource.TurnId))).ToLowerInvariant()[..32];
        PendingComposerIntentRecord input = restartSource.ExecutionInput! with { IntentId = intentId, ThreadRevision = restartAggregate.Record.Revision, CreatedAtUtc = now };
        string hash = ComposerIntentContractValidator.ComputeCanonicalInputSha256(input);
        ComposerIntentClaimRecord claim = new()
        {
            ClaimId = "claim_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.ClientMutationId))).ToLowerInvariant()[..32],
            IntentId = intentId,
            TurnId = ComposerIntentContractValidator.CreateDeterministicTurnId(intentId, hash),
            CanonicalInputSha256 = hash,
            StartMutationId = request.ClientMutationId,
            SourceQueueRevision = 0,
            ClaimedAtUtc = now
        };
        TurnRecord restarted = new()
        {
            TurnId = claim.TurnId,
            ThreadId = request.ThreadId,
            Ordinal = restartAggregate.Turns.Count + 1,
            CreatedAtUtc = now,
            TaskSummary = restartSource.TaskSummary,
            Mode = "desktop-restart",
            SourceCorrelation = restartSource.TurnId,
            ExecutionInput = input,
            CanonicalInputSha256 = hash
        };
        string prompt = ApplicationProjection.Safe(input.Prompt, ThreadPersistenceLimits.MaxTimelineSummaryBytes);
        TimelineItemRecord message = Item(restartAggregate.Record, restarted.TurnId, "restart-" + request.ClientMutationId,
            TimelineItemType.UserMessage, "committed", prompt, now,
            new TimelinePayloadRecord { Message = new TimelineMessagePayloadRecord(prompt) });
        ThreadStoreMutationResult result = store.StartTurnFromIntent(request.ThreadId, restartAggregate.Record.Revision,
            request.ClientMutationId, claim, input, restarted, message);
        if (!result.Succeeded || result.Aggregate is null) return ThreadFailure(result.Diagnostic);
        return ApplicationResult<TurnExecutionStateProjection>.Success(Project(result.Aggregate,
            result.Aggregate.Turns.Single(turn => turn.TurnId == restarted.TurnId), result.Idempotent));
    }

    private static string RecoveryMutationId(string clientMutationId)
    {
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(clientMutationId))).ToLowerInvariant();
        return "restart-recovery-" + hash[..32];
    }

    private sealed class PersistedEventSink(
        ThreadStore store,
        string threadId,
        string turnId,
        Func<TurnExecutionStateProjection, ValueTask>? committed) : ITurnExecutionEventSink
    {
        private readonly HashSet<string> correlationIds = new(StringComparer.Ordinal);
        private long nextEventSequence = 1;

        public async ValueTask EmitAsync(TurnRuntimeEvent runtimeEvent, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (runtimeEvent.EventSequence != nextEventSequence)
                throw new InvalidOperationException("Runtime event sequence is not strictly monotonic.");
            if (string.IsNullOrWhiteSpace(runtimeEvent.CorrelationId) ||
                !correlationIds.Add(runtimeEvent.CorrelationId))
                throw new InvalidOperationException("Runtime event correlation identity is invalid or duplicated.");
            nextEventSequence++;
            ThreadStoreReadResult read = store.Read(threadId, cancellationToken);
            if (!read.Succeeded || read.Aggregate is null) throw new InvalidOperationException(read.Diagnostic?.SafeMessage);
            DateTimeOffset timestamp = runtimeEvent.TimestampUtc.ToUniversalTime();
            if (timestamp < read.Aggregate.Record.UpdatedAtUtc) timestamp = read.Aggregate.Record.UpdatedAtUtc;
            (string type, TimelinePayloadRecord payload) = Map(runtimeEvent);
            TimelineItemRecord item = Item(read.Aggregate.Record, turnId, $"runtime-{runtimeEvent.EventSequence}-{runtimeEvent.CorrelationId}",
                type, runtimeEvent.Status, runtimeEvent.Summary, timestamp, payload);
            ThreadStoreMutationResult result = store.AppendTimeline(threadId, turnId, read.Aggregate.Record.Revision,
                read.Aggregate.Record.CommittedSequence + 1, $"evt-{turnId}-{runtimeEvent.EventSequence}", [item]);
            if (!result.Succeeded || result.Aggregate is null) throw new InvalidOperationException(result.Diagnostic?.SafeMessage);
            if (committed is not null)
                await committed(Project(result.Aggregate, result.Aggregate.Turns.Single(turn => turn.TurnId == turnId), result.Idempotent)).ConfigureAwait(false);
        }
    }

    private sealed class PersistedApprovalGateway(
        ThreadStore store,
        string workspaceId,
        string threadId,
        string turnId,
        IInteractiveApprovalWaiter waiter,
        Func<TurnExecutionStateProjection, ValueTask>? committed) : IInteractiveApprovalGateway
    {
        public async ValueTask<InteractiveApprovalDecision> RequestAsync(InteractiveApprovalAction action, CancellationToken cancellationToken)
        {
            ThreadStoreReadResult read = store.Read(threadId, cancellationToken);
            if (!read.Succeeded || read.Aggregate is null) throw new InvalidOperationException(read.Diagnostic?.SafeMessage);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (now < read.Aggregate.Record.UpdatedAtUtc) now = read.Aggregate.Record.UpdatedAtUtc;
            string requestId = "approval_" + Guid.NewGuid().ToString("N");
            DurableApprovalRequestRecord request = new()
            {
                RequestId = requestId,
                WorkspaceId = workspaceId,
                PolicyIdentity = ApplicationProjection.Safe(action.PolicyIdentity, 256),
                PolicyRevision = ApplicationProjection.Safe(action.PolicyRevision, 256),
                Risk = ApplicationProjection.Safe(action.Risk, 64),
                Operation = ApplicationProjection.Safe(action.Operation, 1024),
                TargetClass = ApplicationProjection.Safe(action.TargetClass, 1024),
                CanonicalActionSha256 = action.CanonicalActionSha256,
                SafeSummary = ApplicationProjection.Safe(action.SafeSummary, TurnExecutionLimits.MaxApprovalSummaryBytes),
                CreatedAtUtc = now,
                ExpiresAtUtc = now.Add(TurnExecutionLimits.ApprovalLifetime)
            };
            TimelineItemRecord item = Item(read.Aggregate.Record, turnId, "approval-request-" + requestId,
                TimelineItemType.ApprovalRequested, "waiting", request.SafeSummary, now,
                new TimelinePayloadRecord { Approval = new TimelineApprovalPayloadRecord("pending") });
            ThreadStoreMutationResult requested = store.RequestApproval(threadId, turnId, read.Aggregate.Record.Revision,
                "approval-request-" + requestId, request, item);
            if (!requested.Succeeded || requested.Aggregate is null) throw new InvalidOperationException(requested.Diagnostic?.SafeMessage);
            TurnRecord waitingTurn = requested.Aggregate.Turns.Single(turn => turn.TurnId == turnId);
            DurableApprovalProjection projection = ProjectApproval(waitingTurn.ActiveApproval!);
            using CancellationTokenSource expiry =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            expiry.CancelAfter(TurnExecutionLimits.ApprovalLifetime);
            ValueTask<InteractiveApprovalDecision> pendingDecision =
                waiter.WaitAsync(projection, expiry.Token);
            InteractiveApprovalDecision decision;
            try
            {
                if (committed is not null)
                    await committed(Project(requested.Aggregate, waitingTurn, false)).ConfigureAwait(false);
                decision = await pendingDecision.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("Durable approval request expired.");
            }
            catch
            {
                expiry.Cancel();
                throw;
            }
            ThreadStoreReadResult resolved = store.Read(threadId, cancellationToken);
            TurnRecord? resolvedTurn = resolved.Aggregate?.Turns.SingleOrDefault(turn => turn.TurnId == turnId);
            if (!resolved.Succeeded || resolved.Aggregate is null || resolvedTurn is null ||
                resolvedTurn.Status != TurnStatus.Running || resolvedTurn.ActiveApproval is not null)
                throw new InvalidOperationException("Approval decision was not durably resolved before releasing execution.");
            if (committed is not null) await committed(Project(resolved.Aggregate, resolvedTurn, false)).ConfigureAwait(false);
            return decision;
        }
    }

    private static (string, TimelinePayloadRecord) Map(TurnRuntimeEvent value) => value.Kind switch
    {
        TurnRuntimeEventKind.Plan => (TimelineItemType.PlanUpdated, new TimelinePayloadRecord { Plan = new TimelinePlanPayloadRecord(value.Summary) }),
        TurnRuntimeEventKind.ToolStarted => (TimelineItemType.ToolStarted, Operation(value)),
        TurnRuntimeEventKind.ToolCompleted => (TimelineItemType.ToolCompleted, Operation(value)),
        TurnRuntimeEventKind.CommandStarted => (TimelineItemType.CommandStarted, Operation(value)),
        TurnRuntimeEventKind.CommandCompleted => (TimelineItemType.CommandCompleted, Operation(value)),
        TurnRuntimeEventKind.Verification => (TimelineItemType.VerificationCompleted, Operation(value)),
        TurnRuntimeEventKind.Changes => (TimelineItemType.ChangesUpdated, new TimelinePayloadRecord { Changes = new TimelineChangesPayloadRecord(value.ChangedFileCount ?? 0) }),
        TurnRuntimeEventKind.Final => (TimelineItemType.AssistantFinal, new TimelinePayloadRecord { Message = new TimelineMessagePayloadRecord(value.Summary) }),
        TurnRuntimeEventKind.Warning => (TimelineItemType.WarningRaised, new TimelinePayloadRecord { Warning = new TimelineWarningPayloadRecord(value.ErrorCode ?? "runtime-warning") }),
        _ => (TimelineItemType.AssistantMessage, new TimelinePayloadRecord { Message = new TimelineMessagePayloadRecord(value.Summary) })
    };

    private static TimelinePayloadRecord Operation(TurnRuntimeEvent value) => new()
    {
        Operation = new TimelineOperationPayloadRecord(value.Name ?? "operation", value.Succeeded, value.ErrorCode)
    };

    private static TimelineItemRecord Item(ThreadRecord thread, string turnId, string seed, string type, string status,
        string summary, DateTimeOffset timestamp, TimelinePayloadRecord payload) => new()
    {
        ItemId = ThreadIdentity.CreateDeterministicItemId($"{turnId}:{seed}", type, 0),
        ThreadId = thread.ThreadId,
        TurnId = turnId,
        Sequence = thread.CommittedSequence + 1,
        TimestampUtc = timestamp.ToUniversalTime(),
        Type = type,
        Status = ApplicationProjection.Safe(status, 256),
        Summary = ApplicationProjection.Safe(summary, ThreadPersistenceLimits.MaxTimelineSummaryBytes),
        Payload = payload,
        Redaction = new TimelineRedactionRecord { Applied = true }
    };

    private static TurnExecutionStateProjection Project(ThreadAggregate aggregate, TurnRecord turn, bool idempotent) => new(
        aggregate.Record.WorkspaceId, aggregate.Record.ThreadId, turn.TurnId, aggregate.Record.Revision, turn.Revision,
        turn.Status, aggregate.Record.CommittedSequence, turn.RecoveryRequired, idempotent,
        turn.ActiveApproval is null ? null : ProjectApproval(turn.ActiveApproval));

    private static DurableApprovalProjection ProjectApproval(DurableApprovalRequestRecord value) => new(
        value.RequestId, value.WorkspaceId, value.ThreadId, value.TurnId, value.TurnRevision, value.ApprovalRevision,
        value.PolicyIdentity, value.PolicyRevision, value.Risk, value.Operation, value.TargetClass, value.SafeSummary,
        value.CreatedAtUtc, value.ExpiresAtUtc);

    private DateTimeOffset NowAtLeast(DateTimeOffset boundary)
    {
        DateTimeOffset now = clock().ToUniversalTime();
        return now < boundary ? boundary : now;
    }

    private static ApplicationError? RevalidateContext(PendingComposerIntentRecord intent, string root)
    {
        string fullRoot = Path.GetFullPath(root);
        foreach (ComposerContextReferenceRecord context in intent.Context)
        {
            string path = Path.GetFullPath(Path.Combine(fullRoot, context.RelativePath));
            if (!path.StartsWith(Path.TrimEndingDirectorySeparator(fullRoot) + Path.DirectorySeparatorChar,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
                (context.Kind == ComposerContextKind.File ? !File.Exists(path) : !Directory.Exists(path)))
                return new ApplicationError("context-reference-stale", ApplicationErrorCategory.Conflict,
                    "Controlled context changed before execution.", false);
        }
        return null;
    }

    private static ApplicationResult<TurnExecutionStateProjection> ThreadFailure(ThreadStoreDiagnostic? diagnostic) =>
        Failure(diagnostic?.ErrorCode ?? ThreadErrorCode.ThreadStoreUnavailable,
            diagnostic?.ErrorCode?.Contains("conflict", StringComparison.Ordinal) == true ? ApplicationErrorCategory.Conflict : ApplicationErrorCategory.Unavailable,
            diagnostic?.SafeMessage ?? "Thread store is unavailable.");

    private static ApplicationResult<TurnExecutionStateProjection> ComposerFailure(ComposerIntentStoreDiagnostic? diagnostic) =>
        Failure(diagnostic?.ErrorCode ?? ComposerIntentErrorCode.Unavailable,
            diagnostic?.ErrorCode?.Contains("conflict", StringComparison.Ordinal) == true ? ApplicationErrorCategory.Conflict : ApplicationErrorCategory.Unavailable,
            diagnostic?.SafeMessage ?? "Composer queue is unavailable.");

    private static ApplicationResult<TurnExecutionStateProjection> Failure(string code, string category, string message) =>
        ApplicationResult<TurnExecutionStateProjection>.Failure(new ApplicationError(code, category, message, category == ApplicationErrorCategory.Unavailable));
}
