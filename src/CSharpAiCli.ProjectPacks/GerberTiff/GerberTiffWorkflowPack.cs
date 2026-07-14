using CSharpAiCli.Core;
using CSharpAiCli.ProjectPacks;

namespace CSharpAiCli.ProjectPacks.GerberTiff;

public sealed class GerberTiffWorkflowPack : IProjectPack
{
    public const string ProfileName = "gerber-tiff";

    public ProjectPackManifest Manifest { get; } = CreateManifest();

    public GerberTiffStatusReport CreateStatusReport(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        string plansDirectory = Path.Combine(workspaceRoot, "docs_md", "plans");
        List<string> planFiles = Directory.Exists(plansDirectory)
            ? Directory.EnumerateFiles(plansDirectory, "*.md").Select(Path.GetFileName).Where(name => name is not null).Cast<string>().OrderBy(name => name, StringComparer.Ordinal).ToList()
            : [];
        string status = Directory.Exists(plansDirectory)
            ? "plans-found"
            : "plans-missing";

        return new GerberTiffStatusReport(
            WorkspaceRoot: workspaceRoot,
            PlansDirectory: plansDirectory,
            Status: status,
            PlanFiles: planFiles);
    }

    public WorkflowValidationSuggestion CreateValidationSuggestion(
        WorkflowRegistry registry,
        WorkspaceContext workspace)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(workspace);

        if (registry.TrySuggestValidation(ProfileName, workspace, out WorkflowValidationSuggestion? suggestion) &&
            suggestion is not null)
        {
            return suggestion;
        }

        return new WorkflowValidationSuggestion(
            ProfileName: ProfileName,
            WorkspacePath: workspace.RootPath,
            WorkspacePathSource: "--workspace",
            ValidationCommand: null,
            Source: "missing",
            RequiresApproval: false);
    }

    private static ProjectPackManifest CreateManifest()
    {
        return new ProjectPackManifest(
            SchemaVersion: ProjectPackSchema.CurrentVersion,
            Id: ProfileName,
            DisplayName: "Gerber/TIFF v1",
            Version: "1.0.0-preview.1",
            Description: "Deterministic Gerber render and TIFF inspection workflow contract.",
            Capabilities:
            [
                new("input.inventory", "Build a bounded identity inventory without parsing complete Gerber content."),
                new("gerber.render", "Render approved Gerber inputs through a fixed external-tool adapter."),
                new("tiff.encode", "Encode managed raster intermediates as TIFF through a fixed adapter."),
                new("tiff.inspect", "Inspect managed TIFF outputs with bounded metadata and preview tooling."),
                new("human.review", "Record a later explicit human acceptance or rejection decision.")
            ],
            Dependencies:
            [
                new ExternalToolRequirement(
                    Id: "gerbv",
                    DisplayName: "Gerbv",
                    MinimumVersion: "2.13.0",
                    License: "GPL-2.0-or-later",
                    RedistributionAllowed: true,
                    ConfigurationKey: "gerbv",
                    ExecutableFileNames: ["gerbv.exe"],
                    ProbeArguments: ["--version"],
                    VersionOutputMarker: "gerbv version"),
                new ExternalToolRequirement(
                    Id: "imagemagick",
                    DisplayName: "ImageMagick",
                    MinimumVersion: "7.1.2-27",
                    License: "ImageMagick License",
                    RedistributionAllowed: true,
                    ConfigurationKey: "imagemagick",
                    ExecutableFileNames: ["magick.exe"],
                    ProbeArguments: ["-version"],
                    VersionOutputMarker: "Version: ImageMagick")
            ],
            Stages:
            [
                new ProjectPackStage(
                    "inventory", "inventory", "input.inventory", null,
                    RequiresApproval: false,
                    RestartPolicy: ProjectPackRestartPolicy.SafeReplay,
                    ArtifactIds: ["input-inventory"]),
                new ProjectPackStage(
                    "render", "external-tool", "gerber.render", "gerbv",
                    RequiresApproval: true,
                    RestartPolicy: ProjectPackRestartPolicy.ManualReapproval,
                    ArtifactIds: ["render-intermediate"]),
                new ProjectPackStage(
                    "encode", "external-tool", "tiff.encode", "imagemagick",
                    RequiresApproval: true,
                    RestartPolicy: ProjectPackRestartPolicy.ManualReapproval,
                    ArtifactIds: ["tiff-output"]),
                new ProjectPackStage(
                    "inspect", "external-tool", "tiff.inspect", "imagemagick",
                    RequiresApproval: true,
                    RestartPolicy: ProjectPackRestartPolicy.ManualReapproval,
                    ArtifactIds: ["inspection-report"]),
                new ProjectPackStage(
                    "review", "human-gate", "human.review", null,
                    RequiresApproval: false,
                    RestartPolicy: ProjectPackRestartPolicy.NotRestartable)
            ],
            Artifacts:
            [
                new("input-inventory", "evidence", "application/json", Required: true, Managed: true),
                new("render-intermediate", "intermediate", "image/png", Required: true, Managed: true),
                new("tiff-output", "primary", "image/tiff", Required: true, Managed: true),
                new("inspection-report", "evidence", "application/json", Required: true, Managed: true)
            ]);
    }
}
