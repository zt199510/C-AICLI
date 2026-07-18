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
}
