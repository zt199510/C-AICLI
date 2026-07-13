using System.Globalization;
using System.Text.Json;

namespace CSharpAiCli.Core;

public sealed class TaskQueueTextRenderer
{
    private readonly TextWriter output;

    public TaskQueueTextRenderer(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        this.output = output;
    }

    public void WriteAdded(TaskQueueItem item)
    {
        output.WriteLine("C# AI CLI queue item added");
        WriteItem(item, includeTask: true);
    }

    public void WriteList(TaskQueueListResult result)
    {
        output.WriteLine("C# AI CLI task queue");
        if (result.Items.Count == 0)
        {
            output.WriteLine("status: empty");
        }
        else
        {
            output.WriteLine($"count: {result.Items.Count.ToString(CultureInfo.InvariantCulture)}");
            foreach (TaskQueueItem item in result.Items)
            {
                output.WriteLine(
                    $"- {item.QueueId} status={item.Status} family={item.Request.Family} " +
                    $"attempts={item.Attempts.Count.ToString(CultureInfo.InvariantCulture)} " +
                    $"created={item.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)}" +
                    (string.IsNullOrWhiteSpace(item.Request.Skill) ? string.Empty : $" skill={item.Request.Skill}") +
                    (string.IsNullOrWhiteSpace(item.LatestJobId) ? string.Empty : $" job={item.LatestJobId}"));
            }
        }

        WriteDiagnostics(result.Diagnostics);
    }

    public void WriteShow(TaskQueueItem item)
    {
        output.WriteLine("C# AI CLI queue item");
        WriteItem(item, includeTask: true);
        if (item.Attempts.Count > 0)
        {
            output.WriteLine("attempts:");
            foreach (TaskQueueAttempt attempt in item.Attempts)
            {
                output.WriteLine(
                    $"- attempt={attempt.Attempt.ToString(CultureInfo.InvariantCulture)} status={attempt.Status} " +
                    $"started={attempt.StartedAtUtc.ToString("O", CultureInfo.InvariantCulture)}" +
                    (attempt.CompletedAtUtc is null ? string.Empty : $" completed={attempt.CompletedAtUtc.Value.ToString("O", CultureInfo.InvariantCulture)}") +
                    (string.IsNullOrWhiteSpace(attempt.JobId) ? string.Empty : $" job={attempt.JobId}") +
                    (string.IsNullOrWhiteSpace(attempt.ErrorCode) ? string.Empty : $" errorCode={attempt.ErrorCode}"));
            }
        }
    }

    public void WriteTransition(string heading, TaskQueueItem item)
    {
        output.WriteLine(heading);
        WriteItem(item, includeTask: false);
    }

    public void WriteCleanup(TaskQueueCleanupResult result)
    {
        output.WriteLine("C# AI CLI queue cleanup");
        output.WriteLine($"status: {(result.Succeeded ? "succeeded" : "failed")}");
        output.WriteLine($"deletedCount: {result.DeletedCount.ToString(CultureInfo.InvariantCulture)}");
        if (!string.IsNullOrWhiteSpace(result.ErrorCode))
        {
            output.WriteLine($"errorCode: {result.ErrorCode}");
        }

        if (!string.IsNullOrWhiteSpace(result.Summary))
        {
            output.WriteLine($"summary: {DiagnosticSecretRedactor.Redact(result.Summary)}");
        }

        WriteDiagnostics(result.Diagnostics);
    }

    private void WriteItem(TaskQueueItem item, bool includeTask)
    {
        output.WriteLine($"queueId: {item.QueueId}");
        output.WriteLine($"status: {item.Status}");
        output.WriteLine($"family: {item.Request.Family}");
        if (!string.IsNullOrWhiteSpace(item.Request.Skill))
        {
            output.WriteLine($"skill: {item.Request.Skill}");
        }

        output.WriteLine($"workspace: {item.Request.WorkspaceRoot}");
        if (!string.IsNullOrWhiteSpace(item.Request.Cwd))
        {
            output.WriteLine($"cwd: {item.Request.Cwd}");
        }

        if (!string.IsNullOrWhiteSpace(item.Request.Expert))
        {
            output.WriteLine($"expert: {item.Request.Expert}");
        }

        if (!string.IsNullOrWhiteSpace(item.Request.ReportMode))
        {
            output.WriteLine($"report: {item.Request.ReportMode}");
        }

        if (item.Request.Automation is not null)
        {
            output.WriteLine($"automation: {item.Request.Automation.Automation}");
            output.WriteLine($"automationRunId: {item.Request.Automation.RunId}");
            output.WriteLine($"automationTarget: {item.Request.Automation.TargetType}");
        }

        if (includeTask)
        {
            output.WriteLine($"task: {item.Request.Task}");
        }

        output.WriteLine($"attemptCount: {item.Attempts.Count.ToString(CultureInfo.InvariantCulture)}");
        output.WriteLine($"createdAtUtc: {item.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)}");
        output.WriteLine($"updatedAtUtc: {item.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture)}");
        if (!string.IsNullOrWhiteSpace(item.LatestJobId))
        {
            output.WriteLine($"latestJobId: {item.LatestJobId}");
        }

        if (!string.IsNullOrWhiteSpace(item.ErrorCode))
        {
            output.WriteLine($"errorCode: {item.ErrorCode}");
        }

        if (!string.IsNullOrWhiteSpace(item.Summary))
        {
            output.WriteLine($"summary: {item.Summary}");
        }
    }

    private void WriteDiagnostics(IReadOnlyList<TaskQueueDiagnostic> diagnostics)
    {
        if (diagnostics.Count == 0)
        {
            return;
        }

        output.WriteLine("diagnostics:");
        foreach (TaskQueueDiagnostic diagnostic in diagnostics)
        {
            output.WriteLine($"- errorCode={diagnostic.ErrorCode} summary={DiagnosticSecretRedactor.Redact(diagnostic.Summary)}");
        }
    }
}

public sealed class TaskQueueJsonRenderer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TextWriter output;

    public TaskQueueJsonRenderer(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        this.output = output;
    }

    public void WriteAdded(TaskQueueItem item) => Write(new
    {
        type = "queue.add",
        status = "succeeded",
        item
    });

    public void WriteList(TaskQueueListResult result) => Write(new
    {
        type = "queue.list",
        status = "succeeded",
        items = result.Items.Select(ToListItem).ToArray(),
        diagnostics = result.Diagnostics.Select(ToDiagnostic).ToArray()
    });

    public void WriteShow(TaskQueueItem item) => Write(new
    {
        type = "queue.show",
        status = "succeeded",
        item
    });

    public void WriteTransition(string type, TaskQueueItem item) => Write(new
    {
        type,
        status = item.Status,
        item
    });

    public void WriteCleanup(TaskQueueCleanupResult result) => Write(new
    {
        type = "queue.cleanup",
        status = result.Succeeded ? "succeeded" : "failed",
        deletedCount = result.DeletedCount,
        deletedQueueIds = result.DeletedQueueIds.Select(Safe).ToArray(),
        diagnostics = result.Diagnostics.Select(ToDiagnostic).ToArray(),
        errorCode = SafeOrNull(result.ErrorCode),
        summary = SafeOrNull(result.Summary)
    });

    private void Write(object value) => output.WriteLine(JsonSerializer.Serialize(value, JsonOptions));

    private static object ToListItem(TaskQueueItem item) => new
    {
        item.QueueId,
        item.Status,
        family = item.Request.Family,
        skill = item.Request.Skill,
        automation = item.Request.Automation,
        workspace = item.Request.WorkspaceRoot,
        attemptCount = item.Attempts.Count,
        item.LatestJobId,
        item.ErrorCode,
        item.CreatedAtUtc,
        item.UpdatedAtUtc,
        item.CompletedAtUtc
    };

    private static object ToDiagnostic(TaskQueueDiagnostic diagnostic) => new
    {
        errorCode = Safe(diagnostic.ErrorCode),
        summary = Safe(diagnostic.Summary),
        path = SafeOrNull(diagnostic.Path),
        queueId = SafeOrNull(diagnostic.QueueId)
    };

    private static string Safe(string value) =>
        DiagnosticSecretRedactor.Redact(value ?? string.Empty);

    private static string? SafeOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Safe(value);
}
