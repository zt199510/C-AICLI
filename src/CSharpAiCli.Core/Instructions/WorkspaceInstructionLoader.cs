namespace CSharpAiCli.Core;

public sealed class WorkspaceInstructionLoader : IInstructionLoader
{
    public const string DefaultInstructionFileName = "AICLI.md";
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

        string instructionPath = Path.Combine(workspace.RootPath, DefaultInstructionFileName);
        if (!File.Exists(instructionPath))
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
}
