namespace CSharpAiCli.Core;

public sealed record InstructionLoadResult(
    string? Instructions,
    string? SourcePath,
    IReadOnlyList<string> Warnings)
{
    public IReadOnlyList<InstructionSource> Sources { get; init; } = [];

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

        return Loaded(
            instructions,
            [new InstructionSource(sourcePath, 0)],
            warnings);
    }

    public static InstructionLoadResult Loaded(
        string instructions,
        IReadOnlyList<InstructionSource> sources,
        IReadOnlyList<string>? warnings = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instructions);
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 0)
        {
            throw new ArgumentException("At least one instruction source is required.", nameof(sources));
        }

        return new InstructionLoadResult(
            Instructions: instructions,
            SourcePath: sources[0].SourcePath,
            Warnings: warnings ?? [])
        {
            Sources = sources.ToArray()
        };
    }
}
