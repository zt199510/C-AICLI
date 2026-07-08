namespace CSharpAiCli.Core;

public sealed record InstructionLoadResult
{
    private static readonly IReadOnlyList<InstructionSource> EmptySources =
        Array.AsReadOnly(Array.Empty<InstructionSource>());
    private static readonly IReadOnlyList<string> EmptyWarnings =
        Array.AsReadOnly(Array.Empty<string>());

    private IReadOnlyList<InstructionSource> sources = EmptySources;
    private IReadOnlyList<string> warnings = EmptyWarnings;

    public InstructionLoadResult(
        string? Instructions,
        string? SourcePath,
        IReadOnlyList<string> Warnings)
    {
        this.Instructions = Instructions;
        this.Warnings = Warnings;
        sources = CreateCompatibleSources(SourcePath);
    }

    public string? Instructions { get; init; }

    public string? SourcePath => Sources.Count == 0
        ? null
        : Sources[0].SourcePath;

    public IReadOnlyList<string> Warnings
    {
        get => warnings;
        init => warnings = NormalizeWarnings(value);
    }

    public IReadOnlyList<InstructionSource> Sources
    {
        get => sources;
        init => sources = NormalizeSources(value, SourcePath);
    }

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
        IReadOnlyList<InstructionSource> normalizedSources = NormalizeSources(sources, sourcePath: null);
        if (normalizedSources.Count == 0)
        {
            throw new ArgumentException("At least one instruction source is required.", nameof(sources));
        }

        return new InstructionLoadResult(
            Instructions: instructions,
            SourcePath: normalizedSources[0].SourcePath,
            Warnings: warnings ?? [])
        {
            Sources = normalizedSources
        };
    }

    private static IReadOnlyList<InstructionSource> NormalizeSources(
        IReadOnlyList<InstructionSource> sources,
        string? sourcePath)
    {
        ArgumentNullException.ThrowIfNull(sources);

        if (sources.Count == 0)
        {
            return CreateCompatibleSources(sourcePath);
        }

        InstructionSource[] normalizedSources = new InstructionSource[sources.Count];
        for (int index = 0; index < sources.Count; index++)
        {
            InstructionSource source = sources[index]
                ?? throw new ArgumentException("Instruction sources cannot contain null values.", nameof(sources));
            normalizedSources[index] = new InstructionSource(source.SourcePath, index);
        }

        return Array.AsReadOnly(normalizedSources);
    }

    private static IReadOnlyList<InstructionSource> CreateCompatibleSources(string? sourcePath)
    {
        return string.IsNullOrWhiteSpace(sourcePath)
            ? EmptySources
            : Array.AsReadOnly([new InstructionSource(sourcePath, 0)]);
    }

    private static IReadOnlyList<string> NormalizeWarnings(IReadOnlyList<string>? warnings)
    {
        if (warnings is null || warnings.Count == 0)
        {
            return EmptyWarnings;
        }

        string[] normalizedWarnings = new string[warnings.Count];
        for (int index = 0; index < warnings.Count; index++)
        {
            normalizedWarnings[index] = warnings[index];
        }

        return Array.AsReadOnly(normalizedWarnings);
    }
}
