namespace CSharpAiCli.Core;

public sealed record InstructionLoadResult(
    string? Instructions,
    string? SourcePath,
    IReadOnlyList<string> Warnings)
{
    public bool HasInstructions => !string.IsNullOrWhiteSpace(Instructions);

    public static InstructionLoadResult Empty(IReadOnlyList<string>? warnings = null)
    {
        return new InstructionLoadResult(
            Instructions: null,
            SourcePath: null,
            Warnings: warnings ?? []);
    }

    public static InstructionLoadResult Loaded(
        string instructions,
        string sourcePath,
        IReadOnlyList<string>? warnings = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instructions);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        return new InstructionLoadResult(
            Instructions: instructions,
            SourcePath: sourcePath,
            Warnings: warnings ?? []);
    }
}
