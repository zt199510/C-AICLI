using System.Text;
using System.Text.Json;

namespace CSharpAiCli.Core;

public sealed class GitDiffTool : ITool
{
    public const string TruncationWarning = "WARNING: git output was truncated; diff is incomplete.";
    private const int MaxAggregateOutputBytes = 64 * 1024;
    private const string CliCommandLogPathspec = ":(exclude).caicli/logs/**";
    private static readonly IReadOnlySet<int> NoIndexDiffSuccessExitCodes = new HashSet<int> { 0, 1 };
    private readonly IWorkspaceGuard workspaceGuard;
    private readonly IGitCommandRunner gitCommandRunner;

    public GitDiffTool(IWorkspaceGuard workspaceGuard)
        : this(workspaceGuard, new GitCommandRunner())
    {
    }

    internal GitDiffTool(IWorkspaceGuard workspaceGuard, IGitCommandRunner gitCommandRunner)
    {
        ArgumentNullException.ThrowIfNull(workspaceGuard);
        ArgumentNullException.ThrowIfNull(gitCommandRunner);
        this.workspaceGuard = workspaceGuard;
        this.gitCommandRunner = gitCommandRunner;
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
        DiffOutputBuilder outputs = new(MaxAggregateOutputBytes);
        bool truncated = false;

        GitCommandResult head = gitCommandRunner.RunArgumentList(
            workspaceRoot,
            ["rev-parse", "--verify", "HEAD"]);
        if (!head.Succeeded && !IsMissingHead(head))
        {
            return GitDiffReadResult.Failed(ToGitFailure(head));
        }

        truncated |= IsTruncated(head);
        GitCommandResult stagedDiff = gitCommandRunner.RunArgumentList(
            workspaceRoot,
            CreateTrackedDiffArguments(stat, staged: true));
        if (!stagedDiff.Succeeded)
        {
            return GitDiffReadResult.Failed(ToGitFailure(stagedDiff));
        }

        truncated |= IsTruncated(stagedDiff);
        truncated |= !outputs.TryAdd(stagedDiff.Stdout);

        GitCommandResult unstagedDiff = gitCommandRunner.RunArgumentList(
            workspaceRoot,
            CreateTrackedDiffArguments(stat, staged: false));
        if (!unstagedDiff.Succeeded)
        {
            return GitDiffReadResult.Failed(ToGitFailure(unstagedDiff));
        }

        truncated |= IsTruncated(unstagedDiff);
        truncated |= !outputs.TryAdd(unstagedDiff.Stdout);
        StringComparison cliCommandLogPathComparison = GetCliCommandLogPathComparison(workspaceRoot);

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
            if (!outputs.HasBudget)
            {
                truncated = true;
                break;
            }

            if (IsCliCommandLogPath(relativePath, cliCommandLogPathComparison))
            {
                continue;
            }

            string[] arguments = stat
                ? ["diff", "--no-ext-diff", "--no-textconv", "--no-index", "--stat", "--", "/dev/null", relativePath]
                : ["diff", "--no-ext-diff", "--no-textconv", "--no-index", "--", "/dev/null", relativePath];
            GitCommandResult untrackedDiff = gitCommandRunner.RunArgumentList(
                workspaceRoot,
                arguments,
                NoIndexDiffSuccessExitCodes);
            if (!IsSuccessfulNoIndexDiff(untrackedDiff))
            {
                return GitDiffReadResult.Failed(ToGitFailure(untrackedDiff));
            }

            truncated |= IsTruncated(untrackedDiff);
            truncated |= !outputs.TryAdd(untrackedDiff.Stdout);
        }

        return GitDiffReadResult.Succeeded(outputs.ToString(), truncated);
    }

    private static string[] CreateTrackedDiffArguments(bool stat, bool staged)
    {
        List<string> arguments = ["diff", "--no-ext-diff", "--no-textconv"];
        if (staged)
        {
            arguments.Add("--cached");
        }

        if (stat)
        {
            arguments.Add("--stat");
        }

        arguments.Add("--");
        arguments.Add(CliCommandLogPathspec);
        return [.. arguments];
    }

    private StringComparison GetCliCommandLogPathComparison(string workspaceRoot)
    {
        GitCommandResult ignoreCase = gitCommandRunner.RunArgumentList(
            workspaceRoot,
            ["config", "--bool", "core.ignorecase"]);
        if (ignoreCase.Succeeded
            && bool.TryParse(ignoreCase.Stdout.Trim(), out bool parsedIgnoreCase))
        {
            return parsedIgnoreCase
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
        }

        return OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }

    private static bool IsCliCommandLogPath(string relativePath, StringComparison comparison)
    {
        string normalizedPath = relativePath.Replace('\\', '/');
        return normalizedPath.StartsWith(".caicli/logs/", comparison);
    }

    private static bool IsSuccessfulNoIndexDiff(GitCommandResult result)
    {
        if (!result.Succeeded)
        {
            return false;
        }

        return result.ExitCode != 1 || !string.IsNullOrWhiteSpace(result.Stdout);
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

    private sealed class DiffOutputBuilder(int maxBytes)
    {
        private readonly StringBuilder builder = new();
        private int remainingBytes = maxBytes;

        public bool HasBudget => remainingBytes > 0;

        public bool TryAdd(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                return true;
            }

            string trimmed = output.Trim();
            if (builder.Length > 0 && !TryAppendWithinBudget(Environment.NewLine + Environment.NewLine))
            {
                return false;
            }

            return TryAppendWithinBudget(trimmed);
        }

        public override string ToString()
        {
            return builder.ToString();
        }

        private bool TryAppendWithinBudget(string text)
        {
            int byteCount = Encoding.UTF8.GetByteCount(text);
            if (byteCount <= remainingBytes)
            {
                builder.Append(text);
                remainingBytes -= byteCount;
                return true;
            }

            builder.Append(TakeUtf8Prefix(text, remainingBytes));
            remainingBytes = 0;
            return false;
        }

        private static string TakeUtf8Prefix(string text, int maxBytes)
        {
            if (maxBytes <= 0)
            {
                return string.Empty;
            }

            int low = 0;
            int high = text.Length;
            while (low < high)
            {
                int mid = low + (high - low + 1) / 2;
                if (Encoding.UTF8.GetByteCount(text.AsSpan(0, mid)) <= maxBytes)
                {
                    low = mid;
                }
                else
                {
                    high = mid - 1;
                }
            }

            return text[..low];
        }
    }
}
