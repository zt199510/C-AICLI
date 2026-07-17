using CSharpAiCli.Core;

namespace CSharpAiCli.Application;

public sealed record ComposerCatalogSelection(string Kind, string Id, string CatalogRevision);

public sealed record ComposerGetRequest(CliEnvironmentSnapshot Snapshot, string ThreadId);

public sealed record ComposerEnqueueRequest(
    CliEnvironmentSnapshot Snapshot,
    string ThreadId,
    long ExpectedThreadRevision,
    long ExpectedQueueRevision,
    string ClientMutationId,
    string Prompt,
    IReadOnlyList<string> ContextSelectionIds,
    IReadOnlyList<ComposerCatalogSelection> CatalogSelections);

public sealed record ComposerClearRequest(
    CliEnvironmentSnapshot Snapshot,
    string ThreadId,
    long ExpectedQueueRevision,
    string ClientMutationId);

public sealed record PendingComposerIntentProjection(
    string IntentId,
    string Delivery,
    DateTimeOffset CreatedAtUtc,
    int ContextCount,
    int CatalogCount);

public sealed record ComposerStateProjection(
    string WorkspaceId,
    string ThreadId,
    long ThreadRevision,
    long QueueRevision,
    PendingComposerIntentProjection? PendingIntent,
    string EffectiveModel,
    string ModelSource,
    string ApprovalMode,
    string ApprovalModeSource,
    bool ControlledContext);
