using System.Collections.ObjectModel;

namespace CSharpAiCli.ProjectPacks.GerberTiff;

public static class GerberTiffInputEnvelope
{
    public const int MaxRecursionDepth = 8;
    public const int MaxFileCount = 256;
    public const int MaxDirectoryCount = 256;
    public const long MaxSingleFileBytes = 64L * 1024 * 1024;
    public const long MaxTotalBytes = 512L * 1024 * 1024;
    public const int MaxRelativePathCharacters = 512;
    public const int ScanTimeoutMilliseconds = 10_000;

    public static IReadOnlyList<string> GerberExtensions { get; } = Array.AsReadOnly(
        [".gbr", ".ger", ".gbx", ".gtl", ".gbl", ".gts", ".gbs", ".gto", ".gbo", ".gm1"]);

    public static IReadOnlyList<string> DrillExtensions { get; } = Array.AsReadOnly(
        [".drl", ".xln"]);

    public static IReadOnlyList<string> SidecarExtensions { get; } = Array.AsReadOnly(
        [".gbrjob"]);

    public static IReadOnlyList<string> RequiredLayerGroups { get; } = Array.AsReadOnly(
        ["gerber-layer"]);

    public static IReadOnlyList<string> OptionalLayerRoles { get; } = Array.AsReadOnly(
        [
            "generic-gerber",
            "top-copper",
            "bottom-copper",
            "top-solder-mask",
            "bottom-solder-mask",
            "top-silkscreen",
            "bottom-silkscreen",
            "board-outline",
            "drill",
            "gerber-job-sidecar"
        ]);

    internal static IReadOnlyDictionary<string, string> LayerRoleByExtension { get; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".gtl"] = "top-copper",
            [".gbl"] = "bottom-copper",
            [".gts"] = "top-solder-mask",
            [".gbs"] = "bottom-solder-mask",
            [".gto"] = "top-silkscreen",
            [".gbo"] = "bottom-silkscreen",
            [".gm1"] = "board-outline"
        });

    internal static bool IsGerberExtension(string extension) =>
        GerberExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);

    internal static bool IsDrillExtension(string extension) =>
        DrillExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);

    internal static bool IsSidecarExtension(string extension) =>
        SidecarExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
}
