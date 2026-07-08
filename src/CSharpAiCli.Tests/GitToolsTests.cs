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
    public void Git_diff_reports_staged_tracked_file_in_temporary_repo()
    {
        using TempDirectory temp = TempDirectory.Create();
        string filePath = InitializeGitRepository(temp.Path);
        File.AppendAllText(filePath, "staged change\n");
        RunGit(temp.Path, "add", "tracked.txt");
        GitDiffTool tool = new(new WorkspaceGuard());

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path));

        Assert.True(result.Succeeded);
        Assert.Contains("diff --git", result.Summary, StringComparison.Ordinal);
        Assert.Contains("+staged change", result.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("no diff", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Git_diff_reports_canceling_staged_and_unstaged_tracked_changes()
    {
        using TempDirectory temp = TempDirectory.Create();
        string filePath = InitializeGitRepository(temp.Path);
        File.WriteAllText(filePath, "staged\n");
        RunGit(temp.Path, "add", "tracked.txt");
        File.WriteAllText(filePath, "original\n");
        GitDiffTool tool = new(new WorkspaceGuard());

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path));

        Assert.True(result.Succeeded);
        Assert.Contains("+staged", result.Summary, StringComparison.Ordinal);
        Assert.Contains("-staged", result.Summary, StringComparison.Ordinal);
        Assert.Contains("+original", result.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("no diff", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Git_diff_reports_untracked_file_in_temporary_repo()
    {
        using TempDirectory temp = TempDirectory.Create();
        InitializeGitRepository(temp.Path);
        File.WriteAllText(Path.Combine(temp.Path, "new file.txt"), "fresh\n");
        GitDiffTool tool = new(new WorkspaceGuard());

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path));

        Assert.True(result.Succeeded);
        Assert.Contains("diff --git", result.Summary, StringComparison.Ordinal);
        Assert.Contains("new file.txt", result.Summary, StringComparison.Ordinal);
        Assert.Contains("+fresh", result.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("no diff", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Git_diff_ignores_untracked_cli_command_logs()
    {
        using TempDirectory temp = TempDirectory.Create();
        InitializeGitRepository(temp.Path);
        WriteCliCommandLog(temp.Path);
        GitDiffTool tool = new(new WorkspaceGuard());

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path));

        Assert.True(result.Succeeded);
        Assert.Equal("no diff", result.Summary);
    }

    [Fact]
    public void Git_diff_ignores_cli_logs_without_hiding_other_caicli_files()
    {
        using TempDirectory temp = TempDirectory.Create();
        InitializeGitRepository(temp.Path);
        WriteCliCommandLog(temp.Path);
        string configPath = Path.Combine(temp.Path, ".caicli", "config.json");
        File.WriteAllText(configPath, "{}\n");
        GitDiffTool tool = new(new WorkspaceGuard());

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path));

        Assert.True(result.Succeeded);
        Assert.Contains(".caicli/config.json", result.Summary, StringComparison.Ordinal);
        Assert.Contains("+{}", result.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain(".caicli/logs", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no diff", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Git_diff_reports_staged_and_untracked_files_in_no_head_repo()
    {
        using TempDirectory temp = TempDirectory.Create();
        InitializeNoHeadGitRepository(temp.Path);
        File.WriteAllText(Path.Combine(temp.Path, "staged.txt"), "staged\n");
        RunGit(temp.Path, "add", "staged.txt");
        File.WriteAllText(Path.Combine(temp.Path, "untracked.txt"), "loose\n");
        GitDiffTool tool = new(new WorkspaceGuard());

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path));

        Assert.True(result.Succeeded);
        Assert.Contains("staged.txt", result.Summary, StringComparison.Ordinal);
        Assert.Contains("+staged", result.Summary, StringComparison.Ordinal);
        Assert.Contains("untracked.txt", result.Summary, StringComparison.Ordinal);
        Assert.Contains("+loose", result.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("bad revision", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no diff", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Git_diff_reports_no_diff_for_clean_temporary_repo()
    {
        using TempDirectory temp = TempDirectory.Create();
        InitializeGitRepository(temp.Path);
        GitDiffTool tool = new(new WorkspaceGuard());

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path));

        Assert.True(result.Succeeded);
        Assert.Equal("no diff", result.Summary);
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
    public void Git_diff_stat_reports_canceling_staged_and_unstaged_tracked_changes()
    {
        using TempDirectory temp = TempDirectory.Create();
        string filePath = InitializeGitRepository(temp.Path);
        File.WriteAllText(filePath, "staged\n");
        RunGit(temp.Path, "add", "tracked.txt");
        File.WriteAllText(filePath, "original\n");
        GitDiffTool tool = new(new WorkspaceGuard());

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path, """{"stat":true}"""));

        Assert.True(result.Succeeded);
        Assert.Contains("tracked.txt", result.Summary, StringComparison.Ordinal);
        Assert.Contains("1 file changed", result.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("diff --git", result.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("no diff", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Git_diff_stat_reports_staged_and_untracked_files_in_temporary_repo()
    {
        using TempDirectory temp = TempDirectory.Create();
        string filePath = InitializeGitRepository(temp.Path);
        File.AppendAllText(filePath, "staged change\n");
        RunGit(temp.Path, "add", "tracked.txt");
        File.WriteAllText(Path.Combine(temp.Path, "new file.txt"), "fresh\n");
        GitDiffTool tool = new(new WorkspaceGuard());

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path, """{"stat":true}"""));

        Assert.True(result.Succeeded);
        Assert.Contains("tracked.txt", result.Summary, StringComparison.Ordinal);
        Assert.Contains("new file.txt", result.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("diff --git", result.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("no diff", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Git_diff_stat_reports_staged_and_untracked_files_in_no_head_repo()
    {
        using TempDirectory temp = TempDirectory.Create();
        InitializeNoHeadGitRepository(temp.Path);
        File.WriteAllText(Path.Combine(temp.Path, "staged.txt"), "staged\n");
        RunGit(temp.Path, "add", "staged.txt");
        File.WriteAllText(Path.Combine(temp.Path, "untracked.txt"), "loose\n");
        GitDiffTool tool = new(new WorkspaceGuard());

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path, """{"stat":true}"""));

        Assert.True(result.Succeeded);
        Assert.Contains("staged.txt", result.Summary, StringComparison.Ordinal);
        Assert.Contains("untracked.txt", result.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("diff --git", result.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("bad revision", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no diff", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Git_diff_warns_when_output_is_truncated()
    {
        using TempDirectory temp = TempDirectory.Create();
        string filePath = InitializeGitRepository(temp.Path);
        File.AppendAllText(filePath, new string('x', 70 * 1024) + "\n");
        GitDiffTool tool = new(new WorkspaceGuard());

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path));

        Assert.True(result.Succeeded);
        Assert.Contains("WARNING: git output was truncated; diff is incomplete.", result.Summary, StringComparison.Ordinal);
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

    [Fact]
    public void Git_temp_repo_commit_ignores_configured_prepare_commit_msg_hook()
    {
        using TempDirectory temp = TempDirectory.Create();
        string hooksPath = Path.Combine(temp.Path, "failing-hooks");
        WriteFailingHook(hooksPath, "prepare-commit-msg");

        string filePath = InitializeGitRepository(temp.Path, hooksPath);

        Assert.True(File.Exists(filePath));
    }

    private static ToolExecutionContext CreateContext(string workspaceRoot, string argumentsJson = "{}")
    {
        return new ToolExecutionContext(
            "call_git",
            WorkspaceContext.Detect(workspaceRoot, workspaceRoot),
            argumentsJson);
    }

    private static void InitializeNoHeadGitRepository(string root)
    {
        RunGit(root, "init");
        RunGit(root, "config", "user.email", "test@example.invalid");
        RunGit(root, "config", "user.name", "Test User");
    }

    private static string InitializeGitRepository(string root, string? configuredHooksPath = null)
    {
        RunGit(root, "init");
        RunGit(root, "config", "user.email", "test@example.invalid");
        RunGit(root, "config", "user.name", "Test User");
        if (!string.IsNullOrWhiteSpace(configuredHooksPath))
        {
            RunGit(root, "config", "core.hooksPath", configuredHooksPath);
        }

        string filePath = Path.Combine(root, "tracked.txt");
        File.WriteAllText(filePath, "original\n");
        RunGit(root, "add", "tracked.txt");
        string emptyHooksPath = Path.Combine(root, ".caicli-empty-hooks");
        Directory.CreateDirectory(emptyHooksPath);
        RunGit(
            root,
            "-c",
            "commit.gpgSign=false",
            "-c",
            "core.hooksPath=" + emptyHooksPath,
            "commit",
            "--no-gpg-sign",
            "--no-verify",
            "-m",
            "initial");
        return filePath;
    }

    private static void WriteFailingHook(string hooksPath, string hookName)
    {
        Directory.CreateDirectory(hooksPath);
        File.WriteAllText(
            Path.Combine(hooksPath, hookName),
            "#!/bin/sh\necho configured hook failed >&2\nexit 1\n");
    }

    private static void WriteCliCommandLog(string root)
    {
        string logsPath = Path.Combine(root, ".caicli", "logs");
        Directory.CreateDirectory(logsPath);
        File.WriteAllText(Path.Combine(logsPath, "2026-07-09.log"), "command=diff\n");
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
