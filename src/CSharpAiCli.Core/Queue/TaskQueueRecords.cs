using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CSharpAiCli.Core;

public static class TaskQueueStatus
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Canceled = "canceled";

    public static bool IsKnown(string? status) =>
        status is Pending or Running or Succeeded or Failed or Canceled;

    public static bool IsTerminal(string? status) =>
        status is Succeeded or Failed or Canceled;

    public static bool CanRun(string? status) => status is Pending or Failed;
}

public static class TaskQueueCommandFamily
{
    public const string Exec = "exec";
    public const string Skill = "skills run";

    public static bool IsKnown(string? family) => family is Exec or Skill;
}

public static class TaskQueueErrorCode
{
    public const string NotFound = "queue-item-not-found";
    public const string CorruptRecord = "corrupt-queue-record";
    public const string RecordUnreadable = "queue-record-unreadable";
    public const string RecordWriteFailed = "queue-record-write-failed";
    public const string InvalidState = "queue-invalid-state";
    public const string InvalidRequest = "queue-invalid-request";
    public const string ExecutionFailed = "queue-execution-failed";
    public const string JobRecordMissing = "queue-job-record-missing";
    public const string UnsafeCleanupStatus = "queue-cleanup-status-unsafe";
}

public sealed record TaskQueueRequest
{
    public TaskQueueRequest(
        string Family,
        string Task,
        string WorkspaceRoot,
        string? Cwd = null,
        string? Skill = null,
        string? Expert = null,
        string? ReportMode = null)
    {
        if (!TaskQueueCommandFamily.IsKnown(Family))
        {
            throw new ArgumentException("Queue command family is invalid.", nameof(Family));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(Task);
        ArgumentException.ThrowIfNullOrWhiteSpace(WorkspaceRoot);
        if (Family == TaskQueueCommandFamily.Skill && string.IsNullOrWhiteSpace(Skill))
        {
            throw new ArgumentException("A skill name is required for a queued skill run.", nameof(Skill));
        }

        this.Family = Family;
        this.Task = Safe(Task, 16_384);
        this.WorkspaceRoot = Safe(WorkspaceRoot, 4_096);
        this.Cwd = SafeOrNull(Cwd, 4_096);
        this.Skill = SafeOrNull(Skill, 256);
        this.Expert = SafeOrNull(Expert, 256);
        this.ReportMode = SafeOrNull(ReportMode, 64);
    }

    public string Family { get; }

    public string Task { get; }

    public string WorkspaceRoot { get; }

    public string? Cwd { get; }

    public string? Skill { get; }

    public string? Expert { get; }

    public string? ReportMode { get; }

    private static string Safe(string value, int maxLength)
    {
        string safe = DiagnosticSecretRedactor.Redact(value);
        return safe.Length <= maxLength ? safe : safe[..maxLength];
    }

    private static string? SafeOrNull(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Safe(value, maxLength);
}

public sealed record TaskQueueAttempt
{
    public TaskQueueAttempt(
        int Attempt,
        string Status,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset? CompletedAtUtc = null,
        string? JobId = null,
        int? ExitCode = null,
        string? ErrorCode = null,
        string? Summary = null)
    {
        if (Attempt <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Attempt));
        }

        if (Status is not (TaskQueueStatus.Running or TaskQueueStatus.Succeeded or TaskQueueStatus.Failed))
        {
            throw new ArgumentException("Queue attempt status is invalid.", nameof(Status));
        }

        if (!string.IsNullOrWhiteSpace(JobId) && !JobIdGenerator.IsValid(JobId))
        {
            throw new ArgumentException("Queue attempt job id is invalid.", nameof(JobId));
        }

        this.Attempt = Attempt;
        this.Status = Status;
        this.StartedAtUtc = StartedAtUtc;
        this.CompletedAtUtc = CompletedAtUtc;
        this.JobId = JobId;
        this.ExitCode = ExitCode;
        this.ErrorCode = SafeOrNull(ErrorCode);
        this.Summary = SafeOrNull(Summary);
    }

    public int Attempt { get; }

    public string Status { get; }

    public DateTimeOffset StartedAtUtc { get; }

    public DateTimeOffset? CompletedAtUtc { get; }

    public string? JobId { get; }

    public int? ExitCode { get; }

    public string? ErrorCode { get; }

    public string? Summary { get; }

    public TaskQueueAttempt Complete(
        string status,
        DateTimeOffset completedAtUtc,
        string? jobId,
        int exitCode,
        string? errorCode,
        string? summary)
    {
        if (Status != TaskQueueStatus.Running ||
            status is not (TaskQueueStatus.Succeeded or TaskQueueStatus.Failed))
        {
            throw new InvalidOperationException("Only a running queue attempt can be completed.");
        }

        return new TaskQueueAttempt(
            Attempt,
            status,
            StartedAtUtc,
            completedAtUtc,
            jobId,
            exitCode,
            errorCode,
            summary);
    }

    private static string? SafeOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string safe = DiagnosticSecretRedactor.Redact(value);
        return safe.Length <= 4_096 ? safe : safe[..4_096];
    }
}

public sealed record TaskQueueRedactionSummary(
    bool SecretsRedacted,
    bool RawReferencesStored,
    bool RawToolArgumentsStored,
    bool ApprovalOverridesStored,
    string Policy);

public sealed record TaskQueueItem
{
    public const int CurrentSchemaVersion = 1;

    public TaskQueueItem(
        int SchemaVersion,
        string QueueId,
        string Status,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc,
        TaskQueueRequest Request,
        IReadOnlyList<TaskQueueAttempt>? Attempts = null,
        string? LatestJobId = null,
        DateTimeOffset? CompletedAtUtc = null,
        string? ErrorCode = null,
        string? Summary = null,
        IReadOnlyList<string>? Warnings = null,
        TaskQueueRedactionSummary? Redaction = null)
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(SchemaVersion));
        }

        if (!TaskQueueIdGenerator.IsValid(QueueId))
        {
            throw new ArgumentException("Queue id format is invalid.", nameof(QueueId));
        }

        if (!TaskQueueStatus.IsKnown(Status))
        {
            throw new ArgumentException("Queue status is invalid.", nameof(Status));
        }

        ArgumentNullException.ThrowIfNull(Request);
        if (!string.IsNullOrWhiteSpace(LatestJobId) && !JobIdGenerator.IsValid(LatestJobId))
        {
            throw new ArgumentException("Latest job id is invalid.", nameof(LatestJobId));
        }

        this.SchemaVersion = SchemaVersion;
        this.QueueId = QueueId;
        this.Status = Status;
        this.CreatedAtUtc = CreatedAtUtc;
        this.UpdatedAtUtc = UpdatedAtUtc;
        this.Request = Request;
        this.Attempts = new ReadOnlyCollection<TaskQueueAttempt>((Attempts ?? []).ToArray());
        this.LatestJobId = LatestJobId;
        this.CompletedAtUtc = CompletedAtUtc;
        this.ErrorCode = SafeOrNull(ErrorCode);
        this.Summary = SafeOrNull(Summary);
        this.Warnings = new ReadOnlyCollection<string>((Warnings ?? []).Take(64).Select(Safe).ToArray());
        this.Redaction = Redaction ?? DefaultRedaction;
    }

    public int SchemaVersion { get; }

    public string QueueId { get; }

    public string Status { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; }

    public TaskQueueRequest Request { get; }

    public IReadOnlyList<TaskQueueAttempt> Attempts { get; }

    public string? LatestJobId { get; }

    public DateTimeOffset? CompletedAtUtc { get; }

    public string? ErrorCode { get; }

    public string? Summary { get; }

    public IReadOnlyList<string> Warnings { get; }

    public TaskQueueRedactionSummary Redaction { get; }

    public static TaskQueueRedactionSummary DefaultRedaction { get; } = new(
        SecretsRedacted: true,
        RawReferencesStored: false,
        RawToolArgumentsStored: false,
        ApprovalOverridesStored: false,
        Policy: "queue records store bounded redacted requests and never persist approval overrides");

    public static TaskQueueItem CreatePending(
        string queueId,
        DateTimeOffset nowUtc,
        TaskQueueRequest request,
        IReadOnlyList<string>? warnings = null) =>
        new(
            CurrentSchemaVersion,
            queueId,
            TaskQueueStatus.Pending,
            nowUtc,
            nowUtc,
            request,
            Warnings: warnings);

    public TaskQueueItem StartAttempt(DateTimeOffset nowUtc)
    {
        if (!TaskQueueStatus.CanRun(Status))
        {
            throw new InvalidOperationException("Queue item is not in a runnable state.");
        }

        List<TaskQueueAttempt> attempts = Attempts.ToList();
        attempts.Add(new TaskQueueAttempt(attempts.Count + 1, TaskQueueStatus.Running, nowUtc));
        return Copy(
            TaskQueueStatus.Running,
            nowUtc,
            attempts,
            LatestJobId,
            completedAtUtc: null,
            errorCode: null,
            summary: "Queue item execution started.");
    }

    public TaskQueueItem CompleteAttempt(
        DateTimeOffset nowUtc,
        int exitCode,
        string? jobId,
        string? errorCode,
        string? summary)
    {
        if (Status != TaskQueueStatus.Running || Attempts.Count == 0)
        {
            throw new InvalidOperationException("Queue item is not running.");
        }

        string status = exitCode == 0 ? TaskQueueStatus.Succeeded : TaskQueueStatus.Failed;
        List<TaskQueueAttempt> attempts = Attempts.ToList();
        attempts[^1] = attempts[^1].Complete(status, nowUtc, jobId, exitCode, errorCode, summary);
        return Copy(status, nowUtc, attempts, jobId, nowUtc, errorCode, summary);
    }

    public TaskQueueItem Cancel(DateTimeOffset nowUtc)
    {
        if (Status != TaskQueueStatus.Pending)
        {
            throw new InvalidOperationException("Only a pending queue item can be canceled.");
        }

        return Copy(
            TaskQueueStatus.Canceled,
            nowUtc,
            Attempts,
            LatestJobId,
            nowUtc,
            errorCode: null,
            summary: "Queue item canceled before execution.");
    }

    private TaskQueueItem Copy(
        string status,
        DateTimeOffset updatedAtUtc,
        IReadOnlyList<TaskQueueAttempt> attempts,
        string? latestJobId,
        DateTimeOffset? completedAtUtc,
        string? errorCode,
        string? summary) =>
        new(
            SchemaVersion,
            QueueId,
            status,
            CreatedAtUtc,
            updatedAtUtc,
            Request,
            attempts,
            latestJobId,
            completedAtUtc,
            errorCode,
            summary,
            Warnings,
            Redaction);

    private static string Safe(string value)
    {
        string safe = DiagnosticSecretRedactor.Redact(value ?? string.Empty);
        return safe.Length <= 4_096 ? safe : safe[..4_096];
    }

    private static string? SafeOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Safe(value);
}

public static class TaskQueueIdGenerator
{
    private static readonly Regex QueueIdPattern = new(
        @"^queue_[0-9]{8}T[0-9]{9}Z_[a-f0-9]{8}$",
        RegexOptions.CultureInvariant);

    public static string Create(DateTimeOffset timestampUtc)
    {
        Span<byte> bytes = stackalloc byte[4];
        RandomNumberGenerator.Fill(bytes);
        return Create(timestampUtc, Convert.ToHexString(bytes).ToLowerInvariant());
    }

    internal static string Create(DateTimeOffset timestampUtc, string suffix)
    {
        string normalizedSuffix = Regex.Replace(suffix ?? string.Empty, "[^a-fA-F0-9]", string.Empty)
            .ToLowerInvariant()
            .PadRight(8, '0')[..8];
        return "queue_" + timestampUtc.UtcDateTime.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture) +
            "_" + normalizedSuffix;
    }

    public static bool IsValid(string? queueId) =>
        !string.IsNullOrWhiteSpace(queueId) && QueueIdPattern.IsMatch(queueId);
}

public static class TaskQueueItemJsonSchema
{
    public static string Render()
    {
        Dictionary<string, object?> schema = new(StringComparer.Ordinal)
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = "https://c-aicli.local/schemas/task-queue-item.v1.json",
            ["title"] = "C-AICLI TaskQueueItem v1",
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new[]
            {
                "schemaVersion", "queueId", "status", "createdAtUtc", "updatedAtUtc",
                "request", "attempts", "warnings", "redaction"
            },
            ["properties"] = new Dictionary<string, object?>
            {
                ["schemaVersion"] = new Dictionary<string, object?> { ["const"] = TaskQueueItem.CurrentSchemaVersion },
                ["queueId"] = new Dictionary<string, object?> { ["type"] = "string", ["pattern"] = "^queue_[0-9]{8}T[0-9]{9}Z_[a-f0-9]{8}$" },
                ["status"] = new Dictionary<string, object?> { ["enum"] = new[] { TaskQueueStatus.Pending, TaskQueueStatus.Running, TaskQueueStatus.Succeeded, TaskQueueStatus.Failed, TaskQueueStatus.Canceled } },
                ["createdAtUtc"] = DateTimeString(),
                ["updatedAtUtc"] = DateTimeString(),
                ["completedAtUtc"] = NullableDateTimeString(),
                ["latestJobId"] = NullableString(),
                ["errorCode"] = NullableString(),
                ["summary"] = NullableString(),
                ["request"] = RequestSchema(),
                ["attempts"] = new Dictionary<string, object?>
                {
                    ["type"] = "array",
                    ["items"] = AttemptSchema(),
                    ["maxItems"] = 1_024
                },
                ["warnings"] = StringArray(64),
                ["redaction"] = RedactionSchema()
            }
        };

        return JsonSerializer.Serialize(
            schema,
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
    }

    private static Dictionary<string, object?> RequestSchema() => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new[] { "family", "task", "workspaceRoot" },
        ["properties"] = new Dictionary<string, object?>
        {
            ["family"] = new Dictionary<string, object?> { ["enum"] = new[] { TaskQueueCommandFamily.Exec, TaskQueueCommandFamily.Skill } },
            ["task"] = new Dictionary<string, object?> { ["type"] = "string", ["maxLength"] = 16_384 },
            ["workspaceRoot"] = new Dictionary<string, object?> { ["type"] = "string", ["maxLength"] = 4_096 },
            ["cwd"] = NullableString(),
            ["skill"] = NullableString(),
            ["expert"] = NullableString(),
            ["reportMode"] = NullableString()
        }
    };

    private static Dictionary<string, object?> AttemptSchema() => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new[] { "attempt", "status", "startedAtUtc" },
        ["properties"] = new Dictionary<string, object?>
        {
            ["attempt"] = new Dictionary<string, object?> { ["type"] = "integer", ["minimum"] = 1 },
            ["status"] = new Dictionary<string, object?> { ["enum"] = new[] { TaskQueueStatus.Running, TaskQueueStatus.Succeeded, TaskQueueStatus.Failed } },
            ["startedAtUtc"] = DateTimeString(),
            ["completedAtUtc"] = NullableDateTimeString(),
            ["jobId"] = NullableString(),
            ["exitCode"] = new Dictionary<string, object?> { ["type"] = new[] { "integer", "null" } },
            ["errorCode"] = NullableString(),
            ["summary"] = NullableString()
        }
    };

    private static Dictionary<string, object?> RedactionSchema() => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new[] { "secretsRedacted", "rawReferencesStored", "rawToolArgumentsStored", "approvalOverridesStored", "policy" },
        ["properties"] = new Dictionary<string, object?>
        {
            ["secretsRedacted"] = new Dictionary<string, object?> { ["type"] = "boolean" },
            ["rawReferencesStored"] = new Dictionary<string, object?> { ["type"] = "boolean" },
            ["rawToolArgumentsStored"] = new Dictionary<string, object?> { ["type"] = "boolean" },
            ["approvalOverridesStored"] = new Dictionary<string, object?> { ["type"] = "boolean" },
            ["policy"] = new Dictionary<string, object?> { ["type"] = "string" }
        }
    };

    private static Dictionary<string, object?> DateTimeString() => new()
    {
        ["type"] = "string",
        ["format"] = "date-time"
    };

    private static Dictionary<string, object?> NullableDateTimeString() => new()
    {
        ["type"] = new[] { "string", "null" },
        ["format"] = "date-time"
    };

    private static Dictionary<string, object?> NullableString() => new()
    {
        ["type"] = new[] { "string", "null" }
    };

    private static Dictionary<string, object?> StringArray(int maxItems) => new()
    {
        ["type"] = "array",
        ["items"] = new Dictionary<string, object?> { ["type"] = "string" },
        ["maxItems"] = maxItems
    };
}
