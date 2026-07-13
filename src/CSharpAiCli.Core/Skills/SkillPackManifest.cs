using System.Collections.ObjectModel;

namespace CSharpAiCli.Core;

public static class SkillPackErrorCode
{
    public const string NotFound = "skill-not-found";
    public const string ManifestInvalid = "skill-manifest-invalid";
    public const string VersionUnsupported = "skill-version-unsupported";
    public const string SafetyPolicyInvalid = "skill-safety-policy-invalid";
    public const string RunPlanInvalid = "skill-run-plan-invalid";
    public const string LocalSourceDenied = "skill-local-source-denied";
}

public sealed record SkillPackManifest
{
    public string? Name { get; init; }

    public string? Version { get; init; }

    public string? Description { get; init; }

    public SkillPackAppliesTo? AppliesTo { get; init; }

    public SkillPackEntry? Entry { get; init; }

    public SkillPackReferences? References { get; init; }

    public IReadOnlyList<string>? Instructions { get; init; }

    public string? ValidationCommand { get; init; }

    public string? ReportTemplate { get; init; }

    public SkillPackSafety? Safety { get; init; }
}

public sealed record SkillPackAppliesTo
{
    public IReadOnlyList<string>? ProjectTypes { get; init; }

    public IReadOnlyList<string>? RequiredFiles { get; init; }
}

public sealed record SkillPackEntry
{
    public string? Mode { get; init; }

    public string? Expert { get; init; }

    public string? Report { get; init; }
}

public sealed record SkillPackReferences
{
    public IReadOnlyList<string>? Suggested { get; init; }

    public int? MaxFiles { get; init; }
}

public sealed record SkillPackSafety
{
    public bool AllowWrites { get; init; }

    public string? AllowShell { get; init; }

    public bool AllowMcp { get; init; }

    public string ShellMode => string.IsNullOrWhiteSpace(AllowShell)
        ? "none"
        : AllowShell.Trim();

    public string ToSummary()
    {
        return string.Join(
            " ",
            [
                "allowWrites=" + (AllowWrites ? "true" : "false"),
                "allowShell=" + ShellMode,
                "allowMcp=" + (AllowMcp ? "true" : "false")
            ]);
    }
}

public sealed record SkillPackSource
{
    public SkillPackSource(string Kind, string? Path = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Kind);

        this.Kind = Kind;
        this.Path = Path;
    }

    public string Kind { get; }

    public string? Path { get; }

    public static SkillPackSource BuiltIn { get; } = new("built-in");

    public static SkillPackSource Local(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new SkillPackSource("local", path);
    }
}

public sealed record SkillPackCatalogItem
{
    public SkillPackCatalogItem(SkillPackManifest Manifest, SkillPackSource Source)
    {
        ArgumentNullException.ThrowIfNull(Manifest);
        ArgumentNullException.ThrowIfNull(Source);

        this.Manifest = Manifest;
        this.Source = Source;
    }

    public SkillPackManifest Manifest { get; }

    public SkillPackSource Source { get; }
}

public sealed record SkillPackDiagnostic
{
    public SkillPackDiagnostic(
        string ErrorCode,
        string Summary,
        string? SourcePath = null,
        string? SkillName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ErrorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(Summary);

        this.ErrorCode = ErrorCode;
        this.Summary = Summary;
        this.SourcePath = SourcePath;
        this.SkillName = SkillName;
    }

    public string ErrorCode { get; }

    public string Summary { get; }

    public string? SourcePath { get; }

    public string? SkillName { get; }
}

public sealed record SkillValidationResult
{
    public SkillValidationResult(IReadOnlyList<SkillPackDiagnostic>? Diagnostics = null)
    {
        this.Diagnostics = new ReadOnlyCollection<SkillPackDiagnostic>((Diagnostics ?? []).ToArray());
    }

    public IReadOnlyList<SkillPackDiagnostic> Diagnostics { get; }

    public bool Succeeded => Diagnostics.Count == 0;
}
