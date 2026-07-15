using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;
using CSharpAiCli.Core;

namespace CSharpAiCli.ProjectPacks.GerberTiff;

public sealed record GerberTiffToolPlanIdentity(
    string DependencyId,
    bool Required,
    string Status,
    string ConfigurationSource,
    string? FileName,
    long? FileSize,
    string? Sha256,
    string? TrustStatus,
    string? ProbeStatus,
    string? Version);

public sealed record GerberTiffPlannedStage(
    string Id,
    string Kind,
    string? DependencyId,
    bool RequiresApproval,
    string RestartPolicy,
    int? TimeoutMilliseconds,
    int InputCount,
    IReadOnlyList<string> ExpectedArtifactKinds);

public sealed record GerberTiffExpectedArtifact(
    string Id,
    string Kind,
    string MediaType,
    string Scope,
    string RelativePath,
    bool Required,
    string OverwritePolicy);

public sealed record GerberTiffConversionPlan
{
    public GerberTiffConversionPlan(
        string? planId,
        string? fingerprint,
        string packVersion,
        string? inputDirectory,
        string? outputDirectory,
        GerberTiffInputInventory inventory,
        IReadOnlyList<GerberTiffToolPlanIdentity>? tools,
        IReadOnlyList<GerberTiffPlannedStage>? stages,
        IReadOnlyList<GerberTiffExpectedArtifact>? expectedArtifacts,
        IReadOnlyList<ProjectPackDiagnostic>? diagnostics,
        bool readyForStaging,
        bool runnable,
        ProjectPackPlan? contractPlan)
    {
        SchemaVersion = ProjectPackSchema.CurrentVersion;
        PlanSchema = "gerber-tiff.plan.v1";
        PackId = GerberTiffWorkflowPack.ProfileName;
        PackVersion = packVersion;
        PlanId = planId;
        Fingerprint = fingerprint;
        InputDirectory = inputDirectory;
        OutputDirectory = outputDirectory;
        Inventory = inventory;
        Tools = new ReadOnlyCollection<GerberTiffToolPlanIdentity>((tools ?? []).ToArray());
        Stages = new ReadOnlyCollection<GerberTiffPlannedStage>((stages ?? []).ToArray());
        ExpectedArtifacts = new ReadOnlyCollection<GerberTiffExpectedArtifact>((expectedArtifacts ?? []).ToArray());
        Diagnostics = new ReadOnlyCollection<ProjectPackDiagnostic>((diagnostics ?? []).ToArray());
        ReadyForStaging = readyForStaging;
        Runnable = runnable;
        ContractPlan = contractPlan;
    }

    public int SchemaVersion { get; }

    public string PlanSchema { get; }

    public string PackId { get; }

    public string PackVersion { get; }

    public string? PlanId { get; }

    public string? Fingerprint { get; }

    public string? InputDirectory { get; }

    public string? OutputDirectory { get; }

    public string InputSource => "--input";

    public string OutputSource => "--output-dir";

    public GerberTiffInputInventory Inventory { get; }

    public IReadOnlyList<GerberTiffToolPlanIdentity> Tools { get; }

    public IReadOnlyList<GerberTiffPlannedStage> Stages { get; }

    public IReadOnlyList<GerberTiffExpectedArtifact> ExpectedArtifacts { get; }

    public IReadOnlyList<ProjectPackDiagnostic> Diagnostics { get; }

    public bool ReadyForStaging { get; }

    public bool Runnable { get; }

    public bool ConversionExecuted => false;

    public bool ExecutionAuthorized => false;

    public bool ApprovalPersisted => false;

    public string OverwritePolicy => "deny-existing-output-directory";

    public ProjectPackPlan? ContractPlan { get; }
}

public sealed class GerberTiffConversionPlanBuilder
{
    private const int RenderTimeoutMilliseconds = 30_000;
    private const int EncodeTimeoutMilliseconds = 30_000;
    private const int InspectTimeoutMilliseconds = 15_000;

    private readonly GerberTiffWorkflowPack pack;
    private readonly GerberTiffInputDiscovery discovery;
    private readonly GerberTiffPreflight preflight;
    private readonly GerberTiffWorkspacePathPolicy pathPolicy = new();

    public GerberTiffConversionPlanBuilder(
        GerberTiffWorkflowPack? pack = null,
        GerberTiffInputDiscovery? discovery = null,
        GerberTiffPreflight? preflight = null)
    {
        this.pack = pack ?? new GerberTiffWorkflowPack();
        this.discovery = discovery ?? new GerberTiffInputDiscovery();
        this.preflight = preflight ?? new GerberTiffPreflight(this.pack);
    }

    public GerberTiffConversionPlan Build(
        WorkspaceContext workspace,
        string? inputDirectory,
        string? outputDirectory,
        IReadOnlyDictionary<string, string>? toolPaths = null,
        IReadOnlyDictionary<string, string>? trustedHashes = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        GerberTiffInputInventory inventory = discovery.Discover(workspace, inputDirectory, cancellationToken);
        List<ProjectPackDiagnostic> outputDiagnostics = [];
        GerberTiffResolvedWorkspacePath? output = null;
        if (inventory.InputDirectory is not null)
        {
            string inputFullPath = Path.GetFullPath(inventory.InputDirectory, workspace.RootPath);
            output = pathPolicy.ResolveOutputDirectory(
                workspace,
                outputDirectory,
                inputFullPath,
                outputDiagnostics);
        }
        else
        {
            outputDiagnostics.Add(new ProjectPackDiagnostic(
                GerberTiffDiagnosticCode.OutputPathRequired,
                ProjectPackDiagnosticSeverity.Error,
                "Output validation requires a valid input directory."));
        }

        ProjectPackDoctorReport toolReport = preflight.RunStatic(toolPaths, trustedHashes, cancellationToken);
        GerberTiffToolPlanIdentity[] tools = toolReport.Tools
            .OrderBy(tool => tool.DependencyId, StringComparer.Ordinal)
            .Select(tool => new GerberTiffToolPlanIdentity(
                tool.DependencyId,
                tool.Required,
                tool.Status,
                tool.ConfigurationSource,
                tool.Identity?.FileName,
                tool.Identity?.FileSize,
                tool.Identity?.Sha256,
                tool.Identity?.TrustStatus,
                tool.Identity?.ProbeStatus,
                tool.Identity?.Version))
            .ToArray();

        GerberTiffExpectedArtifact[] expectedArtifacts = BuildExpectedArtifacts(inventory);
        GerberTiffPlannedStage[] stages = BuildStages(inventory, expectedArtifacts);
        List<ProjectPackDiagnostic> allDiagnostics =
        [
            .. inventory.Diagnostics,
            .. outputDiagnostics,
            .. toolReport.Diagnostics
        ];
        ProjectPackDiagnostic[] stableDiagnostics = allDiagnostics
            .OrderBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.DependencyId, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.StageId, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Summary, StringComparer.Ordinal)
            .ToArray();
        bool readyForStaging = inventory.Succeeded && output is not null;
        bool toolsAvailable = tools.All(tool => !tool.Required || tool.FileName is not null) &&
            toolReport.Succeeded;
        bool runnable = readyForStaging && toolsAvailable &&
            stableDiagnostics.All(diagnostic => diagnostic.Severity != ProjectPackDiagnosticSeverity.Error);

        string? fingerprint = null;
        string? planId = null;
        ProjectPackPlan? contractPlan = null;
        if (readyForStaging && inventory.Files
            .Where(file => file.Supported)
            .All(file => file.Sha256 is not null))
        {
            fingerprint = ComputeFingerprint(
                pack.Manifest,
                inventory,
                output!.WorkspaceRelativePath,
                tools,
                stages,
                expectedArtifacts);
            planId = $"gerber-tiff-{fingerprint[..16].ToLowerInvariant()}";
            contractPlan = new ProjectPackPlan(
                pack.Manifest.Id,
                planId,
                inventory.Files
                    .Where(file => file.Supported && file.Sha256 is not null)
                    .Select(file => new ProjectPackInputIdentity(file.Id, file.Size, file.Sha256!))
                    .ToArray(),
                pack.Manifest.Stages,
                pack.Manifest.Artifacts,
                stableDiagnostics);
        }

        return new GerberTiffConversionPlan(
            planId,
            fingerprint,
            pack.Manifest.Version,
            inventory.InputDirectory,
            output?.WorkspaceRelativePath,
            inventory,
            tools,
            stages,
            expectedArtifacts,
            stableDiagnostics,
            readyForStaging,
            runnable,
            contractPlan);
    }

    private GerberTiffPlannedStage[] BuildStages(
        GerberTiffInputInventory inventory,
        IReadOnlyList<GerberTiffExpectedArtifact> artifacts)
    {
        int toolInputCount = inventory.Files.Count(file => file.PassedToExternalTool);
        return pack.Manifest.Stages.Select(stage => new GerberTiffPlannedStage(
            stage.Id,
            stage.Kind,
            stage.DependencyId,
            stage.RequiresApproval,
            stage.RestartPolicy,
            stage.Id switch
            {
                "inventory" => GerberTiffInputEnvelope.ScanTimeoutMilliseconds,
                "render" => RenderTimeoutMilliseconds,
                "encode" => EncodeTimeoutMilliseconds,
                "inspect" => InspectTimeoutMilliseconds,
                _ => null
            },
            stage.Id is "render" or "encode" or "inspect" ? toolInputCount : 1,
            artifacts
                .Where(artifact => StageOwnsArtifact(stage.Id, artifact.Kind))
                .Select(artifact => artifact.Kind)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(kind => kind, StringComparer.Ordinal)
                .ToArray())).ToArray();
    }

    private static GerberTiffExpectedArtifact[] BuildExpectedArtifacts(GerberTiffInputInventory inventory)
    {
        List<GerberTiffExpectedArtifact> artifacts =
        [
            new("input-inventory", "input-inventory", "application/json", "managed-run", "inventory.json", true, "no-overwrite")
        ];
        int sequence = 0;
        foreach (GerberTiffInputFile input in inventory.Files.Where(file => file.PassedToExternalTool))
        {
            sequence++;
            artifacts.Add(new GerberTiffExpectedArtifact(
                $"render-{sequence:D4}",
                "render-intermediate",
                "image/png",
                "managed-run",
                GerberTiffArtifactNaming.RenderRelativePath(sequence, input.RelativePath),
                true,
                "no-overwrite"));
            artifacts.Add(new GerberTiffExpectedArtifact(
                $"tiff-{sequence:D4}",
                "tiff-output",
                "image/tiff",
                "workspace-output",
                GerberTiffArtifactNaming.TiffFileName(sequence, input.RelativePath),
                true,
                "no-overwrite"));
        }

        artifacts.Add(new GerberTiffExpectedArtifact(
            "inspection-report",
            "inspection-report",
            "application/json",
            "managed-run",
            "inspection.json",
            true,
            "no-overwrite"));
        return artifacts.ToArray();
    }

    private static bool StageOwnsArtifact(string stageId, string artifactKind) => (stageId, artifactKind) switch
    {
        ("inventory", "input-inventory") => true,
        ("render", "render-intermediate") => true,
        ("encode", "tiff-output") => true,
        ("inspect", "inspection-report") => true,
        _ => false
    };

    private static string ComputeFingerprint(
        ProjectPackManifest manifest,
        GerberTiffInputInventory inventory,
        string outputDirectory,
        IReadOnlyList<GerberTiffToolPlanIdentity> tools,
        IReadOnlyList<GerberTiffPlannedStage> stages,
        IReadOnlyList<GerberTiffExpectedArtifact> artifacts)
    {
        object canonical = new
        {
            schemaVersion = ProjectPackSchema.CurrentVersion,
            planSchema = "gerber-tiff.plan.v1",
            pack = manifest.Id,
            packVersion = manifest.Version,
            inputDirectory = inventory.InputDirectory,
            outputDirectory,
            limits = inventory.Limits,
            supportedExtensions = GerberTiffInputEnvelope.GerberExtensions
                .Concat(GerberTiffInputEnvelope.DrillExtensions)
                .Concat(GerberTiffInputEnvelope.SidecarExtensions)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray(),
            inputs = inventory.Files
                .Where(file => file.Supported)
                .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
                .Select(file => new
                {
                    file.Id,
                    file.RelativePath,
                    file.Extension,
                    file.Kind,
                    file.LayerRole,
                    file.PassedToExternalTool,
                    file.Size,
                    file.Sha256
                }),
            tools = tools.OrderBy(tool => tool.DependencyId, StringComparer.Ordinal).Select(tool => new
            {
                tool.DependencyId,
                tool.Required,
                tool.Status,
                tool.FileName,
                tool.FileSize,
                tool.Sha256,
                tool.TrustStatus,
                tool.ProbeStatus,
                tool.Version
            }),
            stages,
            artifacts
        };
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(canonical);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

}

internal static class GerberTiffArtifactNaming
{
    public static string RenderRelativePath(int sequence, string sourceRelativePath) =>
        $"render/{sequence:D4}-{Slug(Path.GetFileNameWithoutExtension(sourceRelativePath))}.png";

    public static string TiffFileName(int sequence, string sourceRelativePath) =>
        $"{sequence:D4}-{Slug(Path.GetFileNameWithoutExtension(sourceRelativePath))}.tiff";

    private static string Slug(string value)
    {
        string slug = new(value
            .ToLowerInvariant()
            .Select(character => char.IsAsciiLetterOrDigit(character) ? character : '-')
            .ToArray());
        slug = string.Join('-', slug.Split('-', StringSplitOptions.RemoveEmptyEntries));
        if (slug.Length == 0)
        {
            return "input";
        }

        return slug.Length <= 80 ? slug : slug[..80];
    }
}
