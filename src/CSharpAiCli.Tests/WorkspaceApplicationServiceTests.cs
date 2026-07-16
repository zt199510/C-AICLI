using CSharpAiCli.Application;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class WorkspaceApplicationServiceTests
{
    [Fact]
    public void Open_reuses_core_workspace_identity_and_guard()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspacePath = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(workspacePath);
        WorkspaceApplicationService service = new();

        WorkspaceOpenApplicationResult first = service.Open(new WorkspaceOpenRequest(workspacePath, temp.Path));
        WorkspaceOpenApplicationResult second = service.Open(new WorkspaceOpenRequest("workspace", temp.Path));

        Assert.True(first.Success);
        Assert.Equal("ready", first.Status);
        Assert.Equal(Path.GetFullPath(workspacePath), first.RootPath);
        Assert.StartsWith("ws_", first.WorkspaceId, StringComparison.Ordinal);
        Assert.Equal(first.WorkspaceId, second.WorkspaceId);
        Assert.Equal(WorkspaceStatus.Ready, WorkspaceContext.Detect(workspacePath, temp.Path).Status);
        Assert.True(new WorkspaceGuard().ResolvePath(WorkspaceContext.Detect(workspacePath), ".").IsAllowed);
    }

    [Fact]
    public void Open_returns_bounded_failure_for_missing_workspace()
    {
        using TempDirectory temp = TempDirectory.Create();
        WorkspaceApplicationService service = new();

        WorkspaceOpenApplicationResult result = service.Open(
            new WorkspaceOpenRequest(Path.Combine(temp.Path, "missing"), temp.Path));

        Assert.False(result.Success);
        Assert.Null(result.WorkspaceId);
        Assert.Null(result.RootPath);
        Assert.Equal("missing", result.Status);
        Assert.Equal(ToolErrorCode.WorkspaceUnavailable, result.ErrorCode);
        Assert.Equal("Workspace directory does not exist.", result.SafeMessage);
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;

        public string Path { get; }

        public static TempDirectory Create()
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "caicli-application-tests-" + Guid.NewGuid().ToString("N"));
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
