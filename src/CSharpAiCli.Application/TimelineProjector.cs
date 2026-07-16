using CSharpAiCli.Core;

namespace CSharpAiCli.Application;

internal sealed record TimelineProjectionInput(
    string CollectionKind,
    int SourceIndex,
    DateTimeOffset TimestampUtc,
    string Type,
    string Status,
    string Summary,
    TimelinePayloadRecord Payload,
    ThreadSourcePointerRecord? Source = null,
    bool Redacted = false);

internal static class DeterministicTimelineProjector
{
    public static IReadOnlyList<TimelineItemRecord> Project(
        string threadId,
        string turnId,
        string seed,
        IReadOnlyList<TimelineProjectionInput> inputs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        TimelineProjectionInput[] canonical = inputs
            .OrderBy(input => input.TimestampUtc)
            .ThenBy(input => TypePriority(input.Type))
            .ThenBy(input => input.CollectionKind, StringComparer.Ordinal)
            .ThenBy(input => input.SourceIndex)
            .ToArray();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var items = new TimelineItemRecord[canonical.Length];
        for (int index = 0; index < canonical.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TimelineProjectionInput input = canonical[index];
            string identity = input.CollectionKind + ":" + input.SourceIndex;
            if (!identities.Add(identity))
            {
                throw new ThreadContractException(ThreadErrorCode.TimelineAppendConflict, "Timeline projection source identity is duplicated.");
            }

            TimelineItemRecord item = new()
            {
                ItemId = ThreadIdentity.CreateDeterministicItemId(seed, input.CollectionKind, input.SourceIndex),
                ThreadId = threadId,
                TurnId = turnId,
                Sequence = index + 1,
                TimestampUtc = input.TimestampUtc.ToUniversalTime(),
                Type = input.Type,
                Source = input.Source,
                Status = ApplicationProjection.Safe(input.Status, 256),
                Summary = ApplicationProjection.Safe(input.Summary, ThreadPersistenceLimits.MaxTimelineSummaryBytes),
                Payload = ProjectPayload(input.Payload),
                Redaction = new TimelineRedactionRecord { Applied = input.Redacted }
            };
            ThreadContractValidator.ValidateTimelineItem(item);
            items[index] = item;
        }

        return items;
    }

    private static TimelinePayloadRecord ProjectPayload(TimelinePayloadRecord payload) => new()
    {
        Message = payload.Message is null ? null : new TimelineMessagePayloadRecord(
            ApplicationProjection.Safe(payload.Message.Preview, ThreadPersistenceLimits.MaxTimelineSummaryBytes)),
        Plan = payload.Plan is null ? null : new TimelinePlanPayloadRecord(
            ApplicationProjection.Safe(payload.Plan.Summary, ThreadPersistenceLimits.MaxTimelineSummaryBytes)),
        Operation = payload.Operation is null ? null : new TimelineOperationPayloadRecord(
            ApplicationProjection.Safe(payload.Operation.Name, 1_024),
            payload.Operation.Succeeded,
            ApplicationProjection.SafeOrNull(payload.Operation.ErrorCode, 256)),
        Approval = payload.Approval is null ? null : new TimelineApprovalPayloadRecord(
            ApplicationProjection.Safe(payload.Approval.Status, 256)),
        Changes = payload.Changes,
        Reference = payload.Reference is null ? null : new TimelineReferencePayloadRecord(
            ApplicationProjection.Safe(payload.Reference.ReferenceId, 1_024)),
        Warning = payload.Warning is null ? null : new TimelineWarningPayloadRecord(
            ApplicationProjection.Safe(payload.Warning.Code, 256)),
        TurnCompleted = payload.TurnCompleted is null ? null : new TimelineTurnCompletedPayloadRecord(
            ApplicationProjection.Safe(payload.TurnCompleted.StopReason, 256),
            ApplicationProjection.SafeOrNull(payload.TurnCompleted.ErrorCode, 256))
    };

    private static int TypePriority(string type) => type switch
    {
        TimelineItemType.UserMessage => 10,
        TimelineItemType.AssistantMessage => 20,
        TimelineItemType.PlanUpdated => 30,
        TimelineItemType.ToolStarted => 40,
        TimelineItemType.CommandStarted => 45,
        TimelineItemType.ApprovalRequested => 50,
        TimelineItemType.ApprovalResolved => 55,
        TimelineItemType.ToolCompleted => 60,
        TimelineItemType.CommandCompleted => 65,
        TimelineItemType.ChangesUpdated => 70,
        TimelineItemType.ReportAvailable => 80,
        TimelineItemType.ArtifactAvailable => 85,
        TimelineItemType.WarningRaised => 90,
        TimelineItemType.TurnCompleted => 100,
        _ => int.MaxValue
    };
}
