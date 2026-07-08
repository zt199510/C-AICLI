using System.Text;
using System.Text.Json;

namespace CSharpAiCli.Core;

public sealed class GitDiffTool : ITool
{
    public const string TruncationWarning = "WARNING: git output was truncated; diff is incomplete.";
    private const int MaxAggregateOutputBytes = 64 * 1024;
    private const string EmptyTreeHash = "4b825dc642cb6eb9a060e54bf8d69288fbee4904";
    private const string CliCommandLogPathspec = ":(exclude).caicli/logs/**";
    private static readonly IReadOnlySet<int> NoIndexDiffSuccessExitCodes = new HashSet<int> { 0, 1 };
    private readonly IWorkspaceGuard workspaceGuard;
    private readonly IGitCommandRunner gitCommandRunner;
    private readonly Func<string, bool> isSymlinkOrReparsePoint;

    public GitDiffTool(IWorkspaceGuard workspaceGuard)
        : this(workspaceGuard, new GitCommandRunner())
    {
    }

    internal GitDiffTool(
        IWorkspaceGuard workspaceGuard,
        IGitCommandRunner gitCommandRunner,
        Func<string, bool>? isSymlinkOrReparsePoint = null)
    {
        ArgumentNullException.ThrowIfNull(workspaceGuard);
        ArgumentNullException.ThrowIfNull(gitCommandRunner);
        this.workspaceGuard = workspaceGuard;
        this.gitCommandRunner = gitCommandRunner;
        this.isSymlinkOrReparsePoint = isSymlinkOrReparsePoint ?? IsSymlinkOrReparsePoint;
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
        using TemporaryDiffDirectory temporaryDiffDirectory = TemporaryDiffDirectory.Create();

        GitCommandResult head = gitCommandRunner.RunArgumentList(
            workspaceRoot,
            ["rev-parse", "--verify", "HEAD"]);
        if (!head.Succeeded && !IsMissingHead(head))
        {
            return GitDiffReadResult.Failed(ToGitFailure(head));
        }

        truncated |= IsTruncated(head);
        bool hasHead = head.Succeeded;
        GitCommandResult stagedDiff = gitCommandRunner.RunArgumentList(
            workspaceRoot,
            CreateStagedDiffArguments(stat, hasHead));
        if (!stagedDiff.Succeeded)
        {
            return GitDiffReadResult.Failed(ToGitFailure(stagedDiff));
        }

        truncated |= IsTruncated(stagedDiff);
        truncated |= !outputs.TryAdd(stagedDiff.Stdout);

        GitCommandResult trackedFiles = gitCommandRunner.RunArgumentList(
            workspaceRoot,
            ["diff-files", "--raw", "--no-ext-diff", "--no-textconv", "-z", "--", CliCommandLogPathspec]);
        if (!trackedFiles.Succeeded)
        {
            return GitDiffReadResult.Failed(ToGitFailure(trackedFiles));
        }

        truncated |= IsTruncated(trackedFiles);
        foreach (string relativePath in ParseRawDiffPaths(trackedFiles.Stdout))
        {
            if (!outputs.HasBudget)
            {
                truncated = true;
                break;
            }

            GitCommandResult unstagedDiff = CreateUnstagedTrackedDiff(
                workspaceRoot,
                temporaryDiffDirectory.Path,
                relativePath,
                stat);
            if (!IsSuccessfulNoIndexDiff(unstagedDiff))
            {
                return GitDiffReadResult.Failed(ToGitFailure(unstagedDiff));
            }

            truncated |= IsTruncated(unstagedDiff);
            truncated |= !outputs.TryAdd(unstagedDiff.Stdout);
        }

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

            GitCommandResult untrackedDiff = CreateUntrackedDiff(
                workspaceRoot,
                temporaryDiffDirectory.Path,
                relativePath,
                stat);
            if (!IsSuccessfulNoIndexDiff(untrackedDiff))
            {
                return GitDiffReadResult.Failed(ToGitFailure(untrackedDiff));
            }

            truncated |= IsTruncated(untrackedDiff);
            truncated |= !outputs.TryAdd(untrackedDiff.Stdout);
        }

        return GitDiffReadResult.Succeeded(outputs.ToString(), truncated);
    }

    private static IReadOnlyList<string> ParseRawDiffPaths(string rawOutput)
    {
        List<string> paths = [];
        int position = 0;
        while (position < rawOutput.Length)
        {
            int headerEnd = rawOutput.IndexOf('\0', position);
            if (headerEnd < 0)
            {
                break;
            }

            string header = rawOutput[position..headerEnd];
            position = headerEnd + 1;
            int pathEnd = rawOutput.IndexOf('\0', position);
            if (pathEnd < 0)
            {
                break;
            }

            string path = rawOutput[position..pathEnd];
            position = pathEnd + 1;
            string status = ReadRawDiffStatus(header);
            if ((status.StartsWith("R", StringComparison.Ordinal)
                    || status.StartsWith("C", StringComparison.Ordinal))
                && position < rawOutput.Length)
            {
                int secondPathEnd = rawOutput.IndexOf('\0', position);
                if (secondPathEnd < 0)
                {
                    break;
                }

                path = rawOutput[position..secondPathEnd];
                position = secondPathEnd + 1;
            }

            if (!string.IsNullOrWhiteSpace(path))
            {
                paths.Add(path);
            }
        }

        return paths;
    }

    private static string ReadRawDiffStatus(string header)
    {
        int statusStart = header.LastIndexOf(' ');
        return statusStart < 0 || statusStart == header.Length - 1
            ? string.Empty
            : header[(statusStart + 1)..];
    }

    private static string[] CreateStagedDiffArguments(bool stat, bool hasHead)
    {
        List<string> arguments = ["diff-index", "--cached", "--no-ext-diff", "--no-textconv"];
        if (stat)
        {
            arguments.Add("--stat");
        }
        else
        {
            arguments.Add("-p");
        }

        arguments.Add(hasHead ? "HEAD" : EmptyTreeHash);
        arguments.Add("--");
        arguments.Add(CliCommandLogPathspec);
        return [.. arguments];
    }

    private GitCommandResult CreateUnstagedTrackedDiff(
        string workspaceRoot,
        string temporaryRoot,
        string relativePath,
        bool stat)
    {
        GitIndexEntryReadResult indexEntry = ReadIndexEntry(workspaceRoot, relativePath);
        if (indexEntry.Failure is not null)
        {
            return indexEntry.Failure;
        }

        GitCommandResult summaryDiff = ReadUnstagedSummaryDiff(workspaceRoot, relativePath);
        if (indexEntry.Entry is null || !indexEntry.Entry.IsRegularFile)
        {
            return CreateMetadataOnlyUnstagedDiffResult(summaryDiff, relativePath, indexEntry.Entry?.Mode);
        }

        string worktreeFullPath = Path.GetFullPath(
            relativePath.Replace('/', Path.DirectorySeparatorChar),
            workspaceRoot);
        if (HasSymlinkOrReparsePointInPath(workspaceRoot, relativePath))
        {
            return CreateSymlinkOrReparsePointDiffResult(summaryDiff, relativePath);
        }

        string oldRelativePath = GetTemporaryRelativePath("old", relativePath);
        string oldFullPath = GetTemporaryFullPath(temporaryRoot, oldRelativePath);
        GitCommandResult indexBlob = gitCommandRunner.RunArgumentListToFile(
            workspaceRoot,
            ["show", ":0:" + relativePath],
            oldFullPath);
        if (!indexBlob.Succeeded)
        {
            return indexBlob;
        }

        string[] arguments;
        bool hasNewPath = File.Exists(worktreeFullPath);
        if (hasNewPath)
        {
            string newRelativePath = GetTemporaryRelativePath("new", relativePath);
            string newFullPath = GetTemporaryFullPath(temporaryRoot, newRelativePath);
            if (!TryCopyToTemporaryFile(worktreeFullPath, newFullPath, out GitCommandResult? failure))
            {
                return failure!;
            }

            arguments = CreateNoIndexDiffArguments(stat, oldRelativePath, newRelativePath);
        }
        else
        {
            arguments = CreateNoIndexDiffArguments(stat, oldRelativePath, "/dev/null");
        }

        GitCommandResult diff = gitCommandRunner.RunArgumentList(
            temporaryRoot,
            arguments,
            NoIndexDiffSuccessExitCodes);
        GitCommandResult normalizedDiff = NormalizeNoIndexDiffPaths(diff, relativePath);
        if (!IsSuccessfulNoIndexDiff(normalizedDiff))
        {
            return normalizedDiff;
        }

        return CombineSummaryAndContentDiff(summaryDiff, normalizedDiff);
    }

    private GitIndexEntryReadResult ReadIndexEntry(string workspaceRoot, string relativePath)
    {
        GitCommandResult result = gitCommandRunner.RunArgumentList(
            workspaceRoot,
            ["ls-files", "-s", "-z", "--", relativePath]);
        if (!result.Succeeded)
        {
            return GitIndexEntryReadResult.Failed(result);
        }

        string? entryText = result.Stdout.Split('\0', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(entryText))
        {
            return GitIndexEntryReadResult.Succeeded(null);
        }

        int modeEnd = entryText.IndexOf(' ', StringComparison.Ordinal);
        if (modeEnd <= 0)
        {
            return GitIndexEntryReadResult.Succeeded(null);
        }

        return GitIndexEntryReadResult.Succeeded(new GitIndexEntry(entryText[..modeEnd]));
    }

    private GitCommandResult ReadUnstagedSummaryDiff(string workspaceRoot, string relativePath)
    {
        return gitCommandRunner.RunArgumentList(
            workspaceRoot,
            ["diff-files", "--summary", "--no-ext-diff", "--no-textconv", "--", relativePath]);
    }

    private static GitCommandResult CreateMetadataOnlyUnstagedDiffResult(
        GitCommandResult summaryDiff,
        string relativePath,
        string? mode)
    {
        if (summaryDiff.Succeeded && !string.IsNullOrWhiteSpace(summaryDiff.Stdout))
        {
            return summaryDiff;
        }

        string modeText = string.IsNullOrWhiteSpace(mode) ? "unknown mode" : "mode " + mode;
        return GitCommandResultSuccess(
            $"WARNING: unstaged tracked non-regular diff omitted for {relativePath} ({modeText}).");
    }

    private static GitCommandResult CombineSummaryAndContentDiff(
        GitCommandResult summaryDiff,
        GitCommandResult contentDiff)
    {
        if (!summaryDiff.Succeeded || string.IsNullOrWhiteSpace(summaryDiff.Stdout))
        {
            return contentDiff;
        }

        string output = string.IsNullOrWhiteSpace(contentDiff.Stdout)
            ? summaryDiff.Stdout.Trim()
            : summaryDiff.Stdout.Trim() + Environment.NewLine + Environment.NewLine + contentDiff.Stdout.Trim();
        return contentDiff with
        {
            Stdout = output,
            StdoutTruncated = summaryDiff.StdoutTruncated || contentDiff.StdoutTruncated,
            StderrTruncated = summaryDiff.StderrTruncated || contentDiff.StderrTruncated
        };
    }

    private GitCommandResult CreateUntrackedDiff(
        string workspaceRoot,
        string temporaryRoot,
        string relativePath,
        bool stat)
    {
        string worktreeFullPath = Path.GetFullPath(
            relativePath.Replace('/', Path.DirectorySeparatorChar),
            workspaceRoot);
        if (HasSymlinkOrReparsePointInPath(workspaceRoot, relativePath))
        {
            return CreateSymlinkOrReparsePointDiffResult(summaryDiff: null, relativePath);
        }

        if (!File.Exists(worktreeFullPath))
        {
            return GitDiffFailure($"Untracked file could not be read: {relativePath}");
        }

        string newRelativePath = GetTemporaryRelativePath("new", relativePath);
        string newFullPath = GetTemporaryFullPath(temporaryRoot, newRelativePath);
        if (!TryCopyToTemporaryFile(worktreeFullPath, newFullPath, out GitCommandResult? failure))
        {
            return failure!;
        }

        GitCommandResult diff = gitCommandRunner.RunArgumentList(
            temporaryRoot,
            CreateNoIndexDiffArguments(stat, "/dev/null", newRelativePath),
            NoIndexDiffSuccessExitCodes);
        return NormalizeNoIndexDiffPaths(diff, relativePath);
    }

    private static bool TryCopyToTemporaryFile(
        string sourcePath,
        string destinationPath,
        out GitCommandResult? failure)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourcePath, destinationPath, overwrite: true);
            File.SetAttributes(destinationPath, FileAttributes.Normal);
            failure = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or PathTooLongException)
        {
            failure = GitDiffFailure("File could not be copied for diff generation.");
            return false;
        }
    }

    private static bool IsSymlinkOrReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception exception) when (exception is FileNotFoundException
            or DirectoryNotFoundException
            or IOException
            or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private bool HasSymlinkOrReparsePointInPath(string workspaceRoot, string relativePath)
    {
        string currentPath = Path.GetFullPath(workspaceRoot);
        foreach (string segment in NormalizeGitPath(relativePath).Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            currentPath = Path.Combine(currentPath, segment);
            if (isSymlinkOrReparsePoint(currentPath))
            {
                return true;
            }
        }

        return false;
    }

    private static GitCommandResult CreateSymlinkOrReparsePointDiffResult(
        GitCommandResult? summaryDiff,
        string relativePath)
    {
        const string warningPrefix = "WARNING: symlink/reparse diff omitted";
        string warning = $"{warningPrefix} for {relativePath}; target content was not copied.";
        if (summaryDiff?.Succeeded == true && !string.IsNullOrWhiteSpace(summaryDiff.Stdout))
        {
            return GitCommandResultSuccess(summaryDiff.Stdout.Trim() + Environment.NewLine + Environment.NewLine + warning);
        }

        return GitCommandResultSuccess(warning);
    }

    private static string[] CreateNoIndexDiffArguments(bool stat, string oldPath, string newPath)
    {
        List<string> arguments = ["diff", "--no-ext-diff", "--no-textconv", "--no-index"];
        if (stat)
        {
            arguments.Add("--stat");
        }

        arguments.Add("--");
        arguments.Add(oldPath);
        arguments.Add(newPath);
        return [.. arguments];
    }

    private static GitCommandResult NormalizeNoIndexDiffPaths(GitCommandResult result, string relativePath)
    {
        string normalizedPath = NormalizeGitPath(relativePath);
        bool inHunk = false;
        string stdout = string.Join(
            "\n",
            result.Stdout.Split('\n').Select(line =>
            {
                if (line.StartsWith("diff --git ", StringComparison.Ordinal))
                {
                    inHunk = false;
                    return NormalizeNoIndexMetadataLine(line, normalizedPath);
                }

                if (line.StartsWith("@@", StringComparison.Ordinal))
                {
                    inHunk = true;
                    return line;
                }

                return inHunk ? line : NormalizeNoIndexMetadataLine(line, normalizedPath);
            }));

        return result with { Stdout = stdout };
    }

    private static string NormalizeNoIndexMetadataLine(string line, string normalizedPath)
    {
        if (line.StartsWith("--- ", StringComparison.Ordinal)
            || line.StartsWith("+++ ", StringComparison.Ordinal)
            || line.StartsWith("Binary files ", StringComparison.Ordinal))
        {
            return ReplaceNoIndexPathPrefixes(line, normalizedPath);
        }

        int statSeparatorIndex = line.IndexOf('|', StringComparison.Ordinal);
        if (statSeparatorIndex > 0)
        {
            string pathPart = line[..statSeparatorIndex];
            string rest = line[statSeparatorIndex..];
            return ReplaceNoIndexPathPrefixes(pathPart, normalizedPath) + rest;
        }

        return line.StartsWith("diff --git ", StringComparison.Ordinal)
            ? ReplaceNoIndexPathPrefixes(line, normalizedPath)
            : line;
    }

    private static string ReplaceNoIndexPathPrefixes(string text, string normalizedPath)
    {
        return text
            .Replace("a/old/" + normalizedPath, "a/" + normalizedPath, StringComparison.Ordinal)
            .Replace("b/old/" + normalizedPath, "b/" + normalizedPath, StringComparison.Ordinal)
            .Replace("a/new/" + normalizedPath, "a/" + normalizedPath, StringComparison.Ordinal)
            .Replace("b/new/" + normalizedPath, "b/" + normalizedPath, StringComparison.Ordinal)
            .Replace("{old => new}/" + normalizedPath, normalizedPath, StringComparison.Ordinal)
            .Replace("/dev/null => new/" + normalizedPath, "/dev/null => " + normalizedPath, StringComparison.Ordinal)
            .Replace("old/" + normalizedPath + " => /dev/null", normalizedPath + " => /dev/null", StringComparison.Ordinal);
    }

    private static string GetTemporaryRelativePath(string rootName, string relativePath)
    {
        return rootName + "/" + NormalizeGitPath(relativePath);
    }

    private static string GetTemporaryFullPath(string temporaryRoot, string temporaryRelativePath)
    {
        return Path.Combine(
            temporaryRoot,
            temporaryRelativePath.Replace('/', Path.DirectorySeparatorChar));
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

    private static string NormalizeGitPath(string path)
    {
        return path.Replace('\\', '/');
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

    private static GitCommandResult GitDiffFailure(string summary)
    {
        return new GitCommandResult(
            Succeeded: false,
            ExitCode: null,
            Stdout: string.Empty,
            Stderr: summary,
            StdoutTruncated: false,
            StderrTruncated: false,
            ErrorCode: "git-diff-failed",
            Summary: summary);
    }

    private static GitCommandResult GitCommandResultSuccess(string stdout)
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

    private sealed record GitIndexEntry(string Mode)
    {
        public bool IsRegularFile => Mode is "100644" or "100755";
    }

    private sealed record GitIndexEntryReadResult(
        GitIndexEntry? Entry,
        GitCommandResult? Failure)
    {
        public static GitIndexEntryReadResult Succeeded(GitIndexEntry? entry)
        {
            return new GitIndexEntryReadResult(entry, null);
        }

        public static GitIndexEntryReadResult Failed(GitCommandResult failure)
        {
            return new GitIndexEntryReadResult(null, failure);
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

    private sealed class TemporaryDiffDirectory : IDisposable
    {
        private TemporaryDiffDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDiffDirectory Create()
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "caicli-diff-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TemporaryDiffDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                try
                {
                    ClearReadOnlyAttributes(Path);
                    Directory.Delete(Path, recursive: true);
                }
                catch (Exception exception) when (exception is IOException
                    or UnauthorizedAccessException)
                {
                }
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
