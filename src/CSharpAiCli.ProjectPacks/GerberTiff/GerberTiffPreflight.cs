using CSharpAiCli.Core;

namespace CSharpAiCli.ProjectPacks.GerberTiff;

public sealed class GerberTiffPreflight
{
    private readonly GerberTiffWorkflowPack pack;
    private readonly ProjectPackDoctorService doctor;

    public GerberTiffPreflight(
        GerberTiffWorkflowPack? pack = null,
        ProjectPackDoctorService? doctor = null)
    {
        this.pack = pack ?? new GerberTiffWorkflowPack();
        this.doctor = doctor ?? new ProjectPackDoctorService();
    }

    public ProjectPackDoctorReport RunStatic(
        IReadOnlyDictionary<string, string>? toolPaths,
        IReadOnlyDictionary<string, string>? trustedHashes = null,
        CancellationToken cancellationToken = default) =>
        doctor.Diagnose(
            pack,
            toolPaths,
            trustedHashes,
            probe: false,
            new DefaultDenyApprovalPolicy(),
            cancellationToken);

    public ProjectPackDoctorReport RunExplicitToolProbe(
        IReadOnlyDictionary<string, string>? toolPaths,
        IReadOnlyDictionary<string, string>? trustedHashes,
        IApprovalPolicy approvalPolicy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(approvalPolicy);
        return doctor.Diagnose(
            pack,
            toolPaths,
            trustedHashes,
            probe: true,
            approvalPolicy,
            cancellationToken);
    }
}
