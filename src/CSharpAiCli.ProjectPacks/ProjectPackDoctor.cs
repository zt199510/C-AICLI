using System.Collections.ObjectModel;
using CSharpAiCli.Core;

namespace CSharpAiCli.ProjectPacks;

public static class ProjectPackDoctorStatus
{
    public const string Ready = "ready";
    public const string StaticOk = "static-ok";
    public const string Unavailable = "unavailable";
    public const string ApprovalRequired = "approval-required";
    public const string Failed = "failed";
}

public sealed record ProjectPackToolDoctorResult(
    string DependencyId,
    string DisplayName,
    bool Required,
    string Status,
    string ConfigurationSource,
    ExternalToolIdentity? Identity,
    ExternalToolProbeResult? Probe,
    IReadOnlyList<ProjectPackDiagnostic> Diagnostics);

public sealed record ProjectPackDoctorReport
{
    public ProjectPackDoctorReport(
        string PackId,
        string Status,
        bool ProbeRequested,
        IReadOnlyList<ProjectPackToolDoctorResult>? Tools,
        IReadOnlyList<ProjectPackDiagnostic>? Diagnostics)
    {
        ProjectPackContractGuard.RequireId(PackId, nameof(PackId));
        ArgumentException.ThrowIfNullOrWhiteSpace(Status);
        SchemaVersion = ProjectPackSchema.CurrentVersion;
        this.PackId = PackId;
        this.Status = Status;
        this.ProbeRequested = ProbeRequested;
        this.Tools = new ReadOnlyCollection<ProjectPackToolDoctorResult>((Tools ?? []).ToArray());
        this.Diagnostics = new ReadOnlyCollection<ProjectPackDiagnostic>((Diagnostics ?? []).ToArray());
    }

    public int SchemaVersion { get; }

    public string PackId { get; }

    public string Status { get; }

    public bool ProbeRequested { get; }

    public IReadOnlyList<ProjectPackToolDoctorResult> Tools { get; }

    public IReadOnlyList<ProjectPackDiagnostic> Diagnostics { get; }

    public bool Succeeded => Status is ProjectPackDoctorStatus.Ready or ProjectPackDoctorStatus.StaticOk;
}

public sealed class ProjectPackDoctorService
{
    private readonly ExternalToolPathInspector inspector;
    private readonly ExternalToolProbeRunner probeRunner;

    public ProjectPackDoctorService(
        ExternalToolPathInspector? inspector = null,
        ExternalToolProbeRunner? probeRunner = null)
    {
        this.inspector = inspector ?? new ExternalToolPathInspector();
        this.probeRunner = probeRunner ?? new ExternalToolProbeRunner(this.inspector);
    }

    public ProjectPackDoctorReport Diagnose(
        IProjectPack pack,
        IReadOnlyDictionary<string, string>? toolPaths,
        IReadOnlyDictionary<string, string>? trustedHashes,
        bool probe,
        IApprovalPolicy approvalPolicy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(approvalPolicy);
        toolPaths ??= new Dictionary<string, string>(StringComparer.Ordinal);
        trustedHashes ??= new Dictionary<string, string>(StringComparer.Ordinal);

        List<ProjectPackToolDoctorResult> tools = [];
        List<ProjectPackDiagnostic> allDiagnostics = [];
        foreach (ExternalToolRequirement requirement in pack.Manifest.Dependencies.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            toolPaths.TryGetValue(requirement.Id, out string? path);
            trustedHashes.TryGetValue(requirement.Id, out string? trustedHash);
            ExternalToolInspectionResult inspection = inspector.Inspect(
                requirement,
                path,
                path is null ? "not-configured" : "--tool-path",
                trustedHash);
            ExternalToolProbeResult? probeResult = null;
            string status = inspection.Succeeded ? ProjectPackDoctorStatus.StaticOk : ProjectPackDoctorStatus.Unavailable;
            List<ProjectPackDiagnostic> diagnostics = [.. inspection.Diagnostics];

            if (probe && inspection.Succeeded)
            {
                probeResult = probeRunner.Run(requirement, inspection, approvalPolicy, cancellationToken);
                diagnostics = [.. probeResult.Diagnostics];
                status = probeResult.Succeeded
                    ? ProjectPackDoctorStatus.Ready
                    : probeResult.ApprovalStatus == "approval-required"
                        ? ProjectPackDoctorStatus.ApprovalRequired
                        : ProjectPackDoctorStatus.Failed;
            }

            tools.Add(new ProjectPackToolDoctorResult(
                requirement.Id,
                requirement.DisplayName,
                requirement.Required,
                status,
                path is null ? "not-configured" : "--tool-path",
                probeResult?.Identity ?? inspection.Identity,
                probeResult,
                new ReadOnlyCollection<ProjectPackDiagnostic>(diagnostics)));
            allDiagnostics.AddRange(diagnostics);
        }

        string reportStatus;
        if (tools.Any(tool => tool.Required && tool.Status == ProjectPackDoctorStatus.ApprovalRequired))
        {
            reportStatus = ProjectPackDoctorStatus.ApprovalRequired;
        }
        else if (tools.Any(tool => tool.Required && tool.Status == ProjectPackDoctorStatus.Failed))
        {
            reportStatus = ProjectPackDoctorStatus.Failed;
        }
        else if (tools.Any(tool => tool.Required && tool.Status == ProjectPackDoctorStatus.Unavailable))
        {
            reportStatus = ProjectPackDoctorStatus.Unavailable;
        }
        else
        {
            reportStatus = probe ? ProjectPackDoctorStatus.Ready : ProjectPackDoctorStatus.StaticOk;
        }

        return new ProjectPackDoctorReport(pack.Manifest.Id, reportStatus, probe, tools, allDiagnostics);
    }
}
