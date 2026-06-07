using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class GitDirtyWorkspaceDetectorTests
{
    [Fact]
    public void Detect_returns_not_dirty_for_non_git_workspace()
    {
        using TempDirectory temp = TempDirectory.Create();
        GitDirtyWorkspaceDetector detector = new();

        DirtyWorkspaceStatus status = detector.Detect(WorkspaceContext.Detect(temp.Path, temp.Path));

        Assert.False(status.IsDirty);
        Assert.Equal("not a git workspace", status.Summary);
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
