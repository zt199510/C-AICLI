using System.Globalization;
using System.Text.Json;

namespace CSharpAiCli.Core;

public sealed class AutomationTextRenderer
{
    private readonly TextWriter output;

    public AutomationTextRenderer(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        this.output = output;
    }

    public void WriteList(AutomationCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        output.WriteLine("C# AI CLI local automations");
        output.WriteLine($"count: {catalog.Items.Count.ToString(CultureInfo.InvariantCulture)}");
        foreach (AutomationCatalogItem item in catalog.Items)
        {
            AutomationManifest manifest = item.Manifest;
            output.WriteLine($"- {Safe(manifest.Name)}: {Safe(manifest.Description)}");
            output.WriteLine(
                $"  trigger={Safe(manifest.Trigger?.Type)} target={Safe(manifest.Target?.Type)} " +
                $"source={Safe(item.Source.Path)}");
        }

        WriteDiagnostics(catalog.Diagnostics);
    }

    public void WriteValidation(AutomationCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        output.WriteLine("C# AI CLI automation validation");
        output.WriteLine($"status: {(catalog.Diagnostics.Count == 0 ? "succeeded" : "failed")}");
        output.WriteLine($"validCount: {catalog.Items.Count.ToString(CultureInfo.InvariantCulture)}");
        output.WriteLine($"diagnosticCount: {catalog.Diagnostics.Count.ToString(CultureInfo.InvariantCulture)}");
        WriteDiagnostics(catalog.Diagnostics);
    }

    public void WritePlan(AutomationPlan plan) => WritePlan(plan, "C# AI CLI automation plan");

    public void WriteDryRun(AutomationPlan plan) => WritePlan(plan, "C# AI CLI automation dry-run");

    private void WritePlan(AutomationPlan plan, string heading)
    {
        ArgumentNullException.ThrowIfNull(plan);
        AutomationManifest manifest = plan.Item.Manifest;
        AutomationTarget target = manifest.Target!;
        output.WriteLine(heading);
        output.WriteLine($"automation: {Safe(manifest.Name)}");
        output.WriteLine($"description: {Safe(manifest.Description)}");
        output.WriteLine($"source: {Safe(plan.Item.Source.Path)}");
        output.WriteLine($"workspace: {Safe(plan.WorkspaceRoot)}");
        output.WriteLine($"mode: {Safe(plan.Mode)}");
        output.WriteLine($"trigger: {Safe(manifest.Trigger?.Type)}");
        output.WriteLine($"scheduleEnabled: false");
        output.WriteLine($"schedulePreview: {Safe(plan.SchedulePreview.Schedule ?? "none")}");
        output.WriteLine($"timeZone: {Safe(plan.SchedulePreview.TimeZone ?? "none")}");
        output.WriteLine($"scheduleSummary: {Safe(plan.SchedulePreview.Summary)}");
        output.WriteLine($"target: {Safe(target.Type)}");
        if (!string.IsNullOrWhiteSpace(target.Family))
        {
            output.WriteLine($"family: {Safe(target.Family)}");
        }

        if (!string.IsNullOrWhiteSpace(target.Name))
        {
            output.WriteLine($"targetName: {Safe(target.Name)}");
        }

        if (!string.IsNullOrWhiteSpace(target.Expert))
        {
            output.WriteLine($"expert: {Safe(target.Expert)}");
        }

        if (!string.IsNullOrWhiteSpace(target.Cwd))
        {
            output.WriteLine($"cwd: {Safe(target.Cwd)}");
        }

        output.WriteLine($"report: {Safe(target.Report ?? "none")}");
        output.WriteLine($"task: {Safe(target.Task)}");
        output.WriteLine($"safety: {Safe(manifest.Safety?.ToSummary())}");
        output.WriteLine("execution: none; plan and schedule fields are preview only");
    }

    public void WriteFailure(string errorCode, string summary)
    {
        output.WriteLine($"errorCode: {Safe(errorCode)}");
        output.WriteLine($"summary: {Safe(summary)}");
    }

    public void WriteRunResult(AutomationRunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        output.WriteLine("C# AI CLI automation run");
        output.WriteLine($"automation: {Safe(result.Automation)}");
        output.WriteLine($"runId: {Safe(result.RunId)}");
        output.WriteLine($"mode: {Safe(result.Mode)}");
        output.WriteLine($"target: {Safe(result.TargetType)}");
        output.WriteLine($"status: {Safe(result.Status)}");
        output.WriteLine($"exitCode: {result.ExitCode.ToString(CultureInfo.InvariantCulture)}");
        foreach (string queueId in result.QueueIds)
        {
            output.WriteLine($"queueId: {Safe(queueId)}");
        }

        foreach (string jobId in result.JobIds)
        {
            output.WriteLine($"jobId: {Safe(jobId)}");
        }

        if (!string.IsNullOrWhiteSpace(result.PipelineRunId))
        {
            output.WriteLine($"pipelineRunId: {Safe(result.PipelineRunId)}");
        }

        if (!string.IsNullOrWhiteSpace(result.Summary))
        {
            output.WriteLine($"summary: {Safe(result.Summary)}");
        }

        foreach (string warning in result.Warnings)
        {
            output.WriteLine($"warning: {Safe(warning)}");
        }
    }

    private void WriteDiagnostics(IReadOnlyList<AutomationDiagnostic> diagnostics)
    {
        if (diagnostics.Count == 0)
        {
            return;
        }

        output.WriteLine("diagnostics:");
        foreach (AutomationDiagnostic diagnostic in diagnostics)
        {
            output.WriteLine(
                $"- errorCode={Safe(diagnostic.ErrorCode)} automation={Safe(diagnostic.AutomationName ?? "unknown")} " +
                $"source={Safe(diagnostic.SourcePath ?? "unknown")} summary={Safe(diagnostic.Summary)}");
        }
    }

    private static string Safe(string? value) =>
        DiagnosticSecretRedactor.Redact(value ?? string.Empty);
}

public sealed class AutomationJsonRenderer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TextWriter output;

    public AutomationJsonRenderer(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        this.output = output;
    }

    public void WriteList(AutomationCatalog catalog) => Write(new Dictionary<string, object?>
    {
        ["type"] = "automation.list",
        ["status"] = "succeeded",
        ["automations"] = catalog.Items.Select(ToJson).ToArray(),
        ["diagnostics"] = catalog.Diagnostics.Select(ToJson).ToArray()
    });

    public void WriteValidation(AutomationCatalog catalog) => Write(new Dictionary<string, object?>
    {
        ["type"] = "automation.validate",
        ["status"] = catalog.Diagnostics.Count == 0 ? "succeeded" : "failed",
        ["validCount"] = catalog.Items.Count,
        ["diagnosticCount"] = catalog.Diagnostics.Count,
        ["diagnostics"] = catalog.Diagnostics.Select(ToJson).ToArray()
    });

    public void WritePlan(AutomationPlan plan) => Write(new Dictionary<string, object?>
    {
        ["type"] = "automation.plan",
        ["status"] = "succeeded",
        ["plan"] = ToJson(plan)
    });

    public void WriteDryRun(AutomationPlan plan) => Write(new Dictionary<string, object?>
    {
        ["type"] = "automation.run.dry-run",
        ["status"] = "dry-run",
        ["plan"] = ToJson(plan)
    });

    public void WriteFailure(string type, string errorCode, string summary, string? automation = null) =>
        Write(new Dictionary<string, object?>
        {
            ["type"] = type,
            ["status"] = "failed",
            ["errorCode"] = Safe(errorCode),
            ["summary"] = Safe(summary),
            ["automation"] = SafeOrNull(automation)
        });

    public void WriteRunResult(AutomationRunResult result) => Write(new Dictionary<string, object?>
    {
        ["type"] = "automation.result",
        ["status"] = Safe(result.Status),
        ["result"] = result
    });

    private void Write(object value) => output.WriteLine(JsonSerializer.Serialize(value, JsonOptions));

    private static Dictionary<string, object?> ToJson(AutomationCatalogItem item) => new()
    {
        ["schemaVersion"] = item.Manifest.SchemaVersion,
        ["name"] = SafeOrNull(item.Manifest.Name),
        ["description"] = SafeOrNull(item.Manifest.Description),
        ["source"] = new Dictionary<string, object?>
        {
            ["kind"] = item.Source.Kind,
            ["path"] = Safe(item.Source.Path)
        },
        ["trigger"] = ToJson(item.Manifest.Trigger),
        ["target"] = ToJson(item.Manifest.Target),
        ["safety"] = ToJson(item.Manifest.Safety)
    };

    private static Dictionary<string, object?> ToJson(AutomationPlan plan) => new()
    {
        ["schemaVersion"] = plan.SchemaVersion,
        ["automation"] = SafeOrNull(plan.Item.Manifest.Name),
        ["description"] = SafeOrNull(plan.Item.Manifest.Description),
        ["source"] = Safe(plan.Item.Source.Path),
        ["workspace"] = Safe(plan.WorkspaceRoot),
        ["mode"] = Safe(plan.Mode),
        ["trigger"] = ToJson(plan.Item.Manifest.Trigger),
        ["schedulePreview"] = new Dictionary<string, object?>
        {
            ["enabled"] = false,
            ["schedule"] = SafeOrNull(plan.SchedulePreview.Schedule),
            ["timeZone"] = SafeOrNull(plan.SchedulePreview.TimeZone),
            ["summary"] = Safe(plan.SchedulePreview.Summary)
        },
        ["target"] = ToJson(plan.Item.Manifest.Target),
        ["safety"] = ToJson(plan.Item.Manifest.Safety),
        ["execution"] = "none"
    };

    private static Dictionary<string, object?>? ToJson(AutomationTrigger? trigger) => trigger is null ? null : new()
    {
        ["type"] = SafeOrNull(trigger.Type),
        ["schedule"] = SafeOrNull(trigger.Schedule),
        ["timeZone"] = SafeOrNull(trigger.TimeZone)
    };

    private static Dictionary<string, object?>? ToJson(AutomationTarget? target) => target is null ? null : new()
    {
        ["type"] = SafeOrNull(target.Type),
        ["family"] = SafeOrNull(target.Family),
        ["name"] = SafeOrNull(target.Name),
        ["task"] = SafeOrNull(target.Task),
        ["cwd"] = SafeOrNull(target.Cwd),
        ["expert"] = SafeOrNull(target.Expert),
        ["report"] = SafeOrNull(target.Report)
    };

    private static Dictionary<string, object?>? ToJson(AutomationSafety? safety) => safety is null ? null : new()
    {
        ["manualOnly"] = safety.ManualOnly,
        ["allowWrites"] = safety.AllowWrites,
        ["allowShell"] = safety.AllowShell,
        ["allowMcp"] = safety.AllowMcp,
        ["summary"] = Safe(safety.ToSummary())
    };

    private static Dictionary<string, object?> ToJson(AutomationDiagnostic diagnostic) => new()
    {
        ["errorCode"] = Safe(diagnostic.ErrorCode),
        ["summary"] = Safe(diagnostic.Summary),
        ["sourcePath"] = SafeOrNull(diagnostic.SourcePath),
        ["automationName"] = SafeOrNull(diagnostic.AutomationName)
    };

    private static string Safe(string? value) => DiagnosticSecretRedactor.Redact(value ?? string.Empty);

    private static string? SafeOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : Safe(value);
}
