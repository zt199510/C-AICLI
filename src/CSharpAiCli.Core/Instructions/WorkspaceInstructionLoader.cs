namespace CSharpAiCli.Core;

public sealed class WorkspaceInstructionLoader : IInstructionLoader
{
    public const string PrimaryInstructionFileName = "AGENTS.md";
    public const string DefaultInstructionFileName = "AICLI.md";
    public const string LegacyInstructionFileName = DefaultInstructionFileName;
    public const long DefaultMaxInstructionBytes = 64 * 1024;

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

        string? instructionPath = FindInstructionPath(workspace.RootPath);
        if (instructionPath is null)
        {
            return InstructionLoadResult.Empty();
        }

        FileInfo fileInfo = new(instructionPath);
        if (fileInfo.Length > maxInstructionBytes)
        {
            return InstructionLoadResult.Empty(
            [
                $"ignored instruction file over {maxInstructionBytes} bytes: {instructionPath}"
            ]);
        }

        string instructions = File.ReadAllText(instructionPath).Trim();
        if (string.IsNullOrWhiteSpace(instructions))
        {
            return InstructionLoadResult.Empty();
        }

        return InstructionLoadResult.Loaded(instructions, instructionPath);
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
}
