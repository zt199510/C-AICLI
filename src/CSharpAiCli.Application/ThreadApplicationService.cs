using System.Text.Json;
using CSharpAiCli.Core;
using CSharpAiCli.ProjectPacks.Runtime;

namespace CSharpAiCli.Application;

public sealed class ThreadApplicationService
{
    private readonly Func<CliEnvironmentSnapshot, ThreadStore> storeFactory;
    private readonly Func<CliEnvironmentSnapshot, FileConversationStore> conversationStoreFactory;
    private readonly Func<DateTimeOffset> clock;
    private readonly WorkspaceApplicationService workspaceService;

    public ThreadApplicationService()
        : this(
            ThreadStore.Create,
            FileConversationStore.Create,
            () => DateTimeOffset.UtcNow,
            new WorkspaceApplicationService())
    {
    }

    internal ThreadApplicationService(
        Func<CliEnvironmentSnapshot, ThreadStore> storeFactory,
        Func<CliEnvironmentSnapshot, FileConversationStore> conversationStoreFactory,
        Func<DateTimeOffset> clock,
        WorkspaceApplicationService? workspaceService = null)
    {
        this.storeFactory = storeFactory ?? throw new ArgumentNullException(nameof(storeFactory));
        this.conversationStoreFactory = conversationStoreFactory ?? throw new ArgumentNullException(nameof(conversationStoreFactory));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.workspaceService = workspaceService ?? new WorkspaceApplicationService();
    }

    public ApplicationResult<ThreadSummaryProjection> Create(
        ThreadCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        ApplicationResult<WorkspaceSnapshotProjection> workspace = workspaceService.Snapshot(request.Snapshot, cancellationToken);
        if (!workspace.Succeeded || workspace.Data is null)
        {
            return ApplicationResult<ThreadSummaryProjection>.Failure(
                workspace.Error ?? new ApplicationError("workspace-unavailable", ApplicationErrorCategory.Workspace, "Workspace could not be opened.", false),
                workspace.Diagnostics);
        }

        string title = ApplicationProjection.Safe(request.Title?.Trim(), ThreadPersistenceLimits.MaxTitleBytes);
        try
        {
            ThreadContractValidator.ValidateTitle(title);
        }
        catch (ThreadContractException exception)
        {
            return Failure<ThreadSummaryProjection>(exception.ErrorCode, exception.Message);
        }

        DateTimeOffset now = clock().ToUniversalTime();
        ThreadRecord record = new()
        {
            ThreadId = ThreadIdentity.CreateThreadId(),
            WorkspaceId = workspace.Data.WorkspaceId,
            WorkspaceRootIdentity = workspace.Data.RootPath,
            Title = title,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        cancellationToken.ThrowIfCancellationRequested();
        ThreadStoreMutationResult created = storeFactory(request.Snapshot).Create(record);
        cancellationToken.ThrowIfCancellationRequested();
        return created.Succeeded && created.Aggregate is not null
            ? ApplicationResult<ThreadSummaryProjection>.Success(ProjectSummary(created.Aggregate.Record))
            : StoreFailure<ThreadSummaryProjection>(created.Diagnostic);
    }

    public ApplicationResult<ThreadListProjection> List(
        ThreadListRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        ApplicationError? pageError = ApplicationLimits.ValidatePageSize(request.PageSize);
        if (pageError is not null)
        {
            return ApplicationResult<ThreadListProjection>.Failure(pageError);
        }

        if (string.IsNullOrWhiteSpace(request.WorkspaceId))
        {
            return Failure<ThreadListProjection>("workspace-id-invalid", "Workspace id is required.", ApplicationErrorCategory.Validation);
        }

        ThreadStoreListResult list = storeFactory(request.Snapshot).List(request.WorkspaceId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        ThreadSummaryProjection[] projected = list.Records
            .OrderByDescending(record => record.UpdatedAtUtc)
            .ThenBy(record => record.ThreadId, StringComparer.Ordinal)
            .Take(request.PageSize + 1)
            .Select(ProjectSummary)
            .ToArray();
        ApplicationDiagnostic[] diagnostics = list.Diagnostics.Select(ProjectDiagnostic).ToArray();
        bool truncated = projected.Length > request.PageSize || diagnostics.Length > ApplicationLimits.MaxDiagnostics;
        return ApplicationResult<ThreadListProjection>.Success(
            new ThreadListProjection(projected.Take(request.PageSize).ToArray(), truncated),
            diagnostics,
            truncated);
    }

    public ApplicationResult<ThreadDetailProjection> Get(
        ThreadGetRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.TimelinePageSize is < 1 or > 100 || request.AfterSequence < 0)
        {
            return Failure<ThreadDetailProjection>(ThreadErrorCode.TimelineSequenceInvalid, "Timeline cursor or page size is invalid.", ApplicationErrorCategory.Validation);
        }

        ThreadStore store = storeFactory(request.Snapshot);
        ThreadStoreReadResult read = store.Read(request.ThreadId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!read.Succeeded || read.Aggregate is null)
        {
            return StoreFailure<ThreadDetailProjection>(read.Diagnostic);
        }

        ApplicationError? workspaceError = ValidateWorkspaceBinding(request.Snapshot, read.Aggregate.Record, cancellationToken);
        if (workspaceError is not null)
        {
            return ApplicationResult<ThreadDetailProjection>.Failure(workspaceError);
        }

        ThreadTimelinePageResult timeline = store.ReadTimelinePage(
            request.ThreadId, request.AfterSequence, request.TimelinePageSize, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!timeline.Succeeded)
        {
            return StoreFailure<ThreadDetailProjection>(timeline.Diagnostic);
        }

        var diagnostics = new List<ApplicationDiagnostic>();
        TurnSummaryProjection[] turns = read.Aggregate.Turns.Select(turn => ProjectTurn(turn, request.Snapshot, diagnostics, cancellationToken)).ToArray();
        TimelineItemProjection[] items = timeline.Items.Select(item => ProjectItem(item, request.Snapshot, diagnostics, cancellationToken)).ToArray();
        ThreadDetailProjection detail = new(
            ProjectSummary(read.Aggregate.Record),
            turns,
            items,
            timeline.NextSequence,
            timeline.Truncated,
            read.RecoveryRequired);
        bool aggregateTruncated = false;
        while (items.Length > 0 && JsonSerializer.SerializeToUtf8Bytes(detail).Length > ApplicationLimits.TargetAggregateBytes)
        {
            aggregateTruncated = true;
            items = items[..^1];
            detail = detail with
            {
                Timeline = items,
                NextSequence = items.Length > 0 ? items[^1].Sequence : request.AfterSequence,
                TimelineTruncated = true
            };
        }

        cancellationToken.ThrowIfCancellationRequested();
        return ApplicationResult<ThreadDetailProjection>.Success(
            detail,
            diagnostics,
            timeline.Truncated || aggregateTruncated || diagnostics.Count > ApplicationLimits.MaxDiagnostics);
    }

    public ApplicationResult<ThreadSummaryProjection> Rename(
        ThreadRenameRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        ThreadStore store = storeFactory(request.Snapshot);
        ThreadStoreReadResult read = store.Read(request.ThreadId, cancellationToken);
        if (!read.Succeeded || read.Aggregate is null)
        {
            return StoreFailure<ThreadSummaryProjection>(read.Diagnostic);
        }

        ApplicationError? workspaceError = ValidateWorkspaceBinding(request.Snapshot, read.Aggregate.Record, cancellationToken);
        if (workspaceError is not null)
        {
            return ApplicationResult<ThreadSummaryProjection>.Failure(workspaceError);
        }

        string title = ApplicationProjection.Safe(request.Title?.Trim(), ThreadPersistenceLimits.MaxTitleBytes);
        ThreadStoreMutationResult result = store.Rename(
            request.ThreadId, request.ExpectedRevision, title, clock().ToUniversalTime());
        cancellationToken.ThrowIfCancellationRequested();
        return ProjectMutation(result);
    }

    public ApplicationResult<ThreadSummaryProjection> Archive(
        ThreadArchiveRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        ThreadStore store = storeFactory(request.Snapshot);
        ThreadStoreReadResult read = store.Read(request.ThreadId, cancellationToken);
        if (!read.Succeeded || read.Aggregate is null)
        {
            return StoreFailure<ThreadSummaryProjection>(read.Diagnostic);
        }

        ApplicationError? workspaceError = ValidateWorkspaceBinding(request.Snapshot, read.Aggregate.Record, cancellationToken);
        if (workspaceError is not null)
        {
            return ApplicationResult<ThreadSummaryProjection>.Failure(workspaceError);
        }

        ThreadStoreMutationResult result = store.Archive(
            request.ThreadId, request.ExpectedRevision, clock().ToUniversalTime());
        cancellationToken.ThrowIfCancellationRequested();
        return ProjectMutation(result);
    }

    public ApplicationResult<ThreadDeleteProjection> Delete(
        ThreadDeleteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        ThreadStore store = storeFactory(request.Snapshot);
        ThreadStoreReadResult read = store.Read(request.ThreadId, cancellationToken);
        if (!read.Succeeded || read.Aggregate is null)
        {
            return StoreFailure<ThreadDeleteProjection>(read.Diagnostic);
        }

        ApplicationError? workspaceError = ValidateWorkspaceBinding(request.Snapshot, read.Aggregate.Record, cancellationToken);
        if (workspaceError is not null)
        {
            return ApplicationResult<ThreadDeleteProjection>.Failure(workspaceError);
        }

        ThreadStoreMutationResult result = store.Delete(
            request.ThreadId, request.ExpectedRevision, request.Confirmation);
        cancellationToken.ThrowIfCancellationRequested();
        return result.Succeeded
            ? ApplicationResult<ThreadDeleteProjection>.Success(new ThreadDeleteProjection(request.ThreadId, true))
            : StoreFailure<ThreadDeleteProjection>(result.Diagnostic);
    }

    public ApplicationResult<SessionImportPreviewProjection> PreviewSessionImport(
        SessionImportPreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryParseSession(request.SessionName, out ConversationSessionName? sessionName, out ApplicationError? error))
        {
            return ApplicationResult<SessionImportPreviewProjection>.Failure(error!);
        }

        ConversationTranscriptSnapshot snapshot;
        try
        {
            snapshot = conversationStoreFactory(request.Snapshot).ReadBounded(sessionName!);
        }
        catch (ConversationTranscriptReadException exception)
        {
            return Failure<SessionImportPreviewProjection>(exception.ErrorCode, exception.Message);
        }

        cancellationToken.ThrowIfCancellationRequested();
        SessionProjection projection;
        try
        {
            projection = ProjectSession(request.Snapshot, snapshot, cancellationToken);
        }
        catch (ThreadContractException exception)
        {
            return Failure<SessionImportPreviewProjection>(exception.ErrorCode, exception.Message);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return ApplicationResult<SessionImportPreviewProjection>.Success(new SessionImportPreviewProjection(
            projection.Thread.ThreadId,
            ThreadSourceKind.Session,
            ApplicationProjection.Safe(snapshot.SourceIdentity, 256),
            snapshot.Fingerprint,
            projection.Thread.Title,
            projection.Turns.Count,
            projection.Items.Count,
            snapshot.ByteCount,
            snapshot.RecordCount,
            projection.Warnings,
            Truncated: false),
            projection.Warnings.Select(warning => new ApplicationDiagnostic(
                "session-import-warning", ApplicationErrorCategory.Validation, warning)).ToArray());
    }

    public ApplicationResult<SessionImportProjection> ImportSession(
        SessionImportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryParseSession(request.SessionName, out ConversationSessionName? sessionName, out ApplicationError? error))
        {
            return ApplicationResult<SessionImportProjection>.Failure(error!);
        }

        ConversationTranscriptSnapshot snapshot;
        try
        {
            snapshot = conversationStoreFactory(request.Snapshot).ReadBounded(sessionName!);
        }
        catch (ConversationTranscriptReadException exception)
        {
            return Failure<SessionImportProjection>(exception.ErrorCode, exception.Message);
        }

        if (!string.Equals(snapshot.Fingerprint, request.PreviewFingerprint, StringComparison.Ordinal))
        {
            return Failure<SessionImportProjection>(
                ThreadErrorCode.SessionImportSourceChanged,
                "Session source changed after preview.",
                ApplicationErrorCategory.Conflict);
        }

        cancellationToken.ThrowIfCancellationRequested();
        ThreadStore store = storeFactory(request.Snapshot);
        ThreadStoreListResult existing = store.List(cancellationToken: cancellationToken);
        string canonicalSourceId = ApplicationProjection.Safe(snapshot.SourceIdentity, 256);
        ThreadRecord? sameSource = existing.Records.SingleOrDefault(record =>
            record.Origin.Kind == "session-import" && record.Origin.SourceKind == ThreadSourceKind.Session &&
            string.Equals(record.Origin.SourceId, canonicalSourceId, StringComparison.Ordinal));
        if (sameSource is not null)
        {
            if (sameSource.Origin.SourceFingerprint == snapshot.Fingerprint)
            {
                ApplicationError? workspaceError = ValidateWorkspaceBinding(request.Snapshot, sameSource, cancellationToken);
                if (workspaceError is not null)
                {
                    return ApplicationResult<SessionImportProjection>.Failure(workspaceError);
                }

                return ApplicationResult<SessionImportProjection>.Success(new SessionImportProjection(
                    ProjectSummary(sameSource), canonicalSourceId, snapshot.Fingerprint, Idempotent: true));
            }

            return Failure<SessionImportProjection>(
                ThreadErrorCode.SessionImportSourceChanged,
                "Session was previously imported with different content.",
                ApplicationErrorCategory.Conflict);
        }

        SessionProjection projection;
        try
        {
            projection = ProjectSession(request.Snapshot, snapshot, cancellationToken);
        }
        catch (ThreadContractException exception)
        {
            return Failure<SessionImportProjection>(exception.ErrorCode, exception.Message);
        }

        cancellationToken.ThrowIfCancellationRequested();
        ThreadStoreMutationResult created = store.CreateProjection(projection.Thread, projection.Turns, projection.Items);
        cancellationToken.ThrowIfCancellationRequested();
        if (!created.Succeeded || created.Aggregate is null)
        {
            if (created.Diagnostic?.ErrorCode == ThreadErrorCode.ThreadAlreadyExists)
            {
                ThreadStoreReadResult read = store.Read(projection.Thread.ThreadId, cancellationToken);
                if (read.Succeeded && read.Aggregate?.Record.Origin.SourceFingerprint == snapshot.Fingerprint)
                {
                    ApplicationError? workspaceError = ValidateWorkspaceBinding(
                        request.Snapshot, read.Aggregate.Record, cancellationToken);
                    if (workspaceError is not null)
                    {
                        return ApplicationResult<SessionImportProjection>.Failure(workspaceError);
                    }

                    return ApplicationResult<SessionImportProjection>.Success(new SessionImportProjection(
                        ProjectSummary(read.Aggregate.Record), canonicalSourceId, snapshot.Fingerprint, Idempotent: true));
                }
            }

            return StoreFailure<SessionImportProjection>(created.Diagnostic);
        }

        return ApplicationResult<SessionImportProjection>.Success(new SessionImportProjection(
            ProjectSummary(created.Aggregate.Record), canonicalSourceId, snapshot.Fingerprint, Idempotent: false),
            projection.Warnings.Select(warning => new ApplicationDiagnostic(
                "session-import-warning", ApplicationErrorCategory.Validation, warning)).ToArray());
    }

    private SessionProjection ProjectSession(
        CliEnvironmentSnapshot environment,
        ConversationTranscriptSnapshot source,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ApplicationResult<WorkspaceSnapshotProjection> workspace = workspaceService.Snapshot(environment, cancellationToken);
        if (!workspace.Succeeded || workspace.Data is null)
        {
            throw new ThreadContractException("workspace-unavailable", "Workspace could not be opened for session import.");
        }

        ConversationTranscript transcript = source.Transcript;
        string threadId = ThreadIdentity.CreateDeterministicThreadId(ThreadSourceKind.Session, source.SourceIdentity, source.Fingerprint);
        string turnId = ThreadIdentity.CreateDeterministicTurnId(source.Fingerprint, 1);
        ThreadSourcePointerRecord sessionPointer = new()
        {
            Kind = ThreadSourceKind.Session,
            SourceId = ApplicationProjection.Safe(source.SourceIdentity, 256),
            SourceFingerprint = source.Fingerprint,
            Availability = ThreadSourceAvailability.Available
        };
        var inputs = new List<TimelineProjectionInput>();
        for (int index = 0; index < transcript.Messages.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConversationMessage message = transcript.Messages[index];
            string type = string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase)
                ? TimelineItemType.UserMessage
                : TimelineItemType.AssistantMessage;
            string preview = ApplicationProjection.Safe(message.Content, ThreadPersistenceLimits.MaxTimelineSummaryBytes);
            inputs.Add(new TimelineProjectionInput(
                "message", index, message.CreatedAtUtc.ToUniversalTime(), type, "recorded", preview,
                new TimelinePayloadRecord { Message = new TimelineMessagePayloadRecord(preview) }, sessionPointer,
                Redacted: !string.Equals(preview, message.Content, StringComparison.Ordinal)));
        }

        for (int index = 0; index < transcript.ToolCalls.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConversationToolCall call = transcript.ToolCalls[index];
            string summary = ApplicationProjection.Safe(call.OutputSummary ?? call.FailureReason ?? call.ToolName,
                ThreadPersistenceLimits.MaxTimelineSummaryBytes);
            inputs.Add(new TimelineProjectionInput(
                "tool", index, call.CompletedAtUtc.ToUniversalTime(), TimelineItemType.ToolCompleted,
                call.Succeeded ? "completed" : "failed", summary,
                new TimelinePayloadRecord
                {
                    Operation = new TimelineOperationPayloadRecord(
                        ApplicationProjection.Safe(call.ToolName, 1_024), call.Succeeded,
                        ApplicationProjection.SafeOrNull(call.ErrorCode, 256))
                },
                sessionPointer,
                Redacted: true));
        }

        for (int index = 0; index < transcript.Errors.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConversationError item = transcript.Errors[index];
            string code = ApplicationProjection.Safe(item.LocalErrorCode ?? "model-error", 256);
            inputs.Add(new TimelineProjectionInput(
                "error", index, item.CreatedAtUtc.ToUniversalTime(), TimelineItemType.WarningRaised,
                "failed", ApplicationProjection.Safe(item.SafeMessage, ThreadPersistenceLimits.MaxTimelineSummaryBytes),
                new TimelinePayloadRecord { Warning = new TimelineWarningPayloadRecord(code) }, sessionPointer,
                Redacted: true));
        }

        for (int index = 0; index < transcript.AgentRuns.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConversationAgentRun run = transcript.AgentRuns[index];
            DateTimeOffset timestamp = run.CompletedAtUtc.ToUniversalTime();
            if (!string.IsNullOrWhiteSpace(run.PlanSummary))
            {
                string plan = ApplicationProjection.Safe(run.PlanSummary, ThreadPersistenceLimits.MaxTimelineSummaryBytes);
                inputs.Add(new TimelineProjectionInput(
                    "agent-plan", index, timestamp, TimelineItemType.PlanUpdated, "recorded", plan,
                    new TimelinePayloadRecord { Plan = new TimelinePlanPayloadRecord(plan) }, sessionPointer, Redacted: true));
            }

            if (run.ChangedFiles.Count > 0)
            {
                inputs.Add(new TimelineProjectionInput(
                    "agent-changes", index, timestamp, TimelineItemType.ChangesUpdated, "recorded",
                    $"{run.ChangedFiles.Count} changed file(s).",
                    new TimelinePayloadRecord { Changes = new TimelineChangesPayloadRecord(run.ChangedFiles.Count) },
                    sessionPointer,
                    Redacted: true));
            }

            if (run.TaskReport?.Report?.Generated == true)
            {
                string reportId = "session:" + source.SourceIdentity;
                inputs.Add(new TimelineProjectionInput(
                    "agent-report", index, timestamp, TimelineItemType.ReportAvailable, "available",
                    "Session report is available.",
                    new TimelinePayloadRecord { Reference = new TimelineReferencePayloadRecord(reportId) },
                    new ThreadSourcePointerRecord
                    {
                        Kind = ThreadSourceKind.Report,
                        SourceId = reportId,
                        Availability = ThreadSourceAvailability.Available
                    },
                    Redacted: true));
            }
        }

        DateTimeOffset createdAt = transcript.CreatedAtUtc.ToUniversalTime();
        DateTimeOffset completedAt = transcript.UpdatedAtUtc.ToUniversalTime();
        if (completedAt < createdAt)
        {
            throw new ThreadContractException(ThreadErrorCode.ThreadRecordCorrupt, "Session timestamps are not monotonic.");
        }

        ConversationAgentRun? lastRun = transcript.AgentRuns.LastOrDefault();
        bool failed = transcript.Errors.Count > 0 ||
            string.Equals(lastRun?.Status, "failed", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(lastRun?.ErrorCode);
        string stopReason = ApplicationProjection.Safe(lastRun?.StopReason ?? (failed ? "failed" : "completed"), 256);
        string? errorCode = failed ? ApplicationProjection.SafeOrNull(lastRun?.ErrorCode ?? transcript.Errors.LastOrDefault()?.LocalErrorCode, 256) : null;
        inputs.Add(new TimelineProjectionInput(
            "turn-completed", 0, completedAt, TimelineItemType.TurnCompleted,
            failed ? "failed" : "completed",
            ApplicationProjection.Safe(lastRun?.Summary ?? (failed ? "Imported session ended with an error." : "Imported session completed."),
                ThreadPersistenceLimits.MaxTimelineSummaryBytes),
            new TimelinePayloadRecord { TurnCompleted = new TimelineTurnCompletedPayloadRecord(stopReason, errorCode) },
            sessionPointer,
            Redacted: true));

        IReadOnlyList<TimelineItemRecord> items = DeterministicTimelineProjector.Project(
            threadId, turnId, source.Fingerprint, inputs, cancellationToken);
        TurnRecord turn = new()
        {
            TurnId = turnId,
            ThreadId = threadId,
            Ordinal = 1,
            Status = failed ? TurnStatus.Failed : TurnStatus.Completed,
            CreatedAtUtc = createdAt,
            StartedAtUtc = createdAt,
            CompletedAtUtc = completedAt,
            TaskSummary = ApplicationProjection.Safe("Imported session " + source.SourceIdentity, ThreadPersistenceLimits.MaxTaskSummaryBytes),
            StopReason = stopReason,
            ErrorCode = errorCode,
            Mode = "session-import",
            SourceCorrelation = source.Fingerprint,
            SourcePointers = [sessionPointer],
            TimelineFirstSequence = 1,
            TimelineLastSequence = items.Count,
            TimelineItemCount = items.Count
        };
        ThreadRecord thread = new()
        {
            ThreadId = threadId,
            WorkspaceId = workspace.Data.WorkspaceId,
            WorkspaceRootIdentity = workspace.Data.RootPath,
            Title = ApplicationProjection.Safe("Session: " + source.SourceIdentity, ThreadPersistenceLimits.MaxTitleBytes),
            CreatedAtUtc = createdAt,
            UpdatedAtUtc = completedAt,
            Origin = new ThreadOriginRecord
            {
                Kind = "session-import",
                SourceKind = ThreadSourceKind.Session,
                SourceId = ApplicationProjection.Safe(source.SourceIdentity, 256),
                SourceFingerprint = source.Fingerprint
            }
        };
        var warnings = new List<string>();
        if (transcript.ToolCalls.Count > 0)
        {
            warnings.Add("Raw tool arguments and full tool output were omitted from the imported projection.");
        }

        if (transcript.Messages.Count > 0)
        {
            warnings.Add("Session collections were merged by timestamp, source kind, and original index.");
        }

        return new SessionProjection(thread, [turn], items, warnings);
    }

    private TurnSummaryProjection ProjectTurn(
        TurnRecord turn,
        CliEnvironmentSnapshot snapshot,
        List<ApplicationDiagnostic> diagnostics,
        CancellationToken cancellationToken) => new(
        turn.TurnId,
        turn.Ordinal,
        turn.Revision,
        turn.Status,
        turn.CreatedAtUtc,
        turn.StartedAtUtc,
        turn.CompletedAtUtc,
        ApplicationProjection.Safe(turn.TaskSummary, ThreadPersistenceLimits.MaxTaskSummaryBytes),
        ApplicationProjection.SafeOrNull(turn.StopReason, 256),
        ApplicationProjection.SafeOrNull(turn.ErrorCode, 256),
        turn.SourcePointers.Select(pointer => Hydrate(pointer, snapshot, diagnostics, cancellationToken)).ToArray(),
        turn.TimelineFirstSequence,
        turn.TimelineLastSequence,
        turn.TimelineItemCount);

    private TimelineItemProjection ProjectItem(
        TimelineItemRecord item,
        CliEnvironmentSnapshot snapshot,
        List<ApplicationDiagnostic> diagnostics,
        CancellationToken cancellationToken) => new(
        item.ItemId,
        item.TurnId,
        item.Sequence,
        item.TimestampUtc,
        item.Type,
        item.Source is null ? null : Hydrate(item.Source, snapshot, diagnostics, cancellationToken),
        ApplicationProjection.Safe(item.Status, 256),
        ApplicationProjection.Safe(item.Summary, ThreadPersistenceLimits.MaxTimelineSummaryBytes),
        ProjectPayload(item.Payload),
        item.Redaction.Applied);

    private static TimelinePayloadProjection ProjectPayload(TimelinePayloadRecord payload)
    {
        if (payload.Message is not null) return new("message", Text: ApplicationProjection.Safe(payload.Message.Preview));
        if (payload.Plan is not null) return new("plan", Text: ApplicationProjection.Safe(payload.Plan.Summary));
        if (payload.Operation is not null) return new("operation", Name: ApplicationProjection.Safe(payload.Operation.Name), Succeeded: payload.Operation.Succeeded, ErrorCode: ApplicationProjection.SafeOrNull(payload.Operation.ErrorCode));
        if (payload.Approval is not null) return new("approval", Text: ApplicationProjection.Safe(payload.Approval.Status));
        if (payload.Changes is not null) return new("changes", Count: payload.Changes.ChangedFileCount);
        if (payload.Reference is not null) return new("reference", ReferenceId: ApplicationProjection.Safe(payload.Reference.ReferenceId));
        if (payload.Warning is not null) return new("warning", ErrorCode: ApplicationProjection.Safe(payload.Warning.Code));
        if (payload.TurnCompleted is not null) return new("turn-completed", ErrorCode: ApplicationProjection.SafeOrNull(payload.TurnCompleted.ErrorCode), StopReason: ApplicationProjection.Safe(payload.TurnCompleted.StopReason));
        throw new InvalidOperationException("Timeline payload is invalid.");
    }

    private ThreadSourcePointerProjection Hydrate(
        ThreadSourcePointerRecord pointer,
        CliEnvironmentSnapshot snapshot,
        List<ApplicationDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string availability = pointer.Availability;
        string? errorCode = null;
        try
        {
            availability = pointer.Kind switch
            {
                ThreadSourceKind.Session => HydrateSession(pointer, snapshot),
                ThreadSourceKind.Job => JobRecordStore.Create(snapshot).Read(pointer.SourceId).Succeeded
                    ? ThreadSourceAvailability.Available : ThreadSourceAvailability.Missing,
                ThreadSourceKind.Queue => TaskQueueStore.Create(snapshot).Read(pointer.SourceId).Succeeded
                    ? ThreadSourceAvailability.Available : ThreadSourceAvailability.Missing,
                ThreadSourceKind.Run => ManagedProjectPackRunStore.Create(snapshot).Read(pointer.SourceId).Succeeded
                    ? ThreadSourceAvailability.Available : ThreadSourceAvailability.Missing,
                ThreadSourceKind.Report => HydrateReport(pointer, snapshot),
                ThreadSourceKind.Artifact => new ArtifactApplicationService().Get(new ArtifactGetRequest(snapshot, pointer.SourceId), cancellationToken).Succeeded
                    ? ThreadSourceAvailability.Available : ThreadSourceAvailability.Missing,
                _ => ThreadSourceAvailability.Unknown
            };
        }
        catch (ConversationTranscriptReadException exception)
        {
            availability = exception.ErrorCode == ConversationTranscriptReadErrorCode.Corrupt
                ? ThreadSourceAvailability.Corrupt
                : ThreadSourceAvailability.Missing;
            errorCode = exception.ErrorCode;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException)
        {
            availability = ThreadSourceAvailability.Unknown;
            errorCode = "thread-source-unavailable";
        }

        if (errorCode is not null || availability is ThreadSourceAvailability.Missing or ThreadSourceAvailability.Corrupt
            or ThreadSourceAvailability.Stale or ThreadSourceAvailability.Unknown)
        {
            diagnostics.Add(new ApplicationDiagnostic(
                errorCode ?? "thread-source-" + availability,
                availability == ThreadSourceAvailability.Corrupt ? ApplicationErrorCategory.CorruptState : ApplicationErrorCategory.Unavailable,
                $"{pointer.Kind} source metadata is {availability}."));
        }

        return new ThreadSourcePointerProjection(
            pointer.Kind,
            ApplicationProjection.Safe(pointer.SourceId, 4_096),
            ApplicationProjection.SafeOrNull(pointer.SourceRevision, 4_096),
            ApplicationProjection.SafeOrNull(pointer.SourceFingerprint, 4_096),
            availability);
    }

    private string HydrateSession(ThreadSourcePointerRecord pointer, CliEnvironmentSnapshot snapshot)
    {
        ConversationSessionName name = ConversationSessionName.Parse(pointer.SourceId);
        ConversationTranscriptSnapshot current = conversationStoreFactory(snapshot).ReadBounded(name);
        return pointer.SourceFingerprint is not null && pointer.SourceFingerprint != current.Fingerprint
            ? ThreadSourceAvailability.Stale
            : ThreadSourceAvailability.Available;
    }

    private string HydrateReport(ThreadSourcePointerRecord pointer, CliEnvironmentSnapshot snapshot)
    {
        const string sessionPrefix = "session:";
        const string jobPrefix = "job:";
        if (pointer.SourceId.StartsWith(sessionPrefix, StringComparison.Ordinal))
        {
            ConversationSessionName session = ConversationSessionName.Parse(pointer.SourceId[sessionPrefix.Length..]);
            conversationStoreFactory(snapshot).ReadBounded(session);
            return ThreadSourceAvailability.Available;
        }

        if (pointer.SourceId.StartsWith(jobPrefix, StringComparison.Ordinal))
        {
            return JobRecordStore.Create(snapshot).Read(pointer.SourceId[jobPrefix.Length..]).Succeeded
                ? ThreadSourceAvailability.Available
                : ThreadSourceAvailability.Missing;
        }

        return ThreadSourceAvailability.Missing;
    }

    private static ThreadSummaryProjection ProjectSummary(ThreadRecord record) => new(
        record.ThreadId,
        record.Revision,
        record.WorkspaceId,
        ApplicationProjection.Safe(record.Title, ThreadPersistenceLimits.MaxTitleBytes),
        record.Status,
        record.CreatedAtUtc,
        record.UpdatedAtUtc,
        record.ArchivedAtUtc,
        record.Turns.Count,
        record.TimelineItemCount,
        record.ActiveTurnId,
        new ThreadOriginProjection(
            record.Origin.Kind,
            record.Origin.SourceKind,
            ApplicationProjection.SafeOrNull(record.Origin.SourceId, 256),
            ApplicationProjection.SafeOrNull(record.Origin.SourceFingerprint, 128)));

    private ApplicationError? ValidateWorkspaceBinding(
        CliEnvironmentSnapshot snapshot,
        ThreadRecord record,
        CancellationToken cancellationToken)
    {
        ApplicationResult<WorkspaceSnapshotProjection> workspace = workspaceService.Snapshot(snapshot, cancellationToken);
        if (!workspace.Succeeded || workspace.Data is null)
        {
            return workspace.Error ?? new ApplicationError(
                "workspace-unavailable",
                ApplicationErrorCategory.Workspace,
                "Workspace could not be opened.",
                Retryable: false);
        }

        return string.Equals(workspace.Data.WorkspaceId, record.WorkspaceId, StringComparison.Ordinal)
            ? null
            : new ApplicationError(
                "thread-workspace-mismatch",
                ApplicationErrorCategory.Workspace,
                "Thread does not belong to the current workspace.",
                Retryable: false);
    }

    private static ApplicationResult<ThreadSummaryProjection> ProjectMutation(ThreadStoreMutationResult result) =>
        result.Succeeded && result.Aggregate is not null
            ? ApplicationResult<ThreadSummaryProjection>.Success(ProjectSummary(result.Aggregate.Record))
            : StoreFailure<ThreadSummaryProjection>(result.Diagnostic);

    private static bool TryParseSession(
        string value,
        out ConversationSessionName? sessionName,
        out ApplicationError? error)
    {
        try
        {
            sessionName = ConversationSessionName.Parse(value);
            error = null;
            return true;
        }
        catch (ArgumentException)
        {
            sessionName = null;
            error = new ApplicationError("session-name-invalid", ApplicationErrorCategory.Validation, "Session name is invalid.", false);
            return false;
        }
    }

    private static ApplicationDiagnostic ProjectDiagnostic(ThreadStoreDiagnostic diagnostic) => new(
        diagnostic.ErrorCode,
        Category(diagnostic.ErrorCode),
        diagnostic.SafeMessage);

    private static ApplicationResult<T> StoreFailure<T>(ThreadStoreDiagnostic? diagnostic)
    {
        string code = diagnostic?.ErrorCode ?? ThreadErrorCode.ThreadStoreUnavailable;
        return Failure<T>(code, diagnostic?.SafeMessage ?? "Thread store operation failed.");
    }

    private static ApplicationResult<T> Failure<T>(string code, string message, string? category = null)
    {
        string effectiveCategory = category ?? Category(code);
        return ApplicationResult<T>.Failure(new ApplicationError(
            code,
            effectiveCategory,
            message,
            Retryable: effectiveCategory == ApplicationErrorCategory.Unavailable));
    }

    private static string Category(string code)
    {
        if (code is ThreadErrorCode.ThreadIdInvalid or ThreadErrorCode.TurnIdInvalid or ThreadErrorCode.ItemIdInvalid
            or ThreadErrorCode.TimelineSequenceInvalid or ThreadErrorCode.ThreadTitleInvalid or ThreadErrorCode.ThreadConfirmationInvalid
            or "session-name-invalid") return ApplicationErrorCategory.Validation;
        if (code is ThreadErrorCode.ThreadNotFound or ThreadErrorCode.TurnNotFound or ConversationTranscriptReadErrorCode.NotFound) return ApplicationErrorCategory.NotFound;
        if (code is ThreadErrorCode.ThreadPathUnsafe or ThreadErrorCode.ThreadReparsePoint or ThreadErrorCode.ThreadDeleteNotArchived
            or ThreadErrorCode.ThreadArchiveActive or ConversationTranscriptReadErrorCode.ReparsePoint) return ApplicationErrorCategory.Denied;
        if (code is ThreadErrorCode.ThreadRevisionConflict or ThreadErrorCode.ThreadActiveTurnConflict or ThreadErrorCode.TimelineAppendConflict
            or ThreadErrorCode.SessionImportSourceChanged or ThreadErrorCode.ThreadAlreadyExists or ThreadErrorCode.TurnTransitionInvalid) return ApplicationErrorCategory.Conflict;
        if (code is ThreadErrorCode.ThreadRecordCorrupt or ThreadErrorCode.ThreadSchemaUnsupported or ThreadErrorCode.TurnRecordCorrupt
            or ThreadErrorCode.TimelineRecordCorrupt or ThreadErrorCode.ThreadReferenceMissing or ConversationTranscriptReadErrorCode.Corrupt
            or ConversationTranscriptReadErrorCode.SchemaUnsupported) return ApplicationErrorCategory.CorruptState;
        if (code is ThreadErrorCode.ThreadLimitExceeded or ThreadErrorCode.TurnLimitExceeded or ThreadErrorCode.TimelineLimitExceeded
            or ThreadErrorCode.SessionImportLimitExceeded) return ApplicationErrorCategory.LimitExceeded;
        return ApplicationErrorCategory.Unavailable;
    }

    private sealed record SessionProjection(
        ThreadRecord Thread,
        IReadOnlyList<TurnRecord> Turns,
        IReadOnlyList<TimelineItemRecord> Items,
        IReadOnlyList<string> Warnings);
}
