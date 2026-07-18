using System.Text.Json;
using CSharpAiCli.Application;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ThreadApplicationServiceTests
{
    [Fact]
    public void Create_list_get_rename_archive_delete_return_application_dtos()
    {
        using Fixture fixture = Fixture.Create();
        ThreadApplicationService service = fixture.CreateService();
        ApplicationResult<WorkspaceSnapshotProjection> workspace = new WorkspaceApplicationService().Snapshot(fixture.Snapshot);

        ApplicationResult<ThreadSummaryProjection> created = service.Create(
            new ThreadCreateRequest(fixture.Snapshot, " Review apiKey=application-secret "));
        ApplicationResult<ThreadListProjection> listed = service.List(
            new ThreadListRequest(fixture.Snapshot, workspace.Data!.WorkspaceId));
        ApplicationResult<ThreadDetailProjection> detail = service.Get(
            new ThreadGetRequest(fixture.Snapshot, created.Data!.ThreadId));

        Assert.True(created.Succeeded, created.Error?.SafeMessage);
        Assert.DoesNotContain("application-secret", JsonSerializer.Serialize(created), StringComparison.Ordinal);
        Assert.Equal(created.Data.ThreadId, Assert.Single(listed.Data!.Threads).ThreadId);
        Assert.Empty(detail.Data!.Turns);
        Assert.Empty(detail.Data.Timeline);

        fixture.Advance();
        ApplicationResult<ThreadSummaryProjection> renamed = service.Rename(new ThreadRenameRequest(
            fixture.Snapshot, created.Data.ThreadId, created.Data.Revision, "Renamed"));
        fixture.Advance();
        ApplicationResult<ThreadSummaryProjection> archived = service.Archive(new ThreadArchiveRequest(
            fixture.Snapshot, created.Data.ThreadId, renamed.Data!.Revision));
        ApplicationResult<ThreadDeleteProjection> denied = service.Delete(new ThreadDeleteRequest(
            fixture.Snapshot, created.Data.ThreadId, archived.Data!.Revision, "wrong"));
        ApplicationResult<ThreadDeleteProjection> deleted = service.Delete(new ThreadDeleteRequest(
            fixture.Snapshot, created.Data.ThreadId, archived.Data.Revision, created.Data.ThreadId));

        Assert.Equal("Renamed", renamed.Data?.Title);
        Assert.Equal(ThreadStatus.Archived, archived.Data?.Status);
        Assert.Equal(ApplicationErrorCategory.Validation, denied.Error?.Category);
        Assert.True(deleted.Data?.Deleted);
    }

    [Fact]
    public void List_is_workspace_filtered_stably_ordered_and_page_bounded()
    {
        using Fixture fixture = Fixture.Create();
        ThreadApplicationService service = fixture.CreateService();
        string workspaceId = new WorkspaceApplicationService().Snapshot(fixture.Snapshot).Data!.WorkspaceId;
        ApplicationResult<ThreadSummaryProjection> first = service.Create(new ThreadCreateRequest(fixture.Snapshot, "First"));
        fixture.Advance();
        ApplicationResult<ThreadSummaryProjection> second = service.Create(new ThreadCreateRequest(fixture.Snapshot, "Second"));

        ApplicationResult<ThreadListProjection> result = service.List(
            new ThreadListRequest(fixture.Snapshot, workspaceId, PageSize: 1));
        ApplicationResult<ThreadListProjection> other = service.List(
            new ThreadListRequest(fixture.Snapshot, "ws_other", PageSize: 10));

        Assert.True(result.Succeeded);
        Assert.True(result.Truncated);
        Assert.Equal(second.Data?.ThreadId, Assert.Single(result.Data!.Threads).ThreadId);
        Assert.Empty(other.Data!.Threads);
        Assert.NotEqual(first.Data?.ThreadId, second.Data?.ThreadId);
    }

    [Fact]
    public void Get_projects_timeline_cursor_and_preserves_missing_pointer_with_diagnostic()
    {
        using Fixture fixture = Fixture.Create();
        string threadId = ThreadIdentity.CreateThreadId();
        string turnId = ThreadIdentity.CreateTurnId();
        ThreadSourcePointerRecord pointer = new()
        {
            Kind = ThreadSourceKind.Session,
            SourceId = "missing-session",
            Availability = ThreadSourceAvailability.Available
        };
        TurnRecord turn = new()
        {
            TurnId = turnId,
            ThreadId = threadId,
            Ordinal = 1,
            Status = TurnStatus.Completed,
            CreatedAtUtc = fixture.Now,
            StartedAtUtc = fixture.Now,
            CompletedAtUtc = fixture.Now,
            TaskSummary = "Imported",
            StopReason = "completed",
            SourcePointers = [pointer],
            TimelineFirstSequence = 1,
            TimelineLastSequence = 2,
            TimelineItemCount = 2
        };
        TimelineItemRecord[] items =
        [
            fixture.Message(threadId, turnId, 1, "one", pointer),
            fixture.Message(threadId, turnId, 2, "two", pointer)
        ];
        ThreadRecord record = fixture.Record(threadId, "Timeline");
        Assert.True(fixture.Store.CreateProjection(record, [turn], items).Succeeded);
        ThreadApplicationService service = fixture.CreateService();

        ApplicationResult<ThreadDetailProjection> first = service.Get(
            new ThreadGetRequest(fixture.Snapshot, threadId, TimelinePageSize: 1));
        ApplicationResult<ThreadDetailProjection> second = service.Get(
            new ThreadGetRequest(fixture.Snapshot, threadId, AfterSequence: 1, TimelinePageSize: 1));

        Assert.Equal(1, Assert.Single(first.Data!.Timeline).Sequence);
        Assert.True(first.Data.TimelineTruncated);
        Assert.Equal(1, first.Data.NextSequence);
        Assert.Equal(2, Assert.Single(second.Data!.Timeline).Sequence);
        Assert.False(second.Data.TimelineTruncated);
        Assert.Contains(first.Diagnostics, diagnostic => diagnostic.Code == ConversationTranscriptReadErrorCode.NotFound);
        Assert.Equal(ThreadSourceAvailability.Missing, first.Data.Timeline[0].Source?.Availability);
    }

    [Fact]
    public void Public_operations_honor_pre_cancellation()
    {
        using Fixture fixture = Fixture.Create();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        ThreadApplicationService service = fixture.CreateService();

        Assert.Throws<OperationCanceledException>(() => service.Create(
            new ThreadCreateRequest(fixture.Snapshot, "Canceled"), cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => service.List(
            new ThreadListRequest(fixture.Snapshot, "ws_any"), cancellation.Token));
        Assert.Empty(Directory.Exists(fixture.Store.ThreadsRoot)
            ? Directory.EnumerateFileSystemEntries(fixture.Store.ThreadsRoot)
            : []);
    }

    [Fact]
    public void Get_and_mutations_reject_threads_bound_to_another_workspace()
    {
        using Fixture fixture = Fixture.Create();
        ThreadRecord foreign = fixture.Record(ThreadIdentity.CreateThreadId(), "Foreign") with
        {
            WorkspaceId = "ws_ffffffffffffffffffffffff"
        };
        Assert.True(fixture.Store.Create(foreign).Succeeded);
        ThreadApplicationService service = fixture.CreateService();

        ApplicationResult<ThreadDetailProjection> get = service.Get(new ThreadGetRequest(
            fixture.Snapshot, foreign.ThreadId));
        ApplicationResult<ThreadSummaryProjection> rename = service.Rename(new ThreadRenameRequest(
            fixture.Snapshot, foreign.ThreadId, 0, "No"));

        Assert.Equal("thread-workspace-mismatch", get.Error?.Code);
        Assert.Equal(ApplicationErrorCategory.Workspace, get.Error?.Category);
        Assert.Equal("thread-workspace-mismatch", rename.Error?.Code);
        Assert.Equal(0, fixture.Store.Read(foreign.ThreadId).Aggregate?.Record.Revision);
    }

    [Fact]
    public void Get_projects_stale_active_turn_as_recovery_required()
    {
        using Fixture fixture = Fixture.Create();
        string threadId = ThreadIdentity.CreateThreadId();
        string turnId = ThreadIdentity.CreateTurnId();
        TurnRecord turn = new()
        {
            TurnId = turnId,
            ThreadId = threadId,
            Ordinal = 1,
            Status = TurnStatus.Queued,
            CreatedAtUtc = fixture.Now,
            TaskSummary = "Interrupted desktop write"
        };
        ThreadRecord record = fixture.Record(threadId, "Recovery");
        ThreadStoreMutationResult created = fixture.Store.CreateProjection(record, [turn], []);
        Assert.True(created.Succeeded, created.Diagnostic?.SafeMessage);
        ThreadStoreMutationResult running = fixture.Store.TransitionTurn(
            threadId, turnId, created.Aggregate!.Record.Revision, TurnStatus.Running, fixture.Now);
        Assert.True(running.Succeeded, running.Diagnostic?.SafeMessage);

        ApplicationResult<ThreadDetailProjection> detail = fixture.CreateService().Get(
            new ThreadGetRequest(fixture.Snapshot, threadId));

        Assert.True(detail.Succeeded, detail.Error?.SafeMessage);
        Assert.True(detail.Data!.RecoveryRequired);
        Assert.True(Assert.Single(detail.Data.Turns).RecoveryRequired);
        Assert.Equal(TurnStatus.Running, detail.Data.Turns[0].Status);
    }

    [Fact]
    public void List_preserves_valid_neighbor_and_reports_corrupt_state()
    {
        using Fixture fixture = Fixture.Create();
        ThreadApplicationService service = fixture.CreateService();
        ApplicationResult<ThreadSummaryProjection> valid = service.Create(
            new ThreadCreateRequest(fixture.Snapshot, "Valid neighbor"));
        ApplicationResult<ThreadSummaryProjection> corrupt = service.Create(
            new ThreadCreateRequest(fixture.Snapshot, "Corrupt neighbor"));
        ThreadStoreLayout layout = fixture.Store.GetLayout(corrupt.Data!.ThreadId);
        File.WriteAllText(layout.ManifestPath, "{not-json}");
        string workspaceId = new WorkspaceApplicationService().Snapshot(fixture.Snapshot).Data!.WorkspaceId;

        ApplicationResult<ThreadListProjection> listed = service.List(
            new ThreadListRequest(fixture.Snapshot, workspaceId));

        Assert.True(listed.Succeeded, listed.Error?.SafeMessage);
        Assert.Equal(valid.Data!.ThreadId, Assert.Single(listed.Data!.Threads).ThreadId);
        ApplicationDiagnostic diagnostic = Assert.Single(listed.Diagnostics);
        Assert.Equal(ThreadErrorCode.ThreadRecordCorrupt, diagnostic.Code);
        Assert.Equal(ApplicationErrorCategory.CorruptState, diagnostic.Category);
        Assert.DoesNotContain(layout.ManifestPath, diagnostic.SafeMessage, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Fixture : IDisposable
    {
        private DateTimeOffset current;

        private Fixture(string root, CliEnvironmentSnapshot snapshot, ThreadStore store, FileConversationStore sessions)
        {
            Root = root;
            Snapshot = snapshot;
            Store = store;
            Sessions = sessions;
            current = DateTimeOffset.Parse("2026-07-16T01:02:03Z");
        }

        public string Root { get; }
        public CliEnvironmentSnapshot Snapshot { get; }
        public ThreadStore Store { get; }
        public FileConversationStore Sessions { get; }
        public DateTimeOffset Now => current;

        public static Fixture Create()
        {
            string root = Path.Combine(Path.GetTempPath(), "caicli-thread-application-tests-" + Guid.NewGuid().ToString("N"));
            string workspace = Path.Combine(root, "workspace");
            string profile = Path.Combine(root, "profile");
            Directory.CreateDirectory(workspace);
            Directory.CreateDirectory(profile);
            CliEnvironmentSnapshot snapshot = CliEnvironmentSnapshot.Create(
                workspace,
                currentDirectory: workspace,
                userProfile: profile,
                dotnetSdkVersion: "9.0.308",
                dotnetRuntime: ".NET 9",
                openAiApiKey: null,
                hasGlobalJson: false);
            return new Fixture(
                root,
                snapshot,
                new ThreadStore(Path.Combine(root, "state", "threads")),
                new FileConversationStore(Path.Combine(root, "state", "sessions")));
        }

        public void Advance() => current = current.AddMinutes(1);

        public ThreadApplicationService CreateService() => new(_ => Store, _ => Sessions, () => current);

        public ThreadRecord Record(string threadId, string title)
        {
            WorkspaceSnapshotProjection workspace = new WorkspaceApplicationService().Snapshot(Snapshot).Data!;
            return new ThreadRecord
            {
                ThreadId = threadId,
                WorkspaceId = workspace.WorkspaceId,
                WorkspaceRootIdentity = workspace.RootPath,
                Title = title,
                CreatedAtUtc = Now,
                UpdatedAtUtc = Now
            };
        }

        public TimelineItemRecord Message(
            string threadId,
            string turnId,
            long sequence,
            string text,
            ThreadSourcePointerRecord pointer) => new()
        {
            ItemId = ThreadIdentity.CreateDeterministicItemId(threadId, "message", checked((int)sequence)),
            ThreadId = threadId,
            TurnId = turnId,
            Sequence = sequence,
            TimestampUtc = Now.AddSeconds(sequence),
            Type = TimelineItemType.UserMessage,
            Source = pointer,
            Status = "recorded",
            Summary = text,
            Payload = new TimelinePayloadRecord { Message = new TimelineMessagePayloadRecord(text) }
        };

        public void Dispose()
        {
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
