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
