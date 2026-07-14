using System.Text;
using System.Text.Json;
using CSharpAiCli.Core;

namespace CSharpAiCli.ProjectPacks.GerberTiff;

public static class GerberTiffPlanRenderer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string RenderText(GerberTiffConversionPlan plan, string workspaceSource)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceSource);
        StringBuilder builder = new();
        builder.AppendLine($"{ProductInfo.DisplayName} Gerber/TIFF conversion plan");
        builder.AppendLine($"status: {Status(plan)}");
        builder.AppendLine($"pack: {plan.PackId}");
        builder.AppendLine($"packVersion: {plan.PackVersion}");
        builder.AppendLine($"planSchema: {plan.PlanSchema}");
        builder.AppendLine($"planId: {plan.PlanId ?? "not-generated"}");
        builder.AppendLine($"fingerprint: {plan.Fingerprint ?? "not-generated"}");
        builder.AppendLine("workspace: .");
        builder.AppendLine($"workspaceSource: {workspaceSource}");
        builder.AppendLine($"inputDirectory: {plan.InputDirectory ?? "unresolved"}");
        builder.AppendLine($"inputSource: {plan.InputSource}");
        builder.AppendLine($"outputDirectory: {plan.OutputDirectory ?? "unresolved"}");
        builder.AppendLine($"outputSource: {plan.OutputSource}");
        builder.AppendLine($"readyForStaging: {Lower(plan.ReadyForStaging)}");
        builder.AppendLine($"runnable: {Lower(plan.Runnable)}");
        builder.AppendLine("conversionExecuted: false");
        builder.AppendLine("executionAuthorized: false");
        builder.AppendLine($"overwritePolicy: {plan.OverwritePolicy}");
        builder.AppendLine($"inventoryFiles: {plan.Inventory.Files.Count}");
        builder.AppendLine($"supportedFiles: {plan.Inventory.SupportedFileCount}");
        builder.AppendLine($"toolInputs: {plan.Inventory.ToolInputCount}");
        builder.AppendLine($"unknownFiles: {plan.Inventory.UnknownFileCount}");
        builder.AppendLine($"totalSupportedBytes: {plan.Inventory.TotalSupportedBytes}");
        foreach (GerberTiffInputFile file in plan.Inventory.Files)
        {
            builder.AppendLine($"input: {file.Id}");
            builder.AppendLine($"  path: {file.RelativePath}");
            builder.AppendLine($"  kind: {file.Kind}");
            builder.AppendLine($"  layerRole: {file.LayerRole ?? "none"}");
            builder.AppendLine($"  supported: {Lower(file.Supported)}");
            builder.AppendLine($"  passedToExternalTool: {Lower(file.PassedToExternalTool)}");
            builder.AppendLine($"  size: {file.Size}");
            builder.AppendLine($"  sha256: {file.Sha256 ?? "not-hashed"}");
        }

        foreach (GerberTiffToolPlanIdentity tool in plan.Tools)
        {
            builder.AppendLine($"tool: {tool.DependencyId}");
            builder.AppendLine($"  status: {tool.Status}");
            builder.AppendLine($"  fileName: {tool.FileName ?? "not-configured"}");
            builder.AppendLine($"  sha256: {tool.Sha256 ?? "not-available"}");
            builder.AppendLine($"  trustStatus: {tool.TrustStatus ?? "not-available"}");
            builder.AppendLine($"  probeStatus: {tool.ProbeStatus ?? "not-requested"}");
        }

        foreach (GerberTiffPlannedStage stage in plan.Stages)
        {
            builder.AppendLine($"stage: {stage.Id}");
            builder.AppendLine($"  kind: {stage.Kind}");
            builder.AppendLine($"  dependency: {stage.DependencyId ?? "none"}");
            builder.AppendLine($"  requiresApproval: {Lower(stage.RequiresApproval)}");
            builder.AppendLine($"  restartPolicy: {stage.RestartPolicy}");
            builder.AppendLine($"  timeoutMilliseconds: {stage.TimeoutMilliseconds?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"}");
        }

        foreach (GerberTiffExpectedArtifact artifact in plan.ExpectedArtifacts)
        {
            builder.AppendLine($"expectedArtifact: {artifact.Id}");
            builder.AppendLine($"  kind: {artifact.Kind}");
            builder.AppendLine($"  scope: {artifact.Scope}");
            builder.AppendLine($"  path: {artifact.RelativePath}");
            builder.AppendLine($"  overwritePolicy: {artifact.OverwritePolicy}");
        }

        foreach (ProjectPackDiagnostic diagnostic in plan.Diagnostics)
        {
            builder.AppendLine($"diagnostic: {diagnostic.Severity} {diagnostic.Code}: {diagnostic.Summary}");
        }

        builder.AppendLine("redaction: absolute paths and raw input content are not stored");
        return builder.ToString().TrimEnd();
    }

    public static string RenderJson(GerberTiffConversionPlan plan, string workspaceSource)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceSource);
        object payload = new
        {
            type = "packs.plan",
            schemaVersion = plan.SchemaVersion,
            planSchema = plan.PlanSchema,
            status = Status(plan),
            pack = plan.PackId,
            packVersion = plan.PackVersion,
            planId = plan.PlanId,
            fingerprint = plan.Fingerprint,
            workspace = new
            {
                directory = ".",
                source = workspaceSource,
                pathFormat = "workspace-relative-canonical"
            },
            input = new
            {
                directory = plan.InputDirectory,
                source = plan.InputSource
            },
            outputDirectory = new
            {
                directory = plan.OutputDirectory,
                source = plan.OutputSource,
                overwritePolicy = plan.OverwritePolicy
            },
            readyForStaging = plan.ReadyForStaging,
            runnable = plan.Runnable,
            conversionExecuted = plan.ConversionExecuted,
            executionAuthorized = plan.ExecutionAuthorized,
            approvalPersisted = plan.ApprovalPersisted,
            inventory = plan.Inventory,
            tools = plan.Tools,
            stages = plan.Stages,
            expectedArtifacts = plan.ExpectedArtifacts,
            diagnostics = plan.Diagnostics,
            redaction = new
            {
                secretsRedacted = true,
                absoluteWorkspacePathsStored = false,
                absoluteToolPathsStored = false,
                rawInputContentStored = false,
                unknownFileContentHashed = false,
                rawToolArgumentsStored = false,
                approvalBypassStored = false
            }
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static string Status(GerberTiffConversionPlan plan) =>
        plan.Runnable ? "runnable" : plan.ReadyForStaging ? "ready-for-staging" : "blocked";

    private static string Lower(bool value) => value.ToString().ToLowerInvariant();
}
