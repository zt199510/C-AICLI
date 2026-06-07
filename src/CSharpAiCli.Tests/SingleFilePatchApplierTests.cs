using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class SingleFilePatchApplierTests
{
    [Fact]
    public void Preview_creates_summary_diff_and_dirty_status()
    {
        using TempDirectory temp = TempDirectory.Create();
        string filePath = Path.Combine(temp.Path, "notes.txt");
        File.WriteAllText(filePath, "hello world");
        StaticDirtyWorkspaceDetector dirtyDetector = new(new DirtyWorkspaceStatus(true, "2 changed path(s)"));
        SingleFilePatchApplier applier = new(new WorkspaceGuard(), dirtyDetector);

        PatchPreview preview = applier.Preview(
            WorkspaceContext.Detect(temp.Path, temp.Path),
            new PatchOperation("notes.txt", "hello", "hi"));

        Assert.Equal(filePath, preview.FullPath);
        Assert.Equal(1, preview.Replacements);
        Assert.Contains("notes.txt", preview.Summary, StringComparison.Ordinal);
        Assert.Contains("2 changed path(s)", preview.Summary, StringComparison.Ordinal);
        Assert.Contains("--- a/notes.txt", preview.Diff, StringComparison.Ordinal);
        Assert.Contains("-hello", preview.Diff, StringComparison.Ordinal);
        Assert.Contains("+hi", preview.Diff, StringComparison.Ordinal);
        Assert.True(preview.DirtyWorkspace.IsDirty);
        Assert.False(string.IsNullOrWhiteSpace(preview.OriginalContentHash));
    }

    [Fact]
    public void Apply_writes_single_file_when_context_still_matches()
    {
        using TempDirectory temp = TempDirectory.Create();
        string filePath = Path.Combine(temp.Path, "notes.txt");
        File.WriteAllText(filePath, "hello world");
        SingleFilePatchApplier applier = new(
            new WorkspaceGuard(),
            new StaticDirtyWorkspaceDetector(new DirtyWorkspaceStatus(false, "clean")));
        WorkspaceContext workspace = WorkspaceContext.Detect(temp.Path, temp.Path);
        PatchPreview preview = applier.Preview(workspace, new PatchOperation("notes.txt", "hello", "hi"));

        PatchApplyResult result = applier.Apply(workspace, preview);

        Assert.True(result.Succeeded);
        Assert.Equal("approved", result.ApprovalStatus);
        Assert.Equal("hi world", File.ReadAllText(filePath));
    }

    [Fact]
    public void Apply_rejects_when_target_changed_after_preview()
    {
        using TempDirectory temp = TempDirectory.Create();
        string filePath = Path.Combine(temp.Path, "notes.txt");
        File.WriteAllText(filePath, "hello world");
        SingleFilePatchApplier applier = new(
            new WorkspaceGuard(),
            new StaticDirtyWorkspaceDetector(new DirtyWorkspaceStatus(false, "clean")));
        WorkspaceContext workspace = WorkspaceContext.Detect(temp.Path, temp.Path);
        PatchPreview preview = applier.Preview(workspace, new PatchOperation("notes.txt", "hello", "hi"));
        File.WriteAllText(filePath, "hello changed");

        PatchApplyResult result = applier.Apply(workspace, preview);

        Assert.False(result.Succeeded);
        Assert.Equal("patch-target-changed", result.ErrorCode);
        Assert.Equal("not-required", result.ApprovalStatus);
        Assert.Equal("hello changed", File.ReadAllText(filePath));
    }

    [Fact]
    public void Preview_denies_workspace_outside_target()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspaceRoot = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(workspaceRoot);
        File.WriteAllText(Path.Combine(temp.Path, "outside.txt"), "outside");
        SingleFilePatchApplier applier = new(
            new WorkspaceGuard(),
            new StaticDirtyWorkspaceDetector(new DirtyWorkspaceStatus(false, "clean")));

        ToolSecurityException exception = Assert.Throws<ToolSecurityException>(() => applier.Preview(
            WorkspaceContext.Detect(workspaceRoot, temp.Path),
            new PatchOperation("../outside.txt", "outside", "inside")));

        Assert.Equal("workspace-boundary-denied", exception.ErrorCode);
    }

    [Fact]
    public void Preview_rejects_missing_context()
    {
        using TempDirectory temp = TempDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "notes.txt"), "hello world");
        SingleFilePatchApplier applier = new(
            new WorkspaceGuard(),
            new StaticDirtyWorkspaceDetector(new DirtyWorkspaceStatus(false, "clean")));

        ToolExecutionException exception = Assert.Throws<ToolExecutionException>(() => applier.Preview(
            WorkspaceContext.Detect(temp.Path, temp.Path),
            new PatchOperation("notes.txt", "missing", "hi")));

        Assert.Equal("patch-context-not-found", exception.ErrorCode);
    }

    private sealed class StaticDirtyWorkspaceDetector(DirtyWorkspaceStatus status) : IDirtyWorkspaceDetector
    {
        public DirtyWorkspaceStatus Detect(WorkspaceContext workspace) => status;
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
