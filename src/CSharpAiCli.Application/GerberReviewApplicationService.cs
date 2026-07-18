using CSharpAiCli.Core;
using CSharpAiCli.ProjectPacks.Runtime;

namespace CSharpAiCli.Application;

public sealed record GerberReviewRequest(CliEnvironmentSnapshot Snapshot, string RunId);

public sealed record GerberDecisionRequest(
    CliEnvironmentSnapshot Snapshot,
    string RunId,
    long ExpectedRevision,
    string Reason,
    string ClientMutationId,
    bool Accept);

public sealed record GerberReviewProjection(
    string RunId,
    long Revision,
    string State,
    bool HardVerificationPassed,
    bool HumanDecisionEligible,
    bool PreviewAvailable,
    bool CorrectnessProof,
    string? Decision,
    string? DisabledReason,
    string? VerificationArtifactId,
    IReadOnlyList<string> PreviewArtifactIds);

public sealed class GerberReviewApplicationService
{
    public ApplicationResult<GerberReviewProjection> Get(
        GerberReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        ManagedProjectPackRunStore store = ManagedProjectPackRunStore.Create(request.Snapshot);
        ProjectPackRunReadResult read = store.Read(request.RunId);
        if (!read.Succeeded || read.Record is null)
        {
            return ApplicationResult<GerberReviewProjection>.Failure(new ApplicationError(
                read.Diagnostic?.ErrorCode ?? "run-not-found", ApplicationErrorCategory.NotFound,
                read.Diagnostic?.Summary ?? "Review run was not found.", false));
        }

        ProjectPackRunRecord record = read.Record;
        ProjectPackAcceptanceService acceptance = new(store);
        ProjectPackRunDiagnostic? hardDiagnostic = acceptance.ValidateHardVerification(record.RunId);
        bool hardPassed = hardDiagnostic is null;
        bool eligible = record.State == ProjectPackRunState.AwaitingAcceptance && record.Acceptance is null && hardPassed;
        ProjectPackRunArtifactPointer? verification = record.Artifacts.SingleOrDefault(pointer =>
            pointer.Kind == "tiff-verification-json" && pointer.Scope == "managed-run");
        string[] previews = record.Artifacts
            .Where(pointer => pointer.Exists && pointer.Scope == "managed-run" &&
                (pointer.Kind.Contains("preview", StringComparison.OrdinalIgnoreCase) ||
                 pointer.Kind.Contains("contact-sheet", StringComparison.OrdinalIgnoreCase)))
            .Select(pointer => ManagedArtifactId.Create(record.RunId, pointer.Id))
            .Take(64)
            .ToArray();
        string? disabled = eligible ? null : record.Acceptance is not null
            ? "A terminal human decision already exists."
            : record.State != ProjectPackRunState.AwaitingAcceptance
                ? "Run is not awaiting human acceptance."
                : hardDiagnostic?.Summary ?? "Current hard verification evidence is unavailable.";
        return ApplicationResult<GerberReviewProjection>.Success(new GerberReviewProjection(
            record.RunId,
            record.Revision,
            record.State,
            hardPassed,
            eligible,
            previews.Length > 0,
            CorrectnessProof: false,
            record.Acceptance?.Outcome,
            ApplicationProjection.SafeOrNull(disabled, 4096),
            verification is null ? null : ManagedArtifactId.Create(record.RunId, verification.Id),
            previews));
    }

    public ApplicationResult<GerberReviewProjection> Preview(
        GerberReviewRequest request,
        CancellationToken cancellationToken = default) => Get(request, cancellationToken);

    public ApplicationResult<GerberReviewProjection> Decide(
        GerberDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ClientMutationId) ||
            (!request.Accept && string.IsNullOrWhiteSpace(request.Reason)))
        {
            return ApplicationResult<GerberReviewProjection>.Failure(new ApplicationError(
                "human-decision-invalid", ApplicationErrorCategory.Validation,
                "Human decision input is invalid.", false));
        }

        ManagedProjectPackRunStore store = ManagedProjectPackRunStore.Create(request.Snapshot);
        ProjectPackRunReadResult current = store.Read(request.RunId);
        if (!current.Succeeded || current.Record is null)
        {
            return ApplicationResult<GerberReviewProjection>.Failure(new ApplicationError(
                current.Diagnostic?.ErrorCode ?? "run-not-found", ApplicationErrorCategory.NotFound,
                current.Diagnostic?.Summary ?? "Review run was not found.", false));
        }
        if (current.Record.Revision != request.ExpectedRevision)
        {
            return ApplicationResult<GerberReviewProjection>.Failure(new ApplicationError(
                "revision-conflict", ApplicationErrorCategory.Conflict,
                "Run revision changed before the human decision.", false));
        }

        cancellationToken.ThrowIfCancellationRequested();
        ProjectPackAcceptanceService service = new(store);
        ProjectPackRunMutationResult result = request.Accept
            ? service.Accept(request.RunId, "desktop-user", request.Reason, DateTimeOffset.UtcNow)
            : service.Reject(request.RunId, "desktop-user", request.Reason, DateTimeOffset.UtcNow);
        if (!result.Succeeded)
        {
            return ApplicationResult<GerberReviewProjection>.Failure(new ApplicationError(
                result.Diagnostic?.ErrorCode ?? "human-decision-failed", ApplicationErrorCategory.Conflict,
                result.Diagnostic?.Summary ?? "Human decision failed closed.", false));
        }

        return Get(new GerberReviewRequest(request.Snapshot, request.RunId), cancellationToken);
    }
}
