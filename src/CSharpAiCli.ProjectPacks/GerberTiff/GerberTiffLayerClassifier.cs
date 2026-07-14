using System.Collections.ObjectModel;

namespace CSharpAiCli.ProjectPacks.GerberTiff;

public static class GerberTiffInputKind
{
    public const string Gerber = "gerber";
    public const string Drill = "drill";
    public const string Sidecar = "sidecar";
    public const string Unknown = "unknown";
}

public sealed record GerberTiffLayerClassification(
    string RelativePath,
    string Extension,
    string Kind,
    string? LayerRole,
    bool Supported,
    bool PassedToExternalTool,
    IReadOnlyList<ProjectPackDiagnostic> Diagnostics);

public sealed class GerberTiffLayerClassifier
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> FileNameRoleAliases =
        new ReadOnlyDictionary<string, IReadOnlyList<string>>(
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["top-copper"] = ["f-cu", "front-copper", "top-copper", "copper-top", "gtl"],
                ["bottom-copper"] = ["b-cu", "back-copper", "bottom-copper", "copper-bottom", "gbl"],
                ["top-solder-mask"] = ["f-mask", "front-mask", "top-mask", "top-solder-mask", "gts"],
                ["bottom-solder-mask"] = ["b-mask", "back-mask", "bottom-mask", "bottom-solder-mask", "gbs"],
                ["top-silkscreen"] = ["f-silkscreen", "front-silkscreen", "top-silkscreen", "gto"],
                ["bottom-silkscreen"] = ["b-silkscreen", "back-silkscreen", "bottom-silkscreen", "gbo"],
                ["board-outline"] = ["edge-cuts", "edge-cut", "board-outline", "outline", "profile", "gm1"]
            });

    public GerberTiffLayerClassification Classify(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        string normalizedPath = GerberTiffWorkspacePathPolicy.NormalizeRelativePath(relativePath);
        string extension = Path.GetExtension(normalizedPath).ToLowerInvariant();
        if (GerberTiffInputEnvelope.IsDrillExtension(extension))
        {
            return Result(normalizedPath, extension, GerberTiffInputKind.Drill, "drill", true, true, []);
        }

        if (GerberTiffInputEnvelope.IsSidecarExtension(extension))
        {
            return Result(normalizedPath, extension, GerberTiffInputKind.Sidecar, "gerber-job-sidecar", true, false, []);
        }

        if (!GerberTiffInputEnvelope.IsGerberExtension(extension))
        {
            return Result(
                normalizedPath,
                extension,
                GerberTiffInputKind.Unknown,
                null,
                false,
                false,
                [Warning(GerberTiffDiagnosticCode.InputUnknownExtension, "An unknown file was reported and excluded from external tool input.")]);
        }

        string stem = NormalizeStem(Path.GetFileNameWithoutExtension(normalizedPath));
        string[] hintedRoles = FindHintedRoles(stem);
        if (GerberTiffInputEnvelope.LayerRoleByExtension.TryGetValue(extension, out string? extensionRole))
        {
            if (hintedRoles.Any(role => !string.Equals(role, extensionRole, StringComparison.Ordinal)))
            {
                return Result(
                    normalizedPath,
                    extension,
                    GerberTiffInputKind.Gerber,
                    null,
                    true,
                    false,
                    [Error(GerberTiffDiagnosticCode.InputLayerAmbiguous, "A Gerber filename hint conflicts with its specific layer extension.")]);
            }

            return Result(normalizedPath, extension, GerberTiffInputKind.Gerber, extensionRole, true, true, []);
        }

        if (hintedRoles.Length > 1)
        {
            return Result(
                normalizedPath,
                extension,
                GerberTiffInputKind.Gerber,
                null,
                true,
                false,
                [Error(GerberTiffDiagnosticCode.InputLayerAmbiguous, "A generic Gerber filename maps to more than one layer role.")]);
        }

        return Result(
            normalizedPath,
            extension,
            GerberTiffInputKind.Gerber,
            hintedRoles.SingleOrDefault() ?? "generic-gerber",
            true,
            true,
            []);
    }

    public IReadOnlyList<ProjectPackDiagnostic> ValidateMappings(
        IReadOnlyList<GerberTiffLayerClassification> classifications)
    {
        ArgumentNullException.ThrowIfNull(classifications);
        List<ProjectPackDiagnostic> diagnostics = [];
        if (!classifications.Any(classification =>
            classification.Supported &&
            classification.Kind == GerberTiffInputKind.Gerber))
        {
            diagnostics.Add(Error(
                GerberTiffDiagnosticCode.InputGerberRequired,
                "At least one supported Gerber layer is required."));
        }

        IEnumerable<IGrouping<string, GerberTiffLayerClassification>> duplicateRoles = classifications
            .Where(classification =>
                classification.Kind == GerberTiffInputKind.Gerber &&
                classification.LayerRole is not null &&
                classification.LayerRole != "generic-gerber")
            .GroupBy(classification => classification.LayerRole!, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .OrderBy(group => group.Key, StringComparer.Ordinal);
        foreach (IGrouping<string, GerberTiffLayerClassification> _ in duplicateRoles)
        {
            diagnostics.Add(Error(
                GerberTiffDiagnosticCode.InputLayerDuplicate,
                "The input directory contains more than one Gerber file for a specific layer role."));
        }

        return new ReadOnlyCollection<ProjectPackDiagnostic>(diagnostics);
    }

    private static string[] FindHintedRoles(string normalizedStem) =>
        FileNameRoleAliases
            .Where(pair => pair.Value.Any(alias => ContainsAlias(normalizedStem, alias)))
            .Select(pair => pair.Key)
            .OrderBy(role => role, StringComparer.Ordinal)
            .ToArray();

    private static bool ContainsAlias(string stem, string alias)
    {
        string paddedStem = $"-{stem}-";
        string paddedAlias = $"-{alias}-";
        return paddedStem.Contains(paddedAlias, StringComparison.Ordinal);
    }

    private static string NormalizeStem(string stem)
    {
        char[] normalized = stem
            .ToLowerInvariant()
            .Select(character => char.IsAsciiLetterOrDigit(character) ? character : '-')
            .ToArray();
        return string.Join('-', new string(normalized).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }

    private static GerberTiffLayerClassification Result(
        string path,
        string extension,
        string kind,
        string? role,
        bool supported,
        bool passedToExternalTool,
        IEnumerable<ProjectPackDiagnostic> diagnostics) =>
        new(
            path,
            extension,
            kind,
            role,
            supported,
            passedToExternalTool,
            new ReadOnlyCollection<ProjectPackDiagnostic>(diagnostics.ToArray()));

    private static ProjectPackDiagnostic Error(string code, string summary) =>
        new(code, ProjectPackDiagnosticSeverity.Error, summary);

    private static ProjectPackDiagnostic Warning(string code, string summary) =>
        new(code, ProjectPackDiagnosticSeverity.Warning, summary);
}
