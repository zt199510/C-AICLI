namespace CSharpAiCli.Core;

public sealed class WorkspaceInstructionLoader : IInstructionLoader
{
    public const string PrimaryInstructionFileName = "AGENTS.md";
    public const string DefaultInstructionFileName = "AICLI.md";
    public const string LegacyInstructionFileName = DefaultInstructionFileName;
    public const long DefaultMaxInstructionBytes = 64 * 1024;

    private static readonly char[] DirectorySeparators =
    [
        Path.DirectorySeparatorChar,
        Path.AltDirectorySeparatorChar
    ];

    private static readonly StringComparison PathStringComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private readonly long maxInstructionBytes;

    public WorkspaceInstructionLoader(long maxInstructionBytes = DefaultMaxInstructionBytes)
    {
        if (maxInstructionBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxInstructionBytes), "Instruction size limit must be positive.");
        }

        this.maxInstructionBytes = maxInstructionBytes;
    }

    public InstructionLoadResult Load(WorkspaceContext workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        if (!workspace.IsUsable)
        {
            return InstructionLoadResult.Empty();
        }

        string workspaceRoot;
        try
        {
            workspaceRoot = ResolveExistingPath(workspace.RootPath);
        }
        catch (Exception exception) when (IsPathResolutionException(exception))
        {
            return InstructionLoadResult.Empty(
            [
                "ignored instruction target that could not be resolved safely."
            ]);
        }

        return LoadFromDirectories(workspaceRoot, [workspaceRoot]);
    }

    public InstructionLoadResult Load(WorkspaceContext workspace, string? targetPath)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return Load(workspace);
        }

        if (!workspace.IsUsable)
        {
            return InstructionLoadResult.Empty();
        }

        string workspaceRoot;
        string requestedFullPath;
        string resolvedTargetPath;
        try
        {
            workspaceRoot = ResolveExistingPath(workspace.RootPath);
            requestedFullPath = Path.GetFullPath(targetPath, workspaceRoot);
            resolvedTargetPath = ResolveExistingPath(requestedFullPath);
        }
        catch (Exception exception) when (IsPathResolutionException(exception))
        {
            return InstructionLoadResult.Empty(
            [
                "ignored instruction target that could not be resolved safely."
            ]);
        }

        if (!IsInsideOrEqual(resolvedTargetPath, workspaceRoot))
        {
            return InstructionLoadResult.Empty(
            [
                $"ignored instruction target outside the workspace: {requestedFullPath}"
            ]);
        }

        string targetDirectory = File.Exists(resolvedTargetPath)
            ? Path.GetDirectoryName(resolvedTargetPath) ?? workspaceRoot
            : resolvedTargetPath;

        targetDirectory = NormalizeDirectoryPath(targetDirectory);
        return LoadFromDirectories(workspaceRoot, GetDirectoriesRootToLeaf(workspaceRoot, targetDirectory));
    }

    private InstructionLoadResult LoadFromDirectories(string workspaceRoot, IReadOnlyList<string> directories)
    {
        List<string> loadedInstructions = [];
        List<string> warnings = [];
        string? sourcePath = null;

        foreach (string directory in directories)
        {
            string resolvedDirectory;
            try
            {
                resolvedDirectory = ResolveExistingPath(directory);
            }
            catch (Exception exception) when (IsPathResolutionException(exception))
            {
                return InstructionLoadResult.Empty(
                [
                    $"ignored instruction directory that could not be resolved safely: {directory}"
                ]);
            }

            if (!IsInsideOrEqual(resolvedDirectory, workspaceRoot))
            {
                return InstructionLoadResult.Empty(
                [
                    $"ignored instruction directory outside the workspace: {directory}"
                ]);
            }

            string? instructionPath = FindInstructionPath(resolvedDirectory);
            if (instructionPath is null)
            {
                continue;
            }

            string? instructions = LoadInstructionFile(workspaceRoot, instructionPath, warnings);
            if (instructions is null)
            {
                continue;
            }

            sourcePath ??= instructionPath;
            loadedInstructions.Add(instructions);
        }

        if (loadedInstructions.Count == 0)
        {
            return InstructionLoadResult.Empty(warnings);
        }

        return InstructionLoadResult.Loaded(
            string.Join($"{Environment.NewLine}{Environment.NewLine}", loadedInstructions),
            sourcePath!,
            warnings);
    }

    private string? LoadInstructionFile(string workspaceRoot, string instructionPath, List<string> warnings)
    {
        string resolvedInstructionPath;
        try
        {
            resolvedInstructionPath = ResolveExistingPath(instructionPath);
        }
        catch (Exception exception) when (IsPathResolutionException(exception))
        {
            warnings.Add($"ignored instruction file that could not be resolved safely: {instructionPath}");
            return null;
        }

        if (!IsInsideOrEqual(resolvedInstructionPath, workspaceRoot))
        {
            warnings.Add($"ignored instruction file outside the workspace: {instructionPath}");
            return null;
        }

        FileInfo fileInfo = new(resolvedInstructionPath);
        if (fileInfo.Length > maxInstructionBytes)
        {
            warnings.Add($"ignored instruction file over {maxInstructionBytes} bytes: {instructionPath}");
            return null;
        }

        string instructions = File.ReadAllText(resolvedInstructionPath).Trim();
        if (string.IsNullOrWhiteSpace(instructions))
        {
            return null;
        }

        return instructions;
    }

    private static string? FindInstructionPath(string rootPath)
    {
        string primaryInstructionPath = Path.Combine(rootPath, PrimaryInstructionFileName);
        if (File.Exists(primaryInstructionPath))
        {
            return primaryInstructionPath;
        }

        string legacyInstructionPath = Path.Combine(rootPath, LegacyInstructionFileName);
        return File.Exists(legacyInstructionPath)
            ? legacyInstructionPath
            : null;
    }

    private static IReadOnlyList<string> GetDirectoriesRootToLeaf(string rootPath, string targetDirectory)
    {
        List<string> directories = [rootPath];
        string relativePath = Path.GetRelativePath(rootPath, targetDirectory);
        if (string.IsNullOrEmpty(relativePath) || relativePath == ".")
        {
            return directories;
        }

        string currentDirectory = rootPath;
        foreach (string segment in relativePath.Split(DirectorySeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            currentDirectory = Path.Combine(currentDirectory, segment);
            directories.Add(currentDirectory);
        }

        return directories;
    }

    private static bool IsInsideOrEqual(string path, string rootPath)
    {
        string normalizedPath = NormalizeDirectoryPath(path);
        string normalizedRootPath = NormalizeDirectoryPath(rootPath);

        if (string.Equals(normalizedPath, normalizedRootPath, PathStringComparison))
        {
            return true;
        }

        string rootWithSeparator = Path.EndsInDirectorySeparator(normalizedRootPath)
            ? normalizedRootPath
            : normalizedRootPath + Path.DirectorySeparatorChar;

        return normalizedPath.StartsWith(rootWithSeparator, PathStringComparison);
    }

    private static string NormalizeDirectoryPath(string path)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static string ResolveExistingPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string? pathRoot = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(pathRoot))
        {
            return NormalizeDirectoryPath(fullPath);
        }

        string relativePath = Path.GetRelativePath(pathRoot, fullPath);
        if (relativePath == ".")
        {
            return NormalizeDirectoryPath(fullPath);
        }

        string current = pathRoot;
        foreach (string segment in relativePath.Split(DirectorySeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            current = ResolveLinkTarget(current);
        }

        return NormalizeDirectoryPath(current);
    }

    private static string ResolveLinkTarget(string path)
    {
        if (Directory.Exists(path))
        {
            DirectoryInfo directoryInfo = new(path);
            if (directoryInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                FileSystemInfo? target = directoryInfo.ResolveLinkTarget(returnFinalTarget: true);
                if (target is not null)
                {
                    return target.FullName;
                }
            }
        }
        else if (File.Exists(path))
        {
            FileInfo fileInfo = new(path);
            if (fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                FileSystemInfo? target = fileInfo.ResolveLinkTarget(returnFinalTarget: true);
                if (target is not null)
                {
                    return target.FullName;
                }
            }
        }

        return path;
    }

    private static bool IsPathResolutionException(Exception exception)
    {
        return exception is ArgumentException
            or IOException
            or NotSupportedException
            or PathTooLongException
            or UnauthorizedAccessException;
    }
}
