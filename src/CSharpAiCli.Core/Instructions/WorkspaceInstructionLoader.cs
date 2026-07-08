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

        return LoadFromDirectories([workspace.RootPath]);
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

        string workspaceRoot = NormalizeDirectoryPath(workspace.RootPath);
        string normalizedTargetPath = Path.GetFullPath(targetPath, workspaceRoot);

        if (!IsInsideOrEqual(normalizedTargetPath, workspaceRoot))
        {
            return InstructionLoadResult.Empty(
            [
                $"ignored instruction target outside the workspace: {normalizedTargetPath}"
            ]);
        }

        string targetDirectory = File.Exists(normalizedTargetPath)
            ? Path.GetDirectoryName(normalizedTargetPath) ?? workspaceRoot
            : normalizedTargetPath;

        targetDirectory = NormalizeDirectoryPath(targetDirectory);
        return LoadFromDirectories(GetDirectoriesRootToLeaf(workspaceRoot, targetDirectory));
    }

    private InstructionLoadResult LoadFromDirectories(IReadOnlyList<string> directories)
    {
        List<string> loadedInstructions = [];
        List<string> warnings = [];
        string? sourcePath = null;

        foreach (string directory in directories)
        {
            string? instructionPath = FindInstructionPath(directory);
            if (instructionPath is null)
            {
                continue;
            }

            string? instructions = LoadInstructionFile(instructionPath, warnings);
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

    private string? LoadInstructionFile(string instructionPath, List<string> warnings)
    {
        FileInfo fileInfo = new(instructionPath);
        if (fileInfo.Length > maxInstructionBytes)
        {
            warnings.Add($"ignored instruction file over {maxInstructionBytes} bytes: {instructionPath}");
            return null;
        }

        string instructions = File.ReadAllText(instructionPath).Trim();
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
}
