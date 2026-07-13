using System.Globalization;
using System.Text.Json;

namespace CSharpAiCli.Core;

public sealed class PipelineTextRenderer
{
    private readonly TextWriter output;

    public PipelineTextRenderer(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        this.output = output;
    }

    public void WriteList(IReadOnlyList<PipelineManifest> pipelines)
    {
        ArgumentNullException.ThrowIfNull(pipelines);
        output.WriteLine("C# AI CLI built-in pipelines");
        output.WriteLine($"count: {pipelines.Count.ToString(CultureInfo.InvariantCulture)}");
        foreach (PipelineManifest pipeline in pipelines)
        {
            output.WriteLine($"- {pipeline.Name}: {pipeline.Description}");
            output.WriteLine($"  roles: {string.Join(" -> ", pipeline.Steps.Select(step => step.Role))}");
        }
    }

    public void WritePlan(PipelinePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        output.WriteLine("C# AI CLI pipeline plan");
        output.WriteLine($"pipeline: {plan.Pipeline.Name}");
        output.WriteLine($"description: {plan.Pipeline.Description}");
        output.WriteLine($"workspace: {plan.WorkspaceRoot}");
        if (!string.IsNullOrWhiteSpace(plan.Cwd))
        {
            output.WriteLine($"cwd: {plan.Cwd}");
        }

        output.WriteLine($"task: {plan.Task}");
        output.WriteLine($"roleOrder: {string.Join(" -> ", plan.Pipeline.Steps.Select(step => step.Role))}");
        output.WriteLine("roles:");
        for (int index = 0; index < plan.Pipeline.Steps.Count; index++)
        {
            PipelineRoleStep step = plan.Pipeline.Steps[index];
            string entry = string.Equals(step.CommandFamily, PipelineCommandFamily.Skill, StringComparison.Ordinal)
                ? $"skill:{step.Skill}"
                : "exec";
            output.WriteLine(
                $"- {(index + 1).ToString(CultureInfo.InvariantCulture)}. {step.Role} " +
                $"step={step.StepId} expert={step.Expert} entry={entry} " +
                $"boundary={(step.Boundary.IsReadOnly ? "read-only" : "write-capable")}");
            output.WriteLine($"  tools: {step.Boundary.Summary}");
            output.WriteLine($"  instructions: {step.Instructions}");
        }

        output.WriteLine("execution: plan-only; no model or tools invoked");
    }

    public void WriteFinalReport(PipelineFinalReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        output.WriteLine("C# AI CLI pipeline result");
        output.WriteLine($"runId: {report.RunId}");
        output.WriteLine($"pipeline: {report.Pipeline}");
        output.WriteLine($"status: {report.Status}");
        output.WriteLine($"stopReason: {report.StopReason}");
        output.WriteLine($"roleCount: {report.Roles.Count.ToString(CultureInfo.InvariantCulture)}");
        output.WriteLine("roles:");
        foreach (PipelineRoleReport role in report.Roles)
        {
            output.WriteLine(
                $"- {role.Role} step={role.StepId} status={role.Status} expert={role.Expert} " +
                $"boundary={(role.Boundary.IsReadOnly ? "read-only" : "write-capable")} " +
                $"queue={role.QueueId} attempt={role.Attempt.ToString(CultureInfo.InvariantCulture)}" +
                (string.IsNullOrWhiteSpace(role.JobId) ? string.Empty : $" job={role.JobId}"));
            if (!string.IsNullOrWhiteSpace(role.Summary))
            {
                output.WriteLine($"  summary: {DiagnosticSecretRedactor.Redact(role.Summary)}");
            }

            if (!string.IsNullOrWhiteSpace(role.ErrorCode))
            {
                output.WriteLine($"  errorCode: {role.ErrorCode}");
            }
        }

        WriteItems("artifacts", report.Artifacts.Select(artifact =>
            $"role={artifact.Role} kind={artifact.Kind} path={artifact.Path} exists={artifact.Exists.ToString().ToLowerInvariant()}"));
        WriteItems("warnings", report.Warnings);
        WriteItems("remainingRisks", report.RemainingRisks);
    }

    public void WriteMarkdown(PipelineFinalReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        output.WriteLine($"# Pipeline report: {report.Pipeline}");
        output.WriteLine();
        output.WriteLine($"- Run: `{report.RunId}`");
        output.WriteLine($"- Status: `{report.Status}`");
        output.WriteLine($"- Stop reason: `{report.StopReason}`");
        output.WriteLine($"- Started: `{report.StartedAtUtc:O}`");
        output.WriteLine($"- Completed: `{report.CompletedAtUtc:O}`");
        output.WriteLine();
        output.WriteLine("## Roles");
        output.WriteLine();
        foreach (PipelineRoleReport role in report.Roles)
        {
            output.WriteLine($"### {role.Role}");
            output.WriteLine();
            output.WriteLine($"- Status: `{role.Status}`");
            output.WriteLine($"- Expert: `{role.Expert}`");
            output.WriteLine($"- Boundary: `{(role.Boundary.IsReadOnly ? "read-only" : "write-capable")}`");
            output.WriteLine($"- Queue attempt: `{role.QueueId}` / `{role.Attempt.ToString(CultureInfo.InvariantCulture)}`");
            output.WriteLine($"- Job: `{role.JobId ?? "none"}`");
            output.WriteLine($"- Summary: {EscapeMarkdown(role.Summary ?? "none")}");
            output.WriteLine();
        }

        WriteMarkdownItems("Artifacts", report.Artifacts.Select(artifact =>
            $"`{artifact.Role}` `{artifact.Kind}` `{artifact.Path}` exists={artifact.Exists.ToString().ToLowerInvariant()}"));
        WriteMarkdownItems("Warnings", report.Warnings);
        WriteMarkdownItems("Remaining risks", report.RemainingRisks);
    }

    private void WriteItems(string heading, IEnumerable<string> items)
    {
        string[] values = items.ToArray();
        output.WriteLine($"{heading}:");
        if (values.Length == 0)
        {
            output.WriteLine("- none");
            return;
        }

        foreach (string value in values)
        {
            output.WriteLine($"- {DiagnosticSecretRedactor.Redact(value)}");
        }
    }

    private void WriteMarkdownItems(string heading, IEnumerable<string> items)
    {
        string[] values = items.ToArray();
        output.WriteLine($"## {heading}");
        output.WriteLine();
        if (values.Length == 0)
        {
            output.WriteLine("- None");
        }
        else
        {
            foreach (string value in values)
            {
                output.WriteLine($"- {EscapeMarkdown(value)}");
            }
        }

        output.WriteLine();
    }

    private static string EscapeMarkdown(string value) =>
        DiagnosticSecretRedactor.Redact(value).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}

public sealed class PipelineJsonRenderer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TextWriter output;

    public PipelineJsonRenderer(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        this.output = output;
    }

    public void WriteList(IReadOnlyList<PipelineManifest> pipelines) => Write(new
    {
        type = "pipeline.list",
        status = "succeeded",
        pipelines
    });

    public void WritePlan(PipelinePlan plan) => Write(new
    {
        type = "pipeline.plan",
        status = "succeeded",
        plan
    });

    public void WriteFinalReport(PipelineFinalReport report) => Write(new
    {
        type = "pipeline.result",
        status = report.Status,
        report
    });

    public void WriteFailure(string type, string errorCode, string summary, string? pipeline = null) => Write(new
    {
        type,
        status = "failed",
        errorCode = DiagnosticSecretRedactor.Redact(errorCode),
        summary = DiagnosticSecretRedactor.Redact(summary),
        pipeline = string.IsNullOrWhiteSpace(pipeline) ? null : DiagnosticSecretRedactor.Redact(pipeline)
    });

    private void Write(object value) => output.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
}
