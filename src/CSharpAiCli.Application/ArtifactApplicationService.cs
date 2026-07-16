using System.Collections.ObjectModel;
using CSharpAiCli.Core;
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

    private static ArtifactMetadataProjection Project(
        ManagedArtifactManifest manifest,
        ManagedArtifactEntry artifact)
    {
        return new ArtifactMetadataProjection(
            ApplicationProjection.Safe(artifact.ArtifactId, 256),
            ApplicationProjection.Safe(artifact.PointerId, 256),
            ApplicationProjection.Safe(artifact.Kind, 256),
            ApplicationProjection.Safe(artifact.Ownership, 128),
            ApplicationProjection.Safe(artifact.Path, 2_048),
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
}
