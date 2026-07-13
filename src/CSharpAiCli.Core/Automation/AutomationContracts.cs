using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CSharpAiCli.Core;

public static class AutomationTriggerType
{
    public const string Manual = "manual";
    public const string Schedule = "schedule";

    public static bool IsKnown(string? value) => value is Manual or Schedule;
}

public static class AutomationTargetType
{
    public const string Queue = "queue";
    public const string Skill = "skill";
    public const string Pipeline = "pipeline";

    public static bool IsKnown(string? value) => value is Queue or Skill or Pipeline;
}

public static class AutomationRunMode
{
    public const string DryRun = "dry-run";
    public const string Manual = "manual";
}

public static class AutomationErrorCode
{
    public const string NotFound = "automation-not-found";
    public const string ManifestInvalid = "automation-manifest-invalid";
    public const string SourceDenied = "automation-local-source-denied";
    public const string UnsafeTarget = "automation-unsafe-target";
    public const string TargetUnavailable = "automation-target-unavailable";
    public const string InvalidRunMode = "automation-run-mode-invalid";
    public const string ExecutionFailed = "automation-execution-failed";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AutomationTrigger
{
    public string? Type { get; init; }

    public string? Schedule { get; init; }

    public string? TimeZone { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AutomationTarget
{
    public string? Type { get; init; }

    public string? Family { get; init; }

    public string? Name { get; init; }

    public string? Task { get; init; }

    public string? Cwd { get; init; }

    public string? Expert { get; init; }

    public string? Report { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AutomationSafety
{
    public bool ManualOnly { get; init; }

    public bool AllowWrites { get; init; }

    public bool AllowShell { get; init; }

    public bool AllowMcp { get; init; }

    public string ToSummary() => string.Join(
        " ",
        [
            "manualOnly=" + ManualOnly.ToString().ToLowerInvariant(),
            "allowWrites=" + AllowWrites.ToString().ToLowerInvariant(),
            "allowShell=" + AllowShell.ToString().ToLowerInvariant(),
            "allowMcp=" + AllowMcp.ToString().ToLowerInvariant()
        ]);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AutomationManifest
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; }

    public string? Name { get; init; }

    public string? Description { get; init; }

    public AutomationTrigger? Trigger { get; init; }

    public AutomationTarget? Target { get; init; }

    public AutomationSafety? Safety { get; init; }
}

public sealed record AutomationSource
{
    public AutomationSource(string Path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Path);
        this.Path = DiagnosticSecretRedactor.Redact(Path);
    }

    public string Kind => "workspace-local";

    public string Path { get; }
}

public sealed record AutomationCatalogItem(AutomationManifest Manifest, AutomationSource Source);

public sealed record AutomationDiagnostic
{
    public AutomationDiagnostic(string ErrorCode, string Summary, string? SourcePath = null, string? AutomationName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ErrorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(Summary);
        this.ErrorCode = DiagnosticSecretRedactor.Redact(ErrorCode);
        this.Summary = Safe(Summary, 4_096);
        this.SourcePath = SafeOrNull(SourcePath, 4_096);
        this.AutomationName = SafeOrNull(AutomationName, 256);
    }

    public string ErrorCode { get; }

    public string Summary { get; }

    public string? SourcePath { get; }

    public string? AutomationName { get; }

    private static string Safe(string value, int maxLength)
    {
        string safe = DiagnosticSecretRedactor.Redact(value);
        return safe.Length <= maxLength ? safe : safe[..maxLength];
    }

    private static string? SafeOrNull(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Safe(value, maxLength);
}

public sealed record AutomationValidationResult
{
    public AutomationValidationResult(IReadOnlyList<AutomationDiagnostic>? Diagnostics = null)
    {
        this.Diagnostics = new ReadOnlyCollection<AutomationDiagnostic>((Diagnostics ?? []).ToArray());
    }

    public IReadOnlyList<AutomationDiagnostic> Diagnostics { get; }

    public bool Succeeded => Diagnostics.Count == 0;
}

public sealed record AutomationSchedulePreview(
    bool Enabled,
    string? Schedule,
    string? TimeZone,
    string Summary);

public sealed record AutomationPlan
{
    public AutomationPlan(
        AutomationCatalogItem Item,
        string WorkspaceRoot,
        AutomationSchedulePreview SchedulePreview,
        string Mode = AutomationRunMode.DryRun)
    {
        ArgumentNullException.ThrowIfNull(Item);
        ArgumentException.ThrowIfNullOrWhiteSpace(WorkspaceRoot);
        ArgumentNullException.ThrowIfNull(SchedulePreview);
        this.SchemaVersion = AutomationManifest.CurrentSchemaVersion;
        this.Item = Item;
        this.WorkspaceRoot = Safe(WorkspaceRoot, 4_096);
        this.SchedulePreview = SchedulePreview;
        this.Mode = Safe(Mode, 64);
    }

    public int SchemaVersion { get; }

    public AutomationCatalogItem Item { get; }

    public string WorkspaceRoot { get; }

    public AutomationSchedulePreview SchedulePreview { get; }

    public string Mode { get; }

    private static string Safe(string value, int maxLength)
    {
        string safe = DiagnosticSecretRedactor.Redact(value);
        return safe.Length <= maxLength ? safe : safe[..maxLength];
    }
}

public sealed record AutomationRunMetadata
{
    public AutomationRunMetadata(string Automation, string RunId, string Mode, string SourcePath, string TargetType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Automation);
        ArgumentException.ThrowIfNullOrWhiteSpace(RunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(Mode);
        ArgumentException.ThrowIfNullOrWhiteSpace(SourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(TargetType);
        this.Automation = Safe(Automation);
        this.RunId = Safe(RunId);
        this.Mode = Safe(Mode);
        this.SourcePath = Safe(SourcePath);
        this.TargetType = Safe(TargetType);
    }

    public string Automation { get; }

    public string RunId { get; }

    public string Mode { get; }

    public string SourcePath { get; }

    public string TargetType { get; }

    private static string Safe(string value)
    {
        string safe = DiagnosticSecretRedactor.Redact(value);
        return safe.Length <= 4_096 ? safe : safe[..4_096];
    }
}

public sealed record AutomationRunResult
{
    public AutomationRunResult(
        string RunId,
        string Automation,
        string TargetType,
        string Status,
        int ExitCode,
        IReadOnlyList<string>? QueueIds = null,
        IReadOnlyList<string>? JobIds = null,
        string? PipelineRunId = null,
        string? Summary = null,
        IReadOnlyList<string>? Warnings = null)
    {
        if (!AutomationRunIdGenerator.IsValid(RunId))
        {
            throw new ArgumentException("Automation run id is invalid.", nameof(RunId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(Automation);
        ArgumentException.ThrowIfNullOrWhiteSpace(TargetType);
        ArgumentException.ThrowIfNullOrWhiteSpace(Status);
        this.SchemaVersion = AutomationManifest.CurrentSchemaVersion;
        this.RunId = RunId;
        this.Automation = Safe(Automation);
        this.Mode = AutomationRunMode.Manual;
        this.TargetType = Safe(TargetType);
        this.Status = Safe(Status);
        this.ExitCode = ExitCode;
        this.QueueIds = new ReadOnlyCollection<string>((QueueIds ?? []).Select(Safe).ToArray());
        this.JobIds = new ReadOnlyCollection<string>((JobIds ?? []).Select(Safe).ToArray());
        this.PipelineRunId = SafeOrNull(PipelineRunId);
        this.Summary = SafeOrNull(Summary);
        this.Warnings = new ReadOnlyCollection<string>((Warnings ?? []).Select(Safe).ToArray());
        this.Redaction = new PipelineRedactionSummary(
            SecretsRedacted: true,
            RawReferencesStored: false,
            RawToolArgumentsStored: false,
            FullDiffStored: false,
            Policy: "automation results contain bounded redacted correlation metadata and artifact pointers only");
    }

    public int SchemaVersion { get; }

    public string RunId { get; }

    public string Automation { get; }

    public string Mode { get; }

    public string TargetType { get; }

    public string Status { get; }

    public int ExitCode { get; }

    public IReadOnlyList<string> QueueIds { get; }

    public IReadOnlyList<string> JobIds { get; }

    public string? PipelineRunId { get; }

    public string? Summary { get; }

    public IReadOnlyList<string> Warnings { get; }

    public PipelineRedactionSummary Redaction { get; }

    private static string Safe(string value)
    {
        string safe = DiagnosticSecretRedactor.Redact(value);
        return safe.Length <= 4_096 ? safe : safe[..4_096];
    }

    private static string? SafeOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : Safe(value);
}

public static class AutomationRunIdGenerator
{
    private static readonly Regex Pattern = new(
        "^automation_[0-9]{8}T[0-9]{9}Z_[a-f0-9]{8}$",
        RegexOptions.CultureInvariant);

    public static string Create(DateTimeOffset timestampUtc)
    {
        Span<byte> bytes = stackalloc byte[4];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return $"automation_{timestampUtc.UtcDateTime:yyyyMMddTHHmmssfffZ}_{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    public static bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value) && Pattern.IsMatch(value);
}

public static class AutomationManifestJsonSchema
{
    public static string Render()
    {
        Dictionary<string, object?> schema = new(StringComparer.Ordinal)
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = "https://c-aicli.local/schemas/automation-manifest.v1.json",
            ["title"] = "C-AICLI AutomationManifest v1",
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new[] { "schemaVersion", "name", "description", "trigger", "target", "safety" },
            ["properties"] = new Dictionary<string, object?>
            {
                ["schemaVersion"] = new Dictionary<string, object?> { ["const"] = AutomationManifest.CurrentSchemaVersion },
                ["name"] = StringSchema(1, 64, "^[a-z][a-z0-9-]{0,63}$"),
                ["description"] = StringSchema(1, 2_048),
                ["trigger"] = TriggerSchema(),
                ["target"] = TargetSchema(),
                ["safety"] = SafetySchema()
            }
        };

        return JsonSerializer.Serialize(schema, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
    }

    private static Dictionary<string, object?> TriggerSchema() => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new[] { "type" },
        ["properties"] = new Dictionary<string, object?>
        {
            ["type"] = new Dictionary<string, object?> { ["enum"] = new[] { AutomationTriggerType.Manual, AutomationTriggerType.Schedule } },
            ["schedule"] = NullableStringSchema(256),
            ["timeZone"] = NullableStringSchema(256)
        }
    };

    private static Dictionary<string, object?> TargetSchema() => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new[] { "type", "task" },
        ["properties"] = new Dictionary<string, object?>
        {
            ["type"] = new Dictionary<string, object?> { ["enum"] = new[] { AutomationTargetType.Queue, AutomationTargetType.Skill, AutomationTargetType.Pipeline } },
            ["family"] = NullableStringSchema(64),
            ["name"] = NullableStringSchema(256),
            ["task"] = StringSchema(1, 16_384),
            ["cwd"] = NullableStringSchema(4_096),
            ["expert"] = NullableStringSchema(256),
            ["report"] = NullableStringSchema(64)
        }
    };

    private static Dictionary<string, object?> SafetySchema() => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new[] { "manualOnly", "allowWrites", "allowShell", "allowMcp" },
        ["properties"] = new Dictionary<string, object?>
        {
            ["manualOnly"] = new Dictionary<string, object?> { ["const"] = true },
            ["allowWrites"] = new Dictionary<string, object?> { ["type"] = "boolean" },
            ["allowShell"] = new Dictionary<string, object?> { ["type"] = "boolean" },
            ["allowMcp"] = new Dictionary<string, object?> { ["type"] = "boolean" }
        }
    };

    private static Dictionary<string, object?> StringSchema(int minLength, int maxLength, string? pattern = null)
    {
        Dictionary<string, object?> value = new()
        {
            ["type"] = "string",
            ["minLength"] = minLength,
            ["maxLength"] = maxLength
        };
        if (!string.IsNullOrWhiteSpace(pattern))
        {
            value["pattern"] = pattern;
        }

        return value;
    }

    private static Dictionary<string, object?> NullableStringSchema(int maxLength) => new()
    {
        ["type"] = new[] { "string", "null" },
        ["maxLength"] = maxLength
    };
}
