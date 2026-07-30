using CSharpAiCli.Application;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class TurnExecutionApplicationServiceTests
{
    [Fact]
    public async Task StartAndExecute_PersistBoundedTimelineAndFinalState()
    {
        using Harness test = new("Run the deterministic fixture");
        ApplicationResult<TurnExecutionStateProjection> started = test.Start("start-1");
        Assert.True(started.Succeeded, started.Error?.SafeMessage);

        ApplicationResult<TurnExecutionStateProjection> completed = await test.Service.ExecuteAsync(
            test.Snapshot, test.ThreadId, started.Data!.TurnId, new DeterministicFakeTurnExecutionRuntime(), new RejectingWaiter());

        Assert.True(completed.Succeeded, completed.Error?.SafeMessage);
        Assert.Equal(TurnStatus.Completed, completed.Data!.Status);
        ThreadStoreReadResult read = test.Threads.Read(test.ThreadId);
        Assert.Equal(TurnStatus.Completed, read.Aggregate!.Turns.Single().Status);
        Assert.Contains(test.Threads.ReadTimelinePage(test.ThreadId, 0, 100).Items, item => item.Type == TimelineItemType.AssistantFinal);
        Assert.All(test.Threads.ReadTimelinePage(test.ThreadId, 0, 100).Items, item => Assert.True(item.Redaction.Applied || item.Type == TimelineItemType.UserMessage));
    }

    [Fact]
    public async Task Provider_attempt_progress_and_active_attempt_content_are_durable()
    {
        using Harness test = new("Stream a retrying response");
        TurnExecutionStateProjection started = test.Start("start-provider-progress").Data!;

        ApplicationResult<TurnExecutionStateProjection> completed = await test.Service.ExecuteAsync(
            test.Snapshot,
            test.ThreadId,
            started.TurnId,
            new ProviderProgressRuntime(),
            new RejectingWaiter());

        Assert.True(completed.Succeeded, completed.Error?.SafeMessage);
        TurnRecord turn = Assert.Single(test.Threads.Read(test.ThreadId).Aggregate!.Turns);
        Assert.Equal(2, turn.ProviderProgress.Attempt);
        Assert.Equal(ProviderAttemptPhase.Streaming, turn.ProviderProgress.Phase);
        Assert.True(turn.ProviderProgress.AttemptHasStreamContent);
        Assert.Equal("assistant-test", turn.ProviderProgress.AssistantMessageId);
        TimelineItemRecord[] timeline = test.Threads.ReadTimelinePage(test.ThreadId, 0, 100).Items.ToArray();
        Assert.Equal(2, timeline.Count(item => item.Type == TimelineItemType.ProviderAttempt));
        TimelineItemRecord message = Assert.Single(timeline, item => item.Type == TimelineItemType.AssistantMessage);
        Assert.Equal(2, message.Payload.Message?.Attempt);
        Assert.Equal("successful attempt", message.Payload.Message?.Preview);
    }

    [Fact]
    public async Task Sequential_turns_do_not_reuse_timeline_item_identity()
    {
        using Harness test = new("Run the first deterministic fixture");
        TurnExecutionStateProjection first = test.Start("start-identity-first").Data!;
        ApplicationResult<TurnExecutionStateProjection> firstCompleted =
            await test.Service.ExecuteAsync(
                test.Snapshot,
                test.ThreadId,
                first.TurnId,
                new DeterministicFakeTurnExecutionRuntime(),
                new RejectingWaiter());
        Assert.True(firstCompleted.Succeeded, firstCompleted.Error?.SafeMessage);

        ThreadAggregate afterFirst = test.Threads.Read(test.ThreadId).Aggregate!;
        ComposerQueueRecord queue = test.Composer.Get(
            test.WorkspaceId,
            test.WorkspaceRoot,
            test.ThreadId).Queue!;
        Assert.True(test.Composer.Enqueue(
            test.WorkspaceId,
            test.WorkspaceRoot,
            test.ThreadId,
            queue.Revision,
            "enqueue-identity-second",
            test.Intent("Run the second deterministic fixture")).Succeeded);
        queue = test.Composer.Get(
            test.WorkspaceId,
            test.WorkspaceRoot,
            test.ThreadId).Queue!;
        TurnExecutionStateProjection second = test.Service.Start(new TurnStartRequest(
            test.Snapshot,
            test.ThreadId,
            afterFirst.Record.Revision,
            queue.Revision,
            "start-identity-second")).Data!;
        ApplicationResult<TurnExecutionStateProjection> secondCompleted =
            await test.Service.ExecuteAsync(
                test.Snapshot,
                test.ThreadId,
                second.TurnId,
                new DeterministicFakeTurnExecutionRuntime(),
                new RejectingWaiter());
        Assert.True(secondCompleted.Succeeded, secondCompleted.Error?.SafeMessage);

        TimelineItemRecord[] timeline =
            test.Threads.ReadTimelinePage(test.ThreadId, 0, 100).Items.ToArray();
        string[] firstIds = timeline
            .Where(item => item.TurnId == first.TurnId)
            .Select(item => item.ItemId)
            .ToArray();
        string[] secondIds = timeline
            .Where(item => item.TurnId == second.TurnId)
            .Select(item => item.ItemId)
            .ToArray();
        Assert.NotEmpty(firstIds);
        Assert.NotEmpty(secondIds);
        Assert.Empty(firstIds.Intersect(secondIds, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Approval_IsIdentityBoundAndResolvedBeforeRuntimeContinues()
    {
        using Harness test = new("[approval] perform the controlled fixture write");
        TurnExecutionStateProjection started = test.Start("start-approval").Data!;
        var waiter = new ResolvingWaiter(test);

        ApplicationResult<TurnExecutionStateProjection> completed = await test.Service.ExecuteAsync(
            test.Snapshot, test.ThreadId, started.TurnId, new DeterministicFakeTurnExecutionRuntime(), waiter);

        Assert.True(completed.Succeeded, completed.Error?.SafeMessage);
        Assert.NotNull(waiter.Request);
        Assert.Equal(TurnStatus.Completed, completed.Data!.Status);
        TimelineItemRecord[] timeline = test.Threads.ReadTimelinePage(test.ThreadId, 0, 100).Items.ToArray();
        Assert.Single(timeline, item => item.Type == TimelineItemType.ApprovalRequested);
        Assert.Single(timeline, item => item.Type == TimelineItemType.ApprovalResolved);
    }

    [Fact]
    public async Task Approval_waiter_is_registered_before_waiting_state_is_published()
    {
        using Harness test = new("[approval] perform the controlled fixture write");
        TurnExecutionStateProjection started = test.Start("start-approval-order").Data!;
        DeferredResolvingWaiter waiter = new(test);
        bool registeredBeforeCommit = false;

        ApplicationResult<TurnExecutionStateProjection> completed = await test.Service.ExecuteAsync(
            test.Snapshot,
            test.ThreadId,
            started.TurnId,
            new DeterministicFakeTurnExecutionRuntime(),
            waiter,
            committed: state =>
            {
                if (state.Approval is not null)
                {
                    registeredBeforeCommit = waiter.Request?.RequestId == state.Approval.RequestId;
                    waiter.Resolve();
                }
                return ValueTask.CompletedTask;
            });

        Assert.True(completed.Succeeded, completed.Error?.SafeMessage);
        Assert.True(registeredBeforeCommit);
        Assert.Equal(TurnStatus.Completed, completed.Data!.Status);
    }

    [Fact]
    public async Task Approval_denial_is_durable_and_terminal_without_retry()
    {
        using Harness test = new("[approval] deny the controlled fixture write");
        TurnExecutionStateProjection started = test.Start("start-denial").Data!;
        DenyingWaiter waiter = new(test);

        ApplicationResult<TurnExecutionStateProjection> completed = await test.Service.ExecuteAsync(
            test.Snapshot,
            test.ThreadId,
            started.TurnId,
            new DeterministicFakeTurnExecutionRuntime(),
            waiter);

        Assert.True(completed.Succeeded, completed.Error?.SafeMessage);
        Assert.Equal(TurnStatus.Failed, completed.Data!.Status);
        TurnRecord turn = Assert.Single(test.Threads.Read(test.ThreadId).Aggregate!.Turns);
        Assert.Equal("approval-denied", turn.StopReason);
        Assert.Equal("approval-denied", turn.ErrorCode);
        TimelineItemRecord[] timeline = test.Threads.ReadTimelinePage(test.ThreadId, 0, 100).Items.ToArray();
        Assert.Single(timeline, item => item.Type == TimelineItemType.ApprovalRequested);
        Assert.Single(timeline, item => item.Type == TimelineItemType.ApprovalResolved);
        Assert.DoesNotContain(timeline, item => item.Type == TimelineItemType.ToolStarted);
    }

    [Theory]
    [InlineData("gap")]
    [InlineData("duplicate-correlation")]
    public async Task Runtime_event_identity_violation_fails_turn_closed(string mode)
    {
        using Harness test = new("Reject invalid runtime event identity");
        TurnExecutionStateProjection started = test.Start("start-invalid-event").Data!;

        ApplicationResult<TurnExecutionStateProjection> completed = await test.Service.ExecuteAsync(
            test.Snapshot,
            test.ThreadId,
            started.TurnId,
            new InvalidEventRuntime(mode),
            new RejectingWaiter());

        Assert.True(completed.Succeeded, completed.Error?.SafeMessage);
        Assert.Equal(TurnStatus.Failed, completed.Data!.Status);
        ThreadAggregate aggregate = test.Threads.Read(test.ThreadId).Aggregate!;
        TurnRecord turn = Assert.Single(aggregate.Turns);
        Assert.Equal(TurnStatus.Failed, turn.Status);
        Assert.Equal("turn-runtime-failed", turn.ErrorCode);
        Assert.DoesNotContain(
            test.Threads.ReadTimelinePage(test.ThreadId, 0, 100).Items,
            item => item.Type == TimelineItemType.AssistantFinal);
    }

    [Fact]
    public async Task Cancel_PersistsCancelingBeforeCooperativeTokenAndConvergesCanceled()
    {
        using Harness test = new("pause for cancellation");
        TurnExecutionStateProjection started = test.Start("start-cancel").Data!;
        var runtime = new BlockingRuntime();
        using CancellationTokenSource cancellation = new();
        Task<ApplicationResult<TurnExecutionStateProjection>> execution = test.Service.ExecuteAsync(
            test.Snapshot, test.ThreadId, started.TurnId, runtime, new RejectingWaiter(), cancellationToken: cancellation.Token);
        await runtime.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        ThreadAggregate running = test.Threads.Read(test.ThreadId).Aggregate!;
        TurnRecord runningTurn = running.Turns.Single();

        ApplicationResult<TurnExecutionStateProjection> canceling = test.Service.Cancel(new TurnCancelRequest(
            test.Snapshot, test.ThreadId, started.TurnId, running.Record.Revision, runningTurn.Revision, "cancel-1"));
        Assert.True(canceling.Succeeded, canceling.Error?.SafeMessage);
        Assert.Equal(TurnStatus.Canceling, test.Threads.Read(test.ThreadId).Aggregate!.Turns.Single().Status);
        cancellation.Cancel();

        ApplicationResult<TurnExecutionStateProjection> canceled = await execution;
        Assert.True(canceled.Succeeded, canceled.Error?.SafeMessage);
        Assert.Equal(TurnStatus.Canceled, canceled.Data!.Status);
    }

    [Fact]
    public async Task Runtime_disconnect_releases_worker_and_fails_turn_interrupted()
    {
        using Harness test = new("pause until AppHost disconnect");
        TurnExecutionStateProjection started = test.Start("start-disconnect").Data!;
        BlockingRuntime runtime = new();
        using CancellationTokenSource cancellation = new();
        Task<ApplicationResult<TurnExecutionStateProjection>> execution = test.Service.ExecuteAsync(
            test.Snapshot,
            test.ThreadId,
            started.TurnId,
            runtime,
            new RejectingWaiter(),
            cancellationToken: cancellation.Token);
        await runtime.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();
        ApplicationResult<TurnExecutionStateProjection> interrupted =
            await execution.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(interrupted.Succeeded, interrupted.Error?.SafeMessage);
        Assert.Equal(TurnStatus.Failed, interrupted.Data!.Status);
        TurnRecord turn = Assert.Single(test.Threads.Read(test.ThreadId).Aggregate!.Turns);
        Assert.Equal("interrupted", turn.StopReason);
        Assert.Equal("turn-disconnected", turn.ErrorCode);
    }

    [Fact]
    public void Start_WhenActiveTurnExistsLeavesPendingComposerIntentClearable()
    {
        using Harness test = new("first turn");
        TurnExecutionStateProjection started = test.Start("start-active").Data!;
        PendingComposerIntentRecord next = test.Intent("second turn");
        ComposerQueueRecord queue = test.Composer.Get(test.WorkspaceId, test.WorkspaceRoot, test.ThreadId).Queue!;
        Assert.True(test.Composer.Enqueue(test.WorkspaceId, test.WorkspaceRoot, test.ThreadId, queue.Revision, "enqueue-second", next).Succeeded);
        queue = test.Composer.Get(test.WorkspaceId, test.WorkspaceRoot, test.ThreadId).Queue!;

        ApplicationResult<TurnExecutionStateProjection> busy = test.Service.Start(new TurnStartRequest(
            test.Snapshot, test.ThreadId, started.ThreadRevision, queue.Revision, "start-second"));

        Assert.False(busy.Succeeded);
        Assert.Equal("write-execution-busy", busy.Error!.Code);
        ComposerQueueRecord after = test.Composer.Get(test.WorkspaceId, test.WorkspaceRoot, test.ThreadId).Queue!;
        Assert.Equal(ComposerQueueLifecycle.Pending, after.Lifecycle);
        Assert.Null(after.Claim);
        Assert.True(test.Composer.Clear(test.WorkspaceId, test.WorkspaceRoot, test.ThreadId, after.Revision, "clear-second").Succeeded);
    }

    [Fact]
    public void Restart_ExplicitlyRecoversStaleRunningTurnAndIsIdempotent()
    {
        using Harness test = new("[pause] recover this controlled write");
        TurnExecutionStateProjection started = test.Start("start-crash").Data!;
        ThreadStoreMutationResult running = test.Threads.TransitionTurn(
            test.ThreadId,
            started.TurnId,
            started.ThreadRevision,
            TurnStatus.Running,
            test.Now.AddSeconds(1));
        Assert.True(running.Succeeded, running.Diagnostic?.SafeMessage);
        TurnRecord stale = Assert.Single(running.Aggregate!.Turns);

        var request = new TurnRestartRequest(
            test.Snapshot,
            test.ThreadId,
            stale.TurnId,
            running.Aggregate.Record.Revision,
            stale.Revision,
            Confirmed: true,
            ClientMutationId: "restart-after-crash");
        ApplicationResult<TurnExecutionStateProjection> restarted = test.Service.Restart(request);
        ApplicationResult<TurnExecutionStateProjection> replayed = test.Service.Restart(request);

        Assert.True(restarted.Succeeded, restarted.Error?.SafeMessage);
        Assert.True(replayed.Succeeded, replayed.Error?.SafeMessage);
        Assert.True(replayed.Data!.Idempotent);
        Assert.Equal(restarted.Data!.TurnId, replayed.Data.TurnId);
        ThreadAggregate aggregate = test.Threads.Read(test.ThreadId).Aggregate!;
        Assert.Equal(2, aggregate.Turns.Count);
        TurnRecord source = aggregate.Turns[0];
        TurnRecord attempt = aggregate.Turns[1];
        Assert.Equal(TurnStatus.Failed, source.Status);
        Assert.Equal("interrupted", source.StopReason);
        Assert.Equal("interrupted", source.ErrorCode);
        Assert.True(source.RecoveryRequired);
        Assert.Null(source.ActiveApproval);
        Assert.NotEqual(source.TurnId, attempt.TurnId);
        Assert.Equal(source.TurnId, attempt.SourceCorrelation);
        Assert.Equal("desktop-restart", attempt.Mode);
        Assert.Equal(attempt.TurnId, aggregate.Record.ActiveTurnId);
        Assert.Equal(1, aggregate.Record.MutationReceipts.Count(receipt => receipt.MutationId == "restart-after-crash"));
        Assert.Contains(test.Threads.ReadTimelinePage(test.ThreadId, 0, 100).Items,
            item => item.Payload.Warning?.Code == "interrupted");
    }

    [Fact]
    public async Task Restart_AfterProviderExhaustionDoesNotReplayCompletedSideEffects()
    {
        using Harness test = new("Perform one operation before the provider fails");
        TurnExecutionStateProjection started = test.Start("start-provider-exhaustion").Data!;
        ApplicationResult<TurnExecutionStateProjection> failed = await test.Service.ExecuteAsync(
            test.Snapshot,
            test.ThreadId,
            started.TurnId,
            new SideEffectThenProviderExhaustionRuntime(),
            new RejectingWaiter());

        Assert.True(failed.Succeeded, failed.Error?.SafeMessage);
        Assert.Equal(TurnStatus.Failed, failed.Data!.Status);
        ThreadAggregate beforeRestart = test.Threads.Read(test.ThreadId).Aggregate!;
        TurnRecord source = Assert.Single(beforeRestart.Turns);
        Assert.True(source.ProviderProgress.RetryExhausted);

        ApplicationResult<TurnExecutionStateProjection> restarted = test.Service.Restart(
            new TurnRestartRequest(
                test.Snapshot,
                test.ThreadId,
                source.TurnId,
                beforeRestart.Record.Revision,
                source.Revision,
                Confirmed: true,
                ClientMutationId: "restart-provider-exhaustion"));

        Assert.False(restarted.Succeeded);
        Assert.Equal("restart-side-effects-present", restarted.Error!.Code);
        Assert.Single(test.Threads.Read(test.ThreadId).Aggregate!.Turns);
        Assert.Single(
            test.Threads.ReadTimelinePage(test.ThreadId, 0, 100).Items,
            item => item.Type == TimelineItemType.ToolCompleted);
    }

    private sealed class Harness : IDisposable
    {
        public Harness(string prompt)
        {
            Root = Path.Combine(Path.GetTempPath(), "caicli-execution-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Snapshot = CliEnvironmentSnapshot.Create(Root, Root, userProfile: Path.Combine(Root, "profile"),
                dotnetSdkVersion: "9.0.308", openAiModel: "fake-week73");
            WorkspaceSnapshotProjection workspace = new WorkspaceApplicationService().Snapshot(Snapshot).Data!;
            WorkspaceId = workspace.WorkspaceId;
            WorkspaceRoot = workspace.RootPath;
            ThreadId = ThreadIdentity.CreateThreadId();
            Now = DateTimeOffset.UtcNow;
            Threads = new ThreadStore(Path.Combine(Root, "state", "threads"));
            Composer = new ComposerIntentStore(Path.Combine(Root, "state", "composer"));
            Assert.True(Threads.Create(new ThreadRecord
            {
                ThreadId = ThreadId,
                WorkspaceId = WorkspaceId,
                WorkspaceRootIdentity = workspace.RootPath,
                Title = "Execution",
                CreatedAtUtc = Now,
                UpdatedAtUtc = Now
            }).Succeeded);
            Assert.True(Composer.Enqueue(WorkspaceId, workspace.RootPath, ThreadId, 0, "enqueue", Intent(prompt)).Succeeded);
            Service = new TurnExecutionApplicationService(_ => Threads, _ => Composer, () => Now.AddSeconds(1), new WorkspaceApplicationService());
        }

        public string Root { get; }
        public CliEnvironmentSnapshot Snapshot { get; }
        public string WorkspaceId { get; }
        public string WorkspaceRoot { get; }
        public string ThreadId { get; }
        public DateTimeOffset Now { get; }
        public ThreadStore Threads { get; }
        public ComposerIntentStore Composer { get; }
        public TurnExecutionApplicationService Service { get; }

        public ApplicationResult<TurnExecutionStateProjection> Start(string mutationId) => Service.Start(new TurnStartRequest(
            Snapshot, ThreadId, 0, 1, mutationId));

        public PendingComposerIntentRecord Intent(string prompt) => new()
        {
            IntentId = "intent_" + Guid.NewGuid().ToString("N"),
            WorkspaceId = WorkspaceId,
            WorkspaceRootIdentity = WorkspaceRoot,
            ThreadId = ThreadId,
            Prompt = prompt,
            EffectiveModel = Snapshot.Configuration.Model,
            ModelSource = Snapshot.Configuration.ModelSource,
            ApprovalMode = Snapshot.Configuration.ApprovalMode.ToString(),
            ApprovalModeSource = Snapshot.Configuration.ApprovalModeSource,
            CreatedAtUtc = Now
        };

        public void Dispose() { try { Directory.Delete(Root, true); } catch { } }
    }

    private sealed class RejectingWaiter : IInteractiveApprovalWaiter
    {
        public ValueTask<InteractiveApprovalDecision> WaitAsync(DurableApprovalProjection request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Approval was not expected.");
    }

    private sealed class ResolvingWaiter(Harness test) : IInteractiveApprovalWaiter
    {
        public DurableApprovalProjection? Request { get; private set; }

        public ValueTask<InteractiveApprovalDecision> WaitAsync(DurableApprovalProjection request, CancellationToken cancellationToken)
        {
            Request = request;
            ThreadAggregate aggregate = test.Threads.Read(test.ThreadId).Aggregate!;
            ApplicationResult<TurnExecutionStateProjection> resolved = test.Service.ResolveApproval(new ApprovalResolveRequest(
                test.Snapshot, test.ThreadId, request.TurnId, request.RequestId, "approve", aggregate.Record.Revision,
                request.TurnRevision, request.ApprovalRevision, "approve-1"));
            Assert.True(resolved.Succeeded, resolved.Error?.SafeMessage);
            return ValueTask.FromResult(new InteractiveApprovalDecision("approve", "approve-1", request.TurnRevision, request.ApprovalRevision));
        }
    }

    private sealed class DenyingWaiter(Harness test) : IInteractiveApprovalWaiter
    {
        public ValueTask<InteractiveApprovalDecision> WaitAsync(
            DurableApprovalProjection request,
            CancellationToken cancellationToken)
        {
            ThreadAggregate aggregate = test.Threads.Read(test.ThreadId).Aggregate!;
            ApplicationResult<TurnExecutionStateProjection> resolved = test.Service.ResolveApproval(
                new ApprovalResolveRequest(
                    test.Snapshot,
                    test.ThreadId,
                    request.TurnId,
                    request.RequestId,
                    "deny",
                    aggregate.Record.Revision,
                    request.TurnRevision,
                    request.ApprovalRevision,
                    "deny-1"));
            Assert.True(resolved.Succeeded, resolved.Error?.SafeMessage);
            return ValueTask.FromResult(new InteractiveApprovalDecision(
                "deny",
                "deny-1",
                request.TurnRevision,
                request.ApprovalRevision));
        }
    }

    private sealed class DeferredResolvingWaiter(Harness test) : IInteractiveApprovalWaiter
    {
        private readonly TaskCompletionSource<InteractiveApprovalDecision> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public DurableApprovalProjection? Request { get; private set; }

        public ValueTask<InteractiveApprovalDecision> WaitAsync(
            DurableApprovalProjection request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return new ValueTask<InteractiveApprovalDecision>(
                completion.Task.WaitAsync(cancellationToken));
        }

        public void Resolve()
        {
            DurableApprovalProjection request =
                Request ?? throw new InvalidOperationException("Approval waiter was not registered.");
            ThreadAggregate aggregate = test.Threads.Read(test.ThreadId).Aggregate!;
            ApplicationResult<TurnExecutionStateProjection> resolved = test.Service.ResolveApproval(
                new ApprovalResolveRequest(
                    test.Snapshot,
                    test.ThreadId,
                    request.TurnId,
                    request.RequestId,
                    "approve",
                    aggregate.Record.Revision,
                    request.TurnRevision,
                    request.ApprovalRevision,
                    "approve-deferred"));
            Assert.True(resolved.Succeeded, resolved.Error?.SafeMessage);
            completion.TrySetResult(new InteractiveApprovalDecision(
                "approve",
                "approve-deferred",
                request.TurnRevision,
                request.ApprovalRevision));
        }
    }

    private sealed class BlockingRuntime : ITurnExecutionRuntime
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<TurnRuntimeResult> ExecuteAsync(TurnExecutionInput input, ITurnExecutionEventSink eventSink,
            IInteractiveApprovalGateway approvalGateway, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new TurnRuntimeResult("completed", "completed", null, "unreachable");
        }
    }

    private sealed class ProviderProgressRuntime : ITurnExecutionRuntime
    {
        public async Task<TurnRuntimeResult> ExecuteAsync(
            TurnExecutionInput input,
            ITurnExecutionEventSink eventSink,
            IInteractiveApprovalGateway approvalGateway,
            CancellationToken cancellationToken)
        {
            await eventSink.EmitAsync(new TurnRuntimeEvent(
                1, "provider-retry", TurnRuntimeEventKind.ProviderProgress, ProviderAttemptPhase.RetryWait,
                "temporary failure", DateTimeOffset.UtcNow,
                ProviderAttempt: 1,
                MaxAdditionalRetries: 5,
                ProviderPhase: ProviderAttemptPhase.RetryWait,
                AttemptHasStreamContent: false,
                AssistantMessageId: "assistant-test",
                ErrorCategory: "transport",
                Retryable: true,
                SafeErrorMessage: "The model connection could not be established."), cancellationToken);
            await eventSink.EmitAsync(new TurnRuntimeEvent(
                2, "provider-streaming", TurnRuntimeEventKind.ProviderProgress, ProviderAttemptPhase.Streaming,
                "streaming", DateTimeOffset.UtcNow,
                ProviderAttempt: 2,
                MaxAdditionalRetries: 5,
                ProviderPhase: ProviderAttemptPhase.Streaming,
                AttemptHasStreamContent: true,
                AssistantMessageId: "assistant-test"), cancellationToken);
            await eventSink.EmitAsync(new TurnRuntimeEvent(
                3, "assistant-stream", TurnRuntimeEventKind.Assistant, ProviderAttemptPhase.Streaming,
                "successful attempt", DateTimeOffset.UtcNow,
                ProviderAttempt: 2,
                MaxAdditionalRetries: 5,
                ProviderPhase: ProviderAttemptPhase.Streaming,
                AttemptHasStreamContent: true,
                AssistantMessageId: "assistant-test"), cancellationToken);
            return new TurnRuntimeResult("completed", "completed", null, "successful attempt");
        }
    }

    private sealed class SideEffectThenProviderExhaustionRuntime : ITurnExecutionRuntime
    {
        public async Task<TurnRuntimeResult> ExecuteAsync(
            TurnExecutionInput input,
            ITurnExecutionEventSink eventSink,
            IInteractiveApprovalGateway approvalGateway,
            CancellationToken cancellationToken)
        {
            await eventSink.EmitAsync(new TurnRuntimeEvent(
                1,
                "completed-operation",
                TurnRuntimeEventKind.ToolCompleted,
                "completed",
                "A controlled operation completed.",
                DateTimeOffset.UtcNow,
                Name: "fixture.write",
                Succeeded: true), cancellationToken);
            await eventSink.EmitAsync(new TurnRuntimeEvent(
                2,
                "provider-exhausted",
                TurnRuntimeEventKind.ProviderProgress,
                ProviderAttemptPhase.Failed,
                "The model connection failed after all retries.",
                DateTimeOffset.UtcNow,
                ProviderAttempt: ProviderRequestRetryLimits.MaxAttempts,
                MaxAdditionalRetries: ProviderRequestRetryLimits.MaxAdditionalRetries,
                ProviderPhase: ProviderAttemptPhase.Failed,
                AttemptHasStreamContent: false,
                AssistantMessageId: "assistant-exhausted",
                ErrorCategory: "transport",
                Retryable: true,
                SafeErrorMessage: "The model connection could not be established.",
                RetryExhausted: true), cancellationToken);
            return new TurnRuntimeResult(
                "failed",
                "provider-failure",
                "provider-retries-exhausted",
                "The model connection could not be established.");
        }
    }

    private sealed class InvalidEventRuntime(string mode) : ITurnExecutionRuntime
    {
        public async Task<TurnRuntimeResult> ExecuteAsync(
            TurnExecutionInput input,
            ITurnExecutionEventSink eventSink,
            IInteractiveApprovalGateway approvalGateway,
            CancellationToken cancellationToken)
        {
            long firstSequence = mode == "gap" ? 2 : 1;
            await eventSink.EmitAsync(new TurnRuntimeEvent(
                firstSequence,
                "correlation-one",
                TurnRuntimeEventKind.Model,
                "success",
                "First runtime event.",
                DateTimeOffset.UtcNow), cancellationToken);
            if (mode == "duplicate-correlation")
            {
                await eventSink.EmitAsync(new TurnRuntimeEvent(
                    2,
                    "correlation-one",
                    TurnRuntimeEventKind.Model,
                    "success",
                    "Duplicated runtime correlation.",
                    DateTimeOffset.UtcNow), cancellationToken);
            }
            return new TurnRuntimeResult("completed", "completed", null, "Invalid success.");
        }
    }
}
