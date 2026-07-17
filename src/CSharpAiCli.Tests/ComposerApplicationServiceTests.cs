using CSharpAiCli.Application;
using CSharpAiCli.Core;
using System.Text.Json;

namespace CSharpAiCli.Tests;

public sealed class ComposerApplicationServiceTests
{
    [Fact]
    public void Catalog_revision_is_deterministic_and_workspace_bound()
    {
        using TempRoot first = new();
        using TempRoot second = new();
        CatalogApplicationService service = new();
        CatalogQueryProjection a = service.Query(new CatalogQueryRequest(WorkspaceContext.Detect(first.Path), "experts", 200)).Data!;
        CatalogQueryProjection again = service.Query(new CatalogQueryRequest(WorkspaceContext.Detect(first.Path), "experts", 200)).Data!;
        CatalogQueryProjection other = service.Query(new CatalogQueryRequest(WorkspaceContext.Detect(second.Path), "experts", 200)).Data!;

        Assert.Equal(a.CatalogRevision, again.CatalogRevision);
        Assert.Equal(a.WorkspaceId, again.WorkspaceId);
        Assert.NotEqual(a.CatalogRevision, other.CatalogRevision);
        Assert.Matches("^[0-9a-f]{64}$", a.CatalogRevision);
    }

    [Fact]
    public void Enqueue_revalidates_context_and_catalog_without_creating_a_turn()
    {
        using TempRoot temp = new();
        File.WriteAllText(Path.Combine(temp.Path, "note.txt"), "hello");
        CliEnvironmentSnapshot snapshot = CliEnvironmentSnapshot.Create(
            temp.Path, temp.Path, userProfile: Path.Combine(temp.Path, "profile"), dotnetSdkVersion: "9.0.308", openAiModel: "gpt-test");
        WorkspaceApplicationService workspaceService = new();
        WorkspaceSnapshotProjection workspace = workspaceService.Snapshot(snapshot).Data!;
        ThreadStore threads = new(Path.Combine(temp.Path, "state", "threads"));
        ComposerIntentStore intents = new(Path.Combine(temp.Path, "state", "composer"));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ThreadRecord thread = new()
        {
            ThreadId = ThreadIdentity.CreateThreadId(), WorkspaceId = workspace.WorkspaceId,
            WorkspaceRootIdentity = workspace.RootPath, Title = "composer", CreatedAtUtc = now, UpdatedAtUtc = now
        };
        Assert.True(threads.Create(thread).Succeeded);
        ControlledContextApplicationService context = new();
        ControlledContextDescriptor descriptor = context.ResolveNativePath(workspace, Path.Combine(temp.Path, "note.txt"), "file").Data!;
        CatalogApplicationService catalog = new();
        CatalogQueryProjection experts = catalog.Query(new CatalogQueryRequest(snapshot.Workspace, "experts", 200)).Data!;
        CatalogItemProjection expert = Assert.Single(experts.Items, item => item.Id == "reviewer");
        ComposerApplicationService service = new(
            _ => threads, _ => intents, catalog, context, workspaceService, () => now.AddSeconds(1));

        ApplicationResult<ComposerStateProjection> enqueued = service.Enqueue(new ComposerEnqueueRequest(
            snapshot, thread.ThreadId, 0, 0, "enqueue-1", "Review this file",
            [descriptor.SelectionId], [new ComposerCatalogSelection("expert", expert.Id, experts.CatalogRevision)]));

        Assert.True(enqueued.Succeeded);
        Assert.Equal("ready", enqueued.Data!.PendingIntent!.Delivery);
        Assert.Empty(threads.Read(thread.ThreadId).Aggregate!.Turns);
        Assert.Equal(0, threads.Read(thread.ThreadId).Aggregate!.Record.Revision);
        Assert.NotNull(intents.Get(workspace.WorkspaceId, workspace.RootPath, thread.ThreadId).Queue!.PendingIntent);

        File.AppendAllText(Path.Combine(temp.Path, "note.txt"), " changed");
        string secondThread = ThreadIdentity.CreateThreadId();
        Assert.True(threads.Create(thread with { ThreadId = secondThread }).Succeeded);
        ApplicationResult<ComposerStateProjection> stale = service.Enqueue(new ComposerEnqueueRequest(
            snapshot, secondThread, 0, 0, "enqueue-2", "Review this file",
            [descriptor.SelectionId], []));
        Assert.False(stale.Succeeded);
        Assert.Equal(ApplicationErrorCategory.Conflict, stale.Error!.Category);

        string activeThreadId = ThreadIdentity.CreateThreadId();
        ThreadRecord activeThread = thread with { ThreadId = activeThreadId };
        Assert.True(threads.Create(activeThread).Succeeded);
        TurnRecord activeTurn = new()
        {
            TurnId = ThreadIdentity.CreateTurnId(), ThreadId = activeThreadId, Ordinal = 1,
            Status = TurnStatus.Queued, CreatedAtUtc = now.AddMilliseconds(1), TaskSummary = "active input"
        };
        Assert.True(threads.CreateTurn(activeThreadId, 0, activeTurn).Succeeded);
        string activeBefore = JsonSerializer.Serialize(threads.Read(activeThreadId).Aggregate);
        ApplicationResult<ComposerStateProjection> nextTurn = service.Enqueue(new ComposerEnqueueRequest(
            snapshot, activeThreadId, 1, 0, "enqueue-active", "Queue after active turn", [], []));
        Assert.True(nextTurn.Succeeded);
        Assert.Equal("next-turn", nextTurn.Data!.PendingIntent!.Delivery);
        Assert.Equal(activeBefore, JsonSerializer.Serialize(threads.Read(activeThreadId).Aggregate));
    }

    private sealed class TempRoot : IDisposable
    {
        public TempRoot() { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "caicli-composer-app-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path); }
        public string Path { get; }
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }
}
