using System.Diagnostics;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class GitToolsTests
{
    [Fact]
    public void Git_status_reports_clean_temporary_repo()
    {
        using TempDirectory temp = TempDirectory.Create();
        InitializeGitRepository(temp.Path);
        GitStatusTool tool = new(new WorkspaceGuard());

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path));

        Assert.True(result.Succeeded);
        Assert.Equal("working tree clean", result.Summary);
    }

    [Fact]
    public void Git_status_reports_modified_file_in_temporary_repo()
    {
        using TempDirectory temp = TempDirectory.Create();
        string filePath = InitializeGitRepository(temp.Path);
        File.AppendAllText(filePath, "changed\n");
        GitStatusTool tool = new(new WorkspaceGuard());

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path));

        Assert.True(result.Succeeded);
        Assert.Contains("M tracked.txt", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Git_diff_reports_modified_file_in_temporary_repo()
    {
        using TempDirectory temp = TempDirectory.Create();
        string filePath = InitializeGitRepository(temp.Path);
        File.AppendAllText(filePath, "changed\n");
        GitDiffTool tool = new(new WorkspaceGuard());

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path));

        Assert.True(result.Succeeded);
        Assert.Contains("diff --git", result.Summary, StringComparison.Ordinal);
        Assert.Contains("+changed", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Git_diff_stat_reports_modified_file_in_temporary_repo()
    {
        using TempDirectory temp = TempDirectory.Create();
        string filePath = InitializeGitRepository(temp.Path);
        File.AppendAllText(filePath, "changed\n");
        GitDiffTool tool = new(new WorkspaceGuard());

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path, """{"stat":true}"""));

        Assert.True(result.Succeeded);
        Assert.Contains("tracked.txt", result.Summary, StringComparison.Ordinal);
        Assert.Contains("1 file changed", result.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("diff --git", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Git_tools_return_clear_failure_for_non_git_workspace()
    {
        using TempDirectory temp = TempDirectory.Create();
        GitStatusTool statusTool = new(new WorkspaceGuard());
        GitDiffTool diffTool = new(new WorkspaceGuard());

        ToolExecutionResult status = statusTool.Execute(CreateContext(temp.Path));
        ToolExecutionResult diff = diffTool.Execute(CreateContext(temp.Path));

        Assert.False(status.Succeeded);
        Assert.Equal("git-not-repository", status.ErrorCode);
        Assert.False(diff.Succeeded);
        Assert.Equal("git-not-repository", diff.ErrorCode);
    }

    private static ToolExecutionContext CreateContext(string workspaceRoot, string argumentsJson = "{}")
    {
        return new ToolExecutionContext(
            "call_git",
            WorkspaceContext.Detect(workspaceRoot, workspaceRoot),
            argumentsJson);
    }

    private static string InitializeGitRepository(string root)
    {
        RunGit(root, "init");
        RunGit(root, "config", "user.email", "test@example.invalid");
        RunGit(root, "config", "user.name", "Test User");
        string filePath = Path.Combine(root, "tracked.txt");
        File.WriteAllText(filePath, "original\n");
        RunGit(root, "add", "tracked.txt");
        RunGit(root, "-c", "commit.gpgSign=false", "commit", "--no-gpg-sign", "--no-verify", "-m", "initial");
        return filePath;
    }

    private static void RunGit(string workingDirectory, params string[] arguments)
    {
        ProcessStartInfo startInfo = new("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new()
        {
            StartInfo = startInfo
        };

        process.Start();
        string commandText = string.Join(" ", arguments);
        Assert.True(process.WaitForExit(10_000), "git command timed out: " + commandText);
        string stderr = process.StandardError.ReadToEnd();
        Assert.True(process.ExitCode == 0, $"git {commandText} failed: {stderr}");
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
                ClearReadOnlyAttributes(Path);
                Directory.Delete(Path, recursive: true);
            }
        }

        private static void ClearReadOnlyAttributes(string path)
        {
            foreach (string filePath in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(filePath, FileAttributes.Normal);
            }

            foreach (string directoryPath in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(directoryPath, FileAttributes.Normal);
            }
        }
    }
}
