using System.Collections.ObjectModel;
using System.Text.Json;

namespace CSharpAiCli.Core;

public sealed class SkillPackCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public SkillPackCatalog(
        IReadOnlyList<SkillPackCatalogItem>? packs = null,
        IReadOnlyList<SkillPackDiagnostic>? diagnostics = null)
    {
        Packs = new ReadOnlyCollection<SkillPackCatalogItem>((packs ?? []).ToArray());
        Diagnostics = new ReadOnlyCollection<SkillPackDiagnostic>((diagnostics ?? []).ToArray());
    }

    public IReadOnlyList<SkillPackCatalogItem> Packs { get; }

    public IReadOnlyList<SkillPackDiagnostic> Diagnostics { get; }

    public static SkillPackCatalog Load(WorkspaceContext workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        SkillPackValidator validator = new();
        List<SkillPackCatalogItem> packs = [];
        List<SkillPackDiagnostic> diagnostics = [];
        Dictionary<string, SkillPackCatalogItem> byName = new(StringComparer.OrdinalIgnoreCase);

        foreach (SkillPackCatalogItem builtIn in SkillPackBuiltIns.Create())
        {
            SkillValidationResult validation = validator.Validate(builtIn.Manifest, builtIn.Source);
            if (!validation.Succeeded)
            {
                diagnostics.AddRange(validation.Diagnostics);
                continue;
            }

            AddPack(builtIn, packs, byName, diagnostics);
        }

        LoadLocalPacks(workspace, validator, packs, byName, diagnostics);
        return new SkillPackCatalog(
            packs.OrderBy(item => item.Manifest.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
            diagnostics);
    }

    public bool TryGet(string? name, out SkillPackCatalogItem? item)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            item = null;
            return false;
        }

        item = Packs.FirstOrDefault(pack =>
            string.Equals(pack.Manifest.Name, name, StringComparison.OrdinalIgnoreCase));
        return item is not null;
    }

    private static void LoadLocalPacks(
        WorkspaceContext workspace,
        SkillPackValidator validator,
        List<SkillPackCatalogItem> packs,
        Dictionary<string, SkillPackCatalogItem> byName,
        List<SkillPackDiagnostic> diagnostics)
    {
        if (workspace.Status != WorkspaceStatus.Ready)
        {
            return;
        }

        WorkspaceGuardResult guardResult = new WorkspaceGuard().ResolvePath(workspace, Path.Combine(".caicli", "skills"));
        if (!guardResult.IsAllowed || string.IsNullOrWhiteSpace(guardResult.FullPath))
        {
            diagnostics.Add(new SkillPackDiagnostic(
                SkillPackErrorCode.LocalSourceDenied,
                guardResult.SafeMessage,
                guardResult.FullPath));
            return;
        }

        string skillsDirectory = guardResult.FullPath!;
        if (!Directory.Exists(skillsDirectory))
        {
            return;
        }

        foreach (string manifestPath in EnumerateLocalManifestFiles(skillsDirectory))
        {
            SkillPackSource source = SkillPackSource.Local(ToWorkspaceRelativePath(workspace, manifestPath));
            SkillPackManifest? manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<SkillPackManifest>(
                    File.ReadAllText(manifestPath),
                    JsonOptions);
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or JsonException
                or NotSupportedException)
            {
                diagnostics.Add(new SkillPackDiagnostic(
                    SkillPackErrorCode.ManifestInvalid,
                    "Local skill pack manifest could not be parsed.",
                    source.Path));
                continue;
            }

            SkillValidationResult validation = validator.Validate(manifest, source);
            if (!validation.Succeeded)
            {
                diagnostics.AddRange(validation.Diagnostics);
                continue;
            }

            AddPack(new SkillPackCatalogItem(manifest!, source), packs, byName, diagnostics);
        }
    }

    private static void AddPack(
        SkillPackCatalogItem item,
        List<SkillPackCatalogItem> packs,
        Dictionary<string, SkillPackCatalogItem> byName,
        List<SkillPackDiagnostic> diagnostics)
    {
        string? name = item.Manifest.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            diagnostics.Add(new SkillPackDiagnostic(
                SkillPackErrorCode.ManifestInvalid,
                "Skill pack name is required.",
                item.Source.Path));
            return;
        }

        if (byName.TryGetValue(name, out SkillPackCatalogItem? existing))
        {
            diagnostics.Add(new SkillPackDiagnostic(
                SkillPackErrorCode.ManifestInvalid,
                $"Skill pack name '{name}' duplicates an existing {existing.Source.Kind} pack.",
                item.Source.Path,
                name));
            return;
        }

        packs.Add(item);
        byName[name] = item;
    }

    private static IReadOnlyList<string> EnumerateLocalManifestFiles(string skillsDirectory)
    {
        List<string> paths = [];
        try
        {
            paths.AddRange(Directory.EnumerateFiles(skillsDirectory, "*.json", SearchOption.TopDirectoryOnly));
            paths.AddRange(Directory.EnumerateFiles(skillsDirectory, "manifest.json", SearchOption.AllDirectories));
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or PathTooLongException)
        {
            return paths;
        }

        return paths
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ToWorkspaceRelativePath(WorkspaceContext workspace, string path)
    {
        string relativePath = Path.GetRelativePath(workspace.RootPath, path);
        return relativePath == "."
            ? "."
            : relativePath.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }
}
