using CSharpAiCli.AppHost.Protocol.Generated;
using CSharpAiCli.Application;

namespace CSharpAiCli.AppHost.Protocol;

internal static class DesktopProtocolMapper
{
    public static ThreadListResult Map(ApplicationResult<ThreadListProjection> value) => new()
    {
        SchemaVersion = DesktopProtocolDefinition.SchemaVersion,
        Succeeded = value.Succeeded,
        Data = value.Data is null
            ? null
            : new ThreadListData
            {
                Threads = value.Data.Threads.Select(Map).ToArray(),
                Truncated = value.Data.Truncated
            },
        Error = Map(value.Error),
        Diagnostics = Map(value.Diagnostics),
        Truncated = value.Truncated
    };

    public static ThreadGetResult Map(ApplicationResult<ThreadDetailProjection> value) => new()
    {
        SchemaVersion = DesktopProtocolDefinition.SchemaVersion,
        Succeeded = value.Succeeded,
        Data = value.Data is null ? null : Map(value.Data),
        Error = Map(value.Error),
        Diagnostics = Map(value.Diagnostics),
        Truncated = value.Truncated
    };

    public static ThreadSummaryResult Map(ApplicationResult<ThreadSummaryProjection> value) => new()
    {
        SchemaVersion = DesktopProtocolDefinition.SchemaVersion,
        Succeeded = value.Succeeded,
        Data = value.Data is null ? null : Map(value.Data),
        Error = Map(value.Error),
        Diagnostics = Map(value.Diagnostics),
        Truncated = value.Truncated
    };

    public static ThreadDeleteResult Map(ApplicationResult<ThreadDeleteProjection> value) => new()
    {
        SchemaVersion = DesktopProtocolDefinition.SchemaVersion,
        Succeeded = value.Succeeded,
        Data = value.Data is null
            ? null
            : new ThreadDeleteData { ThreadId = value.Data.ThreadId, Deleted = value.Data.Deleted },
        Error = Map(value.Error),
        Diagnostics = Map(value.Diagnostics),
        Truncated = value.Truncated
    };

    public static CatalogListResult Map(ApplicationResult<CatalogQueryProjection> value) => new()
    {
        SchemaVersion = DesktopProtocolDefinition.SchemaVersion,
        Succeeded = value.Succeeded,
        Data = value.Data is null
            ? null
            : new CatalogListData
            {
                WorkspaceId = value.Data.WorkspaceId,
                Kind = value.Data.Catalog,
                CatalogRevision = value.Data.CatalogRevision,
                Items = value.Data.Items.Select(item => new CatalogItemData
                {
                    Id = item.Id,
                    DisplayName = item.DisplayName,
                    Version = item.Version,
                    Description = item.Description,
                    SourceKind = item.SourceKind,
                    ReadOnly = item.ReadOnly,
                    ToolBoundary = item.ToolBoundary,
                    Capabilities = item.Capabilities
                }).ToArray(),
                Truncated = value.Data.Truncated
            },
        Error = Map(value.Error),
        Diagnostics = Map(value.Diagnostics),
        Truncated = value.Truncated
    };

    public static ContextSearchResult Map(ApplicationResult<ControlledContextSearchProjection> value) => new()
    {
        SchemaVersion = DesktopProtocolDefinition.SchemaVersion,
        Succeeded = value.Succeeded,
        Data = value.Data is null ? null : new ContextSearchData
        {
            Items = value.Data.Items.Select(Map).ToArray(),
            Truncated = value.Data.Truncated,
            ScannedEntries = value.Data.ScannedEntries
        },
        Error = Map(value.Error),
        Diagnostics = Map(value.Diagnostics),
        Truncated = value.Truncated
    };

    public static ContextResolveResult Map(ApplicationResult<ControlledContextDescriptor> value) => new()
    {
        SchemaVersion = DesktopProtocolDefinition.SchemaVersion,
        Succeeded = value.Succeeded,
        Data = value.Data is null ? null : Map(value.Data),
        Error = Map(value.Error),
        Diagnostics = Map(value.Diagnostics),
        Truncated = value.Truncated
    };

    public static ComposerStateResult Map(ApplicationResult<ComposerStateProjection> value) => new()
    {
        SchemaVersion = DesktopProtocolDefinition.SchemaVersion,
        Succeeded = value.Succeeded,
        Data = value.Data is null ? null : new ComposerStateData
        {
            WorkspaceId = value.Data.WorkspaceId,
            ThreadId = value.Data.ThreadId,
            ThreadRevision = value.Data.ThreadRevision,
            QueueRevision = value.Data.QueueRevision,
            PendingIntent = value.Data.PendingIntent is null ? null : new PendingComposerIntentData
            {
                IntentId = value.Data.PendingIntent.IntentId,
                Delivery = value.Data.PendingIntent.Delivery,
                CreatedAtUtc = value.Data.PendingIntent.CreatedAtUtc,
                ContextCount = value.Data.PendingIntent.ContextCount,
                CatalogCount = value.Data.PendingIntent.CatalogCount
            },
            EffectiveModel = value.Data.EffectiveModel,
            ModelSource = value.Data.ModelSource,
            ApprovalMode = value.Data.ApprovalMode,
            ApprovalModeSource = value.Data.ApprovalModeSource,
            ControlledContext = value.Data.ControlledContext
        },
        Error = Map(value.Error),
        Diagnostics = Map(value.Diagnostics),
        Truncated = value.Truncated
    };

    public static ChangesGetResult Map(ApplicationResult<DesktopChangesProjection> value) => new()
    {
        SchemaVersion = DesktopProtocolDefinition.SchemaVersion,
        Succeeded = value.Succeeded,
        Data = value.Data is null
            ? null
            : new ChangesData
            {
                Status = value.Data.Status,
                ExitCode = value.Data.ExitCode,
                GitStatusSummary = value.Data.GitStatusSummary,
                GitStatusSucceeded = value.Data.GitStatusSucceeded,
                GitStatusErrorCode = value.Data.GitStatusErrorCode,
                Dirty = value.Data.Dirty,
                DiffStatSummary = value.Data.DiffStatSummary,
                DiffSucceeded = value.Data.DiffSucceeded,
                DiffErrorCode = value.Data.DiffErrorCode,
                DiffTruncated = value.Data.DiffTruncated,
                ChangedFiles = value.Data.ChangedFiles.Select(file => new ChangedFileData
                {
                    Path = file.Path,
                    Status = file.Status
                }).ToArray(),
                SessionSource = value.Data.SessionSource,
                SessionName = value.Data.SessionName,
                Warnings = value.Data.Warnings
            },
        Error = Map(value.Error),
        Diagnostics = Map(value.Diagnostics),
        Truncated = value.Truncated
    };

    public static ReportListResult Map(ApplicationResult<ReportListProjection> value) => new()
    {
        SchemaVersion = DesktopProtocolDefinition.SchemaVersion,
        Succeeded = value.Succeeded,
        Data = value.Data is null
            ? null
            : new ReportListData
            {
                Reports = value.Data.Reports.Select(Map).ToArray(),
                Truncated = value.Data.Truncated
            },
        Error = Map(value.Error),
        Diagnostics = Map(value.Diagnostics),
        Truncated = value.Truncated
    };

    public static ReportGetResult Map(ApplicationResult<ReportDetailProjection> value) => new()
    {
        SchemaVersion = DesktopProtocolDefinition.SchemaVersion,
        Succeeded = value.Succeeded,
        Data = value.Data is null
            ? null
            : new ReportDetailData
            {
                Metadata = Map(value.Data.Metadata),
                Summary = value.Data.Summary,
                StopReason = value.Data.StopReason,
                ErrorCode = value.Data.ErrorCode,
                ChangedFiles = value.Data.ChangedFiles,
                Commands = value.Data.Commands,
                Verification = value.Data.Verification,
                Risks = value.Data.Risks,
                ArtifactPointers = value.Data.ArtifactPointers,
                SummaryTruncated = value.Data.SummaryTruncated
            },
        Error = Map(value.Error),
        Diagnostics = Map(value.Diagnostics),
        Truncated = value.Truncated
    };

    public static ArtifactListResult Map(ApplicationResult<ArtifactListProjection> value) => new()
    {
        SchemaVersion = DesktopProtocolDefinition.SchemaVersion,
        Succeeded = value.Succeeded,
        Data = value.Data is null
            ? null
            : new ArtifactListData
            {
                Artifacts = value.Data.Artifacts.Select(Map).ToArray(),
                Truncated = value.Data.Truncated
            },
        Error = Map(value.Error),
        Diagnostics = Map(value.Diagnostics),
        Truncated = value.Truncated
    };

    public static ArtifactGetResult Map(ApplicationResult<ArtifactMetadataProjection> value) => new()
    {
        SchemaVersion = DesktopProtocolDefinition.SchemaVersion,
        Succeeded = value.Succeeded,
        Data = value.Data is null ? null : Map(value.Data),
        Error = Map(value.Error),
        Diagnostics = Map(value.Diagnostics),
        Truncated = value.Truncated
    };

    private static ThreadSummaryData Map(ThreadSummaryProjection value) => new()
    {
        ThreadId = value.ThreadId,
        Revision = value.Revision,
        WorkspaceId = value.WorkspaceId,
        Title = value.Title,
        Status = value.Status,
        CreatedAtUtc = value.CreatedAtUtc,
        UpdatedAtUtc = value.UpdatedAtUtc,
        ArchivedAtUtc = value.ArchivedAtUtc,
        TurnCount = value.TurnCount,
        TimelineItemCount = value.TimelineItemCount,
        ActiveTurnId = value.ActiveTurnId,
        Origin = new ThreadOriginData
        {
            Kind = value.Origin.Kind,
            SourceKind = value.Origin.SourceKind,
            SourceId = value.Origin.SourceId,
            SourceFingerprint = value.Origin.SourceFingerprint
        }
    };

    private static ContextDescriptorData Map(ControlledContextDescriptor value) => new()
    {
        SelectionId = value.SelectionId,
        RelativePath = value.RelativePath,
        Kind = value.Kind,
        ByteCount = value.ByteCount,
        FileCount = value.FileCount,
        Availability = value.Availability
    };

    private static ThreadDetailData Map(ThreadDetailProjection value) => new()
    {
        Thread = Map(value.Thread),
        Turns = value.Turns.Select(turn => new TurnSummaryData
        {
            TurnId = turn.TurnId,
            Ordinal = turn.Ordinal,
            Revision = turn.Revision,
            Status = turn.Status,
            CreatedAtUtc = turn.CreatedAtUtc,
            StartedAtUtc = turn.StartedAtUtc,
            CompletedAtUtc = turn.CompletedAtUtc,
            TaskSummary = turn.TaskSummary,
            StopReason = turn.StopReason,
            ErrorCode = turn.ErrorCode,
            SourcePointers = turn.SourcePointers.Select(Map).ToArray(),
            TimelineFirstSequence = turn.TimelineFirstSequence,
            TimelineLastSequence = turn.TimelineLastSequence,
            TimelineItemCount = turn.TimelineItemCount
        }).ToArray(),
        Timeline = value.Timeline.Select(item => new TimelineItemData
        {
            ItemId = item.ItemId,
            TurnId = item.TurnId,
            Sequence = item.Sequence,
            TimestampUtc = item.TimestampUtc,
            Type = item.Type,
            Source = item.Source is null ? null : Map(item.Source),
            Status = item.Status,
            Summary = item.Summary,
            Payload = new TimelinePayloadData
            {
                Kind = item.Payload.Kind,
                Text = item.Payload.Text,
                Name = item.Payload.Name,
                Succeeded = item.Payload.Succeeded,
                ErrorCode = item.Payload.ErrorCode,
                Count = item.Payload.Count,
                ReferenceId = item.Payload.ReferenceId,
                StopReason = item.Payload.StopReason
            },
            Redacted = item.Redacted
        }).ToArray(),
        NextSequence = value.NextSequence,
        TimelineTruncated = value.TimelineTruncated,
        RecoveryRequired = value.RecoveryRequired
    };

    private static ThreadSourcePointerData Map(ThreadSourcePointerProjection value) => new()
    {
        Kind = value.Kind,
        SourceId = value.SourceId,
        SourceRevision = value.SourceRevision,
        SourceFingerprint = value.SourceFingerprint,
        Availability = value.Availability
    };

    private static ReportMetadataData Map(ReportMetadataProjection value) => new()
    {
        ReportId = value.ReportId,
        SourceKind = value.SourceKind,
        SourceId = value.SourceId,
        Status = value.Status,
        CreatedAtUtc = value.CreatedAtUtc,
        UpdatedAtUtc = value.UpdatedAtUtc,
        TaskReportPointer = value.TaskReportPointer,
        ArtifactPointer = value.ArtifactPointer
    };

    private static ArtifactMetadataData Map(ArtifactMetadataProjection value) => new()
    {
        ArtifactId = value.ArtifactId,
        PointerId = value.PointerId,
        Kind = value.Kind,
        Ownership = value.Ownership,
        RelativePath = value.RelativePath,
        Size = value.Size,
        Sha256 = value.Sha256,
        Availability = value.Availability,
        Verification = value.Verification,
        RunState = value.RunState,
        DeclaredAtUtc = value.DeclaredAtUtc,
        UpdatedAtUtc = value.UpdatedAtUtc,
        Owner = new ArtifactOwnerData
        {
            RunId = value.Owner.RunId,
            JobId = value.Owner.JobId,
            QueueId = value.Owner.QueueId,
            RootRunId = value.Owner.RootRunId,
            ParentRunId = value.Owner.ParentRunId,
            Attempt = value.Owner.Attempt
        },
        Retention = new ArtifactRetentionData
        {
            Class = value.Retention.Class,
            Owned = value.Retention.Owned,
            Prunable = value.Retention.Prunable,
            DefaultMinimumAgeDays = value.Retention.DefaultMinimumAgeDays
        }
    };

    private static ApplicationErrorData? Map(ApplicationError? value) => value is null
        ? null
        : new ApplicationErrorData
        {
            Code = value.Code,
            Category = value.Category,
            SafeMessage = value.SafeMessage,
            Retryable = value.Retryable
        };

    private static IReadOnlyList<ApplicationDiagnosticData> Map(
        IReadOnlyList<ApplicationDiagnostic> values) => values.Select(value => new ApplicationDiagnosticData
        {
            Code = value.Code,
            Category = value.Category,
            SafeMessage = value.SafeMessage
        }).ToArray();
}
