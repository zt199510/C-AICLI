using System.Collections.ObjectModel;
using System.Text.Json;

namespace CSharpAiCli.Core;

public static class CiArtifactType
{
    public const string Summary = "caicli.ci.summary";
}

public static class CiCheckOutcome
{
    public const string Success = "success";
    public const string Warning = "warning";
    public const string Failure = "failure";
    public const string ConfigError = "config-error";

    public static bool IsKnown(string? value) =>
        string.Equals(value, Success, StringComparison.Ordinal) ||
        string.Equals(value, Warning, StringComparison.Ordinal) ||
        string.Equals(value, Failure, StringComparison.Ordinal) ||
        string.Equals(value, ConfigError, StringComparison.Ordinal);
}

public static class CiAnnotationLevel
{
    public const string Notice = "notice";
    public const string Warning = "warning";
    public const string Failure = "failure";

    public static bool IsKnown(string? value) =>
        string.Equals(value, Notice, StringComparison.Ordinal) ||
        string.Equals(value, Warning, StringComparison.Ordinal) ||
        string.Equals(value, Failure, StringComparison.Ordinal);
}

public sealed record CiJobReference(
    string JobId,
    string Status,
    string CommandFamily,
    string? JobName,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record CiCorrelation(
    string? QueueId,
    string? PipelineRunId,
    string? Automation,
    string? AutomationRunId,
    string? AutomationTarget);

public sealed record CiSummary(
    string Title,
    string Message,
    int ChangedFileCount,
    int VerificationCount,
    int WarningCount,
    int RiskCount,
    int AnnotationCount,
    int ArtifactCount);

public sealed record CiCheckResult(
    string Name,
    string Outcome,
    int RecommendedExitCode,
    string Policy,
    string? ErrorCode);

public sealed record CiAnnotation
{
    public CiAnnotation(string Level, string Code, string Message, string? Path = null)
    {
        if (!CiAnnotationLevel.IsKnown(Level))
        {
            throw new ArgumentException("CI annotation level is invalid.", nameof(Level));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(Code);
        ArgumentException.ThrowIfNullOrWhiteSpace(Message);
        this.Level = Level;
        this.Code = Safe(Code, 128);
        this.Message = Safe(Message, 2_048);
        this.Path = SafeOrNull(Path, 1_024);
    }

    public string Level { get; }

    public string Code { get; }

    public string Message { get; }

    public string? Path { get; }

    private static string Safe(string value, int maxLength)
    {
        string safe = DiagnosticSecretRedactor.Redact(value);
        return safe.Length <= maxLength ? safe : safe[..maxLength];
    }

    private static string? SafeOrNull(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Safe(value, maxLength);
}

public sealed record CiArtifactPointer
{
    public CiArtifactPointer(
        string Kind,
        string Path,
        bool Exists,
        string? Summary,
        string? Sha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(Path);
        this.Kind = Safe(Kind, 128);
        this.Path = Safe(Path, 4_096);
        this.Exists = Exists;
        this.Summary = SafeOrNull(Summary, 2_048);
        this.Sha256 = SafeOrNull(Sha256, 128);
    }

    public string Kind { get; }

    public string Path { get; }

    public bool Exists { get; }

    public string? Summary { get; }

    public string? Sha256 { get; }

    private static string Safe(string value, int maxLength)
    {
        string safe = DiagnosticSecretRedactor.Redact(value);
        return safe.Length <= maxLength ? safe : safe[..maxLength];
    }

    private static string? SafeOrNull(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Safe(value, maxLength);
}

public sealed record CiArtifactRedaction(
    bool SecretsRedacted,
    bool RawReferencesStored,
    bool RawToolArgumentsStored,
    bool FullDiffStored,
    string Policy);

public sealed record CiArtifact
{
    public const int CurrentSchemaVersion = 1;

    public CiArtifact(
        DateTimeOffset GeneratedAtUtc,
        CiJobReference Job,
        CiCorrelation Correlation,
        CiSummary Summary,
        CiCheckResult Check,
        IReadOnlyList<CiAnnotation>? Annotations,
        IReadOnlyList<CiArtifactPointer>? Artifacts,
        CiArtifactRedaction Redaction)
    {
        ArgumentNullException.ThrowIfNull(Job);
        ArgumentNullException.ThrowIfNull(Correlation);
        ArgumentNullException.ThrowIfNull(Summary);
        ArgumentNullException.ThrowIfNull(Check);
        ArgumentNullException.ThrowIfNull(Redaction);
        if (!CiCheckOutcome.IsKnown(Check.Outcome))
        {
            throw new ArgumentException("CI check outcome is invalid.", nameof(Check));
        }

        SchemaVersion = CurrentSchemaVersion;
        Type = CiArtifactType.Summary;
        this.GeneratedAtUtc = GeneratedAtUtc;
        this.Job = Job;
        this.Correlation = Correlation;
        this.Summary = Summary;
        this.Check = Check;
        this.Annotations = new ReadOnlyCollection<CiAnnotation>((Annotations ?? []).Take(256).ToArray());
        this.Artifacts = new ReadOnlyCollection<CiArtifactPointer>((Artifacts ?? []).Take(256).ToArray());
        this.Redaction = Redaction;
    }

    public int SchemaVersion { get; }

    public string Type { get; }

    public DateTimeOffset GeneratedAtUtc { get; }

    public CiJobReference Job { get; }

    public CiCorrelation Correlation { get; }

    public CiSummary Summary { get; }

    public CiCheckResult Check { get; }

    public IReadOnlyList<CiAnnotation> Annotations { get; }

    public IReadOnlyList<CiArtifactPointer> Artifacts { get; }

    public CiArtifactRedaction Redaction { get; }
}

public static class CiArtifactJsonSchema
{
    public static string Render()
    {
        Dictionary<string, object?> schema = ObjectSchema(
            required:
            [
                "schemaVersion", "type", "generatedAtUtc", "job", "correlation", "summary",
                "check", "annotations", "artifacts", "redaction"
            ],
            properties: new Dictionary<string, object?>
            {
                ["schemaVersion"] = new Dictionary<string, object?> { ["const"] = CiArtifact.CurrentSchemaVersion },
                ["type"] = new Dictionary<string, object?> { ["const"] = CiArtifactType.Summary },
                ["generatedAtUtc"] = DateTimeSchema(),
                ["job"] = JobSchema(),
                ["correlation"] = CorrelationSchema(),
                ["summary"] = SummarySchema(),
                ["check"] = CheckSchema(),
                ["annotations"] = ArraySchema(AnnotationSchema(), 256),
                ["artifacts"] = ArraySchema(ArtifactSchema(), 256),
                ["redaction"] = RedactionSchema()
            });
        schema["$schema"] = "https://json-schema.org/draft/2020-12/schema";
        schema["$id"] = "https://c-aicli.local/schemas/ci-artifact.v1.json";
        schema["title"] = "C-AICLI CI Artifact v1";

        return JsonSerializer.Serialize(
            schema,
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
    }

    private static Dictionary<string, object?> JobSchema() => ObjectSchema(
        ["jobId", "status", "commandFamily", "jobName", "createdAtUtc", "updatedAtUtc", "completedAtUtc"],
        new Dictionary<string, object?>
        {
            ["jobId"] = StringSchema(),
            ["status"] = StringSchema(),
            ["commandFamily"] = StringSchema(),
            ["jobName"] = NullableStringSchema(),
            ["createdAtUtc"] = DateTimeSchema(),
            ["updatedAtUtc"] = DateTimeSchema(),
            ["completedAtUtc"] = NullableDateTimeSchema()
        });

    private static Dictionary<string, object?> CorrelationSchema() => ObjectSchema(
        ["queueId", "pipelineRunId", "automation", "automationRunId", "automationTarget"],
        new Dictionary<string, object?>
        {
            ["queueId"] = NullableStringSchema(),
            ["pipelineRunId"] = NullableStringSchema(),
            ["automation"] = NullableStringSchema(),
            ["automationRunId"] = NullableStringSchema(),
            ["automationTarget"] = NullableStringSchema()
        });

    private static Dictionary<string, object?> SummarySchema() => ObjectSchema(
        ["title", "message", "changedFileCount", "verificationCount", "warningCount", "riskCount", "annotationCount", "artifactCount"],
        new Dictionary<string, object?>
        {
            ["title"] = StringSchema(),
            ["message"] = StringSchema(),
            ["changedFileCount"] = NonNegativeIntegerSchema(),
            ["verificationCount"] = NonNegativeIntegerSchema(),
            ["warningCount"] = NonNegativeIntegerSchema(),
            ["riskCount"] = NonNegativeIntegerSchema(),
            ["annotationCount"] = NonNegativeIntegerSchema(),
            ["artifactCount"] = NonNegativeIntegerSchema()
        });

    private static Dictionary<string, object?> CheckSchema() => ObjectSchema(
        ["name", "outcome", "recommendedExitCode", "policy", "errorCode"],
        new Dictionary<string, object?>
        {
            ["name"] = StringSchema(),
            ["outcome"] = new Dictionary<string, object?>
            {
                ["enum"] = new[] { CiCheckOutcome.Success, CiCheckOutcome.Warning, CiCheckOutcome.Failure, CiCheckOutcome.ConfigError }
            },
            ["recommendedExitCode"] = NonNegativeIntegerSchema(),
            ["policy"] = StringSchema(),
            ["errorCode"] = NullableStringSchema()
        });

    private static Dictionary<string, object?> AnnotationSchema() => ObjectSchema(
        ["level", "code", "message", "path"],
        new Dictionary<string, object?>
        {
            ["level"] = new Dictionary<string, object?>
            {
                ["enum"] = new[] { CiAnnotationLevel.Notice, CiAnnotationLevel.Warning, CiAnnotationLevel.Failure }
            },
            ["code"] = StringSchema(),
            ["message"] = StringSchema(),
            ["path"] = NullableStringSchema()
        });

    private static Dictionary<string, object?> ArtifactSchema() => ObjectSchema(
        ["kind", "path", "exists", "summary", "sha256"],
        new Dictionary<string, object?>
        {
            ["kind"] = StringSchema(),
            ["path"] = StringSchema(),
            ["exists"] = new Dictionary<string, object?> { ["type"] = "boolean" },
            ["summary"] = NullableStringSchema(),
            ["sha256"] = NullableStringSchema()
        });

    private static Dictionary<string, object?> RedactionSchema() => ObjectSchema(
        ["secretsRedacted", "rawReferencesStored", "rawToolArgumentsStored", "fullDiffStored", "policy"],
        new Dictionary<string, object?>
        {
            ["secretsRedacted"] = new Dictionary<string, object?> { ["const"] = true },
            ["rawReferencesStored"] = new Dictionary<string, object?> { ["const"] = false },
            ["rawToolArgumentsStored"] = new Dictionary<string, object?> { ["const"] = false },
            ["fullDiffStored"] = new Dictionary<string, object?> { ["const"] = false },
            ["policy"] = StringSchema()
        });

    private static Dictionary<string, object?> ObjectSchema(
        IReadOnlyList<string> required,
        Dictionary<string, object?> properties) => new()
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = required,
            ["properties"] = properties
        };

    private static Dictionary<string, object?> StringSchema() => new() { ["type"] = "string" };

    private static Dictionary<string, object?> NullableStringSchema() => new()
    {
        ["type"] = new[] { "string", "null" }
    };

    private static Dictionary<string, object?> DateTimeSchema() => new()
    {
        ["type"] = "string",
        ["format"] = "date-time"
    };

    private static Dictionary<string, object?> NullableDateTimeSchema() => new()
    {
        ["type"] = new[] { "string", "null" },
        ["format"] = "date-time"
    };

    private static Dictionary<string, object?> NonNegativeIntegerSchema() => new()
    {
        ["type"] = "integer",
        ["minimum"] = 0
    };

    private static Dictionary<string, object?> ArraySchema(Dictionary<string, object?> items, int maxItems) => new()
    {
        ["type"] = "array",
        ["maxItems"] = maxItems,
        ["items"] = items
    };
}
