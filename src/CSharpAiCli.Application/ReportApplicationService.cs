using System.Collections.ObjectModel;
using System.Text.Json;
using CSharpAiCli.Core;

namespace CSharpAiCli.Application;

public sealed record ReportListRequest(
    CliEnvironmentSnapshot Snapshot,
    int PageSize = ApplicationLimits.DefaultPageSize);

public sealed record ReportGetRequest(
    CliEnvironmentSnapshot Snapshot,
    string ReportId);

public sealed record ReportMetadataProjection(
    string ReportId,
    string SourceKind,
    string SourceId,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string? TaskReportPointer,
    string? ArtifactPointer);

public sealed record ReportListProjection
{
    public ReportListProjection(IReadOnlyList<ReportMetadataProjection>? Reports, bool Truncated)
    {
        this.Reports = new ReadOnlyCollection<ReportMetadataProjection>((Reports ?? []).ToArray());
        this.Truncated = Truncated;
    }

    public IReadOnlyList<ReportMetadataProjection> Reports { get; }

    public bool Truncated { get; }
}

public sealed record ReportDetailProjection
{
    public ReportDetailProjection(
        ReportMetadataProjection Metadata,
        string? Summary,
        string? StopReason,
        string? ErrorCode,
        IReadOnlyList<string>? ChangedFiles,
        IReadOnlyList<string>? Commands,
        IReadOnlyList<string>? Verification,
        IReadOnlyList<string>? Risks,
        IReadOnlyList<string>? ArtifactPointers,
        bool SummaryTruncated)
    {
        this.Metadata = Metadata;
        this.Summary = Summary;
        this.StopReason = StopReason;
        this.ErrorCode = ErrorCode;
        this.ChangedFiles = ReadOnly(ChangedFiles);
        this.Commands = ReadOnly(Commands);
        this.Verification = ReadOnly(Verification);
        this.Risks = ReadOnly(Risks);
        this.ArtifactPointers = ReadOnly(ArtifactPointers);
        this.SummaryTruncated = SummaryTruncated;
    }

    public ReportMetadataProjection Metadata { get; }

    public string? Summary { get; }

    public string? StopReason { get; }

    public string? ErrorCode { get; }

    public IReadOnlyList<string> ChangedFiles { get; }

    public IReadOnlyList<string> Commands { get; }

    public IReadOnlyList<string> Verification { get; }

    public IReadOnlyList<string> Risks { get; }

    public IReadOnlyList<string> ArtifactPointers { get; }

    public bool SummaryTruncated { get; }

    private static IReadOnlyList<string> ReadOnly(IReadOnlyList<string>? values) =>
        new ReadOnlyCollection<string>((values ?? []).ToArray());
}

public sealed class ReportApplicationService
{
    private const string JobPrefix = "job:";
    private const string SessionPrefix = "session:";
    private readonly Func<CliEnvironmentSnapshot, JobRecordStore> jobStoreFactory;
    private readonly Func<CliEnvironmentSnapshot, IConversationStore> conversationStoreFactory;

    public ReportApplicationService()
        : this(
            snapshot => JobRecordStore.Create(snapshot),
            snapshot => FileConversationStore.Create(snapshot))
    {
    }

    internal ReportApplicationService(
        Func<CliEnvironmentSnapshot, JobRecordStore> jobStoreFactory,
        Func<CliEnvironmentSnapshot, IConversationStore> conversationStoreFactory)
    {
        this.jobStoreFactory = jobStoreFactory ?? throw new ArgumentNullException(nameof(jobStoreFactory));
        this.conversationStoreFactory = conversationStoreFactory ?? throw new ArgumentNullException(nameof(conversationStoreFactory));
    }

    public ApplicationResult<ReportListProjection> List(
        ReportListRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        ApplicationError? pageError = ApplicationLimits.ValidatePageSize(request.PageSize);
        if (pageError is not null)
        {
            return ApplicationResult<ReportListProjection>.Failure(pageError);
        }

        int readLimit = request.PageSize + 1;
        JobRecordListResult jobs = jobStoreFactory(request.Snapshot).List(readLimit);
        cancellationToken.ThrowIfCancellationRequested();
        List<ReportMetadataProjection> reports = jobs.Records
            .Select(ToMetadata)
            .ToList();
        List<ApplicationDiagnostic> diagnostics = jobs.Diagnostics.Select(diagnostic =>
            new ApplicationDiagnostic(
                diagnostic.ErrorCode,
                ApplicationProjection.CategoryForCode(diagnostic.ErrorCode),
                diagnostic.Summary)).ToList();

        try
        {
            IReadOnlyList<ConversationTranscriptSummary> sessions = conversationStoreFactory(request.Snapshot)
                .ListSummaries(readLimit);
            cancellationToken.ThrowIfCancellationRequested();
            foreach (ConversationTranscriptSummary session in sessions.Take(readLimit))
            {
                cancellationToken.ThrowIfCancellationRequested();
                reports.Add(new ReportMetadataProjection(
                    SessionPrefix + ApplicationProjection.Safe(session.Name, 256),
                    "session",
                    ApplicationProjection.Safe(session.Name, 256),
                    "available",
                    session.CreatedAtUtc,
                    session.UpdatedAtUtc,
                    null,
                    null));
            }
        }
        catch (Exception exception) when (IsConversationStoreException(exception))
        {
            diagnostics.Add(new ApplicationDiagnostic(
                exception is JsonException or InvalidOperationException
                    ? "session-transcript-invalid"
                    : "session-store-error",
                exception is JsonException or InvalidOperationException
                    ? ApplicationErrorCategory.CorruptState
                    : ApplicationErrorCategory.Unavailable,
                "Conversation report metadata could not be read safely."));
        }

        cancellationToken.ThrowIfCancellationRequested();
        ReportMetadataProjection[] ordered = reports
            .OrderByDescending(report => report.UpdatedAtUtc)
            .ThenBy(report => report.ReportId, StringComparer.Ordinal)
            .ToArray();
        bool truncated = ordered.Length > request.PageSize || diagnostics.Count > ApplicationLimits.MaxDiagnostics;
        return ApplicationResult<ReportListProjection>.Success(
            new ReportListProjection(ordered.Take(request.PageSize).ToArray(), truncated),
            diagnostics,
            truncated);
    }

    public ApplicationResult<ReportDetailProjection> Get(
        ReportGetRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(request.ReportId))
        {
            return Failure("report-not-found", ApplicationErrorCategory.NotFound, "Report was not found.");
        }

        if (request.ReportId.StartsWith(JobPrefix, StringComparison.Ordinal))
        {
            return GetJob(request.Snapshot, request.ReportId[JobPrefix.Length..], cancellationToken);
        }

        if (request.ReportId.StartsWith(SessionPrefix, StringComparison.Ordinal))
        {
            return GetSession(request.Snapshot, request.ReportId[SessionPrefix.Length..], cancellationToken);
        }

        return ApplicationResult<ReportDetailProjection>.Failure(new ApplicationError(
            "report-id-invalid",
            ApplicationErrorCategory.Validation,
            "Report id must use a job: or session: source prefix.",
            Retryable: false));
    }

    private ApplicationResult<ReportDetailProjection> GetJob(
        CliEnvironmentSnapshot snapshot,
        string jobId,
        CancellationToken cancellationToken)
    {
        JobRecordReadResult read = jobStoreFactory(snapshot).Read(jobId);
        cancellationToken.ThrowIfCancellationRequested();
        if (!read.Succeeded || read.Record is null)
        {
            string code = read.Diagnostic?.ErrorCode ?? "job-not-found";
            return Failure(
                code,
                ApplicationProjection.CategoryForCode(code),
                read.Diagnostic?.Summary ?? "Job report was not found.");
        }

        JobRecord record = read.Record;
        JobTaskReportSummary? task = record.TaskReport;
        (string? summary, bool summaryTruncated) = BoundSummary(task?.Summary ?? record.Summary);
        bool collectionsTruncated = (task?.ChangedFiles.Count ?? 0) > 200 ||
            (task?.Commands.Count ?? 0) > 200 ||
            (task?.Verification.Count ?? 0) > 200 ||
            (task?.Risks.Count ?? 0) > 200 ||
            record.Artifacts.Count > 200;
        string[] artifacts = record.Artifacts.Take(200).Select(artifact =>
            ApplicationProjection.Safe(artifact.Path, 4_096)).ToArray();
        ReportDetailProjection projection = new(
            ToMetadata(record),
            summary,
            ApplicationProjection.SafeOrNull(task?.StopReason ?? record.StopReason, 1_024),
            ApplicationProjection.SafeOrNull(task?.ErrorCode ?? record.ErrorCode, 256),
            SafeList(task?.ChangedFiles),
            SafeList(task?.Commands),
            SafeList(task?.Verification),
            SafeList(task?.Risks),
            artifacts,
            summaryTruncated);
        return ApplicationResult<ReportDetailProjection>.Success(
            projection,
            truncated: summaryTruncated || collectionsTruncated);
    }

    private ApplicationResult<ReportDetailProjection> GetSession(
        CliEnvironmentSnapshot snapshot,
        string sessionNameValue,
        CancellationToken cancellationToken)
    {
        ConversationSessionName sessionName;
        try
        {
            sessionName = ConversationSessionName.Parse(sessionNameValue);
        }
        catch (ArgumentException)
        {
            return Failure("session-not-found", ApplicationErrorCategory.NotFound, "Session report was not found.");
        }

        ConversationTranscript? transcript;
        try
        {
            IConversationStore store = conversationStoreFactory(snapshot);
            cancellationToken.ThrowIfCancellationRequested();
            if (!store.TryLoad(sessionName, out transcript) || transcript is null)
            {
                return Failure("session-not-found", ApplicationErrorCategory.NotFound, "Session report was not found.");
            }
        }
        catch (Exception exception) when (IsConversationStoreException(exception))
        {
            return Failure(
                exception is JsonException or InvalidOperationException
                    ? "session-transcript-invalid"
                    : "session-store-error",
                exception is JsonException or InvalidOperationException
                    ? ApplicationErrorCategory.CorruptState
                    : ApplicationErrorCategory.Unavailable,
                "Session report could not be read safely.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        ConversationAgentRun? run = transcript.AgentRuns.LastOrDefault();
        AgentTaskReport? task = run?.TaskReport;
        (string? summary, bool summaryTruncated) = BoundSummary(task?.Summary ?? run?.Summary);
        bool collectionsTruncated = task is not null &&
            (task.ChangedFiles.Count > 200 || task.Commands.Count > 200 ||
                task.Verification.Count > 200 || task.Risks.Count > 200);
        ReportMetadataProjection metadata = new(
            SessionPrefix + ApplicationProjection.Safe(transcript.SessionName, 256),
            "session",
            ApplicationProjection.Safe(transcript.SessionName, 256),
            ApplicationProjection.Safe(run?.Status ?? "empty", 128),
            transcript.CreatedAtUtc,
            transcript.UpdatedAtUtc,
            task is null ? null : SessionPrefix + ApplicationProjection.Safe(transcript.SessionName, 256),
            ApplicationProjection.SafeOrNull(task?.Report?.Path, 4_096));
        ReportDetailProjection projection = new(
            metadata,
            summary,
            ApplicationProjection.SafeOrNull(task?.StopReason ?? run?.StopReason, 1_024),
            ApplicationProjection.SafeOrNull(task?.ErrorCode ?? run?.ErrorCode, 256),
            task?.ChangedFiles.Take(200).Select(file => ApplicationProjection.Safe(file.Path, 4_096)).ToArray(),
            task?.Commands.Take(200).Select(command => ApplicationProjection.Safe(command.Command, 4_096)).ToArray(),
            task?.Verification.Take(200).Select(item => ApplicationProjection.Safe(item.Status, 1_024)).ToArray(),
            SafeList(task?.Risks),
            string.IsNullOrWhiteSpace(task?.Report?.Path)
                ? []
                : [ApplicationProjection.Safe(task.Report.Path, 4_096)],
            summaryTruncated);
        return ApplicationResult<ReportDetailProjection>.Success(
            projection,
            truncated: summaryTruncated || collectionsTruncated);
    }

    private static ReportMetadataProjection ToMetadata(JobRecord record)
    {
        JobArtifact? taskReport = record.Artifacts.FirstOrDefault(artifact =>
            artifact.Kind is JobArtifactKind.TaskReport or JobArtifactKind.MarkdownReport);
        JobArtifact? artifactPointer = record.Artifacts.FirstOrDefault();
        return new ReportMetadataProjection(
            JobPrefix + ApplicationProjection.Safe(record.JobId, 256),
            "job",
            ApplicationProjection.Safe(record.JobId, 256),
            ApplicationProjection.Safe(record.Status, 128),
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            ApplicationProjection.SafeOrNull(taskReport?.Path, 4_096),
            ApplicationProjection.SafeOrNull(artifactPointer?.Path, 4_096));
    }

    private static IReadOnlyList<string> SafeList(IReadOnlyList<string>? values) =>
        (values ?? []).Take(200).Select(value => ApplicationProjection.Safe(value, 4_096)).ToArray();

    private static (string? Value, bool Truncated) BoundSummary(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (null, false);
        }

        string safe = ApplicationProjection.Safe(value, ApplicationLimits.MaxReportSummaryBytes);
        return (safe, !string.Equals(DiagnosticSecretRedactor.Redact(value), safe, StringComparison.Ordinal));
    }

    private static ApplicationResult<ReportDetailProjection> Failure(
        string code,
        string category,
        string message) => ApplicationResult<ReportDetailProjection>.Failure(
            new ApplicationError(code, category, message, Retryable: category == ApplicationErrorCategory.Unavailable));

    private static bool IsConversationStoreException(Exception exception) => exception is
        InvalidOperationException or
        IOException or
        UnauthorizedAccessException or
        JsonException or
        NotSupportedException;
}
