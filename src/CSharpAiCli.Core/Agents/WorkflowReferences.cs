using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CSharpAiCli.Core;

public static class WorkflowReferenceErrorCode
{
    public const string TooManyReferences = "workflow-reference-too-many";
    public const string BoundaryDenied = "workflow-reference-boundary-denied";
    public const string NotFound = "workflow-reference-not-found";
    public const string NotFile = "workflow-reference-not-file";
    public const string NotFolder = "workflow-reference-not-folder";
    public const string BinaryNotSupported = "workflow-reference-binary-not-supported";
    public const string Unreadable = "workflow-reference-unreadable";
    public const string TooLarge = "workflow-reference-too-large";
    public const string ResolutionFailed = "workflow-reference-resolution-failed";
}

public sealed record WorkflowReferenceToken(
    string Kind,
    string SourceToken,
    string RequestedPath,
    int StartIndex);

public sealed record WorkflowReferenceFile(
    string Path,
    long ByteCount,
    int CharacterCount,
    bool Truncated,
    string Content);

public sealed record WorkflowReferenceWarning(
    string ErrorCode,
    string Message,
    string? Path = null);

public sealed record WorkflowReferenceEntry
{
    public WorkflowReferenceEntry(
        string Kind,
        string SourceToken,
        string RequestedPath,
        string? ResolvedPath,
        string Status,
        IReadOnlyList<WorkflowReferenceFile>? Files = null,
        IReadOnlyList<WorkflowReferenceWarning>? Warnings = null,
        string? ErrorCode = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(SourceToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(Status);

        this.Kind = Kind;
        this.SourceToken = SourceToken;
        this.RequestedPath = RequestedPath ?? string.Empty;
        this.ResolvedPath = ResolvedPath;
        this.Status = Status;
        this.Files = new ReadOnlyCollection<WorkflowReferenceFile>((Files ?? []).ToArray());
        this.Warnings = new ReadOnlyCollection<WorkflowReferenceWarning>((Warnings ?? []).ToArray());
        this.ErrorCode = ErrorCode;
    }

    public string Kind { get; }

    public string SourceToken { get; }

    public string RequestedPath { get; }

    public string? ResolvedPath { get; }

    public string Status { get; }

    public IReadOnlyList<WorkflowReferenceFile> Files { get; }

    public IReadOnlyList<WorkflowReferenceWarning> Warnings { get; }

    public string? ErrorCode { get; }

    public int IncludedFileCount => Files.Count;

    public int SkippedFileCount => Warnings.Count(warning => !string.IsNullOrWhiteSpace(warning.Path));

    public long ByteCount => Files.Sum(file => file.ByteCount);

    public bool Truncated => Files.Any(file => file.Truncated);
}

public sealed record WorkflowReferenceResolution
{
    public WorkflowReferenceResolution(IReadOnlyList<WorkflowReferenceEntry>? References = null)
    {
        this.References = new ReadOnlyCollection<WorkflowReferenceEntry>((References ?? []).ToArray());
    }

    public static WorkflowReferenceResolution Empty { get; } = new();

    public IReadOnlyList<WorkflowReferenceEntry> References { get; }

    public bool HasReferences => References.Count > 0;

    public bool HasErrors => References.Any(reference =>
        string.Equals(reference.Status, DiagnosticEventStatus.Failure, StringComparison.Ordinal));

    public bool HasWarnings => References.Any(reference =>
        string.Equals(reference.Status, DiagnosticEventStatus.Warning, StringComparison.Ordinal) ||
        reference.Warnings.Count > 0);

    public int IncludedFileCount => References.Sum(reference => reference.IncludedFileCount);

    public int SkippedFileCount => References.Sum(reference => reference.SkippedFileCount);

    public long ByteCount => References.Sum(reference => reference.ByteCount);

    public bool Truncated => References.Any(reference => reference.Truncated);

    public string? FirstErrorCode => References
        .Select(reference => reference.ErrorCode)
        .FirstOrDefault(errorCode => !string.IsNullOrWhiteSpace(errorCode));
}

public sealed record WorkflowReferenceResolverOptions
{
    public int MaxReferences { get; init; } = 20;

    public int MaxFileBytes { get; init; } = 64 * 1024;

    public int MaxFolderFiles { get; init; } = 50;

    public int MaxFolderBytes { get; init; } = 256 * 1024;

    public int MaxFolderDepth { get; init; } = 4;

    public IReadOnlySet<string> IgnoredDirectoryNames { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".git",
            ".caicli",
            "bin",
            "obj",
            "node_modules",
            ".vs",
            ".idea"
        };

    public static WorkflowReferenceResolverOptions Default { get; } = new();
}

public sealed class WorkflowReferenceParser
{
    private static readonly Regex ReferencePattern = new(
        """(?<!\S)@(?<kind>file|folder):(?:"(?<quoted>[^"]+)"|'(?<single>[^']+)'|`(?<backtick>[^`]+)`|(?<plain>[^\s]+))""",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public IReadOnlyList<WorkflowReferenceToken> Parse(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return [];
        }

        return ReferencePattern
            .Matches(prompt)
            .Select(match =>
            {
                string kind = match.Groups["kind"].Value.ToLowerInvariant();
                string path = ReadPath(match);
                string rawToken = match.Value;
                if (match.Groups["plain"].Success)
                {
                    string trimmedPath = TrimTrailingTokenPunctuation(path);
                    if (trimmedPath.Length != path.Length)
                    {
                        rawToken = rawToken[..^(path.Length - trimmedPath.Length)];
                        path = trimmedPath;
                    }
                }

                return new WorkflowReferenceToken(kind, rawToken, path, match.Index);
            })
            .Where(token => !string.IsNullOrWhiteSpace(token.RequestedPath))
            .ToArray();
    }

    private static string ReadPath(Match match)
    {
        foreach (string groupName in new[] { "quoted", "single", "backtick", "plain" })
        {
            Group group = match.Groups[groupName];
            if (group.Success)
            {
                return group.Value.Trim();
            }
        }

        return string.Empty;
    }

    private static string TrimTrailingTokenPunctuation(string path)
    {
        return path.TrimEnd(',', ';', ')', ']', '}');
    }
}

public sealed class WorkflowReferenceResolver
{
    private readonly IWorkspaceGuard workspaceGuard;
    private readonly WorkflowReferenceParser parser;
    private readonly WorkflowReferenceResolverOptions options;

    public WorkflowReferenceResolver()
        : this(new WorkspaceGuard(), new WorkflowReferenceParser(), WorkflowReferenceResolverOptions.Default)
    {
    }

    public WorkflowReferenceResolver(
        IWorkspaceGuard workspaceGuard,
        WorkflowReferenceParser? parser = null,
        WorkflowReferenceResolverOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(workspaceGuard);

        this.workspaceGuard = workspaceGuard;
        this.parser = parser ?? new WorkflowReferenceParser();
        this.options = options ?? WorkflowReferenceResolverOptions.Default;
    }

    public WorkflowReferenceResolution Resolve(WorkspaceContext workspace, string? prompt)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        IReadOnlyList<WorkflowReferenceToken> tokens = parser.Parse(prompt);
        if (tokens.Count == 0)
        {
            return WorkflowReferenceResolution.Empty;
        }

        if (tokens.Count > options.MaxReferences)
        {
            WorkflowReferenceToken first = tokens[options.MaxReferences];
            return new WorkflowReferenceResolution(
            [
                Failure(
                    first,
                    WorkflowReferenceErrorCode.TooManyReferences,
                    $"Workflow reference count exceeds the {options.MaxReferences.ToString(CultureInfo.InvariantCulture)} reference limit.")
            ]);
        }

        List<WorkflowReferenceEntry> entries = [];
        foreach (WorkflowReferenceToken token in tokens)
        {
            entries.Add(string.Equals(token.Kind, "folder", StringComparison.OrdinalIgnoreCase)
                ? ResolveFolder(workspace, token)
                : ResolveFile(workspace, token));
        }

        return new WorkflowReferenceResolution(entries);
    }

    private WorkflowReferenceEntry ResolveFile(WorkspaceContext workspace, WorkflowReferenceToken token)
    {
        WorkspaceGuardResult guardResult = workspaceGuard.ResolvePath(workspace, token.RequestedPath);
        if (!guardResult.IsAllowed || string.IsNullOrWhiteSpace(guardResult.FullPath))
        {
            return Failure(
                token,
                WorkflowReferenceErrorCode.BoundaryDenied,
                guardResult.SafeMessage,
                guardResult.FullPath);
        }

        string fullPath = guardResult.FullPath!;
        string relativePath = ToWorkspaceRelativePath(workspace, fullPath);
        if (!File.Exists(fullPath))
        {
            return Failure(token, WorkflowReferenceErrorCode.NotFound, "Referenced file was not found.", relativePath);
        }

        if (Directory.Exists(fullPath))
        {
            return Failure(token, WorkflowReferenceErrorCode.NotFile, "Referenced path is not a file.", relativePath);
        }

        FileInfo fileInfo = new(fullPath);
        try
        {
            if (TextFileUtilities.IsLikelyBinary(fullPath))
            {
                return Failure(token, WorkflowReferenceErrorCode.BinaryNotSupported, "Referenced file is binary and cannot be used as text context.", relativePath);
            }

            WorkflowReferenceFile file = ReadTextFile(relativePath, fullPath, fileInfo.Length, options.MaxFileBytes);
            List<WorkflowReferenceWarning> warnings = [];
            if (file.Truncated)
            {
                warnings.Add(new WorkflowReferenceWarning(
                    WorkflowReferenceErrorCode.TooLarge,
                    $"Referenced file was truncated to {options.MaxFileBytes.ToString(CultureInfo.InvariantCulture)} bytes.",
                    relativePath));
            }

            return new WorkflowReferenceEntry(
                token.Kind,
                token.SourceToken,
                token.RequestedPath,
                relativePath,
                warnings.Count == 0 ? DiagnosticEventStatus.Success : DiagnosticEventStatus.Warning,
                [file],
                warnings);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or PathTooLongException)
        {
            return Failure(token, WorkflowReferenceErrorCode.Unreadable, "Referenced file could not be read.", relativePath);
        }
    }

    private WorkflowReferenceEntry ResolveFolder(WorkspaceContext workspace, WorkflowReferenceToken token)
    {
        WorkspaceGuardResult guardResult = workspaceGuard.ResolvePath(workspace, token.RequestedPath);
        if (!guardResult.IsAllowed || string.IsNullOrWhiteSpace(guardResult.FullPath))
        {
            return Failure(
                token,
                WorkflowReferenceErrorCode.BoundaryDenied,
                guardResult.SafeMessage,
                guardResult.FullPath);
        }

        string fullPath = guardResult.FullPath!;
        string relativePath = ToWorkspaceRelativePath(workspace, fullPath);
        if (!Directory.Exists(fullPath))
        {
            return Failure(token, WorkflowReferenceErrorCode.NotFolder, "Referenced folder was not found.", relativePath);
        }

        List<WorkflowReferenceFile> files = [];
        List<WorkflowReferenceWarning> warnings = [];
        long aggregateBytes = 0;
        foreach (string filePath in EnumerateFolderFiles(fullPath, 0, warnings, workspace))
        {
            if (files.Count >= options.MaxFolderFiles)
            {
                warnings.Add(new WorkflowReferenceWarning(
                    WorkflowReferenceErrorCode.TooLarge,
                    $"Folder reference reached the {options.MaxFolderFiles.ToString(CultureInfo.InvariantCulture)} file limit.",
                    relativePath));
                break;
            }

            string fileRelativePath = ToWorkspaceRelativePath(workspace, filePath);
            FileInfo fileInfo;
            try
            {
                fileInfo = new FileInfo(filePath);
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or PathTooLongException)
            {
                warnings.Add(new WorkflowReferenceWarning(
                    WorkflowReferenceErrorCode.Unreadable,
                    "Folder child file could not be inspected.",
                    fileRelativePath));
                continue;
            }

            if (aggregateBytes >= options.MaxFolderBytes)
            {
                warnings.Add(new WorkflowReferenceWarning(
                    WorkflowReferenceErrorCode.TooLarge,
                    $"Folder reference reached the {options.MaxFolderBytes.ToString(CultureInfo.InvariantCulture)} byte limit.",
                    relativePath));
                break;
            }

            try
            {
                if (TextFileUtilities.IsLikelyBinary(filePath))
                {
                    warnings.Add(new WorkflowReferenceWarning(
                        WorkflowReferenceErrorCode.BinaryNotSupported,
                        "Folder child file is binary and was skipped.",
                        fileRelativePath));
                    continue;
                }

                int remainingBytes = (int)Math.Min(options.MaxFileBytes, options.MaxFolderBytes - aggregateBytes);
                WorkflowReferenceFile file = ReadTextFile(fileRelativePath, filePath, fileInfo.Length, remainingBytes);
                files.Add(file);
                aggregateBytes += file.ByteCount;
                if (file.Truncated)
                {
                    warnings.Add(new WorkflowReferenceWarning(
                        WorkflowReferenceErrorCode.TooLarge,
                        "Folder child file was truncated by reference bounds.",
                        fileRelativePath));
                }
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or PathTooLongException)
            {
                warnings.Add(new WorkflowReferenceWarning(
                    WorkflowReferenceErrorCode.Unreadable,
                    "Folder child file could not be read.",
                    fileRelativePath));
            }
        }

        string status = warnings.Count == 0
            ? DiagnosticEventStatus.Success
            : DiagnosticEventStatus.Warning;
        return new WorkflowReferenceEntry(
            token.Kind,
            token.SourceToken,
            token.RequestedPath,
            relativePath,
            status,
            files,
            warnings);
    }

    private IEnumerable<string> EnumerateFolderFiles(
        string folderPath,
        int depth,
        List<WorkflowReferenceWarning> warnings,
        WorkspaceContext workspace)
    {
        if (depth > options.MaxFolderDepth)
        {
            warnings.Add(new WorkflowReferenceWarning(
                WorkflowReferenceErrorCode.TooLarge,
                $"Folder recursion exceeded depth {options.MaxFolderDepth.ToString(CultureInfo.InvariantCulture)}.",
                ToWorkspaceRelativePath(workspace, folderPath)));
            yield break;
        }

        IEnumerable<string> files;
        IEnumerable<string> directories;
        try
        {
            files = Directory.EnumerateFiles(folderPath).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            directories = Directory.EnumerateDirectories(folderPath).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or PathTooLongException)
        {
            warnings.Add(new WorkflowReferenceWarning(
                WorkflowReferenceErrorCode.Unreadable,
                "Referenced folder could not be enumerated.",
                ToWorkspaceRelativePath(workspace, folderPath)));
            yield break;
        }

        foreach (string filePath in files)
        {
            yield return filePath;
        }

        foreach (string directoryPath in directories)
        {
            string name = Path.GetFileName(directoryPath);
            if (options.IgnoredDirectoryNames.Contains(name))
            {
                warnings.Add(new WorkflowReferenceWarning(
                    "workflow-reference-folder-ignored",
                    "Folder child directory was ignored by default.",
                    ToWorkspaceRelativePath(workspace, directoryPath)));
                continue;
            }

            foreach (string filePath in EnumerateFolderFiles(directoryPath, depth + 1, warnings, workspace))
            {
                yield return filePath;
            }
        }
    }

    private static WorkflowReferenceEntry Failure(
        WorkflowReferenceToken token,
        string errorCode,
        string message,
        string? resolvedPath = null)
    {
        return new WorkflowReferenceEntry(
            token.Kind,
            token.SourceToken,
            token.RequestedPath,
            resolvedPath,
            DiagnosticEventStatus.Failure,
            Files: [],
            Warnings:
            [
                new WorkflowReferenceWarning(errorCode, message, resolvedPath ?? token.RequestedPath)
            ],
            ErrorCode: errorCode);
    }

    private static WorkflowReferenceFile ReadTextFile(
        string relativePath,
        string fullPath,
        long fileLength,
        int maxBytes)
    {
        int byteLimit = Math.Max(0, maxBytes);
        byte[] buffer = new byte[Math.Min(byteLimit, (int)Math.Min(fileLength, int.MaxValue))];
        using FileStream stream = File.OpenRead(fullPath);
        int read = buffer.Length == 0 ? 0 : stream.Read(buffer, 0, buffer.Length);
        string text = Encoding.UTF8.GetString(buffer, 0, read);
        bool truncated = fileLength > byteLimit;
        return new WorkflowReferenceFile(
            relativePath,
            read,
            text.Length,
            truncated,
            text);
    }

    private static string ToWorkspaceRelativePath(WorkspaceContext workspace, string path)
    {
        string relativePath = Path.GetRelativePath(workspace.RootPath, path);
        return relativePath == "."
            ? "."
            : relativePath.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }
}

public static class WorkflowReferenceEventFactory
{
    public static AgentRunEvent Create(
        WorkflowReferenceResolution? references,
        long sequence,
        DateTimeOffset timestampUtc)
    {
        references ??= WorkflowReferenceResolution.Empty;
        Dictionary<string, string> payload = new(StringComparer.Ordinal)
        {
            ["referenceCount"] = references.References.Count.ToString(CultureInfo.InvariantCulture),
            ["includedFileCount"] = references.IncludedFileCount.ToString(CultureInfo.InvariantCulture),
            ["skippedFileCount"] = references.SkippedFileCount.ToString(CultureInfo.InvariantCulture),
            ["byteCount"] = references.ByteCount.ToString(CultureInfo.InvariantCulture),
            ["truncated"] = references.Truncated ? "true" : "false"
        };

        if (references.References.Count > 0)
        {
            payload["paths"] = string.Join(
                ";",
                references.References.Select(reference => reference.ResolvedPath ?? reference.RequestedPath));
            payload["kinds"] = string.Join(";", references.References.Select(reference => reference.Kind));
        }

        IReadOnlyList<WorkflowReferenceWarning> warnings = references.References
            .SelectMany(reference => reference.Warnings)
            .ToArray();
        if (warnings.Count > 0)
        {
            payload["warningCount"] = warnings.Count.ToString(CultureInfo.InvariantCulture);
            payload["warnings"] = string.Join(" | ", warnings.Select(warning => warning.ErrorCode + ":" + warning.Path));
        }

        string status = references.HasErrors
            ? DiagnosticEventStatus.Failure
            : references.HasWarnings || references.Truncated
                ? DiagnosticEventStatus.Warning
                : DiagnosticEventStatus.Success;

        return new AgentRunEvent(
            Type: "context.references",
            Sequence: sequence,
            Timestamp: timestampUtc,
            Message: "Collected bounded workflow references.",
            Summary: CreateSummary(references),
            Payload: payload,
            ErrorCode: references.FirstErrorCode,
            Status: status,
            StopReason: references.HasErrors ? AgentStopReason.ToolFailure : null);
    }

    private static string CreateSummary(WorkflowReferenceResolution references)
    {
        if (!references.HasReferences)
        {
            return "No workflow references provided.";
        }

        string paths = string.Join(
            ",",
            references.References.Select(reference => reference.ResolvedPath ?? reference.RequestedPath));
        string suffix = references.Truncated ? " truncated=true" : string.Empty;
        return $"references={references.References.Count.ToString(CultureInfo.InvariantCulture)} files={references.IncludedFileCount.ToString(CultureInfo.InvariantCulture)} skipped={references.SkippedFileCount.ToString(CultureInfo.InvariantCulture)} paths={paths}{suffix}";
    }
}
