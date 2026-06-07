using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class WorkspaceGuardTests
{
    [Fact]
    public void Resolve_path_allows_workspace_file()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspaceRoot = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(workspaceRoot);
        string filePath = Path.Combine(workspaceRoot, "notes.txt");
        File.WriteAllText(filePath, "hello");
        WorkspaceContext workspace = WorkspaceContext.Detect(workspaceRoot, temp.Path);
        WorkspaceGuard guard = new();

        WorkspaceGuardResult result = guard.ResolvePath(workspace, "notes.txt");

        Assert.True(result.IsAllowed);
        Assert.Equal(Path.GetFullPath(filePath), result.FullPath);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public void Resolve_path_denies_parent_traversal_outside_workspace()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspaceRoot = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(workspaceRoot);
        File.WriteAllText(Path.Combine(temp.Path, "outside.txt"), "outside");
        WorkspaceContext workspace = WorkspaceContext.Detect(workspaceRoot, temp.Path);
        WorkspaceGuard guard = new();

        WorkspaceGuardResult result = guard.ResolvePath(workspace, "..\\outside.txt");

        Assert.False(result.IsAllowed);
        Assert.Equal("workspace-boundary-denied", result.ErrorCode);
    }

    [Fact]
    public void Resolve_path_denies_absolute_path_outside_workspace()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspaceRoot = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(workspaceRoot);
        string outside = Path.Combine(temp.Path, "outside.txt");
        File.WriteAllText(outside, "outside");
        WorkspaceContext workspace = WorkspaceContext.Detect(workspaceRoot, temp.Path);
        WorkspaceGuard guard = new();

        WorkspaceGuardResult result = guard.ResolvePath(workspace, outside);

        Assert.False(result.IsAllowed);
        Assert.Equal("workspace-boundary-denied", result.ErrorCode);
    }

    [Fact]
    public void Resolve_path_does_not_allow_prefix_sibling_directory()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspaceRoot = Path.Combine(temp.Path, "work");
        string siblingRoot = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(siblingRoot);
        WorkspaceContext workspace = WorkspaceContext.Detect(workspaceRoot, temp.Path);
        WorkspaceGuard guard = new();

        WorkspaceGuardResult result = guard.ResolvePath(workspace, siblingRoot);

        Assert.False(result.IsAllowed);
        Assert.Equal("workspace-boundary-denied", result.ErrorCode);
    }

    [Fact]
    public void Resolve_path_denies_missing_workspace()
    {
        using TempDirectory temp = TempDirectory.Create();
        string missingWorkspace = Path.Combine(temp.Path, "missing");
        WorkspaceContext workspace = WorkspaceContext.Detect(missingWorkspace, temp.Path);
        WorkspaceGuard guard = new();

        WorkspaceGuardResult result = guard.ResolvePath(workspace, "notes.txt");

        Assert.False(result.IsAllowed);
        Assert.Equal("workspace-unavailable", result.ErrorCode);
    }

    [Fact]
    public void Resolve_path_denies_directory_symlink_or_junction_outside_workspace_when_available()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspaceRoot = Path.Combine(temp.Path, "workspace");
        string outsideRoot = Path.Combine(temp.Path, "outside");
        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(outsideRoot);
        File.WriteAllText(Path.Combine(outsideRoot, "secret.txt"), "secret");
        string linkPath = Path.Combine(workspaceRoot, "linked");
        DirectoryInfo link = new(linkPath);

        try
        {
            link.CreateAsSymbolicLink(outsideRoot);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or PlatformNotSupportedException)
        {
            return;
        }

        WorkspaceContext workspace = WorkspaceContext.Detect(workspaceRoot, temp.Path);
        WorkspaceGuard guard = new();

        WorkspaceGuardResult result = guard.ResolvePath(workspace, Path.Combine("linked", "secret.txt"));

        Assert.False(result.IsAllowed);
        Assert.Equal("workspace-boundary-denied", result.ErrorCode);
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempDirectory Create()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "caicli-tests-" + Guid.NewGuid().ToString("N"));
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
