using System.Text.Json;
using CSharpAiCli.Core;

namespace CSharpAiCli.Application;

public sealed record ChangesQueryRequest(
    CliEnvironmentSnapshot Snapshot,
    ConversationSessionName? Session = null);

public sealed class ChangesApplicationService
{
    private readonly Func<CliEnvironmentSnapshot, IConversationStore> conversationStoreFactory;
    private readonly Func<ToolExecutionContext, CancellationToken, ToolExecutionResult> gitStatusQuery;
    private readonly Func<ToolExecutionContext, CancellationToken, ToolExecutionResult> gitDiffStatQuery;

    public ChangesApplicationService()
        : this(snapshot => FileConversationStore.Create(snapshot))
    {
    }

    internal ChangesApplicationService(Func<CliEnvironmentSnapshot, IConversationStore> conversationStoreFactory)
        : this(conversationStoreFactory, CreateGitStatusQuery(), CreateGitDiffStatQuery())
    {
    }

    internal ChangesApplicationService(
        Func<CliEnvironmentSnapshot, IConversationStore> conversationStoreFactory,
        Func<ToolExecutionContext, CancellationToken, ToolExecutionResult> gitStatusQuery,
        Func<ToolExecutionContext, CancellationToken, ToolExecutionResult> gitDiffStatQuery)
    {
        this.conversationStoreFactory = conversationStoreFactory ?? throw new ArgumentNullException(nameof(conversationStoreFactory));
        this.gitStatusQuery = gitStatusQuery ?? throw new ArgumentNullException(nameof(gitStatusQuery));
        this.gitDiffStatQuery = gitDiffStatQuery ?? throw new ArgumentNullException(nameof(gitDiffStatQuery));
    }

    public ApplicationResult<ChangesViewReport> Query(
        ChangesQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Snapshot);
        cancellationToken.ThrowIfCancellationRequested();

        CliEnvironmentSnapshot snapshot = request.Snapshot;
        ToolExecutionResult gitStatus = gitStatusQuery(new ToolExecutionContext(
            "changes_git_status",
            snapshot.Workspace,
            "{}",
            ToolExecutionPhase.Planning), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        ToolExecutionResult gitDiffStat = gitDiffStatQuery(new ToolExecutionContext(
            "changes_git_diff_stat",
            snapshot.Workspace,
            """{"stat":true}""",
            ToolExecutionPhase.Planning), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        ConversationTranscript? transcript = null;
        string? sessionPath = null;
        string? sessionWarning = null;
        if (request.Session is not null)
        {
            sessionPath = ResolveSessionPath(snapshot, request.Session);
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                IConversationStore conversationStore = conversationStoreFactory(snapshot);
                cancellationToken.ThrowIfCancellationRequested();
                if (!conversationStore.TryLoad(request.Session, out transcript) || transcript is null)
                {
                    sessionWarning = "Session transcript was not found.";
                }
            }
            catch (Exception exception) when (IsConversationStoreException(exception))
            {
                sessionWarning = "Conversation session store operation failed.";
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        ChangesViewReport created = ChangesViewReport.Create(
            snapshot.Workspace,
            gitStatus,
            gitDiffStat,
            transcript,
            request.Session?.Value,
            sessionPath,
            sessionWarning);
        cancellationToken.ThrowIfCancellationRequested();

        bool filesTruncated = created.ChangedFiles.Count > ApplicationLimits.MaxChangedFiles;
        AgentTaskReport? taskReport = created.Session?.TaskReport;
        bool taskReportTruncated = taskReport is not null && IsTaskReportTruncated(taskReport);
        AgentTaskReport? safeTaskReport = taskReport is null ? null : ProjectTaskReport(taskReport);
        List<string> warnings = created.Warnings
            .Select(warning => ApplicationProjection.Safe(warning))
            .ToList();
        if (filesTruncated)
        {
            warnings.Add($"Changed files were truncated to {ApplicationLimits.MaxChangedFiles} items.");
        }

        if (taskReportTruncated)
        {
            warnings.Add("Session task report collections were truncated to application limits.");
        }

        ChangesViewReport report = new(
            ApplicationProjection.Safe(created.Status, 64),
            created.ExitCode,
            created.WorkspaceRoot,
            ApplicationProjection.Safe(created.GitStatusSummary, ApplicationLimits.TargetAggregateBytes / 4),
            created.GitStatusSucceeded,
            ApplicationProjection.SafeOrNull(created.GitStatusErrorCode, 256),
            created.Dirty,
            ApplicationProjection.Safe(created.DiffStatSummary, ApplicationLimits.TargetAggregateBytes / 4),
            created.DiffSucceeded,
            ApplicationProjection.SafeOrNull(created.DiffErrorCode, 256),
            created.DiffTruncated,
            created.ChangedFiles.Take(ApplicationLimits.MaxChangedFiles).Select(file =>
                new ChangesViewChangedFile(
                    ApplicationProjection.Safe(file.Path, 4_096),
                    ApplicationProjection.Safe(file.Status, 64))).ToArray(),
            created.Session is null
                ? null
                : new ChangesViewSession(
                    ApplicationProjection.Safe(created.Session.Source, 256),
                    ApplicationProjection.SafeOrNull(created.Session.Name, 256),
                    ApplicationProjection.SafeOrNull(created.Session.Path, 4_096),
                    safeTaskReport),
            warnings);

        bool aggregateTruncated = JsonSerializer.SerializeToUtf8Bytes(report).Length >
            ApplicationLimits.TargetAggregateBytes;
        if (aggregateTruncated)
        {
            warnings.Add("Session task report details were omitted to keep the application result bounded.");
            report = new ChangesViewReport(
                report.Status,
                report.ExitCode,
                report.WorkspaceRoot,
                report.GitStatusSummary,
                report.GitStatusSucceeded,
                report.GitStatusErrorCode,
                report.Dirty,
                report.DiffStatSummary,
                report.DiffSucceeded,
                report.DiffErrorCode,
                report.DiffTruncated,
                report.ChangedFiles,
                report.Session is null
                    ? null
                    : new ChangesViewSession(
                        report.Session.Source,
                        report.Session.Name,
                        report.Session.Path,
                        null),
                warnings);
        }

        ApplicationDiagnostic[] diagnostics = warnings.Select(warning =>
            new ApplicationDiagnostic(
                "changes-warning",
                ApplicationErrorCategory.Unavailable,
                warning)).ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        return ApplicationResult<ChangesViewReport>.Success(
            report,
            diagnostics,
            filesTruncated || taskReportTruncated || aggregateTruncated || report.DiffTruncated);
    }

    private static bool IsTaskReportTruncated(AgentTaskReport report) =>
        report.Tools.Count > 200 ||
        report.ChangedFiles.Count > ApplicationLimits.MaxChangedFiles ||
        report.Commands.Count > 200 ||
        report.Verification.Count > 200 ||
        report.Risks.Count > 200 ||
        report.Secrets.Count > 100 ||
        report.References.Count > 200;

    private static AgentTaskReport ProjectTaskReport(AgentTaskReport report)
    {
        return new AgentTaskReport(
            ApplicationProjection.Safe(report.Status, 256),
            ApplicationProjection.Safe(report.StopReason, 256),
            ApplicationProjection.Safe(report.Prompt, 4_096),
            ApplicationProjection.SafeOrNull(report.Plan, 4_096),
            report.Tools.Take(200).Select(value => ApplicationProjection.Safe(value, 1_024)).ToArray(),
            report.ChangedFiles.Take(ApplicationLimits.MaxChangedFiles).Select(file => new ChangedFileSummary(
                ApplicationProjection.Safe(file.Path, 4_096),
                ApplicationProjection.Safe(file.Status, 256),
                ApplicationProjection.Safe(file.SourceToolCallId, 256),
                ApplicationProjection.SafeOrNull(file.DiffStat, 4_096),
                file.DiffStatTruncated,
                ApplicationProjection.SafeOrNull(file.ErrorCode, 256))).ToArray(),
            report.Commands.Take(200).Select(command => new AgentTaskCommandReport(
                ApplicationProjection.Safe(command.Source, 256),
                ApplicationProjection.Safe(command.Command, 4_096),
                ApplicationProjection.SafeOrNull(command.WorkingDirectory, 4_096),
                ApplicationProjection.SafeOrNull(command.Status, 256),
                ApplicationProjection.SafeOrNull(command.ErrorCode, 256))).ToArray(),
            report.Verification.Take(200).Select(item => new AgentTaskVerificationReport(
                ApplicationProjection.Safe(item.Status, 256),
                ApplicationProjection.Safe(item.Source, 256),
                ApplicationProjection.SafeOrNull(item.Command, 4_096),
                ApplicationProjection.SafeOrNull(item.WorkingDirectory, 4_096),
                item.Succeeded,
                ApplicationProjection.Safe(item.ApprovalStatus, 256),
                ApplicationProjection.SafeOrNull(item.ErrorCode, 256),
                item.ExitCode,
                item.TimedOut,
                ApplicationProjection.Safe(item.Summary, 4_096))).ToArray(),
            report.Risks.Take(200).Select(value => ApplicationProjection.Safe(value, 4_096)).ToArray(),
            ApplicationProjection.SafeOrNull(report.TracePath, 4_096),
            report.Secrets.Take(100).Select(secret => new AgentTaskSecretPresence(
                ApplicationProjection.Safe(secret.Source, 256),
                ApplicationProjection.Safe(secret.Kind, 256))).ToArray(),
            report.References.Take(200).Select(reference => new AgentTaskReferenceReport(
                ApplicationProjection.Safe(reference.Kind, 256),
                ApplicationProjection.Safe(reference.RequestedPath, 4_096),
                ApplicationProjection.SafeOrNull(reference.ResolvedPath, 4_096),
                ApplicationProjection.Safe(reference.Status, 256),
                reference.IncludedFileCount,
                reference.SkippedFileCount,
                reference.ByteCount,
                reference.Truncated,
                reference.Warnings.Take(100).Select(value => ApplicationProjection.Safe(value, 4_096)).ToArray(),
                ApplicationProjection.SafeOrNull(reference.ErrorCode, 256))).ToArray(),
            ApplicationProjection.SafeOrNull(report.Summary, 4_096),
            ApplicationProjection.SafeOrNull(report.ErrorCode, 256),
            report.ReviewGate is null
                ? null
                : new AgentTaskReviewGateReport(
                    ApplicationProjection.Safe(report.ReviewGate.Status, 256),
                    ApplicationProjection.Safe(report.ReviewGate.Summary, 4_096),
                    report.ReviewGate.HasDiff,
                    report.ReviewGate.Truncated,
                    ApplicationProjection.SafeOrNull(report.ReviewGate.ErrorCode, 256)),
            report.Expert is null
                ? null
                : new AgentTaskExpertReport(
                    ApplicationProjection.Safe(report.Expert.Name, 256),
                    ApplicationProjection.Safe(report.Expert.DisplayName, 256),
                    ApplicationProjection.Safe(report.Expert.ToolBoundary, 2_048),
                    ApplicationProjection.Safe(report.Expert.BoundarySummary, 2_048),
                    ApplicationProjection.Safe(report.Expert.ReportFocus, 2_048)),
            report.Report is null
                ? null
                : new ExecReportMetadata(
                    ApplicationProjection.Safe(report.Report.Mode, 256),
                    report.Report.Generated,
                    ApplicationProjection.SafeOrNull(report.Report.Path, 4_096),
                    ApplicationProjection.SafeOrNull(report.Report.WriteStatus, 256),
                    ApplicationProjection.SafeOrNull(report.Report.ErrorCode, 256),
                    ApplicationProjection.SafeOrNull(report.Report.Summary, 4_096)),
            ApplicationProjection.SafeOrNull(report.WorkspaceRoot, 4_096),
            ApplicationProjection.SafeOrNull(report.SessionName, 256),
            report.Skill is null
                ? null
                : new AgentTaskSkillReport(
                    ApplicationProjection.Safe(report.Skill.Name, 256),
                    ApplicationProjection.Safe(report.Skill.Version, 128),
                    ApplicationProjection.Safe(report.Skill.Description, 2_048),
                    ApplicationProjection.Safe(report.Skill.SourceKind, 128),
                    ApplicationProjection.SafeOrNull(report.Skill.SourcePath, 4_096),
                    ApplicationProjection.Safe(report.Skill.EntryMode, 256),
                    ApplicationProjection.Safe(report.Skill.Expert, 256),
                    ApplicationProjection.Safe(report.Skill.Report, 256),
                    ApplicationProjection.Safe(report.Skill.SafetySummary, 2_048),
                    ApplicationProjection.SafeOrNull(report.Skill.ValidationCommand, 4_096),
                    report.Skill.SuggestedReferences.Take(200)
                        .Select(value => ApplicationProjection.Safe(value, 4_096))
                        .ToArray()));
    }

    private static Func<ToolExecutionContext, CancellationToken, ToolExecutionResult> CreateGitStatusQuery()
    {
        GitStatusTool tool = new(new WorkspaceGuard());
        return tool.Execute;
    }

    private static Func<ToolExecutionContext, CancellationToken, ToolExecutionResult> CreateGitDiffStatQuery()
    {
        GitDiffTool tool = new(new WorkspaceGuard());
        return tool.Execute;
    }

    private static bool IsConversationStoreException(Exception exception) => exception is
        InvalidOperationException or
        IOException or
        UnauthorizedAccessException or
        JsonException or
        NotSupportedException;

    private static string ResolveSessionPath(
        CliEnvironmentSnapshot snapshot,
        ConversationSessionName sessionName)
    {
        string? userConfigDirectory = Path.GetDirectoryName(snapshot.UserConfigPath);
        string root = string.IsNullOrWhiteSpace(userConfigDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".caicli")
            : userConfigDirectory;
        string sessionDirectory = Path.GetFullPath(Path.Combine(root, "sessions"));
        string path = Path.GetFullPath(Path.Combine(
            sessionDirectory,
            $"{sessionName.FileSafeName}.transcript.json"));
        string rootedSessionDirectory = sessionDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? sessionDirectory
            : sessionDirectory + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!path.StartsWith(rootedSessionDirectory, comparison))
        {
            throw new InvalidOperationException("Conversation transcript path must remain inside the session directory.");
        }

        return path;
    }
}
