namespace CSharpAiCli.Core;

public sealed record ArtifactRetentionConfiguration(
    int DefaultMinimumAgeDays,
    string Source)
{
    public const int MinimumDays = 1;
    public const int MaximumDays = 3650;
    public const int DefaultDays = 30;

    public static ArtifactRetentionConfiguration Default { get; } = new(DefaultDays, "default");
}

public sealed class ArtifactRetentionConfig
{
    public int? DefaultMinimumAgeDays { get; init; }
}
