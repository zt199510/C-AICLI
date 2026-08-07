using CSharpAiCli.Core;

namespace CSharpAiCli.Application;

public sealed class ComposerApplicationService
{
    private readonly Func<CliEnvironmentSnapshot, ThreadStore> threadStoreFactory;
    private readonly Func<CliEnvironmentSnapshot, ComposerIntentStore> intentStoreFactory;
    private readonly CatalogApplicationService catalogService;
    private readonly ControlledContextApplicationService contextService;
    private readonly WorkspaceApplicationService workspaceService;
    private readonly Func<DateTimeOffset> clock;

    public ComposerApplicationService(ControlledContextApplicationService? contextService = null)
        : this(ThreadStore.Create, ComposerIntentStore.Create, new CatalogApplicationService(),
            contextService ?? new ControlledContextApplicationService(), new WorkspaceApplicationService(), () => DateTimeOffset.UtcNow)
    {
    }

    internal ComposerApplicationService(
        Func<CliEnvironmentSnapshot, ThreadStore> threadStoreFactory,
        Func<CliEnvironmentSnapshot, ComposerIntentStore> intentStoreFactory,
        CatalogApplicationService catalogService,
        ControlledContextApplicationService contextService,
        WorkspaceApplicationService workspaceService,
        Func<DateTimeOffset> clock)
    {
        this.threadStoreFactory = threadStoreFactory;
        this.intentStoreFactory = intentStoreFactory;
        this.catalogService = catalogService;
        this.contextService = contextService;
        this.workspaceService = workspaceService;
        this.clock = clock;
    }

    public ApplicationResult<ComposerStateProjection> Get(ComposerGetRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ApplicationResult<Authority> authority = LoadAuthority(request.Snapshot, request.ThreadId, cancellationToken);
        if (!authority.Succeeded || authority.Data is null)
            return ApplicationResult<ComposerStateProjection>.Failure(authority.Error!, authority.Diagnostics);
        ComposerIntentStoreResult queue = intentStoreFactory(request.Snapshot).Get(
            authority.Data.Workspace.WorkspaceId, authority.Data.Thread.Record.WorkspaceRootIdentity, request.ThreadId, cancellationToken);
        return queue.Succeeded && queue.Queue is not null
            ? ApplicationResult<ComposerStateProjection>.Success(Project(authority.Data, queue.Queue))
            : StoreFailure(queue.Diagnostic);
    }

    public ApplicationResult<ComposerStateProjection> Enqueue(ComposerEnqueueRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!ComposerIntentContractValidator.IsSafeMutationId(request.ClientMutationId) ||
            request.ContextSelectionIds.Count > ComposerIntentLimits.MaxContextSelections ||
            request.CatalogSelections.Count > ComposerIntentLimits.MaxCatalogSelections)
            return Failure("composer-request-invalid", ApplicationErrorCategory.Validation, "Composer request is invalid.");
        string prompt = request.Prompt?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(prompt) || System.Text.Encoding.UTF8.GetByteCount(prompt) > ComposerIntentLimits.MaxPromptBytes)
            return Failure("composer-prompt-invalid", ApplicationErrorCategory.Validation, "Prompt must be non-empty and no larger than 64 KiB.");

        ApplicationResult<Authority> authorityResult = LoadAuthority(request.Snapshot, request.ThreadId, cancellationToken);
        if (!authorityResult.Succeeded || authorityResult.Data is null)
            return ApplicationResult<ComposerStateProjection>.Failure(authorityResult.Error!, authorityResult.Diagnostics);
        Authority authority = authorityResult.Data;
        if (authority.Thread.Record.Revision != request.ExpectedThreadRevision)
            return Failure("thread-revision-conflict", ApplicationErrorCategory.Conflict, "Thread revision changed before enqueue.");
        if (authority.Thread.Record.Status == ThreadStatus.Archived)
            return Failure("composer-thread-archived", ApplicationErrorCategory.Conflict, "Archived threads cannot accept composer input.");

        var contexts = new List<ComposerContextReferenceRecord>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (string selectionId in request.ContextSelectionIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ids.Add(selectionId)) return Failure("context-selection-duplicate", ApplicationErrorCategory.Validation, "Context selection is duplicated.");
            ApplicationResult<ComposerContextReferenceRecord> validated = contextService.Revalidate(authority.Workspace, selectionId, cancellationToken);
            if (!validated.Succeeded || validated.Data is null)
                return ApplicationResult<ComposerStateProjection>.Failure(validated.Error!, validated.Diagnostics);
            contexts.Add(validated.Data);
        }

        var catalogReferences = new List<ComposerCatalogReferenceRecord>();
        var catalogIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (IGrouping<string, ComposerCatalogSelection> group in request.CatalogSelections.GroupBy(item => item.Kind, StringComparer.Ordinal))
        {
            string applicationKind = group.Key switch
            {
                ComposerCatalogKind.Skill => ApplicationCatalogKind.Skills,
                ComposerCatalogKind.Expert => ApplicationCatalogKind.Experts,
                ComposerCatalogKind.Automation => ApplicationCatalogKind.Automations,
                _ => string.Empty
            };
            if (applicationKind.Length == 0) return Failure("catalog-selection-invalid", ApplicationErrorCategory.Validation, "Catalog selection kind is invalid.");
            ApplicationResult<CatalogQueryProjection> listed = catalogService.Query(
                new CatalogQueryRequest(request.Snapshot.Workspace, applicationKind, ApplicationLimits.MaxCatalogItems), cancellationToken);
            if (!listed.Succeeded || listed.Data is null)
                return ApplicationResult<ComposerStateProjection>.Failure(listed.Error!, listed.Diagnostics);
            if (listed.Data.Truncated || listed.Truncated)
                return Failure("catalog-selection-truncated", ApplicationErrorCategory.Conflict, "Catalog changed or was truncated; refresh it before enqueue.");
            foreach (ComposerCatalogSelection selection in group)
            {
                if (!catalogIds.Add($"{selection.Kind}\0{selection.Id}"))
                    return Failure("catalog-selection-duplicate", ApplicationErrorCategory.Validation, "Catalog selection is duplicated.");
                if (selection.CatalogRevision != listed.Data.CatalogRevision)
                    return Failure("catalog-revision-stale", ApplicationErrorCategory.Conflict, "Catalog changed; refresh selections before enqueue.");
                if (listed.Data.Items.Count(item => item.Id == selection.Id) != 1)
                    return Failure("catalog-item-stale", ApplicationErrorCategory.Conflict, "Catalog item is missing or ambiguous.");
                catalogReferences.Add(new ComposerCatalogReferenceRecord
                {
                    Kind = selection.Kind,
                    Id = selection.Id,
                    CatalogRevision = selection.CatalogRevision
                });
            }
        }

        PendingComposerIntentRecord intent = new()
        {
            IntentId = "intent_" + Guid.NewGuid().ToString("N"),
            WorkspaceId = authority.Workspace.WorkspaceId,
            WorkspaceRootIdentity = authority.Thread.Record.WorkspaceRootIdentity,
            ThreadId = request.ThreadId,
            ThreadRevision = authority.Thread.Record.Revision,
            Delivery = authority.Thread.Record.ActiveTurnId is null ? ComposerDelivery.Ready : ComposerDelivery.NextTurn,
            Prompt = prompt,
            Context = contexts,
            Catalog = catalogReferences,
            SourcePointer = request.SourcePointer,
            EffectiveModel = ApplicationProjection.Safe(request.Snapshot.Configuration.Model, 256),
            ModelSource = ApplicationProjection.Safe(request.Snapshot.Configuration.ModelSource, 256),
            ApprovalMode = request.Snapshot.Configuration.ApprovalMode.ToString(),
            ApprovalModeSource = ApplicationProjection.Safe(request.Snapshot.Configuration.ApprovalModeSource, 256),
            CreatedAtUtc = clock().ToUniversalTime()
        };
        ComposerIntentStoreResult stored = intentStoreFactory(request.Snapshot).Enqueue(
            authority.Workspace.WorkspaceId, authority.Thread.Record.WorkspaceRootIdentity, request.ThreadId,
            request.ExpectedQueueRevision, request.ClientMutationId, intent);
        return stored.Succeeded && stored.Queue is not null
            ? ApplicationResult<ComposerStateProjection>.Success(Project(authority, stored.Queue))
            : StoreFailure(stored.Diagnostic);
    }

    public ApplicationResult<ComposerStateProjection> Clear(ComposerClearRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ApplicationResult<Authority> authority = LoadAuthority(request.Snapshot, request.ThreadId, cancellationToken);
        if (!authority.Succeeded || authority.Data is null)
            return ApplicationResult<ComposerStateProjection>.Failure(authority.Error!, authority.Diagnostics);
        ComposerIntentStoreResult stored = intentStoreFactory(request.Snapshot).Clear(
            authority.Data.Workspace.WorkspaceId, authority.Data.Thread.Record.WorkspaceRootIdentity,
            request.ThreadId, request.ExpectedQueueRevision, request.ClientMutationId);
        return stored.Succeeded && stored.Queue is not null
            ? ApplicationResult<ComposerStateProjection>.Success(Project(authority.Data, stored.Queue))
            : StoreFailure(stored.Diagnostic);
    }

    private ApplicationResult<Authority> LoadAuthority(CliEnvironmentSnapshot snapshot, string threadId, CancellationToken cancellationToken)
    {
        if (snapshot is null || !ThreadIdentity.IsThreadId(threadId))
            return ApplicationResult<Authority>.Failure(new ApplicationError("composer-thread-invalid", ApplicationErrorCategory.Validation, "Thread id is invalid.", false));
        ApplicationResult<WorkspaceSnapshotProjection> workspace = workspaceService.Snapshot(snapshot, cancellationToken);
        if (!workspace.Succeeded || workspace.Data is null)
            return ApplicationResult<Authority>.Failure(workspace.Error!, workspace.Diagnostics);
        ThreadStoreReadResult read = threadStoreFactory(snapshot).Read(threadId, cancellationToken);
        if (!read.Succeeded || read.Aggregate is null)
            return ApplicationResult<Authority>.Failure(new ApplicationError(
                read.Diagnostic?.ErrorCode ?? "thread-not-found",
                Category(read.Diagnostic?.ErrorCode), read.Diagnostic?.SafeMessage ?? "Thread was not found.", false));
        if (read.Aggregate.Record.WorkspaceId != workspace.Data.WorkspaceId ||
            !string.Equals(read.Aggregate.Record.WorkspaceRootIdentity, workspace.Data.RootPath,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            return ApplicationResult<Authority>.Failure(new ApplicationError("workspace-changed", ApplicationErrorCategory.Conflict, "Thread belongs to a different workspace.", false));
        return ApplicationResult<Authority>.Success(new Authority(workspace.Data, read.Aggregate, snapshot.Configuration));
    }

    private static ComposerStateProjection Project(Authority authority, ComposerQueueRecord queue) => new(
        authority.Workspace.WorkspaceId,
        authority.Thread.Record.ThreadId,
        authority.Thread.Record.Revision,
        queue.Revision,
        queue.PendingIntent is null ? null : new PendingComposerIntentProjection(
            queue.PendingIntent.IntentId, queue.PendingIntent.Delivery, queue.PendingIntent.CreatedAtUtc,
            queue.PendingIntent.Context.Count, queue.PendingIntent.Catalog.Count),
        ApplicationProjection.Safe(authority.Configuration.Model, 256),
        ApplicationProjection.Safe(authority.Configuration.ModelSource, 256),
        authority.Configuration.ApprovalMode.ToString(),
        ApplicationProjection.Safe(authority.Configuration.ApprovalModeSource, 256),
        ControlledContext: true);

    private static ApplicationResult<ComposerStateProjection> StoreFailure(ComposerIntentStoreDiagnostic? diagnostic) =>
        Failure(diagnostic?.ErrorCode ?? ComposerIntentErrorCode.Unavailable, Category(diagnostic?.ErrorCode), diagnostic?.SafeMessage ?? "Composer queue is unavailable.");
    private static ApplicationResult<ComposerStateProjection> Failure(string code, string category, string message) =>
        ApplicationResult<ComposerStateProjection>.Failure(new ApplicationError(code, category, message, category == ApplicationErrorCategory.Unavailable));
    private static string Category(string? code) => code switch
    {
        ComposerIntentErrorCode.Corrupt => ApplicationErrorCategory.CorruptState,
        ComposerIntentErrorCode.RevisionConflict or ComposerIntentErrorCode.MutationConflict or ComposerIntentErrorCode.AlreadyPending => ApplicationErrorCategory.Conflict,
        ComposerIntentErrorCode.NotFound => ApplicationErrorCategory.NotFound,
        ComposerIntentErrorCode.Invalid => ApplicationErrorCategory.Validation,
        _ when code?.Contains("not-found", StringComparison.Ordinal) == true => ApplicationErrorCategory.NotFound,
        _ when code?.Contains("corrupt", StringComparison.Ordinal) == true => ApplicationErrorCategory.CorruptState,
        _ => ApplicationErrorCategory.Unavailable
    };

    private sealed record Authority(
        WorkspaceSnapshotProjection Workspace,
        ThreadAggregate Thread,
        EffectiveConfiguration Configuration);
}
