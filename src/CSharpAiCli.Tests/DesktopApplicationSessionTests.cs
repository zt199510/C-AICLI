using System.Reflection;
using CSharpAiCli.Application;

namespace CSharpAiCli.Tests;

public sealed class DesktopApplicationSessionTests
{
    [Fact]
    public void Session_owns_workspace_snapshot_and_thread_use_cases()
    {
        using TempDirectory temp = TempDirectory.Create();
        DesktopApplicationSessionOpenResult opened = new DesktopApplicationSessionFactory().Open(temp.Path);

        Assert.True(opened.Succeeded);
        using DesktopApplicationSession session = Assert.IsType<DesktopApplicationSession>(opened.Session);
        Assert.Equal(Path.GetFullPath(temp.Path), session.Workspace.RootPath);

        ApplicationResult<ThreadSummaryProjection> created = session.CreateThread("desktop thread");
        ThreadSummaryProjection summary = Assert.IsType<ThreadSummaryProjection>(created.Data);
        ApplicationResult<ThreadListProjection> listed = session.ListThreads(50);
        Assert.Contains(listed.Data!.Threads, item => item.ThreadId == summary.ThreadId);
        Assert.True(session.GetThread(summary.ThreadId, 0, 50).Succeeded);

        ApplicationResult<ThreadSummaryProjection> renamed = session.RenameThread(
            summary.ThreadId,
            summary.Revision,
            "renamed thread");
        Assert.Equal("renamed thread", renamed.Data!.Title);
        ApplicationResult<ThreadSummaryProjection> archived = session.ArchiveThread(
            summary.ThreadId,
            renamed.Data.Revision);
        Assert.Equal("archived", archived.Data!.Status);
        ApplicationResult<ThreadDeleteProjection> deleted = session.DeleteThread(
            summary.ThreadId,
            archived.Data.Revision,
            summary.ThreadId);
        Assert.True(deleted.Data!.Deleted);
    }

    [Fact]
    public void Session_public_surface_does_not_expose_core_or_transport_types()
    {
        Type[] sessionTypes =
        [
            typeof(DesktopApplicationSession),
            typeof(DesktopApplicationSessionFactory),
            typeof(DesktopApplicationSessionOpenResult)
        ];
        string[] prohibited =
        [
            "CSharpAiCli.Core",
            "System.IO.Stream",
            "System.IO.FileStream",
            "System.Text.Json"
        ];

        IEnumerable<Type> exposed = sessionTypes.SelectMany(type =>
            type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .SelectMany(constructor => constructor.GetParameters().Select(parameter => parameter.ParameterType))
                .Concat(type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Select(property => property.PropertyType))
                .Concat(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .SelectMany(method => method.GetParameters()
                        .Select(parameter => parameter.ParameterType)
                        .Append(method.ReturnType))));
        string[] names = exposed.SelectMany(Flatten).Select(type => type.FullName ?? type.Name).ToArray();

        Assert.DoesNotContain(names, name => prohibited.Any(prefix =>
            name.StartsWith(prefix, StringComparison.Ordinal)));
    }

    [Fact]
    public void Composer_next_turn_settings_are_authoritative_and_thread_scoped()
    {
        using TempDirectory temp = TempDirectory.Create();
        DesktopApplicationSessionOpenResult opened = new DesktopApplicationSessionFactory().Open(temp.Path);
        using DesktopApplicationSession session = Assert.IsType<DesktopApplicationSession>(opened.Session);
        ThreadSummaryProjection thread = Assert.IsType<ThreadSummaryProjection>(session.CreateThread("settings turn").Data);
        ComposerStateProjection initial = Assert.IsType<ComposerStateProjection>(session.GetComposer(thread.ThreadId).Data);

        ApplicationResult<ComposerStateProjection> queued = session.EnqueueComposer(
            thread.ThreadId, initial.ThreadRevision, initial.QueueRevision, "settings-enqueue", "inspect only", [], [],
            modelOverride: "gpt-settings-test", approvalPreference: "read-only", disabledTools: ["workspace.run_shell"]);

        Assert.True(queued.Succeeded);
        Assert.Equal("gpt-settings-test", queued.Data!.EffectiveModel);
        Assert.Equal("Never", queued.Data.ApprovalMode);
        ComposerStateProjection refreshed = Assert.IsType<ComposerStateProjection>(session.GetComposer(thread.ThreadId).Data);
        Assert.Equal("gpt-settings-test", refreshed.EffectiveModel);
        Assert.Equal("Never", refreshed.ApprovalMode);
    }

    [Fact]
    public void Message_branch_persists_and_hydrates_the_exact_source_item()
    {
        using TempDirectory temp = TempDirectory.Create();
        DesktopApplicationSessionOpenResult opened = new DesktopApplicationSessionFactory().Open(temp.Path);
        using DesktopApplicationSession session = Assert.IsType<DesktopApplicationSession>(opened.Session);

        ThreadSummaryProjection source = Assert.IsType<ThreadSummaryProjection>(session.CreateThread("source").Data);
        ComposerStateProjection sourceComposer = Assert.IsType<ComposerStateProjection>(session.GetComposer(source.ThreadId).Data);
        ComposerStateProjection sourceQueued = Assert.IsType<ComposerStateProjection>(session.EnqueueComposer(
            source.ThreadId, sourceComposer.ThreadRevision, sourceComposer.QueueRevision, "source-enqueue", "original prompt", [], []).Data);
        Assert.True(session.StartTurn(source.ThreadId, sourceQueued.ThreadRevision, sourceQueued.QueueRevision, "source-start").Succeeded);
        ThreadDetailProjection sourceDetail = Assert.IsType<ThreadDetailProjection>(session.GetThread(source.ThreadId, 0, 50).Data);
        TimelineItemProjection sourceMessage = Assert.Single(sourceDetail.Timeline, item => item.Type == "user.message");

        ThreadSummaryProjection branch = Assert.IsType<ThreadSummaryProjection>(session.CreateThread("branch").Data);
        ComposerStateProjection branchComposer = Assert.IsType<ComposerStateProjection>(session.GetComposer(branch.ThreadId).Data);
        ComposerStateProjection branchQueued = Assert.IsType<ComposerStateProjection>(session.EnqueueComposer(
            branch.ThreadId, branchComposer.ThreadRevision, branchComposer.QueueRevision, "branch-enqueue", "edited prompt", [], [],
            sourceThreadId: source.ThreadId, sourceItemId: sourceMessage.ItemId, sourceAction: "edit").Data);
        Assert.True(session.StartTurn(branch.ThreadId, branchQueued.ThreadRevision, branchQueued.QueueRevision, "branch-start").Succeeded);

        ThreadDetailProjection branchDetail = Assert.IsType<ThreadDetailProjection>(session.GetThread(branch.ThreadId, 0, 50).Data);
        ThreadSourcePointerProjection pointer = Assert.Single(Assert.Single(branchDetail.Turns).SourcePointers);
        Assert.Equal("thread-message", pointer.Kind);
        Assert.Equal($"{source.ThreadId}/{sourceMessage.ItemId}", pointer.SourceId);
        Assert.Equal("edit", pointer.SourceRevision);
        Assert.Equal("available", pointer.Availability);
    }

    private static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;
        foreach (Type argument in type.GetGenericArguments())
        {
            foreach (Type nested in Flatten(argument))
            {
                yield return nested;
            }
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;

        public string Path { get; }

        public static TempDirectory Create()
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "caicli-desktop-session-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
