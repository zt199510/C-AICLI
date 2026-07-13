using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CSharpAiCli.Core;

public static class PipelineCommandFamily
{
    public const string Exec = "exec";
    public const string Skill = "skill";

    public static bool IsKnown(string? value) =>
        string.Equals(value, Exec, StringComparison.Ordinal) ||
        string.Equals(value, Skill, StringComparison.Ordinal);
}

public static class PipelineStatus
{
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
}

public static class PipelineStopReason
{
    public const string Completed = "completed";
    public const string RoleFailed = "role-failed";
}

public sealed record PipelineRoleBoundary
{
    public PipelineRoleBoundary(
        bool IsReadOnly,
        bool AllowWrites,
        bool AllowShell,
        bool AllowMcp,
        bool AllowMcpDiscovery,
        IReadOnlyList<string>? DisabledTools,
        string Summary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Summary);
        if (IsReadOnly && (AllowWrites || AllowShell || AllowMcp || AllowMcpDiscovery))
        {
            throw new ArgumentException("A read-only pipeline role cannot allow write-capable tools.");
        }

        this.IsReadOnly = IsReadOnly;
        this.AllowWrites = AllowWrites;
        this.AllowShell = AllowShell;
        this.AllowMcp = AllowMcp;
        this.AllowMcpDiscovery = AllowMcpDiscovery;
        this.DisabledTools = new ReadOnlyCollection<string>(
            (DisabledTools ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
        this.Summary = Safe(Summary);
    }

    public bool IsReadOnly { get; }

    public bool AllowWrites { get; }

    public bool AllowShell { get; }

    public bool AllowMcp { get; }

    public bool AllowMcpDiscovery { get; }

    public IReadOnlyList<string> DisabledTools { get; }

    public string Summary { get; }

    public static PipelineRoleBoundary FromExpert(ExpertProfile expert)
    {
        ArgumentNullException.ThrowIfNull(expert);
        if (expert.IsReadOnly)
        {
            return new PipelineRoleBoundary(
                IsReadOnly: true,
                AllowWrites: false,
                AllowShell: false,
                AllowMcp: false,
                AllowMcpDiscovery: false,
                DisabledTools: ["workspace.apply_patch", "workspace.run_shell", "mcp.*"],
                Summary: expert.ToolBoundary);
        }

        return new PipelineRoleBoundary(
            IsReadOnly: false,
            AllowWrites: true,
            AllowShell: true,
            AllowMcp: true,
            AllowMcpDiscovery: true,
            DisabledTools: [],
            Summary: expert.ToolBoundary);
    }

    private static string Safe(string value)
    {
        string safe = DiagnosticSecretRedactor.Redact(value);
        return safe.Length <= 2_048 ? safe : safe[..2_048];
    }
}

public sealed record PipelineRoleStep
{
    public PipelineRoleStep(
        string StepId,
        string Role,
        string CommandFamily,
        string Expert,
        string Instructions,
        PipelineRoleBoundary Boundary,
        string? Skill = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(StepId);
        ArgumentException.ThrowIfNullOrWhiteSpace(Role);
        ArgumentException.ThrowIfNullOrWhiteSpace(CommandFamily);
        ArgumentException.ThrowIfNullOrWhiteSpace(Expert);
        ArgumentException.ThrowIfNullOrWhiteSpace(Instructions);
        ArgumentNullException.ThrowIfNull(Boundary);
        if (!PipelineCommandFamily.IsKnown(CommandFamily))
        {
            throw new ArgumentException("Pipeline command family is invalid.", nameof(CommandFamily));
        }

        if (!ExpertProfileCatalog.TryGet(Expert, out ExpertProfile? profile) || profile is null)
        {
            throw new ArgumentException("Pipeline expert profile is invalid.", nameof(Expert));
        }

        if (string.Equals(CommandFamily, PipelineCommandFamily.Skill, StringComparison.Ordinal) &&
            string.IsNullOrWhiteSpace(Skill))
        {
            throw new ArgumentException("A skill pipeline step requires a skill name.", nameof(Skill));
        }

        if ((string.Equals(Role, "reviewer", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(Role, "security", StringComparison.OrdinalIgnoreCase) ||
             profile.IsReadOnly) &&
            (!Boundary.IsReadOnly || Boundary.AllowWrites || Boundary.AllowShell ||
             Boundary.AllowMcp || Boundary.AllowMcpDiscovery))
        {
            throw new ArgumentException("Reviewer and security pipeline roles must remain read-only.", nameof(Boundary));
        }

        this.StepId = Safe(StepId);
        this.Role = Safe(Role);
        this.CommandFamily = CommandFamily;
        this.Expert = profile.Name;
        this.Skill = SafeOrNull(Skill);
        this.Instructions = Safe(Instructions);
        this.Boundary = Boundary;
    }

    public string StepId { get; }

    public string Role { get; }

    public string CommandFamily { get; }

    public string Expert { get; }

    public string? Skill { get; }

    public string Instructions { get; }

    public PipelineRoleBoundary Boundary { get; }

    private static string Safe(string value)
    {
        string safe = DiagnosticSecretRedactor.Redact(value);
        return safe.Length <= 4_096 ? safe : safe[..4_096];
    }

    private static string? SafeOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Safe(value);
}

public sealed record PipelineManifest
{
    public const int CurrentSchemaVersion = 1;

    public PipelineManifest(
        string Name,
        string Description,
        IReadOnlyList<PipelineRoleStep> Steps,
        int SchemaVersion = CurrentSchemaVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(Description);
        ArgumentNullException.ThrowIfNull(Steps);
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(SchemaVersion));
        }

        if (Steps.Count == 0)
        {
            throw new ArgumentException("A pipeline must contain at least one role step.", nameof(Steps));
        }

        if (Steps.Select(step => step.StepId).Distinct(StringComparer.Ordinal).Count() != Steps.Count)
        {
            throw new ArgumentException("Pipeline step ids must be unique.", nameof(Steps));
        }

        this.SchemaVersion = SchemaVersion;
        this.Name = DiagnosticSecretRedactor.Redact(Name);
        this.Description = DiagnosticSecretRedactor.Redact(Description);
        this.Steps = new ReadOnlyCollection<PipelineRoleStep>(Steps.ToArray());
    }

    public int SchemaVersion { get; }

    public string Name { get; }

    public string Description { get; }

    public IReadOnlyList<PipelineRoleStep> Steps { get; }
}

public sealed record PipelinePlan
{
    public PipelinePlan(PipelineManifest Pipeline, string Task, string WorkspaceRoot, string? Cwd = null)
    {
        ArgumentNullException.ThrowIfNull(Pipeline);
        ArgumentException.ThrowIfNullOrWhiteSpace(Task);
        ArgumentException.ThrowIfNullOrWhiteSpace(WorkspaceRoot);

        this.SchemaVersion = PipelineManifest.CurrentSchemaVersion;
        this.Pipeline = Pipeline;
        this.Task = Safe(Task, 16_384);
        this.WorkspaceRoot = Safe(WorkspaceRoot, 4_096);
        this.Cwd = string.IsNullOrWhiteSpace(Cwd) ? null : Safe(Cwd, 4_096);
    }

    public int SchemaVersion { get; }

    public PipelineManifest Pipeline { get; }

    public string Task { get; }

    public string WorkspaceRoot { get; }

    public string? Cwd { get; }

    private static string Safe(string value, int maxLength)
    {
        string safe = DiagnosticSecretRedactor.Redact(value);
        return safe.Length <= maxLength ? safe : safe[..maxLength];
    }
}

public sealed record PipelineRoleReport(
    string StepId,
    string Role,
    string Expert,
    PipelineRoleBoundary Boundary,
    string Status,
    int ExitCode,
    string QueueId,
    int Attempt,
    string? JobId,
    string? StopReason,
    string? ErrorCode,
    string? Summary,
    JobTaskReportSummary? TaskReport,
    IReadOnlyList<JobArtifact> Artifacts,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> RemainingRisks);

public sealed record PipelineArtifactReference(
    string StepId,
    string Role,
    string? JobId,
    string Kind,
    string Path,
    bool Exists,
    string? Summary,
    string? Sha256);

public sealed record PipelineRedactionSummary(
    bool SecretsRedacted,
    bool RawReferencesStored,
    bool RawToolArgumentsStored,
    bool FullDiffStored,
    string Policy);

public sealed record PipelineFinalReport
{
    public const int CurrentSchemaVersion = 1;

    public PipelineFinalReport(
        string RunId,
        string Pipeline,
        string Status,
        string StopReason,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset CompletedAtUtc,
        IReadOnlyList<PipelineRoleReport>? Roles,
        IReadOnlyList<PipelineArtifactReference>? Artifacts,
        IReadOnlyList<string>? Warnings,
        IReadOnlyList<string>? RemainingRisks)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(RunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(Pipeline);
        ArgumentException.ThrowIfNullOrWhiteSpace(Status);
        ArgumentException.ThrowIfNullOrWhiteSpace(StopReason);

        SchemaVersion = CurrentSchemaVersion;
        this.RunId = DiagnosticSecretRedactor.Redact(RunId);
        this.Pipeline = DiagnosticSecretRedactor.Redact(Pipeline);
        this.Status = Status;
        this.StopReason = StopReason;
        this.StartedAtUtc = StartedAtUtc;
        this.CompletedAtUtc = CompletedAtUtc;
        this.Roles = new ReadOnlyCollection<PipelineRoleReport>((Roles ?? []).ToArray());
        this.Artifacts = new ReadOnlyCollection<PipelineArtifactReference>((Artifacts ?? []).ToArray());
        this.Warnings = new ReadOnlyCollection<string>((Warnings ?? []).Select(DiagnosticSecretRedactor.Redact).Distinct(StringComparer.Ordinal).ToArray());
        this.RemainingRisks = new ReadOnlyCollection<string>((RemainingRisks ?? []).Select(DiagnosticSecretRedactor.Redact).Distinct(StringComparer.Ordinal).ToArray());
        Redaction = new PipelineRedactionSummary(
            SecretsRedacted: true,
            RawReferencesStored: false,
            RawToolArgumentsStored: false,
            FullDiffStored: false,
            Policy: "pipeline reports aggregate bounded redacted job/task report metadata and artifact pointers only");
    }

    public int SchemaVersion { get; }

    public string RunId { get; }

    public string Pipeline { get; }

    public string Status { get; }

    public string StopReason { get; }

    public DateTimeOffset StartedAtUtc { get; }

    public DateTimeOffset CompletedAtUtc { get; }

    public IReadOnlyList<PipelineRoleReport> Roles { get; }

    public IReadOnlyList<PipelineArtifactReference> Artifacts { get; }

    public IReadOnlyList<string> Warnings { get; }

    public IReadOnlyList<string> RemainingRisks { get; }

    public PipelineRedactionSummary Redaction { get; }
}

public static class PipelineRunIdGenerator
{
    private static readonly Regex Pattern = new(
        "^pipeline_[0-9]{8}T[0-9]{9}Z_[a-f0-9]{8}$",
        RegexOptions.CultureInvariant);

    public static string Create(DateTimeOffset timestampUtc)
    {
        Span<byte> bytes = stackalloc byte[4];
        RandomNumberGenerator.Fill(bytes);
        return $"pipeline_{timestampUtc.UtcDateTime:yyyyMMddTHHmmssfffZ}_{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    public static bool IsValid(string? value) =>
        !string.IsNullOrWhiteSpace(value) && Pattern.IsMatch(value);
}

public static class PipelineJsonSchema
{
    public static string CreateFinalReportSchema()
    {
        Dictionary<string, object?> schema = new()
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = "https://c-aicli.local/schemas/pipeline-final-report.v1.json",
            ["title"] = "C-AICLI PipelineFinalReport v1",
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new[]
            {
                "schemaVersion", "runId", "pipeline", "status", "stopReason",
                "startedAtUtc", "completedAtUtc", "roles", "artifacts", "warnings",
                "remainingRisks", "redaction"
            },
            ["properties"] = new Dictionary<string, object?>
            {
                ["schemaVersion"] = new Dictionary<string, object?> { ["const"] = PipelineFinalReport.CurrentSchemaVersion },
                ["runId"] = new Dictionary<string, object?> { ["type"] = "string", ["pattern"] = "^pipeline_[0-9]{8}T[0-9]{9}Z_[a-f0-9]{8}$" },
                ["pipeline"] = StringSchema(),
                ["status"] = new Dictionary<string, object?> { ["enum"] = new[] { PipelineStatus.Succeeded, PipelineStatus.Failed } },
                ["stopReason"] = new Dictionary<string, object?> { ["enum"] = new[] { PipelineStopReason.Completed, PipelineStopReason.RoleFailed } },
                ["startedAtUtc"] = DateTimeSchema(),
                ["completedAtUtc"] = DateTimeSchema(),
                ["roles"] = ArraySchema("object"),
                ["artifacts"] = ArraySchema("object"),
                ["warnings"] = ArraySchema("string"),
                ["remainingRisks"] = ArraySchema("string"),
                ["redaction"] = new Dictionary<string, object?> { ["type"] = "object" }
            }
        };

        return JsonSerializer.Serialize(
            schema,
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
    }

    private static Dictionary<string, object?> StringSchema() => new() { ["type"] = "string" };

    private static Dictionary<string, object?> DateTimeSchema() => new()
    {
        ["type"] = "string",
        ["format"] = "date-time"
    };

    private static Dictionary<string, object?> ArraySchema(string itemType) => new()
    {
        ["type"] = "array",
        ["items"] = new Dictionary<string, object?> { ["type"] = itemType }
    };
}
