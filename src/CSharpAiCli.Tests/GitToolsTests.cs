using System.Diagnostics;
using System.Text;
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
    public void Git_diff_does_not_run_configured_external_diff_helper()
    {
        using TempDirectory temp = TempDirectory.Create();
        string filePath = InitializeGitRepository(temp.Path);
        string markerPath = Path.Combine(temp.Path, "external-diff-ran.marker");
        string helperPath = WriteExternalDiffHelper(temp.Path, markerPath);
        RunGit(temp.Path, "config", "diff.external", "sh '" + NormalizeGitPath(helperPath) + "'");
        File.AppendAllText(filePath, "changed\n");
        GitDiffTool tool = new(new WorkspaceGuard());

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path));

        Assert.True(result.Succeeded);
        Assert.Contains("+changed", result.Summary, StringComparison.Ordinal);
        Assert.False(File.Exists(markerPath), "Configured external diff helper should not be invoked.");
    }

    [Fact]
    public void Git_diff_does_not_rewrite_index_when_clean_tracked_file_timestamp_changes()
    {
        using TempDirectory temp = TempDirectory.Create();
        string filePath = InitializeGitRepository(temp.Path);
        string indexPath = Path.Combine(temp.Path, ".git", "index");
        DateTime indexBefore = File.GetLastWriteTimeUtc(indexPath);
        Thread.Sleep(1200);
        File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow.AddMinutes(5));
        GitDiffTool tool = new(new WorkspaceGuard());

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path));

        DateTime indexAfter = File.GetLastWriteTimeUtc(indexPath);
        Assert.True(result.Succeeded);
        Assert.Equal("no diff", result.Summary);
        Assert.Equal(indexBefore, indexAfter);
    }

    [Fact]
    public void Git_diff_does_not_run_clean_filter_for_unstaged_tracked_file()
    {
        using TempDirectory temp = TempDirectory.Create();
        (string filePath, string markerPath) = InitializeGitRepositoryWithCleanFilter(temp.Path, "tracked.txt");
        File.AppendAllText(filePath, "changed\n");
        DeleteCleanFilterMarker(markerPath);
        GitDiffTool tool = new(new WorkspaceGuard());

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path));

        Assert.True(result.Succeeded);
        Assert.Contains("+changed", result.Summary, StringComparison.Ordinal);
        Assert.False(File.Exists(markerPath), "Clean filter should not run during read-only diff collection.");
    }

    [Fact]
    public void Git_diff_does_not_run_clean_filter_for_untracked_file()
    {
        using TempDirectory temp = TempDirectory.Create();
        (_, string markerPath) = InitializeGitRepositoryWithCleanFilter(temp.Path, "new.txt");
        File.WriteAllText(Path.Combine(temp.Path, "new.txt"), "fresh\n");
        DeleteCleanFilterMarker(markerPath);
        Assert.False(File.Exists(markerPath), "Clean filter marker should be absent before diff collection.");
        GitDiffTool tool = new(new WorkspaceGuard());

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path));

        Assert.True(result.Succeeded);
        Assert.Contains("new.txt", result.Summary, StringComparison.Ordinal);
        Assert.Contains("+fresh", result.Summary, StringComparison.Ordinal);
        Assert.False(File.Exists(markerPath), "Clean filter should not run while rendering untracked file diffs.");
    }

    [Fact]
    public void Git_diff_preserves_hunk_lines_that_look_like_temp_diff_paths()
    {
        using TempDirectory temp = TempDirectory.Create();
        string filePath = InitializeGitRepository(temp.Path);
        File.WriteAllText(filePath, "a/old/tracked.txt\nb/new/tracked.txt\n");
        GitDiffTool tool = new(new WorkspaceGuard());

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path));

        Assert.True(result.Succeeded);
        Assert.Contains("+a/old/tracked.txt", result.Summary, StringComparison.Ordinal);
        Assert.Contains("+b/new/tracked.txt", result.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("+a/tracked.txt", result.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("+b/tracked.txt", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Git_diff_disables_external_helpers_for_all_diff_commands()
    {
        using TempDirectory temp = TempDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "untracked.txt"), "fresh\n");
        FakeGitCommandRunner runner = new(args =>
        {
            if (args is ["rev-parse", "--verify", "HEAD"])
            {
                return SuccessfulGitResult(string.Empty);
            }

            if (args is ["config", "--bool", "core.ignorecase"])
            {
                return FailedGitResult(exitCode: 1, stderr: string.Empty);
            }

            if (args is ["ls-files", "--others", "--exclude-standard", "-z"])
            {
                return SuccessfulGitResult("untracked.txt\0");
            }

            if (args.Contains("--no-index", StringComparer.Ordinal))
            {
                return new GitCommandResult(
                    Succeeded: true,
                    ExitCode: 1,
                    Stdout: "diff --git a/untracked.txt b/untracked.txt\n+fresh\n",
                    Stderr: string.Empty,
                    StdoutTruncated: false,
                    StderrTruncated: false,
                    ErrorCode: null,
                    Summary: "Git command completed with exit code 1.");
            }

            return SuccessfulGitResult(string.Empty);
        });
        GitDiffTool tool = new(new WorkspaceGuard(), runner);

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path, """{"stat":true}"""));

        Assert.True(result.Succeeded);
        IReadOnlyList<string[]> diffCommands = runner.Commands
            .Where(command => command.FirstOrDefault() is "diff" or "diff-index")
            .ToList();
        Assert.Equal(2, diffCommands.Count);
        Assert.All(diffCommands, command =>
        {
            Assert.Contains("--no-ext-diff", command);
            Assert.Contains("--no-textconv", command);
        });
    }

    [Fact]
    public void Git_diff_uses_unstaged_candidate_list_without_rendering_clean_tracked_paths()
    {
        using TempDirectory temp = TempDirectory.Create();
        FakeGitCommandRunner runner = new(args =>
        {
            if (args is ["rev-parse", "--verify", "HEAD"])
            {
                return SuccessfulGitResult(string.Empty);
            }

            if (args.FirstOrDefault() == "diff-index")
            {
                return SuccessfulGitResult(string.Empty);
            }

            if (args is ["diff-files", "--raw", "--no-ext-diff", "--no-textconv", "-z", "--", ":(exclude).caicli/logs/**"])
            {
                return SuccessfulGitResult(string.Empty);
            }

            if (args is ["ls-files", "--debug", "-z", "--", ":(exclude).caicli/logs/**"])
            {
                return SuccessfulGitResult("deps/sub\0");
            }

            if (args is ["show", ":0:deps/sub"])
            {
                return FailedGitResult(exitCode: 128, stderr: "fatal: path 'deps/sub' is a gitlink\n");
            }

            if (args is ["config", "--bool", "core.ignorecase"])
            {
                return FailedGitResult(exitCode: 1, stderr: string.Empty);
            }

            if (args is ["ls-files", "--others", "--exclude-standard", "-z"])
            {
                return SuccessfulGitResult(string.Empty);
            }

            return SuccessfulGitResult(string.Empty);
        });
        GitDiffTool tool = new(new WorkspaceGuard(), runner);

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path));

        Assert.True(result.Succeeded);
        Assert.Equal("no diff", result.Summary);
        Assert.Contains(runner.Commands, command => command is ["diff-files", "--raw", "--no-ext-diff", "--no-textconv", "-z", "--", ":(exclude).caicli/logs/**"]);
        Assert.DoesNotContain(runner.Commands, command => command.FirstOrDefault() == "show");
        Assert.DoesNotContain(runner.Commands, command => command.Contains("--no-index", StringComparer.Ordinal));
    }

    [Fact]
    public void Git_diff_reports_unstaged_gitlink_candidate_without_failing()
    {
        using TempDirectory temp = TempDirectory.Create();
        Directory.CreateDirectory(Path.Combine(temp.Path, "deps", "sub"));
        FakeGitCommandRunner runner = new((args, _) =>
        {
            if (args is ["rev-parse", "--verify", "HEAD"])
            {
                return SuccessfulGitResult(string.Empty);
            }

            if (args.FirstOrDefault() == "diff-index")
            {
                return SuccessfulGitResult(string.Empty);
            }

            if (args is ["diff-files", "--raw", "--no-ext-diff", "--no-textconv", "-z", "--", ":(exclude).caicli/logs/**"])
            {
                return SuccessfulGitResult(":160000 160000 abcdef1234567890abcdef1234567890abcdef12 0000000000000000000000000000000000000000 M\0deps/sub\0");
            }

            if (args is ["ls-files", "--debug", "-z", "--", ":(exclude).caicli/logs/**"])
            {
                return SuccessfulGitResult("deps/sub\0  mtime: 0:0\n  size: 0\tflags: 0\n");
            }

            if (args is ["ls-files", "-s", "-z", "--", "deps/sub"])
            {
                return SuccessfulGitResult("160000 abcdef1234567890abcdef1234567890abcdef12 0\tdeps/sub\0");
            }

            if (args is ["diff-files", "--summary", "--no-ext-diff", "--no-textconv", "--", "deps/sub"])
            {
                return SuccessfulGitResult("Submodule deps/sub contains modified content\n");
            }

            if (args is ["show", ":0:deps/sub"])
            {
                return FailedGitResult(exitCode: 128, stderr: "fatal: path 'deps/sub' is a gitlink\n");
            }

            if (args is ["config", "--bool", "core.ignorecase"])
            {
                return FailedGitResult(exitCode: 1, stderr: string.Empty);
            }

            if (args is ["ls-files", "--others", "--exclude-standard", "-z"])
            {
                return SuccessfulGitResult(string.Empty);
            }

            return SuccessfulGitResult(string.Empty);
        });
        GitDiffTool tool = new(new WorkspaceGuard(), runner);

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path));

        Assert.True(result.Succeeded);
        Assert.Contains("deps/sub", result.Summary, StringComparison.Ordinal);
        Assert.Contains("Submodule", result.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain(runner.Commands, command => command is ["show", ":0:deps/sub"]);
    }

    [Fact]
    public void Git_diff_reports_unstaged_mode_only_candidate()
    {
        using TempDirectory temp = TempDirectory.Create();
        string scriptPath = Path.Combine(temp.Path, "script.sh");
        File.WriteAllText(scriptPath, "echo hi\n");
        long mtimeSeconds = new DateTimeOffset(File.GetLastWriteTimeUtc(scriptPath)).ToUnixTimeSeconds();
        FakeGitCommandRunner runner = new((args, stdoutPath) =>
        {
            if (args is ["rev-parse", "--verify", "HEAD"])
            {
                return SuccessfulGitResult(string.Empty);
            }

            if (args.FirstOrDefault() == "diff-index")
            {
                return SuccessfulGitResult(string.Empty);
            }

            if (args is ["diff-files", "--raw", "--no-ext-diff", "--no-textconv", "-z", "--", ":(exclude).caicli/logs/**"])
            {
                return SuccessfulGitResult(":100644 100755 abcdef1234567890abcdef1234567890abcdef12 abcdef1234567890abcdef1234567890abcdef12 M\0script.sh\0");
            }

            if (args is ["ls-files", "--debug", "-z", "--", ":(exclude).caicli/logs/**"])
            {
                return SuccessfulGitResult($"script.sh\0  mtime: {mtimeSeconds}:0\n  size: 8\tflags: 0\n");
            }

            if (args is ["ls-files", "-s", "-z", "--", "script.sh"])
            {
                return SuccessfulGitResult("100644 abcdef1234567890abcdef1234567890abcdef12 0\tscript.sh\0");
            }

            if (args is ["show", ":0:script.sh"])
            {
                Directory.CreateDirectory(Path.GetDirectoryName(stdoutPath!)!);
                File.WriteAllText(stdoutPath!, "echo hi\n");
                return SuccessfulGitResult(string.Empty);
            }

            if (args.Contains("--no-index", StringComparer.Ordinal))
            {
                return SuccessfulGitResult(string.Empty);
            }

            if (args is ["diff-files", "--summary", "--no-ext-diff", "--no-textconv", "--", "script.sh"])
            {
                return SuccessfulGitResult(" mode change 100644 => 100755 script.sh\n");
            }

            if (args is ["config", "--bool", "core.ignorecase"])
            {
                return FailedGitResult(exitCode: 1, stderr: string.Empty);
            }

            if (args is ["ls-files", "--others", "--exclude-standard", "-z"])
            {
                return SuccessfulGitResult(string.Empty);
            }

            return SuccessfulGitResult(string.Empty);
        });
        GitDiffTool tool = new(new WorkspaceGuard(), runner);

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path));

        Assert.True(result.Succeeded);
        Assert.Contains("mode change", result.Summary, StringComparison.Ordinal);
        Assert.Contains("script.sh", result.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("no diff", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Git_diff_reports_same_size_same_second_unstaged_tracked_candidate_from_raw_diff()
    {
        using TempDirectory temp = TempDirectory.Create();
        string samePath = Path.Combine(temp.Path, "same.txt");
        File.WriteAllText(samePath, "vwxyz");
        long mtimeSeconds = new DateTimeOffset(File.GetLastWriteTimeUtc(samePath)).ToUnixTimeSeconds();
        FakeGitCommandRunner runner = new((args, stdoutPath) =>
        {
            if (args is ["rev-parse", "--verify", "HEAD"])
            {
                return SuccessfulGitResult(string.Empty);
            }

            if (args.FirstOrDefault() == "diff-index")
            {
                return SuccessfulGitResult(string.Empty);
            }

            if (args is ["diff-files", "--raw", "--no-ext-diff", "--no-textconv", "-z", "--", ":(exclude).caicli/logs/**"])
            {
                return SuccessfulGitResult(":100644 100644 6a8165460570531a1247bd99a73b53a5a6e500d5 0000000000000000000000000000000000000000 M\0same.txt\0");
            }

            if (args is ["ls-files", "--debug", "-z", "--", ":(exclude).caicli/logs/**"])
            {
                return SuccessfulGitResult($"same.txt\0  mtime: {mtimeSeconds}:0\n  size: 5\tflags: 0\n");
            }

            if (args is ["ls-files", "-s", "-z", "--", "same.txt"])
            {
                return SuccessfulGitResult("100644 6a8165460570531a1247bd99a73b53a5a6e500d5 0\tsame.txt\0");
            }

            if (args is ["show", ":0:same.txt"])
            {
                Directory.CreateDirectory(Path.GetDirectoryName(stdoutPath!)!);
                File.WriteAllText(stdoutPath!, "abcde");
                return SuccessfulGitResult(string.Empty);
            }

            if (args.Contains("--no-index", StringComparer.Ordinal))
            {
                return new GitCommandResult(
                    Succeeded: true,
                    ExitCode: 1,
                    Stdout: "diff --git a/old/same.txt b/new/same.txt\n--- a/old/same.txt\n+++ b/new/same.txt\n@@ -1 +1 @@\n-abcde\n+vwxyz\n",
                    Stderr: string.Empty,
                    StdoutTruncated: false,
                    StderrTruncated: false,
                    ErrorCode: null,
                    Summary: "Git command completed with exit code 1.");
            }

            if (args is ["diff-files", "--summary", "--no-ext-diff", "--no-textconv", "--", "same.txt"])
            {
                return SuccessfulGitResult(string.Empty);
            }

            if (args is ["config", "--bool", "core.ignorecase"])
            {
                return FailedGitResult(exitCode: 1, stderr: string.Empty);
            }

            if (args is ["ls-files", "--others", "--exclude-standard", "-z"])
            {
                return SuccessfulGitResult(string.Empty);
            }

            return SuccessfulGitResult(string.Empty);
        });
        GitDiffTool tool = new(new WorkspaceGuard(), runner);

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path));

        Assert.True(result.Succeeded);
        Assert.Contains("same.txt", result.Summary, StringComparison.Ordinal);
        Assert.Contains("-abcde", result.Summary, StringComparison.Ordinal);
        Assert.Contains("+vwxyz", result.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("no diff", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Git_diff_warns_for_untracked_symlink_without_copying_target_content()
    {
        using TempDirectory temp = TempDirectory.Create();
        string linkPath = Path.Combine(temp.Path, "linked.txt");
        File.WriteAllText(linkPath, "outside secret\n");
        FakeGitCommandRunner runner = new(args =>
        {
            if (args is ["rev-parse", "--verify", "HEAD"])
            {
                return SuccessfulGitResult(string.Empty);
            }

            if (args.FirstOrDefault() == "diff-index")
            {
                return SuccessfulGitResult(string.Empty);
            }

            if (args is ["diff-files", "--raw", "--no-ext-diff", "--no-textconv", "-z", "--", ":(exclude).caicli/logs/**"])
            {
                return SuccessfulGitResult(string.Empty);
            }

            if (args is ["config", "--bool", "core.ignorecase"])
            {
                return FailedGitResult(exitCode: 1, stderr: string.Empty);
            }

            if (args is ["ls-files", "--others", "--exclude-standard", "-z"])
            {
                return SuccessfulGitResult("linked.txt\0");
            }

            if (args.Contains("--no-index", StringComparer.Ordinal))
            {
                return new GitCommandResult(
                    Succeeded: true,
                    ExitCode: 1,
                    Stdout: "diff --git a/linked.txt b/linked.txt\n+outside secret\n",
                    Stderr: string.Empty,
                    StdoutTruncated: false,
                    StderrTruncated: false,
                    ErrorCode: null,
                    Summary: "Git command completed with exit code 1.");
            }

            return SuccessfulGitResult(string.Empty);
        });
        GitDiffTool tool = new(
            new WorkspaceGuard(),
            runner,
            path => string.Equals(path, linkPath, StringComparison.Ordinal));

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path));

        Assert.True(result.Succeeded);
        Assert.Contains("linked.txt", result.Summary, StringComparison.Ordinal);
        Assert.Contains("WARNING", result.Summary, StringComparison.Ordinal);
        Assert.Contains("symlink/reparse", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("outside secret", result.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain(runner.Commands, command => command.Contains("--no-index", StringComparer.Ordinal));
    }

    [Fact]
    public void Git_diff_warns_for_untracked_file_under_reparse_directory_without_copying_target_content()
    {
        using TempDirectory temp = TempDirectory.Create();
        string linkDirPath = Path.Combine(temp.Path, "linkdir");
        string exposedPath = Path.Combine(linkDirPath, "outside.txt");
        Directory.CreateDirectory(linkDirPath);
        File.WriteAllText(exposedPath, "outside secret\n");
        FakeGitCommandRunner runner = new(args =>
        {
            if (args is ["rev-parse", "--verify", "HEAD"])
            {
                return SuccessfulGitResult(string.Empty);
            }

            if (args.FirstOrDefault() == "diff-index")
            {
                return SuccessfulGitResult(string.Empty);
            }

            if (args is ["diff-files", "--raw", "--no-ext-diff", "--no-textconv", "-z", "--", ":(exclude).caicli/logs/**"])
            {
                return SuccessfulGitResult(string.Empty);
            }

            if (args is ["config", "--bool", "core.ignorecase"])
            {
                return FailedGitResult(exitCode: 1, stderr: string.Empty);
            }

            if (args is ["ls-files", "--others", "--exclude-standard", "-z"])
            {
                return SuccessfulGitResult("linkdir/outside.txt\0");
            }

            if (args.Contains("--no-index", StringComparer.Ordinal))
            {
                return new GitCommandResult(
                    Succeeded: true,
                    ExitCode: 1,
                    Stdout: "diff --git a/linkdir/outside.txt b/linkdir/outside.txt\n+outside secret\n",
                    Stderr: string.Empty,
                    StdoutTruncated: false,
                    StderrTruncated: false,
                    ErrorCode: null,
                    Summary: "Git command completed with exit code 1.");
            }

            return SuccessfulGitResult(string.Empty);
        });
        GitDiffTool tool = new(
            new WorkspaceGuard(),
            runner,
            path => string.Equals(Path.GetFullPath(path), linkDirPath, StringComparison.Ordinal));

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path));

        Assert.True(result.Succeeded);
        Assert.Contains("linkdir/outside.txt", result.Summary, StringComparison.Ordinal);
        Assert.Contains("WARNING", result.Summary, StringComparison.Ordinal);
        Assert.Contains("symlink/reparse", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("outside secret", result.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain(runner.Commands, command => command.Contains("--no-index", StringComparer.Ordinal));
    }

    [Fact]
    public void Git_diff_warns_for_tracked_file_replaced_by_symlink_without_copying_target_content()
    {
        using TempDirectory temp = TempDirectory.Create();
        string trackedPath = Path.Combine(temp.Path, "tracked.txt");
        File.WriteAllText(trackedPath, "outside secret\n");
        FakeGitCommandRunner runner = new((args, stdoutPath) =>
        {
            if (args is ["rev-parse", "--verify", "HEAD"])
            {
                return SuccessfulGitResult(string.Empty);
            }

            if (args.FirstOrDefault() == "diff-index")
            {
                return SuccessfulGitResult(string.Empty);
            }

            if (args is ["diff-files", "--raw", "--no-ext-diff", "--no-textconv", "-z", "--", ":(exclude).caicli/logs/**"])
            {
                return SuccessfulGitResult(":100644 120000 abcdef1234567890abcdef1234567890abcdef12 0000000000000000000000000000000000000000 T\0tracked.txt\0");
            }

            if (args is ["ls-files", "-s", "-z", "--", "tracked.txt"])
            {
                return SuccessfulGitResult("100644 abcdef1234567890abcdef1234567890abcdef12 0\ttracked.txt\0");
            }

            if (args is ["diff-files", "--summary", "--no-ext-diff", "--no-textconv", "--", "tracked.txt"])
            {
                return SuccessfulGitResult(" mode change 100644 => 120000 tracked.txt\n");
            }

            if (args is ["show", ":0:tracked.txt"])
            {
                Directory.CreateDirectory(Path.GetDirectoryName(stdoutPath!)!);
                File.WriteAllText(stdoutPath!, "original\n");
                return SuccessfulGitResult(string.Empty);
            }

            if (args is ["config", "--bool", "core.ignorecase"])
            {
                return FailedGitResult(exitCode: 1, stderr: string.Empty);
            }

            if (args is ["ls-files", "--others", "--exclude-standard", "-z"])
            {
                return SuccessfulGitResult(string.Empty);
            }

            if (args.Contains("--no-index", StringComparer.Ordinal))
            {
                return new GitCommandResult(
                    Succeeded: true,
                    ExitCode: 1,
                    Stdout: "diff --git a/tracked.txt b/tracked.txt\n+outside secret\n",
                    Stderr: string.Empty,
                    StdoutTruncated: false,
                    StderrTruncated: false,
                    ErrorCode: null,
                    Summary: "Git command completed with exit code 1.");
            }

            return SuccessfulGitResult(string.Empty);
        });
        GitDiffTool tool = new(
            new WorkspaceGuard(),
            runner,
            path => string.Equals(path, trackedPath, StringComparison.Ordinal));

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path));

        Assert.True(result.Succeeded);
        Assert.Contains("tracked.txt", result.Summary, StringComparison.Ordinal);
        Assert.Contains("WARNING", result.Summary, StringComparison.Ordinal);
        Assert.Contains("symlink/reparse", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("outside secret", result.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain(runner.Commands, command => command is ["show", ":0:tracked.txt"]);
        Assert.DoesNotContain(runner.Commands, command => command.Contains("--no-index", StringComparer.Ordinal));
    }

    [Fact]
    public void Git_diff_warns_for_tracked_file_under_reparse_directory_without_copying_target_content()
    {
        using TempDirectory temp = TempDirectory.Create();
        string linkDirPath = Path.Combine(temp.Path, "linkdir");
        string trackedPath = Path.Combine(linkDirPath, "tracked.txt");
        Directory.CreateDirectory(linkDirPath);
        File.WriteAllText(trackedPath, "outside secret\n");
        FakeGitCommandRunner runner = new((args, stdoutPath) =>
        {
            if (args is ["rev-parse", "--verify", "HEAD"])
            {
                return SuccessfulGitResult(string.Empty);
            }

            if (args.FirstOrDefault() == "diff-index")
            {
                return SuccessfulGitResult(string.Empty);
            }

            if (args is ["diff-files", "--raw", "--no-ext-diff", "--no-textconv", "-z", "--", ":(exclude).caicli/logs/**"])
            {
                return SuccessfulGitResult(":100644 100644 abcdef1234567890abcdef1234567890abcdef12 0000000000000000000000000000000000000000 M\0linkdir/tracked.txt\0");
            }

            if (args is ["ls-files", "-s", "-z", "--", "linkdir/tracked.txt"])
            {
                return SuccessfulGitResult("100644 abcdef1234567890abcdef1234567890abcdef12 0\tlinkdir/tracked.txt\0");
            }

            if (args is ["diff-files", "--summary", "--no-ext-diff", "--no-textconv", "--", "linkdir/tracked.txt"])
            {
                return SuccessfulGitResult(string.Empty);
            }

            if (args is ["show", ":0:linkdir/tracked.txt"])
            {
                Directory.CreateDirectory(Path.GetDirectoryName(stdoutPath!)!);
                File.WriteAllText(stdoutPath!, "original\n");
                return SuccessfulGitResult(string.Empty);
            }

            if (args is ["config", "--bool", "core.ignorecase"])
            {
                return FailedGitResult(exitCode: 1, stderr: string.Empty);
            }

            if (args is ["ls-files", "--others", "--exclude-standard", "-z"])
            {
                return SuccessfulGitResult(string.Empty);
            }

            if (args.Contains("--no-index", StringComparer.Ordinal))
            {
                return new GitCommandResult(
                    Succeeded: true,
                    ExitCode: 1,
                    Stdout: "diff --git a/linkdir/tracked.txt b/linkdir/tracked.txt\n+outside secret\n",
                    Stderr: string.Empty,
                    StdoutTruncated: false,
                    StderrTruncated: false,
                    ErrorCode: null,
                    Summary: "Git command completed with exit code 1.");
            }

            return SuccessfulGitResult(string.Empty);
        });
        GitDiffTool tool = new(
            new WorkspaceGuard(),
            runner,
            path => string.Equals(Path.GetFullPath(path), linkDirPath, StringComparison.Ordinal));

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path));

        Assert.True(result.Succeeded);
        Assert.Contains("linkdir/tracked.txt", result.Summary, StringComparison.Ordinal);
        Assert.Contains("WARNING", result.Summary, StringComparison.Ordinal);
        Assert.Contains("symlink/reparse", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            runner.Commands,
            command => command is ["diff-files", "--summary", "--no-ext-diff", "--no-textconv", "--", "linkdir/tracked.txt"]);
        Assert.DoesNotContain("outside secret", result.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain(runner.Commands, command => command is ["show", ":0:linkdir/tracked.txt"]);
        Assert.DoesNotContain(runner.Commands, command => command.Contains("--no-index", StringComparer.Ordinal));
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
    public void Git_diff_preserves_case_variant_caicli_logs_when_repo_is_case_sensitive()
    {
        using TempDirectory temp = TempDirectory.Create();
        InitializeGitRepository(temp.Path);
        RunGit(temp.Path, "config", "core.ignorecase", "false");
        string logsPath = Path.Combine(temp.Path, ".caicli", "Logs");
        Directory.CreateDirectory(logsPath);
        File.WriteAllText(Path.Combine(logsPath, "user.log"), "user content\n");
        GitDiffTool tool = new(new WorkspaceGuard());

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path));

        Assert.True(result.Succeeded);
        Assert.Contains(".caicli/Logs/user.log", result.Summary, StringComparison.Ordinal);
        Assert.Contains("+user content", result.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("no diff", result.Summary, StringComparison.Ordinal);
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
    public void Git_diff_ignores_tracked_cli_command_log_changes()
    {
        using TempDirectory temp = TempDirectory.Create();
        InitializeGitRepository(temp.Path);
        string logPath = TrackCliCommandLogs(temp.Path);
        File.AppendAllText(logPath, "command=diff\n");
        GitDiffTool tool = new(new WorkspaceGuard());

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path));

        Assert.True(result.Succeeded);
        Assert.Equal("no diff", result.Summary);
    }

    [Fact]
    public void Git_diff_ignores_staged_tracked_cli_command_log_changes()
    {
        using TempDirectory temp = TempDirectory.Create();
        InitializeGitRepository(temp.Path);
        string logPath = TrackCliCommandLogs(temp.Path);
        File.AppendAllText(logPath, "command=diff\n");
        RunGit(temp.Path, "add", ".caicli/logs");
        GitDiffTool tool = new(new WorkspaceGuard());

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path));

        Assert.True(result.Succeeded);
        Assert.Equal("no diff", result.Summary);
    }

    [Fact]
    public void Git_diff_ignores_tracked_cli_logs_without_hiding_tracked_caicli_files()
    {
        using TempDirectory temp = TempDirectory.Create();
        InitializeGitRepository(temp.Path);
        string logPath = TrackCliCommandLogs(temp.Path);
        string configPath = TrackCaicliConfig(temp.Path);
        File.AppendAllText(logPath, "command=diff\n");
        File.WriteAllText(configPath, "{\"model\":\"gpt-test\"}\n");
        GitDiffTool tool = new(new WorkspaceGuard());

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path));

        Assert.True(result.Succeeded);
        Assert.Contains(".caicli/config.json", result.Summary, StringComparison.Ordinal);
        Assert.Contains("gpt-test", result.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain(".caicli/logs", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no diff", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Git_diff_stat_ignores_staged_tracked_cli_logs_without_hiding_staged_user_files()
    {
        using TempDirectory temp = TempDirectory.Create();
        string filePath = InitializeGitRepository(temp.Path);
        string logPath = TrackCliCommandLogs(temp.Path);
        File.AppendAllText(logPath, "command=diff\n");
        File.AppendAllText(filePath, "visible staged user change\n");
        RunGit(temp.Path, "add", ".caicli/logs", "tracked.txt");
        GitDiffTool tool = new(new WorkspaceGuard());

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path, """{"stat":true}"""));

        Assert.True(result.Succeeded);
        Assert.Contains("tracked.txt", result.Summary, StringComparison.Ordinal);
        Assert.Contains("1 file changed", result.Summary, StringComparison.Ordinal);
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
    public void Git_diff_warns_and_bounds_aggregate_output_from_many_untracked_files()
    {
        using TempDirectory temp = TempDirectory.Create();
        InitializeGitRepository(temp.Path);
        for (int index = 0; index < 24; index++)
        {
            string fileName = $"untracked-{index:D2}.txt";
            File.WriteAllText(Path.Combine(temp.Path, fileName), new string((char)('a' + index % 26), 4096) + "\n");
        }

        GitDiffTool tool = new(new WorkspaceGuard());

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path));

        Assert.True(result.Succeeded);
        Assert.Contains(GitDiffTool.TruncationWarning, result.Summary, StringComparison.Ordinal);
        int byteCount = Encoding.UTF8.GetByteCount(result.Summary);
        int warningBytes = Encoding.UTF8.GetByteCount(Environment.NewLine + Environment.NewLine + GitDiffTool.TruncationWarning);
        Assert.True(byteCount <= 64 * 1024 + warningBytes, $"Expected aggregate diff to be bounded, but it was {byteCount} bytes.");
    }

    [Fact]
    public void Git_diff_returns_failure_when_untracked_no_index_exit_one_has_no_diff_output()
    {
        using TempDirectory temp = TempDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "missing.txt"), "fresh\n");
        FakeGitCommandRunner runner = new(args =>
        {
            if (args is ["rev-parse", "--verify", "HEAD"])
            {
                return SuccessfulGitResult(string.Empty);
            }

            if (args is ["config", "--bool", "core.ignorecase"])
            {
                return FailedGitResult(exitCode: 1, stderr: string.Empty);
            }

            if (args is ["ls-files", "--others", "--exclude-standard", "-z"])
            {
                return SuccessfulGitResult("missing.txt\0");
            }

            if (args.Contains("--no-index", StringComparer.Ordinal))
            {
                return new GitCommandResult(
                    Succeeded: true,
                    ExitCode: 1,
                    Stdout: string.Empty,
                    Stderr: "error: Could not access 'missing.txt'\n",
                    StdoutTruncated: false,
                    StderrTruncated: false,
                    ErrorCode: null,
                    Summary: "Git command completed with exit code 1.");
            }

            return SuccessfulGitResult(string.Empty);
        });
        GitDiffTool tool = new(new WorkspaceGuard(), runner);

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path));

        Assert.False(result.Succeeded);
        Assert.Equal("git-diff-failed", result.ErrorCode);
        Assert.Contains("Could not access", result.Summary, StringComparison.Ordinal);
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

    private static (string FilePath, string MarkerPath) InitializeGitRepositoryWithCleanFilter(
        string root,
        string attributesPattern)
    {
        RunGit(root, "init");
        RunGit(root, "config", "user.email", "test@example.invalid");
        RunGit(root, "config", "user.name", "Test User");

        string markerPath = Path.Combine(root, ".git", "clean-filter.marker");
        string helperPath = Path.Combine(root, ".git", "clean-filter.sh");
        File.WriteAllText(
            helperPath,
            "#!/bin/sh\ncat\nprintf invoked > '" + NormalizeGitPath(markerPath) + "'\n");
        RunGit(root, "config", "filter.caicli_sideeffect.clean", "sh '" + NormalizeGitPath(helperPath) + "'");
        RunGit(root, "config", "filter.caicli_sideeffect.smudge", "cat");

        File.WriteAllText(
            Path.Combine(root, ".gitattributes"),
            attributesPattern + " filter=caicli_sideeffect\n");
        string filePath = Path.Combine(root, "tracked.txt");
        File.WriteAllText(filePath, "original\n");
        RunGit(root, "add", ".gitattributes", "tracked.txt");
        CommitAll(root, "initial");
        return (filePath, markerPath);
    }

    private static void DeleteCleanFilterMarker(string markerPath)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            if (File.Exists(markerPath))
            {
                File.Delete(markerPath);
            }

            Thread.Sleep(50);
            if (!File.Exists(markerPath))
            {
                return;
            }
        }

        if (File.Exists(markerPath))
        {
            File.Delete(markerPath);
        }
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

    private static string WriteExternalDiffHelper(string root, string markerPath)
    {
        string helperPath = Path.Combine(root, ".git", "external-diff-helper.sh");
        File.WriteAllText(
            helperPath,
            "#!/bin/sh\nprintf invoked > '" + NormalizeGitPath(markerPath) + "'\nexit 0\n");
        return helperPath;
    }

    private static string NormalizeGitPath(string path)
    {
        return path.Replace('\\', '/');
    }

    private static string TrackCliCommandLogs(string root)
    {
        string logsPath = Path.Combine(root, ".caicli", "logs");
        Directory.CreateDirectory(logsPath);
        DateTime utcToday = DateTime.UtcNow.Date;
        string currentLogPath = Path.Combine(logsPath, utcToday.ToString("yyyy-MM-dd") + ".log");
        foreach (DateTime date in new[] { utcToday.AddDays(-1), utcToday, utcToday.AddDays(1) })
        {
            File.WriteAllText(Path.Combine(logsPath, date.ToString("yyyy-MM-dd") + ".log"), "initial\n");
        }

        RunGit(root, "add", ".caicli/logs");
        CommitAll(root, "track cli logs");
        return currentLogPath;
    }

    private static string TrackCaicliConfig(string root)
    {
        string configPath = Path.Combine(root, ".caicli", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        File.WriteAllText(configPath, "{}\n");
        RunGit(root, "add", ".caicli/config.json");
        CommitAll(root, "track caicli config");
        return configPath;
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

    private static void CommitAll(string workingDirectory, string message)
    {
        string emptyHooksPath = Path.Combine(workingDirectory, ".caicli-empty-hooks");
        Directory.CreateDirectory(emptyHooksPath);
        RunGit(
            workingDirectory,
            "-c",
            "commit.gpgSign=false",
            "-c",
            "core.hooksPath=" + emptyHooksPath,
            "commit",
            "--no-gpg-sign",
            "--no-verify",
            "-m",
            message);
    }

    private static GitCommandResult SuccessfulGitResult(string stdout)
    {
        return new GitCommandResult(
            Succeeded: true,
            ExitCode: 0,
            Stdout: stdout,
            Stderr: string.Empty,
            StdoutTruncated: false,
            StderrTruncated: false,
            ErrorCode: null,
            Summary: "Git command completed with exit code 0.");
    }

    private static GitCommandResult FailedGitResult(int exitCode, string stderr)
    {
        return new GitCommandResult(
            Succeeded: false,
            ExitCode: exitCode,
            Stdout: string.Empty,
            Stderr: stderr,
            StdoutTruncated: false,
            StderrTruncated: false,
            ErrorCode: "git-command-failed",
            Summary: "Git command failed with exit code " + exitCode + ".");
    }

    private sealed class FakeGitCommandRunner : IGitCommandRunner
    {
        private readonly Func<string[], string?, GitCommandResult> handle;

        public FakeGitCommandRunner(Func<string[], GitCommandResult> handle)
            : this((args, _) => handle(args))
        {
        }

        public FakeGitCommandRunner(Func<string[], string?, GitCommandResult> handle)
        {
            this.handle = handle;
        }

        public List<string[]> Commands { get; } = [];

        public GitCommandResult Run(string workspaceRoot, string arguments)
        {
            return handle([arguments], null);
        }

        public GitCommandResult RunArgumentList(
            string workspaceRoot,
            IEnumerable<string> arguments,
            IReadOnlySet<int>? successfulExitCodes = null)
        {
            string[] args = arguments.ToArray();
            Commands.Add(args);
            return handle(args, null);
        }

        public GitCommandResult RunArgumentListToFile(
            string workspaceRoot,
            IEnumerable<string> arguments,
            string stdoutPath,
            IReadOnlySet<int>? successfulExitCodes = null)
        {
            string[] args = arguments.ToArray();
            Commands.Add(args);
            return handle(args, stdoutPath);
        }
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
