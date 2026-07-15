using System.Security.Cryptography;
using System.Text.Json;

namespace CSharpAiCli.ProjectPacks.Runtime;

public sealed class ProjectPackAcceptanceService
{
    private readonly ManagedProjectPackRunStore store;

    public ProjectPackAcceptanceService(ManagedProjectPackRunStore store)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public ProjectPackRunMutationResult Accept(
        string runId,
        string actor,
        string? note,
        DateTimeOffset nowUtc) =>
        Decide(runId, ProjectPackRunState.Accepted, actor, note, nowUtc);

    public ProjectPackRunMutationResult Reject(
        string runId,
        string actor,
        string reason,
        DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return ProjectPackRunMutationResult.Failure(
                ProjectPackRunErrorCode.AcceptanceNotEligible,
                "Reject requires an explicit human reason.",
                runId: runId);
        }

        return Decide(runId, ProjectPackRunState.Rejected, actor, reason, nowUtc);
    }

    public ProjectPackRunDiagnostic? ValidateHardVerification(string runId)
    {
        ProjectPackRunReadResult read = store.Read(runId);
        if (!read.Succeeded || read.Record is null || read.Checkpoint is null)
        {
            return read.Diagnostic ?? new ProjectPackRunDiagnostic(
                ProjectPackRunErrorCode.NotFound,
                "Project pack run could not be read.",
                RunId: runId);
        }

        return ValidateHardVerification(read.Record, read.Checkpoint) is null
            ? new ProjectPackRunDiagnostic(
                ProjectPackRunErrorCode.HardVerificationRequired,
                "Current hard verification evidence is missing, changed, failed, or inconsistent with the run.",
                RunId: runId)
            : null;
    }

    private ProjectPackRunMutationResult Decide(
        string runId,
        string outcome,
        string actor,
        string? reason,
        DateTimeOffset nowUtc)
    {
        ProjectPackRunReadResult read = store.Read(runId);
        if (!read.Succeeded || read.Record is null || read.Checkpoint is null)
        {
            return ProjectPackRunMutationResult.Failure(
                read.Diagnostic?.ErrorCode ?? ProjectPackRunErrorCode.NotFound,
                read.Diagnostic?.Summary ?? "Project pack run could not be read.",
                read.Diagnostic?.Path,
                runId);
        }

        ProjectPackRunRecord record = read.Record;
        if (record.Acceptance is not null || record.State is ProjectPackRunState.Accepted or ProjectPackRunState.Rejected)
        {
            return ProjectPackRunMutationResult.Failure(
                ProjectPackRunErrorCode.DecisionConflict,
                "Project pack run already has a terminal human decision.",
                runId: runId);
        }

        if (record.State != ProjectPackRunState.AwaitingAcceptance)
        {
            return ProjectPackRunMutationResult.Failure(
                ProjectPackRunErrorCode.AcceptanceNotEligible,
                "Only an awaiting-acceptance run can receive a human decision.",
                runId: runId);
        }

        HardVerificationIdentity? verification = ValidateHardVerification(record, read.Checkpoint);
        if (verification is null)
        {
            return ProjectPackRunMutationResult.Failure(
                ProjectPackRunErrorCode.HardVerificationRequired,
                "Current hard verification evidence is missing, changed, failed, or inconsistent with the run.",
                runId: runId);
        }

        try
        {
            ProjectPackAcceptanceDecision decision = new(
                outcome,
                actor,
                reason,
                nowUtc,
                record.Revision,
                ManagedArtifactId.Create(record.RunId, verification.Pointer.Id),
                verification.Pointer.Sha256!);
            ProjectPackRunRecord updated = record.Decide(decision, nowUtc);
            ProjectPackRunCheckpoint checkpoint = new(
                read.Checkpoint.SchemaVersion,
                read.Checkpoint.RunId,
                updated.Revision,
                updated.State,
                read.Checkpoint.PlanFingerprint,
                read.Checkpoint.PolicyFingerprint,
                nowUtc,
                read.Checkpoint.Stages,
                approvalPersisted: false);
            return store.Update(updated, checkpoint, record.Revision);
        }
        catch (Exception exception) when (exception is ArgumentException or ProjectPackContractException)
        {
            return ProjectPackRunMutationResult.Failure(
                exception is ProjectPackContractException contract
                    ? contract.ErrorCode
                    : ProjectPackRunErrorCode.AcceptanceNotEligible,
                "Human decision metadata is invalid after redaction.",
                runId: runId);
        }
    }

    private HardVerificationIdentity? ValidateHardVerification(
        ProjectPackRunRecord record,
        ProjectPackRunCheckpoint checkpoint)
    {
        ProjectPackStageCheckpoint? inspect = checkpoint.Stages.SingleOrDefault(stage => stage.StageId == "inspect");
        ProjectPackRunArtifactPointer[] reports = record.Artifacts
            .Where(artifact => artifact.Kind == "tiff-verification-json")
            .ToArray();
        if (inspect?.Status != ProjectPackStageStatus.Succeeded || reports.Length != 1)
        {
            return null;
        }

        ProjectPackRunArtifactPointer pointer = reports[0];
        if (pointer.Scope != "managed-run" || !pointer.Exists || pointer.Size is null or <= 0 ||
            pointer.Size > TiffVerificationLimits.MaxReportBytes || pointer.Sha256 is null ||
            !pointer.Path.StartsWith("reports/", StringComparison.Ordinal) ||
            pointer.Path.Contains("..", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            ManagedProjectPackRunLayout layout = store.GetLayout(record.RunId);
            string path = Path.GetFullPath(Path.Combine(
                layout.RunRoot,
                pointer.Path.Replace('/', Path.DirectorySeparatorChar)));
            string reportsRoot = Path.TrimEndingDirectorySeparator(layout.ReportsPath) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(reportsRoot, PathComparison))
            {
                return null;
            }

            ManagedProjectPackRunStore.EnsureNoReparseInExistingChain(path);
            byte[] bytes;
            using (FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.SequentialScan))
            {
                if (stream.Length != pointer.Size)
                {
                    return null;
                }

                bytes = new byte[stream.Length];
                stream.ReadExactly(bytes);
                if (stream.Length != pointer.Size ||
                    !Convert.ToHexString(SHA256.HashData(bytes)).Equals(pointer.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
            }

            using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
            JsonElement root = document.RootElement;
            if (RequiredString(root, "type") != TiffVerificationSchema.ResultType ||
                RequiredInt32(root, "schemaVersion") != TiffVerificationSchema.CurrentVersion ||
                RequiredString(root, "runId") != record.RunId ||
                RequiredString(root, "status") != "passed" ||
                !RequiredBoolean(root, "hardVerificationPassed") ||
                !RequiredBoolean(root, "humanReviewRequired") ||
                RequiredBoolean(root, "correctnessProof"))
            {
                return null;
            }

            Dictionary<string, string> levels = root.GetProperty("levels")
                .EnumerateArray()
                .ToDictionary(
                    level => RequiredString(level, "level"),
                    level => RequiredString(level, "status"),
                    StringComparer.Ordinal);
            if (!levels.TryGetValue(TiffVerificationLevel.FileValid, out string? fileStatus) ||
                fileStatus != TiffVerificationStatus.Passed ||
                !levels.TryGetValue(TiffVerificationLevel.MetadataValid, out string? metadataStatus) ||
                metadataStatus != TiffVerificationStatus.Passed ||
                !levels.TryGetValue(TiffVerificationLevel.HumanReviewRequired, out string? humanStatus) ||
                humanStatus != TiffVerificationStatus.Required ||
                levels.TryGetValue(TiffVerificationLevel.ContentCompared, out string? contentStatus) &&
                contentStatus == TiffVerificationStatus.Failed ||
                root.GetProperty("diagnostics").EnumerateArray().Any(diagnostic =>
                    RequiredString(diagnostic, "severity") == "error"))
            {
                return null;
            }

            return new HardVerificationIdentity(pointer, path);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or JsonException
            or KeyNotFoundException
            or InvalidOperationException
            or ArgumentException
            or ProjectPackContractException)
        {
            return null;
        }
    }

    private static string RequiredString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new JsonException();

    private static int RequiredInt32(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int result)
            ? result
            : throw new JsonException();

    private static bool RequiredBoolean(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : throw new JsonException();

    private sealed record HardVerificationIdentity(ProjectPackRunArtifactPointer Pointer, string Path);

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
