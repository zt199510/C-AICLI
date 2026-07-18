using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class Week73TurnStartTests
{
    [Fact]
    public void QueueClaim_IsDurableDeterministicAndFinalizeIsBoundToCommittedTurn()
    {
        using TestRoot test = new();
        PendingComposerIntentRecord intent = test.Intent();
        Assert.True(test.Queue.Enqueue(test.WorkspaceId, test.WorkspaceRoot, test.ThreadId, 0, "enqueue", intent).Succeeded);

        ComposerIntentStoreResult claimed = test.Queue.Claim(
            test.WorkspaceId, test.WorkspaceRoot, test.ThreadId, 1, "claim", "start", test.Now);
        Assert.True(claimed.Succeeded, claimed.Diagnostic?.SafeMessage);
        Assert.Equal(ComposerQueueLifecycle.Claimed, claimed.Queue!.Lifecycle);
        Assert.Equal(ComposerIntentContractValidator.CreateDeterministicTurnId(
            intent.IntentId, ComposerIntentContractValidator.ComputeCanonicalInputSha256(intent)), claimed.Queue.Claim!.TurnId);

        ComposerIntentStore restarted = new(test.ComposerRoot);
        ComposerQueueRecord durable = restarted.Get(test.WorkspaceId, test.WorkspaceRoot, test.ThreadId).Queue!;
        Assert.Equal(claimed.Queue.Claim, durable.Claim);
        Assert.Equal(ComposerIntentErrorCode.MutationConflict,
            restarted.FinalizeClaim(test.WorkspaceId, test.WorkspaceRoot, test.ThreadId, 2, "finalize-bad",
                durable.Claim!.ClaimId, ThreadIdentity.CreateTurnId(), durable.Claim.CanonicalInputSha256).Diagnostic!.ErrorCode);

        ComposerIntentStoreResult finalized = restarted.FinalizeClaim(test.WorkspaceId, test.WorkspaceRoot, test.ThreadId, 2, "finalize",
            durable.Claim.ClaimId, durable.Claim.TurnId, durable.Claim.CanonicalInputSha256);
        Assert.True(finalized.Succeeded);
        Assert.Equal(ComposerQueueLifecycle.Empty, finalized.Queue!.Lifecycle);
        Assert.Null(finalized.Queue.PendingIntent);
    }

    [Fact]
    public void StartTurnFromIntent_CommitsUserMessageAtomicallyAndRetriesIdempotently()
    {
        using TestRoot test = new();
        PendingComposerIntentRecord input = test.Intent();
        Assert.True(test.Threads.Create(test.Thread()).Succeeded);
        Assert.True(test.Queue.Enqueue(test.WorkspaceId, test.WorkspaceRoot, test.ThreadId, 0, "enqueue", input).Succeeded);
        ComposerIntentClaimRecord claim = test.Queue.Claim(
            test.WorkspaceId, test.WorkspaceRoot, test.ThreadId, 1, "claim", "start", test.Now).Queue!.Claim!;
        TurnRecord turn = new()
        {
            TurnId = claim.TurnId,
            ThreadId = test.ThreadId,
            Ordinal = 1,
            CreatedAtUtc = test.Now,
            TaskSummary = input.Prompt,
            Mode = "desktop-write",
            SourceCorrelation = input.IntentId,
            ExecutionInput = input,
            CanonicalInputSha256 = claim.CanonicalInputSha256
        };
        TimelineItemRecord message = new()
        {
            ItemId = ThreadIdentity.CreateDeterministicItemId(claim.TurnId, "user-message", 0),
            ThreadId = test.ThreadId,
            TurnId = claim.TurnId,
            Sequence = 1,
            TimestampUtc = test.Now,
            Type = TimelineItemType.UserMessage,
            Status = "committed",
            Summary = input.Prompt,
            Payload = new TimelinePayloadRecord { Message = new TimelineMessagePayloadRecord(input.Prompt) }
        };

        ThreadStoreMutationResult started = test.Threads.StartTurnFromIntent(
            test.ThreadId, 0, "start", claim, input, turn, message);
        Assert.True(started.Succeeded, started.Diagnostic?.SafeMessage);
        Assert.Equal(claim.TurnId, started.Aggregate!.Record.ActiveTurnId);
        Assert.Equal(1, started.Aggregate.Record.CommittedSequence);
        Assert.Equal(claim.CanonicalInputSha256, started.Aggregate.Turns.Single().CanonicalInputSha256);

        ThreadStoreMutationResult retry = test.Threads.StartTurnFromIntent(
            test.ThreadId, 0, "start", claim, input, turn, message);
        Assert.True(retry.Succeeded, retry.Diagnostic?.SafeMessage);
        Assert.True(retry.Idempotent);
        Assert.Single(retry.Aggregate!.Turns);
        Assert.Single(test.Threads.ReadTimelinePage(test.ThreadId, 0, 10).Items);
    }

    private sealed class TestRoot : IDisposable
    {
        public TestRoot()
        {
            Root = Path.Combine(Path.GetTempPath(), "caicli-week73-" + Guid.NewGuid().ToString("N"));
            WorkspaceRoot = Path.Combine(Root, "workspace");
            ComposerRoot = Path.Combine(Root, "composer");
            Directory.CreateDirectory(WorkspaceRoot);
            ThreadId = ThreadIdentity.CreateThreadId();
            Threads = new ThreadStore(Path.Combine(Root, "threads"));
            Queue = new ComposerIntentStore(ComposerRoot);
        }

        public string Root { get; }
        public string WorkspaceRoot { get; }
        public string ComposerRoot { get; }
        public string WorkspaceId { get; } = "ws_week73";
        public string ThreadId { get; }
        public DateTimeOffset Now { get; } = DateTimeOffset.Parse("2026-07-17T01:02:03Z");
        public ThreadStore Threads { get; }
        public ComposerIntentStore Queue { get; }

        public ThreadRecord Thread() => new()
        {
            ThreadId = ThreadId,
            WorkspaceId = WorkspaceId,
            WorkspaceRootIdentity = WorkspaceRoot,
            Title = "Week 73",
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now
        };

        public PendingComposerIntentRecord Intent() => new()
        {
            IntentId = "intent_" + Guid.NewGuid().ToString("N"),
            WorkspaceId = WorkspaceId,
            WorkspaceRootIdentity = WorkspaceRoot,
            ThreadId = ThreadId,
            Prompt = "Implement the write path",
            EffectiveModel = "fake-week73",
            ModelSource = "test",
            ApprovalMode = "OnRequest",
            ApprovalModeSource = "test",
            CreatedAtUtc = Now
        };

        public void Dispose() { try { Directory.Delete(Root, true); } catch { } }
    }
}
