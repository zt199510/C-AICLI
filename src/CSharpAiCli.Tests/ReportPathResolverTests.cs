using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ReportPathResolverTests
{
    [Fact]
    public void WriteMarkdown_writes_new_file_inside_workspace()
    {
        using TempDirectory temp = TempDirectory.Create();
        WorkspaceContext workspace = CreateWorkspace(temp.Path);
        ReportPathResolver resolver = new();

        ReportWriteResult result = resolver.WriteMarkdown(
            workspace,
            Path.Combine(".caicli", "reports", "run.md"),
            "# report");

        Assert.True(result.Succeeded);
        Assert.Equal("Markdown report written.", result.Summary);
        Assert.True(File.Exists(result.Path));
        Assert.Equal("# report", File.ReadAllText(result.Path!));
    }

    [Fact]
    public void ResolveForWrite_rejects_outside_existing_and_directory_paths()
    {
        using TempDirectory temp = TempDirectory.Create();
        WorkspaceContext workspace = CreateWorkspace(temp.Path);
        string existing = Path.Combine(temp.Path, "existing.md");
        File.WriteAllText(existing, "old");
        Directory.CreateDirectory(Path.Combine(temp.Path, "reports"));
        ReportPathResolver resolver = new();

        ReportWriteResult outside = resolver.ResolveForWrite(workspace, Path.Combine(temp.Path, "..", "outside.md"));
        ReportWriteResult existingResult = resolver.ResolveForWrite(workspace, "existing.md");
        ReportWriteResult directory = resolver.ResolveForWrite(workspace, "reports");

        Assert.False(outside.Succeeded);
        Assert.Equal(ToolErrorCode.WorkspaceBoundaryDenied, outside.ErrorCode);
        Assert.False(existingResult.Succeeded);
        Assert.Equal("report-path-exists", existingResult.ErrorCode);
        Assert.False(directory.Succeeded);
        Assert.Equal("report-path-is-directory", directory.ErrorCode);
    }

    [Fact]
    public void WriteMarkdown_rejects_existing_file_without_overwriting()
    {
        using TempDirectory temp = TempDirectory.Create();
        WorkspaceContext workspace = CreateWorkspace(temp.Path);
        string path = Path.Combine(temp.Path, "existing.md");
        File.WriteAllText(path, "old");
        ReportPathResolver resolver = new();

        ReportWriteResult result = resolver.WriteMarkdown(workspace, "existing.md", "new");

        Assert.False(result.Succeeded);
        Assert.Equal("report-path-exists", result.ErrorCode);
        Assert.Equal("old", File.ReadAllText(path));
    }

    private static WorkspaceContext CreateWorkspace(string root)
    {
        return new WorkspaceContext(
            RootPath: root,
            ConfigPath: Path.Combine(root, ".caicli", "config.json"),
            Status: WorkspaceStatus.Ready);
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
