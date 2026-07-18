using System.Collections.ObjectModel;
using CSharpAiCli.Core;
using CSharpAiCli.ProjectPacks;
using CSharpAiCli.ProjectPacks.Runtime;

namespace CSharpAiCli.Application;

public sealed record ArtifactListRequest(
    CliEnvironmentSnapshot Snapshot,
    int PageSize = ApplicationLimits.DefaultPageSize,
    string? RunId = null,
    string? Status = null);

public sealed record ArtifactGetRequest(
    CliEnvironmentSnapshot Snapshot,
    string ArtifactId);

public sealed record ArtifactReviewRequest(CliEnvironmentSnapshot Snapshot, string ArtifactId);

public sealed record ArtifactExportRequest(
    CliEnvironmentSnapshot Snapshot,
    string ArtifactId,
    string DestinationPath,
    string ClientMutationId);

public sealed record ArtifactOwnerProjection(
    string RunId,
    string? JobId,
    string? QueueId,
    string? RootRunId,
    string? ParentRunId,
    int Attempt);

public sealed record ArtifactRetentionProjection(
    string Class,
    bool Owned,
    bool Prunable,
    int DefaultMinimumAgeDays);

public sealed record ArtifactMetadataProjection(
    string ArtifactId,
    string PointerId,
    string Kind,
    string Ownership,
    string RelativePath,
    long? Size,
    string? Sha256,
    string Availability,
    string Verification,
    string RunState,
    DateTimeOffset DeclaredAtUtc,
    DateTimeOffset UpdatedAtUtc,
    ArtifactOwnerProjection Owner,
    ArtifactRetentionProjection Retention);

public sealed record ArtifactListProjection
{
    public ArtifactListProjection(IReadOnlyList<ArtifactMetadataProjection>? Artifacts, bool Truncated)
    {
        this.Artifacts = new ReadOnlyCollection<ArtifactMetadataProjection>((Artifacts ?? []).ToArray());
        this.Truncated = Truncated;
    }

    public IReadOnlyList<ArtifactMetadataProjection> Artifacts { get; }

    public bool Truncated { get; }
}

public sealed record ArtifactReviewProjection(
    string ArtifactId,
    string Kind,
    string Availability,
    bool Verified,
    long? ObservedSize,
    bool PreviewAvailable,
    bool CorrectnessProof,
    string? DiagnosticCode,
    string SafeMessage);

public sealed record ArtifactExportProjection(string ArtifactId, bool Exported, string FileName, long Size);

public sealed class ArtifactApplicationService
{
    private readonly Func<CliEnvironmentSnapshot, ManagedArtifactStore> storeFactory;

    public ArtifactApplicationService()
        : this(snapshot => ManagedArtifactStore.Create(snapshot))
    {
    }

    internal ArtifactApplicationService(Func<CliEnvironmentSnapshot, ManagedArtifactStore> storeFactory)
    {
        this.storeFactory = storeFactory ?? throw new ArgumentNullException(nameof(storeFactory));
    }

    public ApplicationResult<ArtifactListProjection> List(
        ArtifactListRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        ApplicationError? pageError = ApplicationLimits.ValidatePageSize(request.PageSize);
        if (pageError is not null)
        {
            return ApplicationResult<ArtifactListProjection>.Failure(pageError);
        }

        ManagedArtifactListResult list = storeFactory(request.Snapshot).List(request.RunId, request.Status);
        cancellationToken.ThrowIfCancellationRequested();
        ArtifactMetadataProjection[] projected = list.Artifacts.Select(item =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Project(item.Manifest, item.Artifact);
        }).ToArray();
        ApplicationDiagnostic[] diagnostics = list.Diagnostics.Select(diagnostic =>
            new ApplicationDiagnostic(
                diagnostic.ErrorCode,
                ApplicationProjection.CategoryForCode(diagnostic.ErrorCode),
                diagnostic.Summary)).ToArray();
        bool truncated = projected.Length > request.PageSize || diagnostics.Length > ApplicationLimits.MaxDiagnostics;
        return ApplicationResult<ArtifactListProjection>.Success(
            new ArtifactListProjection(projected.Take(request.PageSize).ToArray(), truncated),
            diagnostics,
            truncated);
    }

    public ApplicationResult<ArtifactMetadataProjection> Get(
        ArtifactGetRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        ManagedArtifactReadResult read = storeFactory(request.Snapshot).Read(request.ArtifactId);
        cancellationToken.ThrowIfCancellationRequested();
        if (!read.Succeeded || read.Manifest is null || read.Artifact is null)
        {
            string code = read.Diagnostic?.ErrorCode ?? ManagedArtifactErrorCode.NotFound;
            string category = ApplicationProjection.CategoryForCode(code);
            return ApplicationResult<ArtifactMetadataProjection>.Failure(new ApplicationError(
                code,
                category,
                read.Diagnostic?.Summary ?? "Managed artifact was not found.",
                Retryable: category == ApplicationErrorCategory.Unavailable));
        }

        return ApplicationResult<ArtifactMetadataProjection>.Success(Project(read.Manifest, read.Artifact));
    }

    public ApplicationResult<ArtifactReviewProjection> Verify(
        ArtifactReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        ManagedArtifactStore store = storeFactory(request.Snapshot);
        ManagedArtifactReadResult read = store.Read(request.ArtifactId);
        if (!read.Succeeded || read.Artifact is null)
        {
            return ApplicationResult<ArtifactReviewProjection>.Failure(ArtifactFailure(read.Diagnostic));
        }

        ManagedArtifactVerificationResult verification = store.Verify(request.ArtifactId, request.Snapshot.Workspace);
        cancellationToken.ThrowIfCancellationRequested();
        return ApplicationResult<ArtifactReviewProjection>.Success(new ArtifactReviewProjection(
            ApplicationProjection.Safe(request.ArtifactId, 256),
            ApplicationProjection.Safe(read.Artifact.Kind, 256),
            ApplicationProjection.Safe(verification.Availability, 128),
            verification.Succeeded && verification.Availability == ManagedArtifactAvailability.Available,
            verification.ObservedSize,
            IsPreviewKind(read.Artifact.Kind) && verification.Succeeded,
            CorrectnessProof: false,
            ApplicationProjection.SafeOrNull(verification.Diagnostic?.ErrorCode, 256),
            ApplicationProjection.Safe(
                verification.Diagnostic?.Summary ?? "Managed artifact identity matches its retained manifest.",
                4096)));
    }

    public ApplicationResult<ArtifactReviewProjection> Preview(
        ArtifactReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ApplicationResult<ArtifactReviewProjection> verified = Verify(request, cancellationToken);
        if (!verified.Succeeded || verified.Data is null)
        {
            return verified;
        }

        ArtifactReviewProjection data = verified.Data;
        return ApplicationResult<ArtifactReviewProjection>.Success(data with
        {
            SafeMessage = data.PreviewAvailable
                ? "A managed preview is available for human inspection; it is not correctness proof."
                : "No managed preview is available; preview metadata is not correctness proof.",
            CorrectnessProof = false
        });
    }

    public ApplicationResult<ArtifactExportProjection> Export(
        ArtifactExportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ClientMutationId))
        {
            return ApplicationResult<ArtifactExportProjection>.Failure(new ApplicationError(
                "artifact-export-mutation-invalid", ApplicationErrorCategory.Validation,
                "Artifact export mutation identity is invalid.", false));
        }

        try
        {
            string destination = Path.GetFullPath(request.DestinationPath);
            string workspaceRoot = Path.TrimEndingDirectorySeparator(request.Snapshot.Workspace.RootPath);
            if (destination.Equals(workspaceRoot, PathComparison) ||
                destination.StartsWith(workspaceRoot + Path.DirectorySeparatorChar, PathComparison) ||
                File.Exists(destination) || Directory.Exists(destination))
            {
                return ApplicationResult<ArtifactExportProjection>.Failure(new ApplicationError(
                    "artifact-export-destination-denied", ApplicationErrorCategory.Denied,
                    "Export destination must be a new file outside the workspace.", false));
            }

            string? parent = Path.GetDirectoryName(destination);
            if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
            {
                return ApplicationResult<ArtifactExportProjection>.Failure(new ApplicationError(
                    "artifact-export-destination-invalid", ApplicationErrorCategory.Validation,
                    "Export destination directory does not exist.", false));
            }

            ManagedProjectPackRunStore.EnsureNoReparseInExistingChain(parent);
            ManagedArtifactStore store = storeFactory(request.Snapshot);
            ManagedArtifactReadResult read = store.Read(request.ArtifactId);
            if (!read.Succeeded || read.Manifest is null || read.Artifact is null)
            {
                return ApplicationResult<ArtifactExportProjection>.Failure(ArtifactFailure(read.Diagnostic));
            }
            if (read.Artifact.Ownership != ManagedArtifactOwnership.Managed)
            {
                return ApplicationResult<ArtifactExportProjection>.Failure(new ApplicationError(
                    "artifact-export-ownership-denied", ApplicationErrorCategory.Denied,
                    "Only C-AICLI managed artifacts can be exported.", false));
            }

            ManagedArtifactVerificationResult verified = store.Verify(request.ArtifactId, request.Snapshot.Workspace);
            if (!verified.Succeeded || verified.Availability != ManagedArtifactAvailability.Available)
            {
                return ApplicationResult<ArtifactExportProjection>.Failure(ArtifactFailure(verified.Diagnostic));
            }

            ManagedProjectPackRunLayout layout = ManagedProjectPackRunStore.Create(request.Snapshot).GetLayout(read.Manifest.RunId);
            string source = Path.GetFullPath(Path.Combine(layout.RunRoot,
                read.Artifact.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (!source.StartsWith(Path.TrimEndingDirectorySeparator(layout.RunRoot) + Path.DirectorySeparatorChar,
                    PathComparison))
            {
                return ApplicationResult<ArtifactExportProjection>.Failure(new ApplicationError(
                    "artifact-export-path-unsafe", ApplicationErrorCategory.Denied,
                    "Managed artifact path is unsafe.", false));
            }

            cancellationToken.ThrowIfCancellationRequested();
            using FileStream input = new(source, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024,
                FileOptions.SequentialScan);
            using FileStream output = new(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                128 * 1024, FileOptions.WriteThrough);
            input.CopyTo(output);
            output.Flush(flushToDisk: true);
            return ApplicationResult<ArtifactExportProjection>.Success(new ArtifactExportProjection(
                request.ArtifactId, true, Path.GetFileName(destination), output.Length));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            NotSupportedException or ArgumentException or ProjectPackContractException)
        {
            return ApplicationResult<ArtifactExportProjection>.Failure(new ApplicationError(
                "artifact-export-failed", ApplicationErrorCategory.Unavailable,
                "Managed artifact could not be exported safely.", true));
        }
    }

    private static bool IsPreviewKind(string kind) => kind.Contains("preview", StringComparison.OrdinalIgnoreCase) ||
        kind.Contains("contact-sheet", StringComparison.OrdinalIgnoreCase);

    private static ApplicationError ArtifactFailure(ManagedArtifactDiagnostic? diagnostic)
    {
        string code = diagnostic?.ErrorCode ?? ManagedArtifactErrorCode.NotFound;
        string category = ApplicationProjection.CategoryForCode(code);
        return new ApplicationError(code, category,
            diagnostic?.Summary ?? "Managed artifact was not found.",
            category == ApplicationErrorCategory.Unavailable);
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static ArtifactMetadataProjection Project(
        ManagedArtifactManifest manifest,
        ManagedArtifactEntry artifact)
    {
        return new ArtifactMetadataProjection(
            ApplicationProjection.Safe(artifact.ArtifactId, 256),
            ApplicationProjection.Safe(artifact.PointerId, 256),
            ApplicationProjection.Safe(artifact.Kind, 256),
            ApplicationProjection.Safe(artifact.Ownership, 128),
            SafeRelativePath(artifact.Path),
            artifact.Size,
            ApplicationProjection.SafeOrNull(artifact.Sha256, 128),
            ApplicationProjection.Safe(artifact.Availability, 128),
            ApplicationProjection.Safe(artifact.Verification, 128),
            ApplicationProjection.Safe(manifest.RunState, 128),
            artifact.DeclaredAtUtc,
            manifest.UpdatedAtUtc,
            new ArtifactOwnerProjection(
                ApplicationProjection.Safe(manifest.Owner.RunId, 256),
                ApplicationProjection.SafeOrNull(manifest.Owner.JobId, 256),
                ApplicationProjection.SafeOrNull(manifest.Owner.QueueId, 256),
                ApplicationProjection.SafeOrNull(manifest.Owner.RootRunId, 256),
                ApplicationProjection.SafeOrNull(manifest.Owner.ParentRunId, 256),
                manifest.Owner.Attempt),
            new ArtifactRetentionProjection(
                ApplicationProjection.Safe(artifact.Retention.Class, 128),
                artifact.Retention.Owned,
                artifact.Retention.Prunable,
                artifact.Retention.DefaultMinimumAgeDays));
    }

    private static string SafeRelativePath(string value)
    {
        if (Path.IsPathRooted(value) || Uri.TryCreate(value, UriKind.Absolute, out _))
        {
            return "[managed path hidden]";
        }
        return ApplicationProjection.Safe(value.Replace('\\', '/'), 2_048);
    }
}
