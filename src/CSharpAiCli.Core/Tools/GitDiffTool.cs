using System.Text.Json;

namespace CSharpAiCli.Core;

public sealed class GitDiffTool : ITool
{
    public const string TruncationWarning = "WARNING: git output was truncated; diff is incomplete.";
    private static readonly IReadOnlySet<int> NoIndexDiffSuccessExitCodes = new HashSet<int> { 0, 1 };
    private readonly IWorkspaceGuard workspaceGuard;
    private readonly GitCommandRunner gitCommandRunner = new();

    public GitDiffTool(IWorkspaceGuard workspaceGuard)
    {
        ArgumentNullException.ThrowIfNull(workspaceGuard);
        this.workspaceGuard = workspaceGuard;
    }

    public ToolDefinition Definition { get; } = new(
        "git.diff",
        "Show git diff for the current workspace.",
        """{"type":"object","properties":{"stat":{"type":"boolean"}}}""",
        ToolRiskLevel.Read);

    public ToolExecutionResult Execute(
        ToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryReadArguments(context.ArgumentsJson, out bool stat, out ToolExecutionResult? failure))
        {
            return failure;
        }

        WorkspaceGuardResult guardResult = workspaceGuard.ResolvePath(context.Workspace, ".");
        if (!guardResult.IsAllowed || guardResult.FullPath is null)
        {
            return guardResult.ToFailure();
        }

        GitDiffReadResult diffResult = ReadCurrentDiff(guardResult.FullPath, stat);
        if (diffResult.Failure is not null)
        {
            return diffResult.Failure;
        }

        string output = string.IsNullOrWhiteSpace(diffResult.Output)
            ? "no diff"
            : diffResult.Output.Trim();
        if (diffResult.Truncated)
        {
            output = AppendTruncationWarning(output);
        }

        return ToolExecutionResult.Success(output);
    }

    private GitDiffReadResult ReadCurrentDiff(string workspaceRoot, bool stat)
    {
        List<string> outputs = [];
        bool truncated = false;

        GitCommandResult head = gitCommandRunner.RunArgumentList(
            workspaceRoot,
            ["rev-parse", "--verify", "HEAD"]);
        if (!head.Succeeded && !IsMissingHead(head))
        {
            return GitDiffReadResult.Failed(ToGitFailure(head));
        }

        truncated |= IsTruncated(head);
        GitCommandResult stagedDiff = gitCommandRunner.Run(
            workspaceRoot,
            stat ? "diff --cached --stat --" : "diff --cached --");
        if (!stagedDiff.Succeeded)
        {
            return GitDiffReadResult.Failed(ToGitFailure(stagedDiff));
        }

        truncated |= IsTruncated(stagedDiff);
        AddOutput(outputs, stagedDiff.Stdout);

        GitCommandResult unstagedDiff = gitCommandRunner.Run(
            workspaceRoot,
            stat ? "diff --stat --" : "diff --");
        if (!unstagedDiff.Succeeded)
        {
            return GitDiffReadResult.Failed(ToGitFailure(unstagedDiff));
        }

        truncated |= IsTruncated(unstagedDiff);
        AddOutput(outputs, unstagedDiff.Stdout);

        GitCommandResult untrackedFiles = gitCommandRunner.RunArgumentList(
            workspaceRoot,
            ["ls-files", "--others", "--exclude-standard", "-z"]);
        if (!untrackedFiles.Succeeded)
        {
            return GitDiffReadResult.Failed(ToGitFailure(untrackedFiles));
        }

        truncated |= IsTruncated(untrackedFiles);
        foreach (string relativePath in ParseNullSeparatedPaths(untrackedFiles.Stdout, untrackedFiles.StdoutTruncated))
        {
            string[] arguments = stat
                ? ["diff", "--no-index", "--stat", "--", "/dev/null", relativePath]
                : ["diff", "--no-index", "--", "/dev/null", relativePath];
            GitCommandResult untrackedDiff = gitCommandRunner.RunArgumentList(
                workspaceRoot,
                arguments,
                NoIndexDiffSuccessExitCodes);
            if (!untrackedDiff.Succeeded)
            {
                return GitDiffReadResult.Failed(ToGitFailure(untrackedDiff));
            }

            truncated |= IsTruncated(untrackedDiff);
            AddOutput(outputs, untrackedDiff.Stdout);
        }

        return GitDiffReadResult.Succeeded(string.Join(Environment.NewLine + Environment.NewLine, outputs), truncated);
    }

    private static bool IsMissingHead(GitCommandResult result)
    {
        return result.ExitCode == 128
            && result.Stderr.Contains("Needed a single revision", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> ParseNullSeparatedPaths(string text, bool truncated)
    {
        List<string> paths = text
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        if (truncated && text.Length > 0 && text[^1] != '\0' && paths.Count > 0)
        {
            paths.RemoveAt(paths.Count - 1);
        }

        return paths;
    }

    private static void AddOutput(List<string> outputs, string output)
    {
        if (!string.IsNullOrWhiteSpace(output))
        {
            outputs.Add(output.Trim());
        }
    }

    private static bool IsTruncated(GitCommandResult result)
    {
        return result.StdoutTruncated || result.StderrTruncated;
    }

    private static string AppendTruncationWarning(string output)
    {
        return string.IsNullOrWhiteSpace(output)
            ? TruncationWarning
            : output.TrimEnd() + Environment.NewLine + Environment.NewLine + TruncationWarning;
    }

    private static ToolExecutionResult ToGitFailure(GitCommandResult result)
    {
        return ToolExecutionResult.Failure(
            NormalizeGitError(result),
            string.IsNullOrWhiteSpace(result.Stderr) ? result.Summary : result.Stderr.Trim());
    }

    private static bool TryReadArguments(
        string? argumentsJson,
        out bool stat,
        out ToolExecutionResult failure)
    {
        stat = false;
        failure = null!;

        try
        {
            using JsonDocument document = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                failure = ToolExecutionResult.Failure(
                    "invalid-tool-arguments",
                    "Tool arguments must be a JSON object.");
                return false;
            }

            if (root.TryGetProperty("stat", out JsonElement statElement))
            {
                if (statElement.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
                {
                    failure = ToolExecutionResult.Failure(
                        "invalid-tool-arguments",
                        "stat must be a boolean.");
                    return false;
                }

                stat = statElement.GetBoolean();
            }

            return true;
        }
        catch (JsonException)
        {
            failure = ToolExecutionResult.Failure(
                "invalid-tool-arguments",
                "Tool arguments must be valid JSON.");
            return false;
        }
    }

    private static string NormalizeGitError(GitCommandResult result)
    {
        return result.Stderr.Contains("not a git repository", StringComparison.OrdinalIgnoreCase)
            ? "git-not-repository"
            : result.ErrorCode ?? "git-diff-failed";
    }

    private sealed record GitDiffReadResult(
        string Output,
        bool Truncated,
        ToolExecutionResult? Failure)
    {
        public static GitDiffReadResult Succeeded(string output, bool truncated)
        {
            return new GitDiffReadResult(output, truncated, null);
        }

        public static GitDiffReadResult Failed(ToolExecutionResult failure)
        {
            return new GitDiffReadResult(string.Empty, false, failure);
        }
    }
}
