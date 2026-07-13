using System.Collections.ObjectModel;
using System.Text;

namespace CSharpAiCli.Core;

public sealed record PipelineRoleExecutionRequest(
    string RunId,
    int StepIndex,
    PipelinePlan Plan,
    PipelineRoleStep Step,
    string Task);

public sealed record PipelineRoleExecutionResult(TaskQueueItem QueueItem, JobRecord? JobRecord)
{
    public TaskQueueAttempt Attempt => QueueItem.Attempts.LastOrDefault() ??
        throw new InvalidOperationException("Pipeline role execution did not produce a queue attempt.");
}

public interface IPipelineRoleExecutor
{
    PipelineRoleExecutionResult Execute(PipelineRoleExecutionRequest request);
}

public sealed class DelegatePipelineRoleExecutor : IPipelineRoleExecutor
{
    private readonly Func<PipelineRoleExecutionRequest, PipelineRoleExecutionResult> execute;

    public DelegatePipelineRoleExecutor(Func<PipelineRoleExecutionRequest, PipelineRoleExecutionResult> execute)
    {
        ArgumentNullException.ThrowIfNull(execute);
        this.execute = execute;
    }

    public PipelineRoleExecutionResult Execute(PipelineRoleExecutionRequest request) => execute(request);
}

public sealed class PipelineRunner
{
    private readonly IPipelineRoleExecutor roleExecutor;
    private readonly Func<DateTimeOffset> utcNowProvider;

    public PipelineRunner(IPipelineRoleExecutor roleExecutor, Func<DateTimeOffset>? utcNowProvider = null)
    {
        ArgumentNullException.ThrowIfNull(roleExecutor);
        this.roleExecutor = roleExecutor;
        this.utcNowProvider = utcNowProvider ?? (() => DateTimeOffset.UtcNow);
    }

    public PipelineFinalReport Run(PipelinePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return Run(plan, PipelineRunIdGenerator.Create(utcNowProvider()));
    }

    public PipelineFinalReport Run(PipelinePlan plan, string runId)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!PipelineRunIdGenerator.IsValid(runId))
        {
            throw new ArgumentException("Pipeline run id is invalid.", nameof(runId));
        }

        DateTimeOffset startedAtUtc = utcNowProvider();
        List<PipelineRoleReport> roles = [];
        List<PipelineArtifactReference> artifacts = [];
        List<string> warnings = [];
        List<string> risks = [];
        string status = PipelineStatus.Succeeded;
        string stopReason = PipelineStopReason.Completed;

        for (int index = 0; index < plan.Pipeline.Steps.Count; index++)
        {
            PipelineRoleStep step = plan.Pipeline.Steps[index];
            string roleTask = BuildRoleTask(plan, runId, index, step, roles);
            PipelineRoleExecutionResult execution = roleExecutor.Execute(new PipelineRoleExecutionRequest(
                runId,
                index + 1,
                plan,
                step,
                roleTask));
            PipelineRoleReport roleReport = CreateRoleReport(step, execution);
            roles.Add(roleReport);
            artifacts.AddRange(roleReport.Artifacts.Select(artifact => new PipelineArtifactReference(
                step.StepId,
                step.Role,
                roleReport.JobId,
                artifact.Kind,
                artifact.Path,
                artifact.Exists,
                artifact.Summary,
                artifact.Sha256)));
            warnings.AddRange(roleReport.Warnings.Select(warning => $"{step.Role}: {warning}"));
            risks.AddRange(roleReport.RemainingRisks.Select(risk => $"{step.Role}: {risk}"));

            if (!string.Equals(roleReport.Status, TaskQueueStatus.Succeeded, StringComparison.Ordinal))
            {
                status = PipelineStatus.Failed;
                stopReason = PipelineStopReason.RoleFailed;
                risks.Add($"Role '{step.Role}' failed with error '{roleReport.ErrorCode ?? "unknown"}'.");
                string[] skippedRoles = plan.Pipeline.Steps
                    .Skip(index + 1)
                    .Select(remaining => remaining.Role)
                    .ToArray();
                if (skippedRoles.Length > 0)
                {
                    warnings.Add($"Pipeline stopped after role '{step.Role}'; skipped roles: {string.Join(", ", skippedRoles)}.");
                    risks.Add($"Roles not executed after failure: {string.Join(", ", skippedRoles)}.");
                }

                break;
            }
        }

        return new PipelineFinalReport(
            runId,
            plan.Pipeline.Name,
            status,
            stopReason,
            startedAtUtc,
            utcNowProvider(),
            roles,
            artifacts,
            warnings,
            risks);
    }

    private static PipelineRoleReport CreateRoleReport(
        PipelineRoleStep step,
        PipelineRoleExecutionResult execution)
    {
        TaskQueueItem item = execution.QueueItem;
        TaskQueueAttempt attempt = execution.Attempt;
        JobRecord? job = execution.JobRecord;
        List<string> warnings = [.. item.Warnings];
        warnings.AddRange(job?.Warnings ?? []);
        List<string> risks = job?.TaskReport?.Risks.ToList() ?? [];
        if (job?.TaskReport is null)
        {
            risks.Add("Role did not produce task report metadata.");
        }

        return new PipelineRoleReport(
            step.StepId,
            step.Role,
            step.Expert,
            step.Boundary,
            item.Status,
            attempt.ExitCode ?? 1,
            item.QueueId,
            attempt.Attempt,
            attempt.JobId ?? item.LatestJobId ?? job?.JobId,
            job?.StopReason,
            attempt.ErrorCode ?? item.ErrorCode ?? job?.ErrorCode,
            attempt.Summary ?? item.Summary ?? job?.Summary,
            job?.TaskReport,
            new ReadOnlyCollection<JobArtifact>((job?.Artifacts ?? []).ToArray()),
            new ReadOnlyCollection<string>(warnings.Distinct(StringComparer.Ordinal).ToArray()),
            new ReadOnlyCollection<string>(risks.Distinct(StringComparer.Ordinal).ToArray()));
    }

    private static string BuildRoleTask(
        PipelinePlan plan,
        string runId,
        int index,
        PipelineRoleStep step,
        IReadOnlyList<PipelineRoleReport> priorRoles)
    {
        StringBuilder builder = new();
        builder.AppendLine($"Pipeline run: {runId}");
        builder.AppendLine($"Pipeline: {plan.Pipeline.Name}");
        builder.AppendLine($"Role step: {index + 1}/{plan.Pipeline.Steps.Count} {step.Role} ({step.StepId})");
        builder.AppendLine($"Role instructions: {step.Instructions}");
        builder.AppendLine($"Tool boundary: {step.Boundary.Summary}");
        builder.AppendLine();
        builder.AppendLine("Original task:");
        builder.AppendLine(plan.Task);
        if (priorRoles.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Prior role evidence:");
            foreach (PipelineRoleReport prior in priorRoles)
            {
                builder.AppendLine(
                    $"- role={prior.Role} status={prior.Status} queue={prior.QueueId} " +
                    $"job={prior.JobId ?? "none"} summary={prior.Summary ?? "none"}");
            }
        }

        string task = DiagnosticSecretRedactor.Redact(builder.ToString().Trim());
        const int maxLength = 16_384;
        return task.Length <= maxLength ? task : task[..maxLength];
    }
}
