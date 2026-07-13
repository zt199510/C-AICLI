using System.Text.RegularExpressions;

namespace CSharpAiCli.Core;

public sealed class AutomationManifestValidator
{
    private static readonly Regex NamePattern = new(
        "^[a-z][a-z0-9-]{0,63}$",
        RegexOptions.CultureInvariant);

    private static readonly Regex CronTokenPattern = new(
        "^[0-9*/,-]+$",
        RegexOptions.CultureInvariant);

    private readonly WorkspaceContext workspace;
    private readonly SkillPackCatalog skillCatalog;

    public AutomationManifestValidator(WorkspaceContext workspace, SkillPackCatalog? skillCatalog = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        this.workspace = workspace;
        this.skillCatalog = skillCatalog ?? SkillPackCatalog.Load(workspace);
    }

    public AutomationValidationResult Validate(AutomationManifest? manifest, AutomationSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        List<AutomationDiagnostic> diagnostics = [];
        if (manifest is null)
        {
            diagnostics.Add(Invalid("Automation manifest could not be read.", source));
            return new AutomationValidationResult(diagnostics);
        }

        if (manifest.SchemaVersion != AutomationManifest.CurrentSchemaVersion)
        {
            diagnostics.Add(Invalid("schemaVersion must be 1.", source, manifest));
        }

        Required(manifest.Name, "name", source, manifest, diagnostics);
        Required(manifest.Description, "description", source, manifest, diagnostics);
        if (!string.IsNullOrWhiteSpace(manifest.Name) && !NamePattern.IsMatch(manifest.Name))
        {
            diagnostics.Add(Invalid("name must use lowercase letters, digits, and hyphens.", source, manifest));
        }

        if ((manifest.Description?.Length ?? 0) > 2_048)
        {
            diagnostics.Add(Invalid("description exceeds 2048 characters.", source, manifest));
        }

        ValidateTrigger(manifest, source, diagnostics);
        TargetCapabilities capabilities = ValidateTarget(manifest, source, diagnostics);
        ValidateSafety(manifest, source, capabilities, diagnostics);
        return new AutomationValidationResult(diagnostics);
    }

    public static AutomationSchedulePreview CreateSchedulePreview(AutomationTrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        if (!string.Equals(trigger.Type, AutomationTriggerType.Schedule, StringComparison.Ordinal))
        {
            return new AutomationSchedulePreview(
                Enabled: false,
                Schedule: null,
                TimeZone: null,
                Summary: "manual trigger only; no schedule is configured");
        }

        string timeZone = string.IsNullOrWhiteSpace(trigger.TimeZone)
            ? TimeZoneInfo.Local.Id
            : trigger.TimeZone.Trim();
        return new AutomationSchedulePreview(
            Enabled: false,
            Schedule: DiagnosticSecretRedactor.Redact(trigger.Schedule ?? string.Empty),
            TimeZone: DiagnosticSecretRedactor.Redact(timeZone),
            Summary: "preview only; C-AICLI does not start a scheduler or register an OS task");
    }

    private static void ValidateTrigger(
        AutomationManifest manifest,
        AutomationSource source,
        List<AutomationDiagnostic> diagnostics)
    {
        AutomationTrigger? trigger = manifest.Trigger;
        if (trigger is null)
        {
            diagnostics.Add(Invalid("trigger is required.", source, manifest));
            return;
        }

        if (!AutomationTriggerType.IsKnown(trigger.Type))
        {
            diagnostics.Add(Invalid("trigger.type must be manual or schedule.", source, manifest));
            return;
        }

        if (string.Equals(trigger.Type, AutomationTriggerType.Manual, StringComparison.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(trigger.Schedule) || !string.IsNullOrWhiteSpace(trigger.TimeZone))
            {
                diagnostics.Add(Invalid("manual triggers cannot define schedule or timeZone.", source, manifest));
            }

            return;
        }

        if (!TryValidateCron(trigger.Schedule, out string cronSummary))
        {
            diagnostics.Add(Invalid(cronSummary, source, manifest));
        }

        if (!string.IsNullOrWhiteSpace(trigger.TimeZone))
        {
            try
            {
                _ = TimeZoneInfo.FindSystemTimeZoneById(trigger.TimeZone.Trim());
            }
            catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                diagnostics.Add(Invalid("trigger.timeZone is not available on this system.", source, manifest));
            }
        }
    }

    private TargetCapabilities ValidateTarget(
        AutomationManifest manifest,
        AutomationSource source,
        List<AutomationDiagnostic> diagnostics)
    {
        AutomationTarget? target = manifest.Target;
        if (target is null)
        {
            diagnostics.Add(Invalid("target is required.", source, manifest));
            return TargetCapabilities.None;
        }

        if (!AutomationTargetType.IsKnown(target.Type))
        {
            diagnostics.Add(Invalid("target.type must be queue, skill, or pipeline.", source, manifest));
            return TargetCapabilities.None;
        }

        Required(target.Task, "target.task", source, manifest, diagnostics);
        if ((target.Task?.Length ?? 0) > 16_384)
        {
            diagnostics.Add(Invalid("target.task exceeds 16384 characters.", source, manifest));
        }

        if (!string.IsNullOrWhiteSpace(target.Cwd))
        {
            WorkspaceGuardResult cwd = new WorkspaceGuard().ResolvePath(workspace, target.Cwd);
            if (!cwd.IsAllowed)
            {
                diagnostics.Add(new AutomationDiagnostic(
                    AutomationErrorCode.UnsafeTarget,
                    "target.cwd must remain inside the selected workspace.",
                    source.Path,
                    manifest.Name));
            }
        }

        if (!string.IsNullOrWhiteSpace(target.Report) && !ExecReportModeParser.TryParse(target.Report, out _))
        {
            diagnostics.Add(Invalid("target.report must be none or markdown.", source, manifest));
        }

        return target.Type switch
        {
            AutomationTargetType.Queue => ValidateQueueTarget(manifest, source, diagnostics),
            AutomationTargetType.Skill => ValidateSkillTarget(manifest, source, diagnostics),
            AutomationTargetType.Pipeline => ValidatePipelineTarget(manifest, source, diagnostics),
            _ => TargetCapabilities.None
        };
    }

    private TargetCapabilities ValidateQueueTarget(
        AutomationManifest manifest,
        AutomationSource source,
        List<AutomationDiagnostic> diagnostics)
    {
        AutomationTarget target = manifest.Target!;
        if (target.Family is not ("exec" or "skill"))
        {
            diagnostics.Add(Invalid("queue targets require target.family exec or skill.", source, manifest));
            return TargetCapabilities.None;
        }

        if (string.Equals(target.Family, "skill", StringComparison.Ordinal))
        {
            return ValidateNamedSkill(manifest, source, diagnostics);
        }

        if (!string.IsNullOrWhiteSpace(target.Name))
        {
            diagnostics.Add(Invalid("queue exec targets cannot define target.name.", source, manifest));
        }

        return ValidateExpert(target.Expert, manifest, source, diagnostics);
    }

    private TargetCapabilities ValidateSkillTarget(
        AutomationManifest manifest,
        AutomationSource source,
        List<AutomationDiagnostic> diagnostics)
    {
        AutomationTarget target = manifest.Target!;
        if (!string.IsNullOrWhiteSpace(target.Family) || !string.IsNullOrWhiteSpace(target.Expert))
        {
            diagnostics.Add(Invalid("skill targets cannot define target.family or target.expert.", source, manifest));
        }

        return ValidateNamedSkill(manifest, source, diagnostics);
    }

    private TargetCapabilities ValidateNamedSkill(
        AutomationManifest manifest,
        AutomationSource source,
        List<AutomationDiagnostic> diagnostics)
    {
        string? name = manifest.Target?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            diagnostics.Add(Invalid("skill targets require target.name.", source, manifest));
            return TargetCapabilities.None;
        }

        if (!skillCatalog.TryGet(name, out SkillPackCatalogItem? skill) || skill?.Manifest.Safety is null)
        {
            diagnostics.Add(new AutomationDiagnostic(
                AutomationErrorCode.TargetUnavailable,
                "Automation target names an unavailable or invalid skill pack.",
                source.Path,
                manifest.Name));
            return TargetCapabilities.None;
        }

        SkillPackSafety safety = skill.Manifest.Safety;
        return new TargetCapabilities(
            safety.AllowWrites,
            !string.Equals(safety.ShellMode, "none", StringComparison.OrdinalIgnoreCase),
            safety.AllowMcp);
    }

    private static TargetCapabilities ValidatePipelineTarget(
        AutomationManifest manifest,
        AutomationSource source,
        List<AutomationDiagnostic> diagnostics)
    {
        AutomationTarget target = manifest.Target!;
        if (!string.IsNullOrWhiteSpace(target.Family) || !string.IsNullOrWhiteSpace(target.Expert))
        {
            diagnostics.Add(Invalid("pipeline targets cannot define target.family or target.expert.", source, manifest));
        }

        if (!BuiltInPipelineCatalog.TryGet(target.Name, out PipelineManifest? pipeline) || pipeline is null)
        {
            diagnostics.Add(new AutomationDiagnostic(
                AutomationErrorCode.TargetUnavailable,
                "Automation target names an unavailable built-in pipeline.",
                source.Path,
                manifest.Name));
            return TargetCapabilities.None;
        }

        return new TargetCapabilities(
            pipeline.Steps.Any(step => step.Boundary.AllowWrites),
            pipeline.Steps.Any(step => step.Boundary.AllowShell),
            pipeline.Steps.Any(step => step.Boundary.AllowMcp));
    }

    private static TargetCapabilities ValidateExpert(
        string? expertName,
        AutomationManifest manifest,
        AutomationSource source,
        List<AutomationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(expertName))
        {
            return TargetCapabilities.Full;
        }

        if (!ExpertProfileCatalog.TryGet(expertName, out ExpertProfile? expert) || expert is null)
        {
            diagnostics.Add(new AutomationDiagnostic(
                AutomationErrorCode.TargetUnavailable,
                "Automation target names an unavailable expert profile.",
                source.Path,
                manifest.Name));
            return TargetCapabilities.None;
        }

        PipelineRoleBoundary boundary = PipelineRoleBoundary.FromExpert(expert);
        return new TargetCapabilities(boundary.AllowWrites, boundary.AllowShell, boundary.AllowMcp);
    }

    private static void ValidateSafety(
        AutomationManifest manifest,
        AutomationSource source,
        TargetCapabilities target,
        List<AutomationDiagnostic> diagnostics)
    {
        AutomationSafety? safety = manifest.Safety;
        if (safety is null)
        {
            diagnostics.Add(Invalid("safety is required.", source, manifest));
            return;
        }

        if (!safety.ManualOnly)
        {
            diagnostics.Add(new AutomationDiagnostic(
                AutomationErrorCode.UnsafeTarget,
                "safety.manualOnly must be true; background automation is not supported.",
                source.Path,
                manifest.Name));
        }

        if (target.AllowWrites && !safety.AllowWrites)
        {
            diagnostics.Add(UnsafeCapability("writes", source, manifest));
        }

        if (target.AllowShell && !safety.AllowShell)
        {
            diagnostics.Add(UnsafeCapability("shell", source, manifest));
        }

        if (target.AllowMcp && !safety.AllowMcp)
        {
            diagnostics.Add(UnsafeCapability("MCP", source, manifest));
        }
    }

    private static bool TryValidateCron(string? schedule, out string summary)
    {
        if (string.IsNullOrWhiteSpace(schedule))
        {
            summary = "schedule triggers require trigger.schedule.";
            return false;
        }

        string[] fields = schedule.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int[] minimum = [0, 0, 1, 1, 0];
        int[] maximum = [59, 23, 31, 12, 7];
        if (fields.Length != 5)
        {
            summary = "trigger.schedule must use a five-field cron preview expression.";
            return false;
        }

        for (int index = 0; index < fields.Length; index++)
        {
            if (!CronTokenPattern.IsMatch(fields[index]) || !ValidateCronField(fields[index], minimum[index], maximum[index]))
            {
                summary = $"trigger.schedule field {index + 1} is invalid for preview.";
                return false;
            }
        }

        summary = "schedule preview is valid.";
        return true;
    }

    private static bool ValidateCronField(string field, int minimum, int maximum)
    {
        foreach (string item in field.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] stepParts = item.Split('/');
            if (stepParts.Length > 2 || (stepParts.Length == 2 && !TryRangeValue(stepParts[1], 1, maximum)))
            {
                return false;
            }

            string range = stepParts[0];
            if (range == "*")
            {
                continue;
            }

            string[] rangeParts = range.Split('-');
            if (rangeParts.Length == 1 && TryRangeValue(rangeParts[0], minimum, maximum))
            {
                continue;
            }

            if (rangeParts.Length != 2 ||
                !TryRangeValue(rangeParts[0], minimum, maximum) ||
                !TryRangeValue(rangeParts[1], minimum, maximum) ||
                int.Parse(rangeParts[0], System.Globalization.CultureInfo.InvariantCulture) >
                int.Parse(rangeParts[1], System.Globalization.CultureInfo.InvariantCulture))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryRangeValue(string value, int minimum, int maximum) =>
        int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int parsed) &&
        parsed >= minimum && parsed <= maximum;

    private static void Required(
        string? value,
        string field,
        AutomationSource source,
        AutomationManifest manifest,
        List<AutomationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            diagnostics.Add(Invalid(field + " is required.", source, manifest));
        }
    }

    private static AutomationDiagnostic UnsafeCapability(
        string capability,
        AutomationSource source,
        AutomationManifest manifest) =>
        new(
            AutomationErrorCode.UnsafeTarget,
            $"Target may use {capability}, but automation safety does not declare that capability.",
            source.Path,
            manifest.Name);

    private static AutomationDiagnostic Invalid(
        string summary,
        AutomationSource source,
        AutomationManifest? manifest = null) =>
        new(AutomationErrorCode.ManifestInvalid, summary, source.Path, manifest?.Name);

    private sealed record TargetCapabilities(bool AllowWrites, bool AllowShell, bool AllowMcp)
    {
        public static TargetCapabilities None { get; } = new(false, false, false);

        public static TargetCapabilities Full { get; } = new(true, true, true);
    }
}
