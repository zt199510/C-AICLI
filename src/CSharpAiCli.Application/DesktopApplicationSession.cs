using System.Collections.ObjectModel;
using CSharpAiCli.Core;

namespace CSharpAiCli.Application;

public sealed class DesktopApplicationSessionOpenResult
{
    internal DesktopApplicationSessionOpenResult(
        DesktopApplicationSession? session,
        ApplicationError? error,
        IReadOnlyList<ApplicationDiagnostic>? diagnostics = null)
    {
        Session = session;
        Error = error;
        Diagnostics = new ReadOnlyCollection<ApplicationDiagnostic>((diagnostics ?? []).ToArray());
    }

    public bool Succeeded => Session is not null;

    public DesktopApplicationSession? Session { get; }

    public ApplicationError? Error { get; }

    public IReadOnlyList<ApplicationDiagnostic> Diagnostics { get; }
}

public sealed class DesktopApplicationSessionFactory
{
    private readonly WorkspaceApplicationService workspaceService;

    public DesktopApplicationSessionFactory()
        : this(new WorkspaceApplicationService())
    {
    }

    internal DesktopApplicationSessionFactory(WorkspaceApplicationService workspaceService)
    {
        this.workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
    }

    public DesktopApplicationSessionOpenResult Open(
        string? path,
        string? currentDirectory = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CliEnvironmentSnapshot snapshot;
        try
        {
            snapshot = CliEnvironmentSnapshot.Create(path, currentDirectory);
        }
        catch (Exception exception) when (exception is ArgumentException
            or IOException
            or NotSupportedException
            or PathTooLongException)
        {
            return new DesktopApplicationSessionOpenResult(
                null,
                new ApplicationError(
                    "workspace-open-failed",
                    ApplicationErrorCategory.Workspace,
                    "Workspace could not be opened safely.",
                    Retryable: false));
        }

        cancellationToken.ThrowIfCancellationRequested();
        ApplicationResult<WorkspaceSnapshotProjection> projected =
            workspaceService.Snapshot(snapshot, cancellationToken);
        if (!projected.Succeeded || projected.Data is null)
        {
            return new DesktopApplicationSessionOpenResult(
                null,
                projected.Error ?? new ApplicationError(
                    "workspace-open-failed",
                    ApplicationErrorCategory.Workspace,
                    "Workspace could not be opened safely.",
                    Retryable: false),
                projected.Diagnostics);
        }

        return new DesktopApplicationSessionOpenResult(
            new DesktopApplicationSession(snapshot, projected.Data),
            null,
            projected.Diagnostics);
    }
}

public sealed record DesktopChangedFileProjection(string Path, string Status);

public sealed record DesktopChangesProjection
{
    public DesktopChangesProjection(
        string Status,
        int ExitCode,
        string GitStatusSummary,
        bool GitStatusSucceeded,
        string? GitStatusErrorCode,
        bool Dirty,
        string DiffStatSummary,
        bool DiffSucceeded,
        string? DiffErrorCode,
        bool DiffTruncated,
        IReadOnlyList<DesktopChangedFileProjection>? ChangedFiles,
        string? SessionSource,
        string? SessionName,
        IReadOnlyList<string>? Warnings)
    {
        this.Status = Status;
        this.ExitCode = ExitCode;
        this.GitStatusSummary = GitStatusSummary;
        this.GitStatusSucceeded = GitStatusSucceeded;
        this.GitStatusErrorCode = GitStatusErrorCode;
        this.Dirty = Dirty;
        this.DiffStatSummary = DiffStatSummary;
        this.DiffSucceeded = DiffSucceeded;
        this.DiffErrorCode = DiffErrorCode;
        this.DiffTruncated = DiffTruncated;
        this.ChangedFiles = new ReadOnlyCollection<DesktopChangedFileProjection>((ChangedFiles ?? []).ToArray());
        this.SessionSource = SessionSource;
        this.SessionName = SessionName;
        this.Warnings = new ReadOnlyCollection<string>((Warnings ?? []).ToArray());
    }

    public string Status { get; }
    public int ExitCode { get; }
    public string GitStatusSummary { get; }
    public bool GitStatusSucceeded { get; }
    public string? GitStatusErrorCode { get; }
    public bool Dirty { get; }
    public string DiffStatSummary { get; }
    public bool DiffSucceeded { get; }
    public string? DiffErrorCode { get; }
    public bool DiffTruncated { get; }
    public IReadOnlyList<DesktopChangedFileProjection> ChangedFiles { get; }
    public string? SessionSource { get; }
    public string? SessionName { get; }
    public IReadOnlyList<string> Warnings { get; }
}

public sealed class DesktopApplicationSession : IDisposable
{
    private readonly CliEnvironmentSnapshot snapshot;
    private readonly CancellationTokenSource lifetime = new();
    private readonly ThreadApplicationService threadService = new();
    private readonly CatalogApplicationService catalogService = new();
    private readonly ChangesApplicationService changesService = new();
    private readonly ReportApplicationService reportService = new();
    private readonly ArtifactApplicationService artifactService = new();
    private readonly GerberReviewApplicationService gerberReviewService = new();
    private readonly ControlledContextApplicationService contextService;
    private readonly ComposerApplicationService composerService;
    private readonly TurnExecutionApplicationService turnExecutionService = new();

    internal DesktopApplicationSession(
        CliEnvironmentSnapshot snapshot,
        WorkspaceSnapshotProjection workspace)
    {
        this.snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        contextService = new ControlledContextApplicationService();
        composerService = new ComposerApplicationService(contextService);
    }

    public WorkspaceSnapshotProjection Workspace { get; }

    public ApplicationResult<ThreadSummaryProjection> CreateThread(
        string title,
        CancellationToken cancellationToken = default) => Execute(
            token => threadService.Create(new ThreadCreateRequest(snapshot, title), token),
            cancellationToken);

    public ApplicationResult<ThreadListProjection> ListThreads(
        int pageSize,
        CancellationToken cancellationToken = default) => Execute(
            token => threadService.List(new ThreadListRequest(snapshot, Workspace.WorkspaceId, pageSize), token),
            cancellationToken);

    public ApplicationResult<ThreadDetailProjection> GetThread(
        string threadId,
        long afterSequence,
        int timelinePageSize,
        CancellationToken cancellationToken = default,
        string? ownedActiveTurnId = null) => Execute(
            token => threadService.Get(
                new ThreadGetRequest(snapshot, threadId, afterSequence, timelinePageSize, ownedActiveTurnId),
                token),
            cancellationToken);

    public ApplicationResult<ThreadSummaryProjection> RenameThread(
        string threadId,
        long expectedRevision,
        string title,
        CancellationToken cancellationToken = default) => Execute(
            token => threadService.Rename(
                new ThreadRenameRequest(snapshot, threadId, expectedRevision, title),
                token),
            cancellationToken);

    public ApplicationResult<ThreadSummaryProjection> ArchiveThread(
        string threadId,
        long expectedRevision,
        CancellationToken cancellationToken = default) => Execute(token =>
        {
            ApplicationResult<ComposerStateProjection> composer = composerService.Get(new ComposerGetRequest(snapshot, threadId), token);
            if (!composer.Succeeded)
            {
                return ApplicationResult<ThreadSummaryProjection>.Failure(
                    composer.Error ?? new ApplicationError("composer-queue-unavailable", ApplicationErrorCategory.Unavailable,
                        "Composer queue could not be checked before archive.", true), composer.Diagnostics);
            }
            if (composer.Succeeded && composer.Data?.PendingIntent is not null)
            {
                return ApplicationResult<ThreadSummaryProjection>.Failure(new ApplicationError(
                    "composer-intent-pending", ApplicationErrorCategory.Conflict,
                    "Clear the pending composer intent before archiving this thread.", false));
            }
            return threadService.Archive(new ThreadArchiveRequest(snapshot, threadId, expectedRevision), token);
        }, cancellationToken);

    public ApplicationResult<ThreadDeleteProjection> DeleteThread(
        string threadId,
        long expectedRevision,
        string confirmation,
        CancellationToken cancellationToken = default) => Execute(
            token => threadService.Delete(
                new ThreadDeleteRequest(snapshot, threadId, expectedRevision, confirmation),
                token),
            cancellationToken);

    public ApplicationResult<CatalogQueryProjection> ListCatalog(
        string kind,
        int pageSize,
        CancellationToken cancellationToken = default) => Execute(
            token => catalogService.Query(new CatalogQueryRequest(snapshot.Workspace, kind, pageSize), token),
            cancellationToken);

    public ApplicationResult<ControlledContextSearchProjection> SearchContext(
        string query,
        CancellationToken cancellationToken = default) => Execute(
            token => contextService.Search(Workspace, query, token), cancellationToken);

    public ApplicationResult<ControlledContextDescriptor> ResolveContext(
        string nativePath,
        string expectedKind,
        CancellationToken cancellationToken = default) => Execute(
            token => contextService.ResolveNativePath(Workspace, nativePath, expectedKind, token), cancellationToken);

    public ApplicationResult<ComposerStateProjection> GetComposer(
        string threadId,
        CancellationToken cancellationToken = default) => Execute(
            token => composerService.Get(new ComposerGetRequest(snapshot, threadId), token), cancellationToken);

    public ApplicationResult<ComposerStateProjection> EnqueueComposer(
        string threadId,
        long expectedThreadRevision,
        long expectedQueueRevision,
        string clientMutationId,
        string prompt,
        IReadOnlyList<string> contextSelectionIds,
        IReadOnlyList<ComposerCatalogSelection> catalogSelections,
        CancellationToken cancellationToken = default) => Execute(
            token => composerService.Enqueue(new ComposerEnqueueRequest(
                snapshot, threadId, expectedThreadRevision, expectedQueueRevision, clientMutationId,
                prompt, contextSelectionIds, catalogSelections), token), cancellationToken);

    public ApplicationResult<ComposerStateProjection> ClearComposer(
        string threadId,
        long expectedQueueRevision,
        string clientMutationId,
        CancellationToken cancellationToken = default) => Execute(
            token => composerService.Clear(new ComposerClearRequest(
                snapshot, threadId, expectedQueueRevision, clientMutationId), token), cancellationToken);

    public ApplicationResult<TurnExecutionStateProjection> StartTurn(
        string threadId,
        long expectedThreadRevision,
        long expectedQueueRevision,
        string clientMutationId,
        CancellationToken cancellationToken = default) => Execute(
            token => turnExecutionService.Start(new TurnStartRequest(snapshot, threadId, expectedThreadRevision,
                expectedQueueRevision, clientMutationId), token), cancellationToken);

    public ApplicationResult<TurnExecutionStateProjection> CancelTurn(
        string threadId,
        string turnId,
        long expectedThreadRevision,
        long expectedTurnRevision,
        string clientMutationId,
        CancellationToken cancellationToken = default) => Execute(
            _ => turnExecutionService.Cancel(new TurnCancelRequest(snapshot, threadId, turnId,
                expectedThreadRevision, expectedTurnRevision, clientMutationId)), cancellationToken);

    public ApplicationResult<TurnExecutionStateProjection> ResolveApproval(
        string threadId,
        string turnId,
        string requestId,
        string decision,
        long expectedThreadRevision,
        long expectedTurnRevision,
        long expectedApprovalRevision,
        string clientMutationId,
        CancellationToken cancellationToken = default) => Execute(
            _ => turnExecutionService.ResolveApproval(new ApprovalResolveRequest(snapshot, threadId, turnId,
                requestId, decision, expectedThreadRevision, expectedTurnRevision, expectedApprovalRevision, clientMutationId)), cancellationToken);

    public ApplicationResult<TurnExecutionStateProjection> ResumeTurn(
        string threadId,
        string turnId,
        long expectedThreadRevision,
        long expectedTurnRevision,
        string checkpointId,
        string clientMutationId,
        CancellationToken cancellationToken = default) => Execute(_ =>
            ApplicationResult<TurnExecutionStateProjection>.Failure(new ApplicationError(
                "restart-required", ApplicationErrorCategory.Conflict,
                "This runtime has no verified safe checkpoint; explicit restart is required.", false)), cancellationToken);

    public ApplicationResult<TurnExecutionStateProjection> RestartTurn(
        string threadId,
        string sourceTurnId,
        long expectedThreadRevision,
        long expectedSourceTurnRevision,
        bool confirmed,
        string clientMutationId,
        CancellationToken cancellationToken = default) => Execute(
            _ => turnExecutionService.Restart(new TurnRestartRequest(snapshot, threadId, sourceTurnId,
                expectedThreadRevision, expectedSourceTurnRevision, confirmed, clientMutationId)), cancellationToken);

    public async Task<ApplicationResult<TurnExecutionStateProjection>> ExecuteTurnAsync(
        string threadId,
        string turnId,
        ITurnExecutionRuntime runtime,
        IInteractiveApprovalWaiter approvalWaiter,
        Func<TurnExecutionStateProjection, ValueTask>? committed = null,
        CancellationToken cancellationToken = default)
    {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
        return await turnExecutionService.ExecuteAsync(snapshot, threadId, turnId, runtime, approvalWaiter,
            committed, linked.Token).ConfigureAwait(false);
    }

    public ApplicationResult<DesktopChangesProjection> GetChanges(
        string? sessionName,
        CancellationToken cancellationToken = default) => Execute(token =>
        {
            ConversationSessionName? session = null;
            if (!string.IsNullOrWhiteSpace(sessionName))
            {
                try
                {
                    session = ConversationSessionName.Parse(sessionName);
                }
                catch (ArgumentException)
                {
                    return ApplicationResult<DesktopChangesProjection>.Failure(new ApplicationError(
                        "changes-session-invalid",
                        ApplicationErrorCategory.Validation,
                        "Session name is invalid.",
                        Retryable: false));
                }
            }

            ApplicationResult<ChangesViewReport> result =
                changesService.Query(new ChangesQueryRequest(snapshot, session), token);
            if (!result.Succeeded || result.Data is null)
            {
                return ApplicationResult<DesktopChangesProjection>.Failure(
                    result.Error ?? new ApplicationError(
                        "changes-query-failed",
                        ApplicationErrorCategory.Internal,
                        "Changes query failed.",
                        Retryable: false),
                    result.Diagnostics);
            }

            ChangesViewReport data = result.Data;
            return ApplicationResult<DesktopChangesProjection>.Success(
                new DesktopChangesProjection(
                    data.Status,
                    data.ExitCode,
                    data.GitStatusSummary,
                    data.GitStatusSucceeded,
                    data.GitStatusErrorCode,
                    data.Dirty,
                    data.DiffStatSummary,
                    data.DiffSucceeded,
                    data.DiffErrorCode,
                    data.DiffTruncated,
                    data.ChangedFiles.Select(file => new DesktopChangedFileProjection(file.Path, file.Status)).ToArray(),
                    data.Session?.Source,
                    data.Session?.Name,
                    data.Warnings),
                result.Diagnostics,
                result.Truncated);
        }, cancellationToken);

    public ApplicationResult<ReportListProjection> ListReports(
        int pageSize,
        CancellationToken cancellationToken = default) => Execute(
            token => reportService.List(new ReportListRequest(snapshot, pageSize), token),
            cancellationToken);

    public ApplicationResult<ReportDetailProjection> GetReport(
        string reportId,
        CancellationToken cancellationToken = default) => Execute(
            token => reportService.Get(new ReportGetRequest(snapshot, reportId), token),
            cancellationToken);

    public ApplicationResult<ArtifactListProjection> ListArtifacts(
        int pageSize,
        string? runId,
        string? status,
        CancellationToken cancellationToken = default) => Execute(
            token => artifactService.List(new ArtifactListRequest(snapshot, pageSize, runId, status), token),
            cancellationToken);

    public ApplicationResult<ArtifactMetadataProjection> GetArtifact(
        string artifactId,
        CancellationToken cancellationToken = default) => Execute(
            token => artifactService.Get(new ArtifactGetRequest(snapshot, artifactId), token),
            cancellationToken);

    public ApplicationResult<ArtifactReviewProjection> PreviewArtifact(
        string artifactId,
        CancellationToken cancellationToken = default) => Execute(
            token => artifactService.Preview(new ArtifactReviewRequest(snapshot, artifactId), token), cancellationToken);

    public ApplicationResult<ArtifactReviewProjection> VerifyArtifact(
        string artifactId,
        CancellationToken cancellationToken = default) => Execute(
            token => artifactService.Verify(new ArtifactReviewRequest(snapshot, artifactId), token), cancellationToken);

    public ApplicationResult<ArtifactExportProjection> ExportArtifact(
        string artifactId,
        string destinationPath,
        string clientMutationId,
        CancellationToken cancellationToken = default) => Execute(
            token => artifactService.Export(new ArtifactExportRequest(
                snapshot, artifactId, destinationPath, clientMutationId), token), cancellationToken);

    public ApplicationResult<GerberReviewProjection> GetGerberReview(
        string runId,
        CancellationToken cancellationToken = default) => Execute(
            token => gerberReviewService.Get(new GerberReviewRequest(snapshot, runId), token), cancellationToken);

    public ApplicationResult<GerberReviewProjection> GetGerberPreview(
        string runId,
        CancellationToken cancellationToken = default) => Execute(
            token => gerberReviewService.Preview(new GerberReviewRequest(snapshot, runId), token), cancellationToken);

    public ApplicationResult<GerberReviewProjection> DecideGerberReview(
        string runId,
        long expectedRevision,
        string reason,
        string clientMutationId,
        bool accept,
        CancellationToken cancellationToken = default) => Execute(
            token => gerberReviewService.Decide(new GerberDecisionRequest(
                snapshot, runId, expectedRevision, reason, clientMutationId, accept), token), cancellationToken);

    public void Dispose()
    {
        contextService.Clear();
        lifetime.Cancel();
        lifetime.Dispose();
    }

    private T Execute<T>(Func<CancellationToken, T> action, CancellationToken cancellationToken)
    {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            lifetime.Token,
            cancellationToken);
        return action(linked.Token);
    }
}
