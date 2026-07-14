using System.Text;
using System.Text.Json;
using CSharpAiCli.Core;

namespace CSharpAiCli.ProjectPacks;

public static class ProjectPackReportRenderer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string RenderListText(ProjectPackRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        StringBuilder builder = new();
        builder.AppendLine($"{ProductInfo.DisplayName} project packs");
        IReadOnlyList<IProjectPack> packs = registry.List();
        builder.AppendLine($"packs: {packs.Count}");
        foreach (IProjectPack pack in packs)
        {
            ProjectPackManifest manifest = pack.Manifest;
            builder.AppendLine($"pack: {manifest.Id}");
            builder.AppendLine($"  displayName: {manifest.DisplayName}");
            builder.AppendLine($"  version: {manifest.Version}");
            builder.AppendLine("  status: contract-only");
            builder.AppendLine($"  capabilities: {string.Join(", ", manifest.Capabilities.Select(item => item.Id).OrderBy(id => id, StringComparer.Ordinal))}");
            builder.AppendLine($"  dependencies: {string.Join(", ", manifest.Dependencies.Select(item => item.Id).OrderBy(id => id, StringComparer.Ordinal))}");
        }

        return builder.ToString().TrimEnd();
    }

    public static string RenderListJson(ProjectPackRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        object payload = new
        {
            type = "packs.list",
            schemaVersion = ProjectPackSchema.CurrentVersion,
            packs = registry.List().Select(pack => new
            {
                id = pack.Manifest.Id,
                displayName = pack.Manifest.DisplayName,
                version = pack.Manifest.Version,
                description = pack.Manifest.Description,
                status = "contract-only",
                capabilities = pack.Manifest.Capabilities.OrderBy(item => item.Id, StringComparer.Ordinal),
                dependencies = pack.Manifest.Dependencies.OrderBy(item => item.Id, StringComparer.Ordinal).Select(dependency => new
                {
                    id = dependency.Id,
                    displayName = dependency.DisplayName,
                    minimumVersion = dependency.MinimumVersion,
                    license = dependency.License,
                    redistributionAllowed = dependency.RedistributionAllowed,
                    configurationKey = dependency.ConfigurationKey,
                    executableFileNames = dependency.ExecutableFileNames,
                    probeArguments = dependency.ProbeArguments,
                    versionOutputMarker = dependency.VersionOutputMarker,
                    probeTimeoutMilliseconds = dependency.ProbeTimeoutMilliseconds,
                    maxProbeOutputCharacters = dependency.MaxProbeOutputCharacters,
                    required = dependency.Required
                }),
                stages = pack.Manifest.Stages,
                artifacts = pack.Manifest.Artifacts
            })
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    public static string RenderDoctorText(ProjectPackDoctorReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        StringBuilder builder = new();
        builder.AppendLine($"{ProductInfo.DisplayName} project pack doctor");
        builder.AppendLine($"pack: {report.PackId}");
        builder.AppendLine($"status: {report.Status}");
        builder.AppendLine($"probeRequested: {report.ProbeRequested.ToString().ToLowerInvariant()}");
        foreach (ProjectPackToolDoctorResult tool in report.Tools)
        {
            builder.AppendLine($"dependency: {tool.DependencyId}");
            builder.AppendLine($"  status: {tool.Status}");
            builder.AppendLine($"  required: {tool.Required.ToString().ToLowerInvariant()}");
            builder.AppendLine($"  configurationSource: {tool.ConfigurationSource}");
            if (tool.Identity is not null)
            {
                builder.AppendLine($"  fileName: {tool.Identity.FileName}");
                builder.AppendLine($"  fileSize: {tool.Identity.FileSize}");
                builder.AppendLine($"  sha256: {tool.Identity.Sha256}");
                builder.AppendLine($"  trustStatus: {tool.Identity.TrustStatus}");
                builder.AppendLine($"  probeStatus: {tool.Identity.ProbeStatus}");
                builder.AppendLine($"  version: {tool.Identity.Version ?? "not-probed"}");
            }

            if (tool.Probe is not null)
            {
                builder.AppendLine($"  approvalStatus: {tool.Probe.ApprovalStatus}");
                builder.AppendLine($"  exitCode: {(tool.Probe.ExitCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none")}");
                builder.AppendLine($"  timedOut: {tool.Probe.TimedOut.ToString().ToLowerInvariant()}");
                builder.AppendLine($"  canceled: {tool.Probe.Canceled.ToString().ToLowerInvariant()}");
                builder.AppendLine($"  processCleanedUp: {tool.Probe.ProcessCleanedUp.ToString().ToLowerInvariant()}");
            }

            foreach (ProjectPackDiagnostic diagnostic in tool.Diagnostics)
            {
                builder.AppendLine($"  diagnostic: {diagnostic.Severity} {diagnostic.Code}: {diagnostic.Summary}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    public static string RenderDoctorJson(ProjectPackDoctorReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        object payload = new
        {
            type = "packs.doctor",
            schemaVersion = report.SchemaVersion,
            pack = report.PackId,
            status = report.Status,
            probeRequested = report.ProbeRequested,
            tools = report.Tools.Select(tool => new
            {
                dependencyId = tool.DependencyId,
                displayName = tool.DisplayName,
                required = tool.Required,
                status = tool.Status,
                configurationSource = tool.ConfigurationSource,
                identity = tool.Identity,
                probe = tool.Probe is null ? null : new
                {
                    status = tool.Probe.Status,
                    identity = tool.Probe.Identity,
                    approvalStatus = tool.Probe.ApprovalStatus,
                    approvalDurationMilliseconds = tool.Probe.ApprovalDurationMilliseconds,
                    exitCode = tool.Probe.ExitCode,
                    durationMilliseconds = tool.Probe.DurationMilliseconds,
                    stdoutCharacters = tool.Probe.Stdout.Length,
                    stderrCharacters = tool.Probe.Stderr.Length,
                    stdoutTruncated = tool.Probe.StdoutTruncated,
                    stderrTruncated = tool.Probe.StderrTruncated,
                    timedOut = tool.Probe.TimedOut,
                    canceled = tool.Probe.Canceled,
                    processCleanedUp = tool.Probe.ProcessCleanedUp,
                    diagnostics = tool.Probe.Diagnostics
                },
                diagnostics = tool.Diagnostics
            }),
            diagnostics = report.Diagnostics,
            redaction = new
            {
                secretsRedacted = true,
                absoluteToolPathsStored = false,
                rawToolArgumentsStored = false
            }
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }
}
