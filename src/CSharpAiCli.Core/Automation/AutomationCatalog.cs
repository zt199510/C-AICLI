using System.Collections.ObjectModel;
using System.Text.Json;

namespace CSharpAiCli.Core;

public sealed class AutomationCatalog
{
    private const long MaxManifestBytes = 256 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    public AutomationCatalog(
        IReadOnlyList<AutomationCatalogItem>? Items = null,
        IReadOnlyList<AutomationDiagnostic>? Diagnostics = null)
    {
        this.Items = new ReadOnlyCollection<AutomationCatalogItem>((Items ?? []).ToArray());
        this.Diagnostics = new ReadOnlyCollection<AutomationDiagnostic>((Diagnostics ?? []).ToArray());
    }

    public IReadOnlyList<AutomationCatalogItem> Items { get; }

    public IReadOnlyList<AutomationDiagnostic> Diagnostics { get; }

    public static AutomationCatalog Load(WorkspaceContext workspace) => Load(workspace, null);

    public static AutomationCatalog Load(WorkspaceContext workspace, int? maxItems)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (maxItems is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxItems));
        }

        List<AutomationCatalogItem> items = [];
        List<AutomationDiagnostic> diagnostics = [];
        if (workspace.Status != WorkspaceStatus.Ready)
        {
            return new AutomationCatalog(items, diagnostics);
        }

        WorkspaceGuard guard = new();
        WorkspaceGuardResult directoryResult = guard.ResolvePath(workspace, Path.Combine(".caicli", "automations"));
        if (!directoryResult.IsAllowed || string.IsNullOrWhiteSpace(directoryResult.FullPath))
        {
            diagnostics.Add(new AutomationDiagnostic(
                AutomationErrorCode.SourceDenied,
                directoryResult.SafeMessage,
                directoryResult.FullPath));
            return new AutomationCatalog(items, diagnostics);
        }

        string directory = directoryResult.FullPath;
        if (!Directory.Exists(directory))
        {
            return new AutomationCatalog(items, diagnostics);
        }

        AutomationManifestValidator validator = new(workspace);
        Dictionary<string, AutomationCatalogItem> byName = new(StringComparer.OrdinalIgnoreCase);
        foreach (string path in EnumerateManifestFiles(directory))
        {
            if (items.Count >= maxItems)
            {
                break;
            }

            string relativePath = ToWorkspaceRelativePath(workspace, path);
            AutomationSource source = new(relativePath);
            WorkspaceGuardResult fileResult = guard.ResolvePath(workspace, relativePath);
            if (!fileResult.IsAllowed || string.IsNullOrWhiteSpace(fileResult.FullPath))
            {
                diagnostics.Add(new AutomationDiagnostic(
                    AutomationErrorCode.SourceDenied,
                    "Automation manifest path must remain inside the workspace.",
                    relativePath));
                continue;
            }

            AutomationManifest? manifest;
            try
            {
                FileInfo file = new(fileResult.FullPath);
                if (file.Length > MaxManifestBytes)
                {
                    diagnostics.Add(new AutomationDiagnostic(
                        AutomationErrorCode.ManifestInvalid,
                        "Automation manifest exceeds the 256 KiB limit.",
                        relativePath));
                    continue;
                }

                manifest = JsonSerializer.Deserialize<AutomationManifest>(File.ReadAllText(file.FullName), JsonOptions);
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or JsonException
                or NotSupportedException)
            {
                diagnostics.Add(new AutomationDiagnostic(
                    AutomationErrorCode.ManifestInvalid,
                    "Automation manifest could not be parsed.",
                    relativePath));
                continue;
            }

            AutomationValidationResult validation = validator.Validate(manifest, source);
            if (!validation.Succeeded)
            {
                diagnostics.AddRange(validation.Diagnostics);
                continue;
            }

            AutomationCatalogItem item = new(manifest!, source);
            string name = manifest!.Name!;
            if (byName.ContainsKey(name))
            {
                diagnostics.Add(new AutomationDiagnostic(
                    AutomationErrorCode.ManifestInvalid,
                    $"Automation name '{name}' duplicates another workspace-local manifest.",
                    relativePath,
                    name));
                continue;
            }

            byName[name] = item;
            items.Add(item);
        }

        return new AutomationCatalog(
            items.OrderBy(item => item.Manifest.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
            diagnostics);
    }

    public bool TryGet(string? name, out AutomationCatalogItem? item)
    {
        item = string.IsNullOrWhiteSpace(name)
            ? null
            : Items.FirstOrDefault(candidate =>
                string.Equals(candidate.Manifest.Name, name, StringComparison.OrdinalIgnoreCase));
        return item is not null;
    }

    private static IReadOnlyList<string> EnumerateManifestFiles(string directory)
    {
        List<string> paths = [];
        try
        {
            paths.AddRange(Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly));
            paths.AddRange(Directory.EnumerateFiles(directory, "manifest.json", SearchOption.AllDirectories));
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

    private static string ToWorkspaceRelativePath(WorkspaceContext workspace, string path) =>
        Path.GetRelativePath(workspace.RootPath, path)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
}
