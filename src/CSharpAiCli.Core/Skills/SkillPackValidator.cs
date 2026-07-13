using System.Text.RegularExpressions;

namespace CSharpAiCli.Core;

public sealed class SkillPackValidator
{
    private static readonly Regex NamePattern = new(
        "^[a-z][a-z0-9-]{0,63}$",
        RegexOptions.CultureInvariant);

    private static readonly Regex VersionPattern = new(
        @"^(?<major>0|[1-9][0-9]*)\.(?<minor>0|[1-9][0-9]*)\.(?<patch>0|[1-9][0-9]*)(?:[-+][0-9A-Za-z.-]+)?$",
        RegexOptions.CultureInvariant);

    private readonly WorkflowReferenceParser referenceParser;

    public SkillPackValidator()
        : this(new WorkflowReferenceParser())
    {
    }

    internal SkillPackValidator(WorkflowReferenceParser referenceParser)
    {
        ArgumentNullException.ThrowIfNull(referenceParser);
        this.referenceParser = referenceParser;
    }

    public SkillValidationResult Validate(SkillPackManifest? manifest, SkillPackSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        List<SkillPackDiagnostic> diagnostics = [];
        if (manifest is null)
        {
            diagnostics.Add(new SkillPackDiagnostic(
                SkillPackErrorCode.ManifestInvalid,
                "Skill pack manifest could not be read.",
                source.Path));
            return new SkillValidationResult(diagnostics);
        }

        ValidateRequired(manifest.Name, "name", source, manifest, diagnostics);
        ValidateRequired(manifest.Version, "version", source, manifest, diagnostics);
        ValidateRequired(manifest.Description, "description", source, manifest, diagnostics);
        if (manifest.Entry is null)
        {
            diagnostics.Add(CreateInvalid("entry is required.", source, manifest));
        }

        if (manifest.Safety is null)
        {
            diagnostics.Add(new SkillPackDiagnostic(
                SkillPackErrorCode.SafetyPolicyInvalid,
                "safety is required.",
                source.Path,
                manifest.Name));
        }

        if (!string.IsNullOrWhiteSpace(manifest.Name) &&
            !NamePattern.IsMatch(manifest.Name))
        {
            diagnostics.Add(CreateInvalid("name must use lowercase letters, digits, and hyphens.", source, manifest));
        }

        ValidateVersion(manifest, source, diagnostics);
        ValidateEntry(manifest, source, diagnostics);
        ValidateReferences(manifest, source, diagnostics);
        ValidateSafety(manifest, source, diagnostics);

        return new SkillValidationResult(diagnostics);
    }

    private static void ValidateRequired(
        string? value,
        string fieldName,
        SkillPackSource source,
        SkillPackManifest manifest,
        List<SkillPackDiagnostic> diagnostics)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        diagnostics.Add(CreateInvalid(fieldName + " is required.", source, manifest));
    }

    private static void ValidateVersion(
        SkillPackManifest manifest,
        SkillPackSource source,
        List<SkillPackDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(manifest.Version))
        {
            return;
        }

        Match match = VersionPattern.Match(manifest.Version);
        if (!match.Success)
        {
            diagnostics.Add(CreateInvalid("version must be a semantic version.", source, manifest));
            return;
        }

        if (!string.Equals(match.Groups["major"].Value, "0", StringComparison.Ordinal))
        {
            diagnostics.Add(new SkillPackDiagnostic(
                SkillPackErrorCode.VersionUnsupported,
                "Only 0.x skill pack manifests are supported in this release.",
                source.Path,
                manifest.Name));
        }
    }

    private static void ValidateEntry(
        SkillPackManifest manifest,
        SkillPackSource source,
        List<SkillPackDiagnostic> diagnostics)
    {
        SkillPackEntry? entry = manifest.Entry;
        if (entry is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(entry.Mode))
        {
            diagnostics.Add(CreateInvalid("entry.mode is required.", source, manifest));
        }
        else if (!string.Equals(entry.Mode, "exec", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(CreateInvalid("entry.mode must be exec.", source, manifest));
        }

        if (string.IsNullOrWhiteSpace(entry.Expert))
        {
            diagnostics.Add(CreateInvalid("entry.expert is required.", source, manifest));
        }
        else if (!ExpertProfileCatalog.TryGet(entry.Expert, out _))
        {
            diagnostics.Add(CreateInvalid("entry.expert must name an existing expert profile.", source, manifest));
        }

        if (string.IsNullOrWhiteSpace(entry.Report))
        {
            diagnostics.Add(CreateInvalid("entry.report is required.", source, manifest));
        }
        else if (!ExecReportModeParser.TryParse(entry.Report, out _))
        {
            diagnostics.Add(CreateInvalid("entry.report must be none or markdown.", source, manifest));
        }
    }

    private void ValidateReferences(
        SkillPackManifest manifest,
        SkillPackSource source,
        List<SkillPackDiagnostic> diagnostics)
    {
        SkillPackReferences? references = manifest.References;
        if (references is null)
        {
            return;
        }

        if (references.MaxFiles is <= 0)
        {
            diagnostics.Add(CreateInvalid("references.maxFiles must be greater than zero.", source, manifest));
        }

        foreach (string reference in references.Suggested ?? [])
        {
            if (!IsValidReference(reference))
            {
                diagnostics.Add(CreateInvalid("references.suggested entries must be @file: or @folder: references.", source, manifest));
            }
        }
    }

    private void ValidateSafety(
        SkillPackManifest manifest,
        SkillPackSource source,
        List<SkillPackDiagnostic> diagnostics)
    {
        SkillPackSafety? safety = manifest.Safety;
        if (safety is null)
        {
            return;
        }

        if (safety.AllowMcp)
        {
            diagnostics.Add(new SkillPackDiagnostic(
                SkillPackErrorCode.SafetyPolicyInvalid,
                "safety.allowMcp must be false in this local-only release.",
                source.Path,
                manifest.Name));
        }

        string shellMode = safety.ShellMode;
        if (!string.Equals(shellMode, "none", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(shellMode, "verification-only", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(new SkillPackDiagnostic(
                SkillPackErrorCode.SafetyPolicyInvalid,
                "safety.allowShell must be none or verification-only.",
                source.Path,
                manifest.Name));
        }

        if (!safety.AllowWrites &&
            !string.Equals(shellMode, "none", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(new SkillPackDiagnostic(
                SkillPackErrorCode.SafetyPolicyInvalid,
                "read-only skill packs must set safety.allowShell to none.",
                source.Path,
                manifest.Name));
        }
    }

    private bool IsValidReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return false;
        }

        IReadOnlyList<WorkflowReferenceToken> tokens = referenceParser.Parse(reference);
        return tokens.Count == 1 &&
            string.Equals(tokens[0].SourceToken, reference.Trim(), StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(tokens[0].RequestedPath);
    }

    private static SkillPackDiagnostic CreateInvalid(
        string summary,
        SkillPackSource source,
        SkillPackManifest manifest)
    {
        return new SkillPackDiagnostic(
            SkillPackErrorCode.ManifestInvalid,
            summary,
            source.Path,
            manifest.Name);
    }
}
