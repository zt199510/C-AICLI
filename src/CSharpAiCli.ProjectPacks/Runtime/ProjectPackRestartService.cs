using CSharpAiCli.Core;
using CSharpAiCli.ProjectPacks.GerberTiff;

namespace CSharpAiCli.ProjectPacks.Runtime;

public sealed record ProjectPackRestartPreparation(
    bool Succeeded,
    ProjectPackRunRecord? ParentRecord,
    ProjectPackRunCheckpoint? ParentCheckpoint,
    GerberTiffRunPlanSnapshot? Plan,
    string? NewRunId,
    int? Attempt,
    string? OutputDirectory,
    ProjectPackRunDiagnostic? Diagnostic)
{
    public static ProjectPackRestartPreparation Failure(string code, string summary, string runId) =>
        new(false, null, null, null, null, null, null, new ProjectPackRunDiagnostic(code, summary, RunId: runId));
}

public sealed class ProjectPackRestartService
{
    private readonly ManagedProjectPackRunStore store;

    public ProjectPackRestartService(ManagedProjectPackRunStore store)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public ProjectPackRestartPreparation PrepareExecuteRestart(
        string runId,
        WorkspaceContext workspace,
        IReadOnlyDictionary<string, string> toolPaths,
        string currentPolicyFingerprint,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(toolPaths);
        ProjectPackRunReadResult read = store.Read(runId);
        if (!read.Succeeded || read.Record is null || read.Checkpoint is null)
        {
            return ProjectPackRestartPreparation.Failure(
                read.Diagnostic?.ErrorCode ?? ProjectPackRunErrorCode.NotFound,
                read.Diagnostic?.Summary ?? "Interrupted project pack run could not be read.",
                runId);
        }

        ProjectPackRunRecord parent = read.Record;
        if (parent.State != ProjectPackRunState.Interrupted || !parent.RestartRequired)
        {
            return ProjectPackRestartPreparation.Failure(
                ProjectPackRunErrorCode.RestartRequired,
                "Execute restart is available only for an interrupted run that requires an explicit restart decision.",
                runId);
        }

        ProjectPackResumeEligibility eligibility = new ProjectPackRunService(store).EvaluateResume(
            runId,
            workspace,
            toolPaths,
            currentPolicyFingerprint,
            cancellationToken);
        if (eligibility.ErrorCode != ProjectPackRunErrorCode.RestartRequired ||
            eligibility.NextAction != "explicit-restart-decision")
        {
            return ProjectPackRestartPreparation.Failure(
                eligibility.ErrorCode ?? ProjectPackRunErrorCode.ResumeNotEligible,
                eligibility.Summary ?? "Interrupted run failed current restart revalidation.",
                runId);
        }

        GerberTiffRunPlanSnapshot original;
        try
        {
            ManagedProjectPackRunLayout layout = store.GetLayout(runId);
            original = GerberTiffRunPlanLoader.Load(
                ManagedProjectPackRunStore.ReadTextBounded(layout.PlanPath, 2 * 1024 * 1024));
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ProjectPackContractException)
        {
            return ProjectPackRestartPreparation.Failure(
                ProjectPackRunErrorCode.RecordCorrupt,
                "Interrupted run plan could not be loaded safely for restart.",
                runId);
        }

        int attempt = parent.RestartPlan?.Attempt ?? checked(parent.Correlation.Attempt + 1);
        string newRunId = parent.RestartPlan?.NewRunId ?? ProjectPackRunId.Create(nowUtc);
        string outputDirectory = parent.RestartPlan?.OutputDirectory ??
            BuildAttemptOutputDirectory(original.OutputDirectory, parent.Correlation.Attempt, attempt);
        GerberTiffConversionPlan current = new GerberTiffConversionPlanBuilder().Build(
            workspace,
            original.InputDirectory,
            outputDirectory,
            toolPaths,
            trustedHashes: null,
            cancellationToken);
        if (!current.Runnable || current.PlanId is null || current.Fingerprint is null)
        {
            return ProjectPackRestartPreparation.Failure(
                current.Diagnostics.Any(diagnostic => diagnostic.Code == GerberTiffDiagnosticCode.OutputAlreadyExists)
                    ? ProjectPackRunErrorCode.OutputConflict
                    : ProjectPackRunErrorCode.PlanInvalid,
                "New-attempt restart plan did not pass current input/tool/output validation.",
                runId);
        }

        string planJson = GerberTiffPlanRenderer.RenderJson(current, "restart");
        GerberTiffRunPlanSnapshot plan;
        try
        {
            plan = GerberTiffRunPlanLoader.Load(planJson);
        }
        catch (ProjectPackContractException exception)
        {
            return ProjectPackRestartPreparation.Failure(exception.ErrorCode, exception.Message, runId);
        }

        if (parent.RestartPlan is not null)
        {
            ProjectPackRunReadResult child = store.Read(parent.RestartPlan.NewRunId);
            if (child.Succeeded)
            {
                return ProjectPackRestartPreparation.Failure(
                    ProjectPackRunErrorCode.DecisionConflict,
                    "Interrupted run already created its reserved restart attempt.",
                    runId);
            }

            if (child.Diagnostic?.ErrorCode != ProjectPackRunErrorCode.NotFound)
            {
                return ProjectPackRestartPreparation.Failure(
                    child.Diagnostic?.ErrorCode ?? ProjectPackRunErrorCode.RecordCorrupt,
                    "Reserved restart attempt path is not safely reusable.",
                    runId);
            }

            return new ProjectPackRestartPreparation(
                true,
                parent,
                read.Checkpoint,
                plan,
                newRunId,
                attempt,
                outputDirectory,
                null);
        }

        ProjectPackRunCorrelation correlation = new(
            parent.Correlation.QueueId,
            parent.Correlation.JobId,
            parent.Correlation.TaskReportPointer,
            parent.Correlation.RootRunId,
            parent.Correlation.ParentRunId,
            parent.Correlation.Attempt,
            newRunId);
        ProjectPackRestartPlan restartPlan = new(newRunId, attempt, outputDirectory, nowUtc);
        ProjectPackRunRecord updated = parent.WithRestartPlan(restartPlan, correlation, nowUtc);
        ProjectPackRunCheckpoint checkpoint = new(
            read.Checkpoint.SchemaVersion,
            read.Checkpoint.RunId,
            updated.Revision,
            updated.State,
            read.Checkpoint.PlanFingerprint,
            read.Checkpoint.PolicyFingerprint,
            nowUtc,
            read.Checkpoint.Stages,
            approvalPersisted: false);
        ProjectPackRunMutationResult reserved = store.Update(updated, checkpoint, parent.Revision);
        if (!reserved.Succeeded || reserved.Record is null || reserved.Checkpoint is null)
        {
            return ProjectPackRestartPreparation.Failure(
                reserved.Diagnostic?.ErrorCode ?? ProjectPackRunErrorCode.RevisionConflict,
                reserved.Diagnostic?.Summary ?? "New restart attempt could not be reserved atomically.",
                runId);
        }

        return new ProjectPackRestartPreparation(
            true,
            reserved.Record,
            reserved.Checkpoint,
            plan,
            newRunId,
            attempt,
            outputDirectory,
            null);
    }

    private static string BuildAttemptOutputDirectory(string outputDirectory, int currentAttempt, int nextAttempt)
    {
        string suffix = $".attempt-{currentAttempt:D4}";
        string root = currentAttempt > 1 && outputDirectory.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? outputDirectory[..^suffix.Length]
            : outputDirectory;
        return $"{root}.attempt-{nextAttempt:D4}";
    }
}
