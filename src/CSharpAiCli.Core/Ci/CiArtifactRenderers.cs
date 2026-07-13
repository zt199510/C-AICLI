using System.Text.Json;
using System.Text.RegularExpressions;

namespace CSharpAiCli.Core;

public static class CiExitCodePolicy
{
    public const int Success = 0;
    public const int Failure = 1;
    public const int ConfigError = 2;

    public const string Description =
        "success/warning=0; failure=1; invalid input or unsafe redaction boundary=2";

    public static int ForOutcome(string outcome) => outcome switch
    {
        CiCheckOutcome.Success => Success,
        CiCheckOutcome.Warning => Success,
        CiCheckOutcome.Failure => Failure,
        CiCheckOutcome.ConfigError => ConfigError,
        _ => ConfigError
    };

    public static int Evaluate(CiArtifact artifact, string? failOn = null)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        string threshold = string.IsNullOrWhiteSpace(failOn) ? CiFailOn.None : failOn;
        if (!CiFailOn.IsKnown(threshold))
        {
            return ConfigError;
        }

        int exitCode = ForOutcome(artifact.Check.Outcome);
        if (exitCode != Success)
        {
            return exitCode;
        }

        if (string.Equals(threshold, CiFailOn.Risks, StringComparison.OrdinalIgnoreCase) &&
            artifact.Summary.RiskCount > 0)
        {
            return Failure;
        }

        if (string.Equals(threshold, CiFailOn.Warnings, StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(artifact.Check.Outcome, CiCheckOutcome.Warning, StringComparison.Ordinal) ||
             artifact.Summary.WarningCount > 0 ||
             artifact.Summary.RiskCount > 0))
        {
            return Failure;
        }

        return Success;
    }

    public static CiArtifact Apply(CiArtifact artifact, string? failOn = null)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        string threshold = string.IsNullOrWhiteSpace(failOn) ? CiFailOn.None : failOn;
        int exitCode = Evaluate(artifact, threshold);
        return new CiArtifact(
            artifact.GeneratedAtUtc,
            artifact.Job,
            artifact.Correlation,
            artifact.Summary,
            artifact.Check with
            {
                RecommendedExitCode = exitCode,
                Policy = Description + "; failOn=" + threshold.ToLowerInvariant()
            },
            artifact.Annotations,
            artifact.Artifacts,
            artifact.Redaction);
    }
}

public static class CiFailOn
{
    public const string None = "none";
    public const string Warnings = "warnings";
    public const string Risks = "risks";

    public static bool IsKnown(string? value) =>
        string.Equals(value, None, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, Warnings, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, Risks, StringComparison.OrdinalIgnoreCase);
}

public sealed class CiArtifactRenderer
{
    private static readonly Regex PipelineRunPattern = new(
        @"(?:^|;)pipeline=(?<id>pipeline_[0-9]{8}T[0-9]{9}Z_[a-f0-9]{8})(?:;|$)",
        RegexOptions.CultureInvariant);

    public CiArtifact Render(JobRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        bool safeBoundary = HasSafeStorageBoundary(record.Redaction);
        string outcome = safeBoundary ? Classify(record) : CiCheckOutcome.ConfigError;
        List<CiAnnotation> annotations = safeBoundary
            ? CreateAnnotations(record, outcome)
            :
            [
                new CiAnnotation(
                    CiAnnotationLevel.Failure,
                    "unsafe-redaction-boundary",
                    "The source job does not declare the required CI redaction and storage boundary.")
            ];
        IReadOnlyList<CiArtifactPointer> artifacts = safeBoundary
            ? record.Artifacts.Select(artifact => new CiArtifactPointer(
                artifact.Kind,
                artifact.Path,
                artifact.Exists,
                artifact.Summary,
                artifact.Sha256)).ToArray()
            : [];
        int warningCount = safeBoundary ? record.Warnings.Count : 0;
        int riskCount = safeBoundary ? record.TaskReport?.RiskCount ?? 0 : 0;
        string title = $"C-AICLI job {record.JobId}";
        string message = safeBoundary
            ? record.Summary ?? record.TaskReport?.Summary ?? $"Job status: {record.Status}."
            : "CI artifact generation was blocked by an unsafe source storage boundary.";
        string? errorCode = safeBoundary
            ? record.ErrorCode ?? record.TaskReport?.ErrorCode
            : "unsafe-redaction-boundary";

        return new CiArtifact(
            GeneratedAtUtc: record.UpdatedAtUtc,
            Job: new CiJobReference(
                Safe(record.JobId, 128),
                Safe(record.Status, 128),
                Safe(record.Command.Family, 128),
                SafeOrNull(record.JobName, 256),
                record.CreatedAtUtc,
                record.UpdatedAtUtc,
                record.CompletedAtUtc),
            Correlation: safeBoundary ? CreateCorrelation(record) : new CiCorrelation(null, null, null, null, null),
            Summary: new CiSummary(
                Safe(title, 512),
                Safe(message, 4_096),
                safeBoundary ? record.TaskReport?.ChangedFileCount ?? 0 : 0,
                safeBoundary ? record.TaskReport?.VerificationCount ?? 0 : 0,
                warningCount,
                riskCount,
                annotations.Count,
                artifacts.Count),
            Check: new CiCheckResult(
                Safe(record.JobName ?? record.Command.Family, 256),
                outcome,
                CiExitCodePolicy.ForOutcome(outcome),
                CiExitCodePolicy.Description,
                SafeOrNull(errorCode, 256)),
            Annotations: annotations,
            Artifacts: artifacts,
            Redaction: new CiArtifactRedaction(
                SecretsRedacted: true,
                RawReferencesStored: false,
                RawToolArgumentsStored: false,
                FullDiffStored: false,
                Policy: "CI artifacts contain bounded redacted job/task-report metadata and artifact pointers only"));
    }

    private static bool HasSafeStorageBoundary(JobRedactionSummary redaction) =>
        redaction.SecretsRedacted &&
        !redaction.RawReferencesStored &&
        !redaction.RawToolArgumentsStored &&
        !redaction.FullDiffStored;

    private static string Classify(JobRecord record)
    {
        if (record.Status is JobStatus.Failed or JobStatus.Canceled or JobStatus.ApprovalRequired ||
            record.ExitCode is not null and not 0)
        {
            return CiCheckOutcome.Failure;
        }

        if (record.Status is not (JobStatus.Succeeded or JobStatus.DryRun or JobStatus.Planned or JobStatus.Running))
        {
            return CiCheckOutcome.ConfigError;
        }

        if (record.Status is JobStatus.Planned or JobStatus.Running ||
            record.Warnings.Count > 0 ||
            (record.TaskReport?.RiskCount ?? 0) > 0)
        {
            return CiCheckOutcome.Warning;
        }

        return CiCheckOutcome.Success;
    }

    private static List<CiAnnotation> CreateAnnotations(JobRecord record, string outcome)
    {
        List<CiAnnotation> annotations = [];
        if (string.Equals(outcome, CiCheckOutcome.Failure, StringComparison.Ordinal))
        {
            annotations.Add(new CiAnnotation(
                CiAnnotationLevel.Failure,
                record.ErrorCode ?? record.TaskReport?.ErrorCode ?? "job-failed",
                record.Summary ?? record.TaskReport?.Summary ?? $"Job ended with status {record.Status}."));
        }
        else if (string.Equals(outcome, CiCheckOutcome.ConfigError, StringComparison.Ordinal))
        {
            annotations.Add(new CiAnnotation(
                CiAnnotationLevel.Failure,
                "unsupported-job-status",
                $"Job status '{record.Status}' is not supported by CI artifact schema v1."));
        }

        annotations.AddRange(record.Warnings.Select(warning =>
            new CiAnnotation(CiAnnotationLevel.Warning, "job-warning", warning)));
        if (record.TaskReport is not null)
        {
            annotations.AddRange(record.TaskReport.Risks.Select(risk =>
                new CiAnnotation(CiAnnotationLevel.Warning, "remaining-risk", risk)));
            annotations.AddRange(record.TaskReport.ChangedFiles.Select(file =>
                new CiAnnotation(CiAnnotationLevel.Notice, "changed-file", file)));
        }

        return annotations.Take(256).ToList();
    }

    private static CiCorrelation CreateCorrelation(JobRecord record)
    {
        string? queueId = TaskQueueIdGenerator.IsValid(record.JobName) ? record.JobName : null;
        string? pipelineRunId = null;
        foreach (string warning in record.Warnings)
        {
            Match match = PipelineRunPattern.Match(warning);
            if (match.Success)
            {
                pipelineRunId = match.Groups["id"].Value;
                break;
            }
        }

        AutomationRunMetadata? automation = record.Command.Automation;
        return new CiCorrelation(
            SafeOrNull(queueId, 128),
            SafeOrNull(pipelineRunId, 128),
            SafeOrNull(automation?.Automation, 256),
            SafeOrNull(automation?.RunId, 128),
            SafeOrNull(automation?.TargetType, 128));
    }

    private static string Safe(string value, int maxLength)
    {
        string safe = DiagnosticSecretRedactor.Redact(value ?? string.Empty);
        safe = safe.Replace('\r', ' ').Replace('\n', ' ');
        return safe.Length <= maxLength ? safe : safe[..maxLength];
    }

    private static string? SafeOrNull(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Safe(value, maxLength);
}

public sealed class CiJsonRenderer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly TextWriter writer;

    public CiJsonRenderer(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        this.writer = writer;
    }

    public void Write(CiArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        writer.WriteLine(JsonSerializer.Serialize(artifact, JsonOptions));
    }
}

public sealed class CiMarkdownRenderer
{
    public string Render(CiArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        List<string> lines =
        [
            "# C-AICLI CI summary",
            string.Empty,
            $"- Job: `{Markdown(artifact.Job.JobId)}`",
            $"- Status: {Markdown(artifact.Job.Status)}",
            $"- Outcome: **{Markdown(artifact.Check.Outcome)}**",
            $"- Recommended exit code: {artifact.Check.RecommendedExitCode}",
            $"- Updated: {artifact.Job.UpdatedAtUtc:O}",
            string.Empty,
            "## Summary",
            string.Empty,
            Markdown(artifact.Summary.Message),
            string.Empty,
            $"- Changed files: {artifact.Summary.ChangedFileCount}",
            $"- Verification: {artifact.Summary.VerificationCount}",
            $"- Warnings: {artifact.Summary.WarningCount}",
            $"- Remaining risks: {artifact.Summary.RiskCount}",
            string.Empty,
            "## Annotations",
            string.Empty
        ];

        if (artifact.Annotations.Count == 0)
        {
            lines.Add("- none");
        }
        else
        {
            lines.AddRange(artifact.Annotations.Select(annotation =>
                $"- [{Markdown(annotation.Level)}] `{Markdown(annotation.Code)}`: {Markdown(annotation.Message)}"));
        }

        lines.Add(string.Empty);
        lines.Add("## Artifacts");
        lines.Add(string.Empty);
        if (artifact.Artifacts.Count == 0)
        {
            lines.Add("- none");
        }
        else
        {
            lines.AddRange(artifact.Artifacts.Select(pointer =>
                $"- {Markdown(pointer.Kind)}: `{Markdown(pointer.Path)}` exists={pointer.Exists.ToString().ToLowerInvariant()}"));
        }

        lines.Add(string.Empty);
        lines.Add("## Redaction");
        lines.Add(string.Empty);
        lines.Add("- " + Markdown(artifact.Redaction.Policy));
        return string.Join("\n", lines) + "\n";
    }

    public void Write(TextWriter writer, CiArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(Render(artifact));
    }

    private static string Markdown(string value)
    {
        string safe = DiagnosticSecretRedactor.Redact(value ?? string.Empty)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace("`", "\\`", StringComparison.Ordinal);
        return safe;
    }
}
