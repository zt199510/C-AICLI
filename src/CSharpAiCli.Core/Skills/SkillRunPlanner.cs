using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CSharpAiCli.Core;

public sealed record SkillRunRequest(
    string SkillName,
    string Task,
    string? ReportOverride = null);

public sealed record SkillRunPlan
{
    public SkillRunPlan(
        SkillPackManifest Manifest,
        SkillPackSource Source,
        string RequestedTask,
        string ExpandedTask,
        string EntryMode,
        string Expert,
        string Report,
        IReadOnlyList<string>? SuggestedReferences,
        IReadOnlyList<string>? Instructions,
        string? ValidationCommand,
        SkillPackSafety Safety,
        ToolExecutionBoundary ToolBoundary)
    {
        ArgumentNullException.ThrowIfNull(Manifest);
        ArgumentNullException.ThrowIfNull(Source);
        ArgumentException.ThrowIfNullOrWhiteSpace(EntryMode);
        ArgumentException.ThrowIfNullOrWhiteSpace(Expert);
        ArgumentException.ThrowIfNullOrWhiteSpace(Report);
        ArgumentNullException.ThrowIfNull(Safety);
        ArgumentNullException.ThrowIfNull(ToolBoundary);

        this.Manifest = Manifest;
        this.Source = Source;
        this.RequestedTask = RequestedTask ?? string.Empty;
        this.ExpandedTask = ExpandedTask ?? string.Empty;
        this.EntryMode = EntryMode;
        this.Expert = Expert;
        this.Report = Report;
        this.SuggestedReferences = new ReadOnlyCollection<string>((SuggestedReferences ?? []).ToArray());
        this.Instructions = new ReadOnlyCollection<string>((Instructions ?? []).ToArray());
        this.ValidationCommand = ValidationCommand;
        this.Safety = Safety;
        this.ToolBoundary = ToolBoundary;
    }

    public SkillPackManifest Manifest { get; }

    public SkillPackSource Source { get; }

    public string RequestedTask { get; }

    public string ExpandedTask { get; }

    public string EntryMode { get; }

    public string Expert { get; }

    public string Report { get; }

    public IReadOnlyList<string> SuggestedReferences { get; }

    public IReadOnlyList<string> Instructions { get; }

    public string? ValidationCommand { get; }

    public SkillPackSafety Safety { get; }

    public ToolExecutionBoundary ToolBoundary { get; }

    public SkillRunMetadata ToMetadata()
    {
        return new SkillRunMetadata(
            Name: Manifest.Name ?? string.Empty,
            Version: Manifest.Version ?? string.Empty,
            Description: Manifest.Description ?? string.Empty,
            SourceKind: Source.Kind,
            SourcePath: Source.Path,
            EntryMode: EntryMode,
            Expert: Expert,
            Report: Report,
            SafetySummary: Safety.ToSummary(),
            ValidationCommand: ValidationCommand,
            SuggestedReferences: SuggestedReferences);
    }
}

public sealed record SkillRunPlanResult
{
    private SkillRunPlanResult(
        bool Succeeded,
        SkillRunPlan? Plan,
        string? ErrorCode,
        string? Summary)
    {
        this.Succeeded = Succeeded;
        this.Plan = Plan;
        this.ErrorCode = ErrorCode;
        this.Summary = Summary;
    }

    public bool Succeeded { get; }

    public SkillRunPlan? Plan { get; }

    public string? ErrorCode { get; }

    public string? Summary { get; }

    public static SkillRunPlanResult Success(SkillRunPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new SkillRunPlanResult(true, plan, null, null);
    }

    public static SkillRunPlanResult Failure(string errorCode, string summary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        return new SkillRunPlanResult(false, null, errorCode, summary);
    }
}

public sealed record SkillRunMetadata(
    string Name,
    string Version,
    string Description,
    string SourceKind,
    string? SourcePath,
    string EntryMode,
    string Expert,
    string Report,
    string SafetySummary,
    string? ValidationCommand,
    IReadOnlyList<string> SuggestedReferences)
{
    public AgentTaskSkillReport ToReportMetadata()
    {
        return new AgentTaskSkillReport(
            Name,
            Version,
            Description,
            SourceKind,
            SourcePath,
            EntryMode,
            Expert,
            Report,
            SafetySummary,
            ValidationCommand,
            SuggestedReferences);
    }
}

public sealed class SkillRunPlanner
{
    private readonly SkillPackValidator validator;

    public SkillRunPlanner()
        : this(new SkillPackValidator())
    {
    }

    internal SkillRunPlanner(SkillPackValidator validator)
    {
        ArgumentNullException.ThrowIfNull(validator);
        this.validator = validator;
    }

    public SkillRunPlanResult Plan(SkillPackCatalog catalog, SkillRunRequest request)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.SkillName))
        {
            return SkillRunPlanResult.Failure(
                SkillPackErrorCode.NotFound,
                "Skill pack name is required.");
        }

        if (!catalog.TryGet(request.SkillName, out SkillPackCatalogItem? item) || item is null)
        {
            return SkillRunPlanResult.Failure(
                SkillPackErrorCode.NotFound,
                $"Skill pack '{request.SkillName}' was not found.");
        }

        SkillValidationResult validation = validator.Validate(item.Manifest, item.Source);
        if (!validation.Succeeded)
        {
            SkillPackDiagnostic first = validation.Diagnostics[0];
            return SkillRunPlanResult.Failure(first.ErrorCode, first.Summary);
        }

        SkillPackEntry entry = item.Manifest.Entry!;
        SkillPackSafety safety = item.Manifest.Safety!;
        string report = string.IsNullOrWhiteSpace(request.ReportOverride)
            ? entry.Report!
            : request.ReportOverride.Trim();
        if (!ExecReportModeParser.TryParse(report, out ExecReportMode reportMode))
        {
            return SkillRunPlanResult.Failure(
                SkillPackErrorCode.RunPlanInvalid,
                "Report mode must be none or markdown.");
        }

        ExpertProfile? expert = ExpertProfileCatalog.GetOrNull(entry.Expert);
        if (expert is null)
        {
            return SkillRunPlanResult.Failure(
                SkillPackErrorCode.RunPlanInvalid,
                "Skill pack expert profile could not be resolved.");
        }

        ToolExecutionBoundary boundary = CreateBoundary(expert, safety);
        string expandedTask = CreateExpandedTask(item.Manifest, item.Source, request.Task, reportMode.ToCanonicalName(), safety);
        return SkillRunPlanResult.Success(new SkillRunPlan(
            item.Manifest,
            item.Source,
            request.Task,
            expandedTask,
            entry.Mode!,
            expert.Name,
            reportMode.ToCanonicalName(),
            item.Manifest.References?.Suggested ?? [],
            item.Manifest.Instructions ?? [],
            item.Manifest.ValidationCommand,
            safety,
            boundary));
    }

    private static ToolExecutionBoundary CreateBoundary(ExpertProfile expert, SkillPackSafety safety)
    {
        if (expert.IsReadOnly || !safety.AllowWrites)
        {
            return ToolExecutionBoundary.ReadOnly(
                "skill boundary: read-only; patch, shell, and MCP tools are disabled");
        }

        HashSet<ToolRiskLevel> allowedRiskLevels =
        [
            ToolRiskLevel.Read,
            ToolRiskLevel.Write
        ];
        HashSet<string> disabledNames = new(StringComparer.Ordinal);
        List<string> disabledPrefixes = [];
        bool allowMcpDiscovery = safety.AllowMcp;

        if (string.Equals(safety.ShellMode, "verification-only", StringComparison.OrdinalIgnoreCase))
        {
            allowedRiskLevels.Add(ToolRiskLevel.Shell);
        }
        else
        {
            disabledNames.Add("workspace.run_shell");
        }

        if (!safety.AllowMcp)
        {
            disabledPrefixes.Add("mcp.");
            allowMcpDiscovery = false;
        }

        return new ToolExecutionBoundary(
            AllowedRiskLevels: allowedRiskLevels,
            DisabledToolNames: disabledNames,
            DisabledToolPrefixes: disabledPrefixes,
            AllowMcpDiscovery: allowMcpDiscovery,
            Summary: "skill boundary: " + safety.ToSummary());
    }

    private static string CreateExpandedTask(
        SkillPackManifest manifest,
        SkillPackSource source,
        string task,
        string report,
        SkillPackSafety safety)
    {
        StringBuilder builder = new();
        builder.AppendLine("Skill pack:");
        builder.AppendLine("- Name: " + manifest.Name);
        builder.AppendLine("- Version: " + manifest.Version);
        builder.AppendLine("- Source: " + FormatSource(source));
        builder.AppendLine("- Entry mode: " + manifest.Entry?.Mode);
        builder.AppendLine("- Expert: " + manifest.Entry?.Expert);
        builder.AppendLine("- Report: " + report);
        builder.AppendLine("- Safety: " + safety.ToSummary());
        if (!string.IsNullOrWhiteSpace(manifest.ValidationCommand))
        {
            builder.AppendLine("- Validation command hint: " + manifest.ValidationCommand);
            builder.AppendLine("- Validation command policy: hint only; do not bypass approval or shell policy.");
        }

        IReadOnlyList<string> suggestedReferences = manifest.References?.Suggested ?? [];
        if (suggestedReferences.Count > 0)
        {
            builder.AppendLine("- Suggested references: " + string.Join(", ", suggestedReferences.Select(reference => reference.TrimStart('@'))));
            builder.AppendLine("- Reference policy: suggestions are not automatically loaded unless the user provided @file: or @folder: references in the task.");
        }

        IReadOnlyList<string> instructions = manifest.Instructions ?? [];
        if (instructions.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Pack instructions:");
            foreach (string instruction in instructions)
            {
                builder.AppendLine("- " + instruction);
            }
        }

        builder.AppendLine();
        builder.AppendLine("User task:");
        builder.AppendLine(task ?? string.Empty);
        return builder.ToString().TrimEnd();
    }

    private static string FormatSource(SkillPackSource source)
    {
        return string.IsNullOrWhiteSpace(source.Path)
            ? source.Kind
            : source.Kind + ":" + source.Path;
    }
}

public static class SkillPackReportRenderer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string RenderListText(SkillPackCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        StringBuilder builder = new();
        builder.AppendLine("C# AI CLI skills");
        if (catalog.Packs.Count == 0)
        {
            builder.AppendLine("status: empty");
        }
        else
        {
            foreach (SkillPackCatalogItem item in catalog.Packs)
            {
                builder.AppendLine(
                    $"- {item.Manifest.Name} {item.Manifest.Version} source={FormatSource(item.Source)} expert={item.Manifest.Entry?.Expert} report={item.Manifest.Entry?.Report} safety={item.Manifest.Safety?.ToSummary()}");
                builder.AppendLine("  " + item.Manifest.Description);
            }
        }

        AppendDiagnostics(builder, catalog.Diagnostics);
        return builder.ToString().TrimEnd();
    }

    public static string RenderListJson(SkillPackCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        Dictionary<string, object?> payload = new(StringComparer.Ordinal)
        {
            ["type"] = "skills.list",
            ["packs"] = catalog.Packs.Select(item => new Dictionary<string, object?>
            {
                ["name"] = item.Manifest.Name,
                ["version"] = item.Manifest.Version,
                ["description"] = item.Manifest.Description,
                ["source"] = SourceToJson(item.Source),
                ["entry"] = new Dictionary<string, object?>
                {
                    ["mode"] = item.Manifest.Entry?.Mode,
                    ["expert"] = item.Manifest.Entry?.Expert,
                    ["report"] = item.Manifest.Entry?.Report
                },
                ["safety"] = SafetyToJson(item.Manifest.Safety),
                ["validationCommand"] = item.Manifest.ValidationCommand
            }).ToArray(),
            ["diagnostics"] = DiagnosticsToJson(catalog.Diagnostics)
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    public static string RenderPlanText(SkillRunPlan plan, bool dryRun)
    {
        ArgumentNullException.ThrowIfNull(plan);

        StringBuilder builder = new();
        builder.AppendLine("C# AI CLI skill run plan");
        builder.AppendLine("status: planned");
        builder.AppendLine("dryRun: " + (dryRun ? "true" : "false"));
        builder.AppendLine("skill: " + plan.Manifest.Name);
        builder.AppendLine("version: " + plan.Manifest.Version);
        builder.AppendLine("description: " + plan.Manifest.Description);
        builder.AppendLine("source: " + FormatSource(plan.Source));
        builder.AppendLine("entry: " + plan.EntryMode);
        builder.AppendLine("expert: " + plan.Expert);
        builder.AppendLine("report: " + plan.Report);
        builder.AppendLine("safety: " + plan.Safety.ToSummary());
        builder.AppendLine("toolBoundary: " + (plan.ToolBoundary.Summary ?? "default"));
        builder.AppendLine("validationCommand: " + (string.IsNullOrWhiteSpace(plan.ValidationCommand) ? "none" : plan.ValidationCommand));
        AppendList(builder, "suggestedReferences", plan.SuggestedReferences);
        AppendList(builder, "instructions", plan.Instructions);
        builder.AppendLine("expandedTask:");
        builder.AppendLine(plan.ExpandedTask);
        return builder.ToString().TrimEnd();
    }

    public static string RenderPlanJson(SkillRunPlan plan, bool dryRun)
    {
        ArgumentNullException.ThrowIfNull(plan);

        Dictionary<string, object?> payload = new(StringComparer.Ordinal)
        {
            ["type"] = "skills.runPlan",
            ["status"] = "planned",
            ["dryRun"] = dryRun,
            ["skill"] = new Dictionary<string, object?>
            {
                ["name"] = plan.Manifest.Name,
                ["version"] = plan.Manifest.Version,
                ["description"] = plan.Manifest.Description,
                ["source"] = SourceToJson(plan.Source)
            },
            ["entry"] = plan.EntryMode,
            ["expert"] = plan.Expert,
            ["report"] = plan.Report,
            ["safety"] = SafetyToJson(plan.Safety),
            ["toolBoundary"] = plan.ToolBoundary.Summary,
            ["validationCommand"] = plan.ValidationCommand,
            ["suggestedReferences"] = plan.SuggestedReferences.ToArray(),
            ["instructions"] = plan.Instructions.ToArray(),
            ["requestedTask"] = plan.RequestedTask,
            ["expandedTask"] = plan.ExpandedTask
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static void AppendDiagnostics(StringBuilder builder, IReadOnlyList<SkillPackDiagnostic> diagnostics)
    {
        if (diagnostics.Count == 0)
        {
            return;
        }

        builder.AppendLine("diagnostics:");
        foreach (SkillPackDiagnostic diagnostic in diagnostics)
        {
            builder.AppendLine(
                $"- errorCode={diagnostic.ErrorCode} skill={diagnostic.SkillName ?? "none"} source={diagnostic.SourcePath ?? "none"} summary={diagnostic.Summary}");
        }
    }

    private static void AppendList(StringBuilder builder, string name, IReadOnlyList<string> values)
    {
        builder.AppendLine(name + ":");
        if (values.Count == 0)
        {
            builder.AppendLine("- none");
            return;
        }

        foreach (string value in values)
        {
            builder.AppendLine("- " + value);
        }
    }

    private static Dictionary<string, object?> SourceToJson(SkillPackSource source)
    {
        return new Dictionary<string, object?>
        {
            ["kind"] = source.Kind,
            ["path"] = source.Path
        };
    }

    private static Dictionary<string, object?>? SafetyToJson(SkillPackSafety? safety)
    {
        if (safety is null)
        {
            return null;
        }

        return new Dictionary<string, object?>
        {
            ["allowWrites"] = safety.AllowWrites,
            ["allowShell"] = safety.ShellMode,
            ["allowMcp"] = safety.AllowMcp,
            ["summary"] = safety.ToSummary()
        };
    }

    private static IReadOnlyList<Dictionary<string, object?>> DiagnosticsToJson(
        IReadOnlyList<SkillPackDiagnostic> diagnostics)
    {
        return diagnostics.Select(diagnostic => new Dictionary<string, object?>
        {
            ["errorCode"] = diagnostic.ErrorCode,
            ["summary"] = diagnostic.Summary,
            ["sourcePath"] = diagnostic.SourcePath,
            ["skillName"] = diagnostic.SkillName
        }).ToArray();
    }

    private static string FormatSource(SkillPackSource source)
    {
        return string.IsNullOrWhiteSpace(source.Path)
            ? source.Kind
            : source.Kind + ":" + source.Path;
    }
}
