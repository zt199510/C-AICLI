using System.Text.Json.Nodes;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ThreadStoreTests
{
    [Fact]
    public void Create_read_rename_archive_and_confirmed_delete_use_expected_revision()
    {
        using TestThreadStore test = TestThreadStore.Create();
        ThreadStoreMutationResult created = test.Store.Create(test.NewThread());
        Assert.True(created.Succeeded, created.Diagnostic?.SafeMessage);
        Assert.Equal(0, created.Aggregate?.Record.Revision);

        ThreadStoreMutationResult staleRename = test.Store.Rename(test.ThreadId, 1, "Renamed", test.Now.AddMinutes(1));
        Assert.Equal(ThreadErrorCode.ThreadRevisionConflict, staleRename.Diagnostic?.ErrorCode);

        ThreadStoreMutationResult renamed = test.Store.Rename(test.ThreadId, 0, "Renamed", test.Now.AddMinutes(1));
        Assert.True(renamed.Succeeded, renamed.Diagnostic?.SafeMessage);
        Assert.Equal(1, renamed.Aggregate?.Record.Revision);

        ThreadStoreMutationResult archived = test.Store.Archive(test.ThreadId, 1, test.Now.AddMinutes(2));
        Assert.True(archived.Succeeded, archived.Diagnostic?.SafeMessage);
        Assert.Equal(ThreadStatus.Archived, archived.Aggregate?.Record.Status);

        ThreadStoreMutationResult denied = test.Store.Delete(test.ThreadId, 2, "wrong");
        Assert.Equal(ThreadErrorCode.ThreadConfirmationInvalid, denied.Diagnostic?.ErrorCode);
        Assert.True(Directory.Exists(test.Store.GetLayout(test.ThreadId).ThreadRoot));

        ThreadStoreMutationResult deleted = test.Store.Delete(test.ThreadId, 2, test.ThreadId);
        Assert.True(deleted.Succeeded, deleted.Diagnostic?.SafeMessage);
        Assert.False(Directory.Exists(test.Store.GetLayout(test.ThreadId).ThreadRoot));
    }

    [Fact]
    public void Turn_mutations_enforce_single_active_and_terminal_immutability()
    {
        using TestThreadStore test = TestThreadStore.Create();
        ThreadStoreMutationResult created = test.Store.Create(test.NewThread());
        TurnRecord first = test.NewTurn(1);
        ThreadStoreMutationResult firstCreated = test.Store.CreateTurn(test.ThreadId, created.Aggregate!.Record.Revision, first);
        Assert.True(firstCreated.Succeeded, firstCreated.Diagnostic?.SafeMessage);
        Assert.Equal(ThreadStatus.Running, firstCreated.Aggregate?.Record.Status);

        TurnRecord second = test.NewTurn(2);
        ThreadStoreMutationResult conflict = test.Store.CreateTurn(test.ThreadId, 1, second);
        Assert.Equal(ThreadErrorCode.ThreadActiveTurnConflict, conflict.Diagnostic?.ErrorCode);

        ThreadStoreMutationResult running = test.Store.TransitionTurn(
            test.ThreadId, first.TurnId, 1, TurnStatus.Running, test.Now.AddSeconds(1));
        Assert.True(running.Succeeded, running.Diagnostic?.SafeMessage);
        ThreadStoreMutationResult completed = test.Store.TransitionTurn(
            test.ThreadId, first.TurnId, 2, TurnStatus.Completed, test.Now.AddSeconds(2), "completed");
        Assert.True(completed.Succeeded, completed.Diagnostic?.SafeMessage);
        Assert.Equal(ThreadStatus.Completed, completed.Aggregate?.Record.Status);
        ThreadStoreMutationResult regression = test.Store.TransitionTurn(
            test.ThreadId, first.TurnId, 3, TurnStatus.Running, test.Now.AddSeconds(3));
        Assert.Equal(ThreadErrorCode.TurnTransitionInvalid, regression.Diagnostic?.ErrorCode);
    }

    [Fact]
    public void Timeline_append_is_contiguous_revision_checked_and_idempotent()
    {
        using TestThreadStore test = TestThreadStore.CreateWithQueuedTurn();
        TimelineItemRecord first = test.NewMessage(1, "hello");
        ThreadStoreMutationResult appended = test.Store.AppendTimeline(
            test.ThreadId, test.TurnId, 1, 1, "append-1", [first]);
        Assert.True(appended.Succeeded, appended.Diagnostic?.SafeMessage);
        Assert.Equal(2, appended.Aggregate?.Record.Revision);
        Assert.Equal(1, appended.Aggregate?.Record.CommittedSequence);

        ThreadStoreMutationResult retry = test.Store.AppendTimeline(
            test.ThreadId, test.TurnId, 1, 1, "append-1", [first]);
        Assert.True(retry.Succeeded, retry.Diagnostic?.SafeMessage);
        Assert.True(retry.Idempotent);
        Assert.Equal(2, retry.Aggregate?.Record.Revision);

        TimelineItemRecord changed = first with { Summary = "changed" };
        ThreadStoreMutationResult reused = test.Store.AppendTimeline(
            test.ThreadId, test.TurnId, 2, 2, "append-1", [changed]);
        Assert.Equal(ThreadErrorCode.TimelineAppendConflict, reused.Diagnostic?.ErrorCode);

        TimelineItemRecord gap = test.NewMessage(3, "gap");
        ThreadStoreMutationResult gapResult = test.Store.AppendTimeline(
            test.ThreadId, test.TurnId, 2, 2, "append-gap", [gap]);
        Assert.Equal(ThreadErrorCode.TimelineAppendConflict, gapResult.Diagnostic?.ErrorCode);
        Assert.Equal(2, test.Store.Read(test.ThreadId).Aggregate?.Record.Revision);
    }

    [Fact]
    public void Uncommitted_orphan_is_invisible_but_missing_committed_child_fails_closed()
    {
        using TestThreadStore test = TestThreadStore.CreateWithQueuedTurn();
        ThreadStoreLayout layout = test.Store.GetLayout(test.ThreadId);
        string orphan = Path.Combine(layout.TimelineRoot, $"{1:D12}-{ThreadIdentity.CreateItemId()}.timeline.json");
        File.WriteAllText(orphan, "{not-committed}");

        ThreadStoreReadResult withOrphan = test.Store.Read(test.ThreadId);
        Assert.True(withOrphan.Succeeded, withOrphan.Diagnostic?.SafeMessage);
        Assert.Equal(0, withOrphan.Aggregate?.Record.CommittedSequence);

        File.Delete(orphan);
        TimelineItemRecord item = test.NewMessage(1, "committed");
        ThreadStoreMutationResult append = test.Store.AppendTimeline(test.ThreadId, test.TurnId, 1, 1, "append", [item]);
        Assert.True(append.Succeeded, append.Diagnostic?.SafeMessage);
        File.Delete(Assert.Single(Directory.EnumerateFiles(layout.TimelineRoot, "*.timeline.json")));

        ThreadStoreReadResult missing = test.Store.Read(test.ThreadId);
        Assert.False(missing.Succeeded);
        Assert.Equal(ThreadErrorCode.ThreadReferenceMissing, missing.Diagnostic?.ErrorCode);
    }

    [Fact]
    public void Turn_snapshot_hash_and_unknown_manifest_members_fail_closed()
    {
        using TestThreadStore hashTest = TestThreadStore.CreateWithQueuedTurn();
        ThreadStoreLayout hashLayout = hashTest.Store.GetLayout(hashTest.ThreadId);
        string turnPath = Assert.Single(Directory.EnumerateFiles(hashLayout.TurnsRoot, "*.turn.json"));
        JsonObject turnJson = Assert.IsType<JsonObject>(JsonNode.Parse(File.ReadAllText(turnPath)));
        turnJson["taskSummary"] = "tampered";
        File.WriteAllText(turnPath, turnJson.ToJsonString());
        Assert.Equal(ThreadErrorCode.TurnRecordCorrupt, hashTest.Store.Read(hashTest.ThreadId).Diagnostic?.ErrorCode);

        using TestThreadStore unknownTest = TestThreadStore.Create();
        Assert.True(unknownTest.Store.Create(unknownTest.NewThread()).Succeeded);
        ThreadStoreLayout unknownLayout = unknownTest.Store.GetLayout(unknownTest.ThreadId);
        JsonObject manifest = Assert.IsType<JsonObject>(JsonNode.Parse(File.ReadAllText(unknownLayout.ManifestPath)));
        manifest["approvalToken"] = "must-not-be-accepted";
        File.WriteAllText(unknownLayout.ManifestPath, manifest.ToJsonString());
        Assert.Equal(ThreadErrorCode.ThreadRecordCorrupt, unknownTest.Store.Read(unknownTest.ThreadId).Diagnostic?.ErrorCode);
    }

    [Fact]
    public void Active_turn_read_requires_explicit_recovery_and_recovery_does_not_replay()
    {
        using TestThreadStore test = TestThreadStore.CreateWithQueuedTurn();
        ThreadStoreMutationResult running = test.Store.TransitionTurn(
            test.ThreadId, test.TurnId, 1, TurnStatus.Running, test.Now.AddSeconds(1));
        Assert.True(running.Succeeded, running.Diagnostic?.SafeMessage);
        ThreadStoreReadResult active = test.Store.Read(test.ThreadId);
        Assert.True(active.RecoveryRequired);
        Assert.Equal(TurnStatus.Running, Assert.Single(active.Aggregate!.Turns).Status);

        ThreadStoreMutationResult recovered = test.Store.RecoverInterrupted(
            test.ThreadId, 2, test.Now.AddSeconds(2), "recovery-1");
        Assert.True(recovered.Succeeded, recovered.Diagnostic?.SafeMessage);
        Assert.Equal(ThreadStatus.Failed, recovered.Aggregate?.Record.Status);
        Assert.Null(recovered.Aggregate?.Record.ActiveTurnId);
        TurnRecord turn = Assert.Single(recovered.Aggregate!.Turns);
        Assert.Equal(TurnStatus.Failed, turn.Status);
        Assert.Equal("interrupted", turn.StopReason);

        ThreadTimelinePageResult page = test.Store.ReadTimelinePage(test.ThreadId);
        Assert.True(page.Succeeded, page.Diagnostic?.SafeMessage);
        Assert.Equal(new[] { TimelineItemType.WarningRaised, TimelineItemType.TurnCompleted }, page.Items.Select(item => item.Type));
    }

    [Fact]
    public void List_keeps_valid_threads_when_one_manifest_is_corrupt_and_orders_are_caller_controlled()
    {
        using TestThreadStore first = TestThreadStore.Create(sharedRoot: null);
        string root = first.Root;
        Assert.True(first.Store.Create(first.NewThread()).Succeeded);
        using TestThreadStore second = TestThreadStore.Create(root);
        Assert.True(second.Store.Create(second.NewThread()).Succeeded);
        File.WriteAllText(second.Store.GetLayout(second.ThreadId).ManifestPath, "{}");

        ThreadStoreListResult listed = first.Store.List(first.WorkspaceId);
        Assert.Single(listed.Records);
        Assert.Single(listed.Diagnostics);
        Assert.Equal(first.ThreadId, listed.Records[0].ThreadId);
    }

    [Fact]
    public void Threads_root_reparse_point_is_denied_without_writing_outside()
    {
        using TestThreadStore test = TestThreadStore.Create();
        string parent = Path.Combine(test.Root, "linked");
        string outside = Path.Combine(test.Root, "outside");
        string link = Path.Combine(parent, "threads");
        Directory.CreateDirectory(parent);
        Directory.CreateDirectory(outside);
        try
        {
            Directory.CreateSymbolicLink(link, outside);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        TestThreadStore linked = TestThreadStore.CreateForExistingRoot(test.Root, link);
        ThreadStoreMutationResult result = linked.Store.Create(linked.NewThread());
        Assert.False(result.Succeeded);
        Assert.Equal(ThreadErrorCode.ThreadReparsePoint, result.Diagnostic?.ErrorCode);
        Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
    }

    [Fact]
    public void Exclusive_lock_and_concurrent_append_allow_only_one_revision_commit()
    {
        using TestThreadStore locked = TestThreadStore.Create();
        Assert.True(locked.Store.Create(locked.NewThread()).Succeeded);
        ThreadStoreLayout layout = locked.Store.GetLayout(locked.ThreadId);
        using (FileStream held = new(layout.LockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            ThreadStoreMutationResult conflict = locked.Store.Rename(
                locked.ThreadId, 0, "Blocked", locked.Now.AddSeconds(1));
            Assert.Equal(ThreadErrorCode.ThreadRevisionConflict, conflict.Diagnostic?.ErrorCode);
        }
        Assert.Equal(0, locked.Store.Read(locked.ThreadId).Aggregate?.Record.Revision);

        using TestThreadStore concurrent = TestThreadStore.CreateWithQueuedTurn();
        TimelineItemRecord first = concurrent.NewMessage(1, "first");
        TimelineItemRecord second = concurrent.NewMessage(1, "second") with { ItemId = ThreadIdentity.CreateItemId() };
        ThreadStoreMutationResult? firstResult = null;
        ThreadStoreMutationResult? secondResult = null;
        Parallel.Invoke(
            () => firstResult = concurrent.Store.AppendTimeline(
                concurrent.ThreadId, concurrent.TurnId, 1, 1, "concurrent-first", [first]),
            () => secondResult = concurrent.Store.AppendTimeline(
                concurrent.ThreadId, concurrent.TurnId, 1, 1, "concurrent-second", [second]));

        Assert.Equal(1, new[] { firstResult, secondResult }.Count(result => result!.Succeeded));
        Assert.Equal(1, concurrent.Store.Read(concurrent.ThreadId).Aggregate?.Record.CommittedSequence);
        Assert.Single(Directory.EnumerateFiles(
            concurrent.Store.GetLayout(concurrent.ThreadId).TurnsRoot, "*.turn.json"));
    }

    [Fact]
    public async Task Concurrent_reads_reconcile_atomic_manifest_and_turn_replacements()
    {
        using TestThreadStore test = TestThreadStore.CreateWithQueuedTurn();
        var failures = new System.Collections.Concurrent.ConcurrentBag<ThreadStoreDiagnostic>();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int complete = 0;
        Task reader = Task.Run(async () =>
        {
            await start.Task;
            while (Volatile.Read(ref complete) == 0)
            {
                ThreadStoreReadResult read = test.Store.Read(test.ThreadId);
                if (!read.Succeeded && read.Diagnostic is not null) failures.Add(read.Diagnostic);
            }
        });
        Task writer = Task.Run(async () =>
        {
            await start.Task;
            try
            {
                long revision = 1;
                for (int sequence = 1; sequence <= 24; sequence++)
                {
                    ThreadStoreMutationResult result = test.Store.AppendTimeline(
                        test.ThreadId, test.TurnId, revision, sequence, $"read-race-{sequence}",
                        [test.NewMessage(sequence, $"message-{sequence}")]);
                    Assert.True(result.Succeeded, result.Diagnostic?.SafeMessage);
                    revision = result.Aggregate!.Record.Revision;
                }
            }
            finally
            {
                Volatile.Write(ref complete, 1);
            }
        });
        start.SetResult();
        await Task.WhenAll(writer, reader).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(failures);
    }

    [Fact]
    public void Manifest_replace_retries_a_bounded_transient_windows_reader_lock()
    {
        if (!OperatingSystem.IsWindows()) return;

        using TestThreadStore test = TestThreadStore.Create();
        Assert.True(test.Store.Create(test.NewThread()).Succeeded);
        string manifest = test.Store.GetLayout(test.ThreadId).ManifestPath;
        FileStream held = new(manifest, FileMode.Open, FileAccess.Read, FileShare.Read);
        var release = new Thread(() =>
        {
            Thread.Sleep(75);
            held.Dispose();
        });
        release.Start();

        ThreadStoreMutationResult result;
        try
        {
            result = test.Store.Rename(test.ThreadId, 0, "Retried", test.Now.AddSeconds(1));
        }
        finally
        {
            release.Join();
            held.Dispose();
        }

        Assert.True(result.Succeeded, result.Diagnostic?.SafeMessage);
        Assert.Equal("Retried", result.Aggregate?.Record.Title);
    }

    [Fact]
    public void Unsupported_oversize_and_duplicate_committed_sequence_fail_closed()
    {
        using TestThreadStore schema = TestThreadStore.Create();
        Assert.True(schema.Store.Create(schema.NewThread()).Succeeded);
        string schemaManifest = schema.Store.GetLayout(schema.ThreadId).ManifestPath;
        JsonObject schemaJson = Assert.IsType<JsonObject>(JsonNode.Parse(File.ReadAllText(schemaManifest)));
        schemaJson["schemaVersion"] = 999;
        File.WriteAllText(schemaManifest, schemaJson.ToJsonString());
        Assert.Equal(ThreadErrorCode.ThreadSchemaUnsupported, schema.Store.Read(schema.ThreadId).Diagnostic?.ErrorCode);

        using TestThreadStore oversize = TestThreadStore.Create();
        Assert.True(oversize.Store.Create(oversize.NewThread()).Succeeded);
        File.WriteAllText(
            oversize.Store.GetLayout(oversize.ThreadId).ManifestPath,
            new string('x', ThreadPersistenceLimits.MaxThreadManifestBytes + 1));
        Assert.Equal(ThreadErrorCode.ThreadLimitExceeded, oversize.Store.Read(oversize.ThreadId).Diagnostic?.ErrorCode);

        using TestThreadStore duplicate = TestThreadStore.CreateWithQueuedTurn();
        TimelineItemRecord item = duplicate.NewMessage(1, "committed");
        Assert.True(duplicate.Store.AppendTimeline(
            duplicate.ThreadId, duplicate.TurnId, 1, 1, "append", [item]).Succeeded);
        string timelineRoot = duplicate.Store.GetLayout(duplicate.ThreadId).TimelineRoot;
        string source = Assert.Single(Directory.EnumerateFiles(timelineRoot, "*.timeline.json"));
        File.Copy(source, Path.Combine(timelineRoot, $"{1:D12}-{ThreadIdentity.CreateItemId()}.timeline.json"));
        Assert.Equal(ThreadErrorCode.TimelineRecordCorrupt, duplicate.Store.Read(duplicate.ThreadId).Diagnostic?.ErrorCode);
    }

    [Fact]
    public void Delete_rejects_descendant_reparse_without_traversing_or_deleting_target()
    {
        using TestThreadStore test = TestThreadStore.Create();
        Assert.True(test.Store.Create(test.NewThread()).Succeeded);
        Assert.True(test.Store.Archive(test.ThreadId, 0, test.Now.AddSeconds(1)).Succeeded);
        string outside = Path.Combine(test.Root, "outside-delete");
        Directory.CreateDirectory(outside);
        string marker = Path.Combine(outside, "preserve.txt");
        File.WriteAllText(marker, "preserve");
        string link = Path.Combine(test.Store.GetLayout(test.ThreadId).TimelineRoot, "outside-link");
        try
        {
            Directory.CreateSymbolicLink(link, outside);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        ThreadStoreMutationResult deleted = test.Store.Delete(test.ThreadId, 1, test.ThreadId);
        Assert.False(deleted.Succeeded);
        Assert.Equal(ThreadErrorCode.ThreadReparsePoint, deleted.Diagnostic?.ErrorCode);
        Assert.True(File.Exists(marker));
        Assert.True(Directory.Exists(test.Store.GetLayout(test.ThreadId).ThreadRoot));
    }

    private sealed class TestThreadStore : IDisposable
    {
        private readonly bool ownsRoot;

        private TestThreadStore(string root, string threadsRoot, bool ownsRoot)
        {
            Root = root;
            this.ownsRoot = ownsRoot;
            Store = new ThreadStore(threadsRoot);
            ThreadId = ThreadIdentity.CreateThreadId();
            TurnId = ThreadIdentity.CreateTurnId();
        }

        public string Root { get; }
        public ThreadStore Store { get; }
        public string ThreadId { get; }
        public string TurnId { get; }
        public string WorkspaceId { get; } = "ws_0123456789abcdef01234567";
        public DateTimeOffset Now { get; } = DateTimeOffset.Parse("2026-07-16T01:02:03Z");

        public static TestThreadStore Create(string? sharedRoot = null)
        {
            bool owns = sharedRoot is null;
            string root = sharedRoot ?? Path.Combine(Path.GetTempPath(), "caicli-thread-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new TestThreadStore(root, Path.Combine(root, ".caicli", "threads"), owns);
        }

        public static TestThreadStore CreateForExistingRoot(string root, string threadsRoot) => new(root, threadsRoot, ownsRoot: false);

        public static TestThreadStore CreateWithQueuedTurn()
        {
            TestThreadStore test = Create();
            ThreadStoreMutationResult created = test.Store.Create(test.NewThread());
            Assert.True(created.Succeeded, created.Diagnostic?.SafeMessage);
            ThreadStoreMutationResult turn = test.Store.CreateTurn(test.ThreadId, 0, test.NewTurn(1));
            Assert.True(turn.Succeeded, turn.Diagnostic?.SafeMessage);
            return test;
        }

        public ThreadRecord NewThread() => new()
        {
            ThreadId = ThreadId,
            WorkspaceId = WorkspaceId,
            WorkspaceRootIdentity = Path.Combine(Root, "workspace"),
            Title = "Test thread",
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now
        };

        public TurnRecord NewTurn(int ordinal) => new()
        {
            TurnId = ordinal == 1 ? TurnId : ThreadIdentity.CreateTurnId(),
            ThreadId = ThreadId,
            Ordinal = ordinal,
            CreatedAtUtc = Now,
            TaskSummary = "Test task"
        };

        public TimelineItemRecord NewMessage(long sequence, string summary) => new()
        {
            ItemId = ThreadIdentity.CreateDeterministicItemId(ThreadId, "message", checked((int)sequence)),
            ThreadId = ThreadId,
            TurnId = TurnId,
            Sequence = sequence,
            TimestampUtc = Now.AddSeconds(sequence),
            Type = TimelineItemType.UserMessage,
            Status = "recorded",
            Summary = summary,
            Payload = new TimelinePayloadRecord { Message = new TimelineMessagePayloadRecord(summary) },
            Redaction = new TimelineRedactionRecord { Applied = false }
        };

        public void Dispose()
        {
            if (!ownsRoot)
            {
                return;
            }

            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch
            {
            }
        }
    }
}
