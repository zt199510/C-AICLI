using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CSharpAiCli.Core;

public sealed record AgentTaskReport
{
    public AgentTaskReport(
        string Status,
        string StopReason,
        string Prompt,
        string? Plan,
        IReadOnlyList<string>? Tools,
        IReadOnlyList<ChangedFileSummary>? ChangedFiles,
        IReadOnlyList<AgentTaskCommandReport>? Commands,
        IReadOnlyList<AgentTaskVerificationReport>? Verification,
        IReadOnlyList<string>? Risks,
        string? TracePath,
        IReadOnlyList<AgentTaskSecretPresence>? Secrets = null,
        IReadOnlyList<AgentTaskReferenceReport>? References = null,
        string? Summary = null,
        string? ErrorCode = null,
        AgentTaskReviewGateReport? ReviewGate = null,
        AgentTaskExpertReport? Expert = null,
        ExecReportMetadata? Report = null,
        string? WorkspaceRoot = null,
        string? SessionName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Status);
        ArgumentException.ThrowIfNullOrWhiteSpace(StopReason);

        this.Status = Status;
        this.StopReason = StopReason;
        this.Prompt = Prompt ?? string.Empty;
        this.Plan = Plan;
        this.Tools = new ReadOnlyCollection<string>((Tools ?? []).ToArray());
        this.ChangedFiles = new ReadOnlyCollection<ChangedFileSummary>((ChangedFiles ?? []).ToArray());
        this.Commands = new ReadOnlyCollection<AgentTaskCommandReport>((Commands ?? []).ToArray());
        this.Verification = new ReadOnlyCollection<AgentTaskVerificationReport>((Verification ?? []).ToArray());
        this.Risks = new ReadOnlyCollection<string>((Risks ?? []).ToArray());
        this.TracePath = TracePath;
        this.Secrets = new ReadOnlyCollection<AgentTaskSecretPresence>((Secrets ?? []).ToArray());
        this.References = new ReadOnlyCollection<AgentTaskReferenceReport>((References ?? []).ToArray());
        this.Summary = Summary;
        this.ErrorCode = ErrorCode;
        this.ReviewGate = ReviewGate;
        this.Expert = Expert;
        this.Report = Report;
        this.WorkspaceRoot = WorkspaceRoot;
        this.SessionName = SessionName;
    }

    public string Status { get; }

    public string StopReason { get; }

    public string Prompt { get; }

    public string? Plan { get; }

    public IReadOnlyList<string> Tools { get; }

    public IReadOnlyList<ChangedFileSummary> ChangedFiles { get; }

    public IReadOnlyList<AgentTaskCommandReport> Commands { get; }

    public IReadOnlyList<AgentTaskVerificationReport> Verification { get; }

    public IReadOnlyList<string> Risks { get; }

    public string? TracePath { get; }

    public IReadOnlyList<AgentTaskSecretPresence> Secrets { get; }

    public IReadOnlyList<AgentTaskReferenceReport> References { get; }

    public string? Summary { get; }

    public string? ErrorCode { get; }

    public AgentTaskReviewGateReport? ReviewGate { get; }

    public AgentTaskExpertReport? Expert { get; }

    public ExecReportMetadata? Report { get; }

    public string? WorkspaceRoot { get; }

    public string? SessionName { get; }

    public AgentTaskReport WithReportMetadata(ExecReportMetadata report)
    {
        ArgumentNullException.ThrowIfNull(report);

        return new AgentTaskReport(
            Status,
            StopReason,
            Prompt,
            Plan,
            Tools,
            ChangedFiles,
            Commands,
            Verification,
            Risks,
            TracePath,
            Secrets,
            References,
            Summary,
            ErrorCode,
            ReviewGate,
            Expert,
            report,
            WorkspaceRoot,
            SessionName);
    }
}

public sealed record AgentTaskCommandReport(
    string Source,
    string Command,
    string? WorkingDirectory = null,
    string? Status = null,
    string? ErrorCode = null);

public sealed record AgentTaskVerificationReport(
    string Status,
    string Source,
    string? Command,
    string? WorkingDirectory,
    bool Succeeded,
    string ApprovalStatus,
    string? ErrorCode,
    int? ExitCode,
    bool TimedOut,
    string Summary);

public sealed record AgentTaskReviewGateReport(
    string Status,
    string Summary,
    bool HasDiff,
    bool Truncated,
    string? ErrorCode = null);

public sealed record AgentTaskExpertReport(
    string Name,
    string DisplayName,
    string ToolBoundary,
    string BoundarySummary,
    string ReportFocus);

public sealed record AgentTaskSecretPresence(
    string Source,
    string Kind);

public sealed record AgentTaskReferenceReport(
    string Kind,
    string RequestedPath,
    string? ResolvedPath,
    string Status,
    int IncludedFileCount,
    int SkippedFileCount,
    long ByteCount,
    bool Truncated,
    IReadOnlyList<string> Warnings,
    string? ErrorCode = null);

public static class AgentTaskReportBuilder
{
    private const int MaxTextCharacters = 4096;
    private const int MaxItemCharacters = 1024;
    private const string ShellToolName = "workspace.run_shell";

    public static AgentTaskReport Build(
        AgentRunRequest request,
        AgentRunResult result,
        string? tracePath = null,
        AgentTaskReviewGateReport? reviewGate = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);

        SecretPresenceCollector secrets = new();
        string prompt = secrets.Sanitize(request.Prompt, "prompt") ?? string.Empty;
        string? plan = Bound(secrets.Sanitize(FindPlanSummary(result.Events), "plan"), MaxTextCharacters);
        string? summary = Bound(
            secrets.Sanitize(result.IsSuccess ? result.Text : result.Error?.SafeMessage, "summary"),
            MaxTextCharacters);

        IReadOnlyList<string> tools = result.ToolCalls
            .Select(toolCall => secrets.Sanitize(toolCall.ToolName, "tools") ?? string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        IReadOnlyList<ChangedFileSummary> changedFiles = result.ChangedFiles
            .Select(file => new ChangedFileSummary(
                Path: Bound(secrets.Sanitize(file.Path, "changedFiles"), MaxItemCharacters) ?? string.Empty,
                Status: Bound(secrets.Sanitize(file.Status, "changedFiles"), MaxItemCharacters) ?? string.Empty,
                SourceToolCallId: Bound(secrets.Sanitize(file.SourceToolCallId, "changedFiles"), MaxItemCharacters) ?? string.Empty,
                DiffStat: Bound(secrets.Sanitize(file.DiffStat, "changedFiles.diffStat"), MaxTextCharacters),
                DiffStatTruncated: file.DiffStatTruncated,
                ErrorCode: Bound(secrets.Sanitize(file.ErrorCode, "changedFiles"), MaxItemCharacters)))
            .ToArray();

        IReadOnlyList<AgentTaskCommandReport> commands = CreateCommandReports(result, secrets);
        IReadOnlyList<AgentTaskVerificationReport> verification = result.VerificationResults
            .Select(item => new AgentTaskVerificationReport(
                Status: Bound(secrets.Sanitize(item.Status, "verification"), MaxItemCharacters) ?? string.Empty,
                Source: Bound(secrets.Sanitize(item.Source, "verification"), MaxItemCharacters) ?? string.Empty,
                Command: Bound(secrets.Sanitize(item.Command, "verification.command"), MaxItemCharacters),
                WorkingDirectory: Bound(secrets.Sanitize(item.WorkingDirectory, "verification.cwd"), MaxItemCharacters),
                Succeeded: item.Succeeded,
                ApprovalStatus: Bound(secrets.Sanitize(item.ApprovalStatus, "verification"), MaxItemCharacters) ?? string.Empty,
                ErrorCode: Bound(secrets.Sanitize(item.ErrorCode, "verification"), MaxItemCharacters),
                ExitCode: item.ExitCode,
                TimedOut: item.TimedOut,
                Summary: Bound(secrets.Sanitize(item.Summary, "verification.summary"), MaxTextCharacters) ?? string.Empty))
            .ToArray();

        IReadOnlyList<string> risks = CreateRisks(result, verification, reviewGate, secrets);
        IReadOnlyList<AgentTaskReferenceReport> references = CreateReferenceReports(
            request.TaskContext?.References,
            secrets);

        AgentTaskReviewGateReport? safeReviewGate = reviewGate is null
            ? null
            : new AgentTaskReviewGateReport(
                Bound(secrets.Sanitize(reviewGate.Status, "reviewGate"), MaxItemCharacters) ?? string.Empty,
                Bound(secrets.Sanitize(reviewGate.Summary, "reviewGate.summary"), MaxTextCharacters) ?? string.Empty,
                reviewGate.HasDiff,
                reviewGate.Truncated,
                Bound(secrets.Sanitize(reviewGate.ErrorCode, "reviewGate"), MaxItemCharacters));

        return new AgentTaskReport(
            Status: result.Status,
            StopReason: result.StopReason,
            Prompt: Bound(prompt, MaxTextCharacters) ?? string.Empty,
            Plan: plan,
            Tools: tools,
            ChangedFiles: changedFiles,
            Commands: commands,
            Verification: verification,
            Risks: risks,
            TracePath: Bound(secrets.Sanitize(tracePath, "tracePath"), MaxItemCharacters),
            Secrets: secrets.ToPresenceList(),
            References: references,
            Summary: summary,
            ErrorCode: result.Error?.LocalErrorCode,
            ReviewGate: safeReviewGate,
            Expert: request.ExpertProfile?.ToReportMetadata(),
            WorkspaceRoot: Bound(secrets.Sanitize(request.Workspace.RootPath, "workspace"), MaxItemCharacters),
            SessionName: Bound(secrets.Sanitize(request.SessionName, "session"), MaxItemCharacters));
    }

    public static AgentRunEvent CreateTaskReportEvent(
        AgentTaskReport report,
        long sequence,
        DateTimeOffset timestampUtc)
    {
        ArgumentNullException.ThrowIfNull(report);

        Dictionary<string, string> payload = new(StringComparer.Ordinal)
        {
            ["status"] = report.Status,
            ["stopReason"] = report.StopReason,
            ["changedFileCount"] = report.ChangedFiles.Count.ToString(CultureInfo.InvariantCulture),
            ["changedFiles"] = string.Join(";", report.ChangedFiles.Select(file => file.Path)),
            ["commandCount"] = report.Commands.Count.ToString(CultureInfo.InvariantCulture),
            ["commands"] = string.Join(";", report.Commands.Select(command => command.Command)),
            ["verificationCount"] = report.Verification.Count.ToString(CultureInfo.InvariantCulture),
            ["verification"] = string.Join(";", report.Verification.Select(verification => verification.Status)),
            ["riskCount"] = report.Risks.Count.ToString(CultureInfo.InvariantCulture),
            ["risks"] = string.Join(" | ", report.Risks),
            ["referenceCount"] = report.References.Count.ToString(CultureInfo.InvariantCulture),
            ["referenceFileCount"] = report.References.Sum(reference => reference.IncludedFileCount).ToString(CultureInfo.InvariantCulture),
            ["referenceSkippedCount"] = report.References.Sum(reference => reference.SkippedFileCount).ToString(CultureInfo.InvariantCulture),
            ["secretPresenceCount"] = report.Secrets.Count.ToString(CultureInfo.InvariantCulture)
        };

        AddIfPresent(payload, "prompt", report.Prompt);
        AddIfPresent(payload, "plan", report.Plan);
        AddIfPresent(payload, "tools", string.Join(";", report.Tools));
        AddIfPresent(payload, "references", string.Join(";", report.References.Select(reference => reference.ResolvedPath ?? reference.RequestedPath)));
        AddIfPresent(payload, "tracePath", report.TracePath);
        AddIfPresent(payload, "summary", report.Summary);
        AddIfPresent(payload, "errorCode", report.ErrorCode);
        AddIfPresent(payload, "workspaceRoot", report.WorkspaceRoot);
        AddIfPresent(payload, "sessionName", report.SessionName);
        if (report.Expert is not null)
        {
            payload["expert"] = report.Expert.Name;
            payload["expertBoundary"] = report.Expert.ToolBoundary;
            payload["expertReportFocus"] = report.Expert.ReportFocus;
        }

        if (report.Report is not null)
        {
            payload["reportMode"] = report.Report.Mode;
            payload["reportGenerated"] = report.Report.Generated ? "true" : "false";
            AddIfPresent(payload, "reportPath", report.Report.Path);
            AddIfPresent(payload, "reportWriteStatus", report.Report.WriteStatus);
            AddIfPresent(payload, "reportErrorCode", report.Report.ErrorCode);
            AddIfPresent(payload, "reportSummary", report.Report.Summary);
        }

        if (report.ReviewGate is not null)
        {
            payload["reviewGateStatus"] = report.ReviewGate.Status;
            payload["reviewGateHasDiff"] = report.ReviewGate.HasDiff ? "true" : "false";
            payload["reviewGateTruncated"] = report.ReviewGate.Truncated ? "true" : "false";
            AddIfPresent(payload, "reviewGateSummary", report.ReviewGate.Summary);
            AddIfPresent(payload, "reviewGateErrorCode", report.ReviewGate.ErrorCode);
        }

        if (report.Secrets.Count > 0)
        {
            payload["secretPresence"] = string.Join(
                ";",
                report.Secrets.Select(secret => $"{secret.Source}:{secret.Kind}"));
        }

        return new AgentRunEvent(
            Type: "taskReport",
            Sequence: sequence,
            Timestamp: timestampUtc,
            Message: "Final agent task report recorded.",
            Summary: CreateSummary(report),
            Payload: payload,
            ErrorCode: report.ErrorCode,
            Status: report.Status,
            StopReason: report.StopReason);
    }

    public static IReadOnlyDictionary<string, object?> ToJsonPayload(AgentTaskReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        Dictionary<string, object?> payload = new(StringComparer.Ordinal)
        {
            ["status"] = report.Status,
            ["stopReason"] = report.StopReason,
            ["prompt"] = report.Prompt,
            ["plan"] = report.Plan,
            ["tools"] = report.Tools.ToArray(),
            ["changedFiles"] = report.ChangedFiles.Select(file => new Dictionary<string, object?>
            {
                ["path"] = file.Path,
                ["status"] = file.Status,
                ["sourceToolCallId"] = file.SourceToolCallId,
                ["diffStatTruncated"] = file.DiffStatTruncated,
                ["errorCode"] = file.ErrorCode
            }).ToArray(),
            ["commands"] = report.Commands.Select(command => new Dictionary<string, object?>
            {
                ["source"] = command.Source,
                ["command"] = command.Command,
                ["workingDirectory"] = command.WorkingDirectory,
                ["status"] = command.Status,
                ["errorCode"] = command.ErrorCode
            }).ToArray(),
            ["verification"] = report.Verification.Select(verification => new Dictionary<string, object?>
            {
                ["status"] = verification.Status,
                ["source"] = verification.Source,
                ["command"] = verification.Command,
                ["workingDirectory"] = verification.WorkingDirectory,
                ["succeeded"] = verification.Succeeded,
                ["approvalStatus"] = verification.ApprovalStatus,
                ["errorCode"] = verification.ErrorCode,
                ["exitCode"] = verification.ExitCode,
                ["timedOut"] = verification.TimedOut,
                ["summary"] = verification.Summary
            }).ToArray(),
            ["risks"] = report.Risks.ToArray(),
            ["references"] = report.References.Select(reference => new Dictionary<string, object?>
            {
                ["kind"] = reference.Kind,
                ["requestedPath"] = reference.RequestedPath,
                ["resolvedPath"] = reference.ResolvedPath,
                ["status"] = reference.Status,
                ["includedFileCount"] = reference.IncludedFileCount,
                ["skippedFileCount"] = reference.SkippedFileCount,
                ["byteCount"] = reference.ByteCount,
                ["truncated"] = reference.Truncated,
                ["warnings"] = reference.Warnings.ToArray(),
                ["errorCode"] = reference.ErrorCode
            }).ToArray(),
            ["tracePath"] = report.TracePath,
            ["workspaceRoot"] = report.WorkspaceRoot,
            ["sessionName"] = report.SessionName,
            ["secrets"] = report.Secrets.Select(secret => new Dictionary<string, object?>
            {
                ["source"] = secret.Source,
                ["kind"] = secret.Kind
            }).ToArray(),
            ["summary"] = report.Summary,
            ["errorCode"] = report.ErrorCode
        };

        if (report.Expert is not null)
        {
            payload["expert"] = new Dictionary<string, object?>
            {
                ["name"] = report.Expert.Name,
                ["displayName"] = report.Expert.DisplayName,
                ["toolBoundary"] = report.Expert.ToolBoundary,
                ["boundarySummary"] = report.Expert.BoundarySummary,
                ["reportFocus"] = report.Expert.ReportFocus
            };
        }

        if (report.Report is not null)
        {
            payload["report"] = new Dictionary<string, object?>
            {
                ["mode"] = report.Report.Mode,
                ["generated"] = report.Report.Generated,
                ["path"] = report.Report.Path,
                ["writeStatus"] = report.Report.WriteStatus,
                ["errorCode"] = report.Report.ErrorCode,
                ["summary"] = report.Report.Summary
            };
        }

        if (report.ReviewGate is not null)
        {
            payload["reviewGate"] = new Dictionary<string, object?>
            {
                ["status"] = report.ReviewGate.Status,
                ["summary"] = report.ReviewGate.Summary,
                ["hasDiff"] = report.ReviewGate.HasDiff,
                ["truncated"] = report.ReviewGate.Truncated,
                ["errorCode"] = report.ReviewGate.ErrorCode
            };
        }

        return payload;
    }

    public static string CreateSummary(AgentTaskReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        string changedFiles = report.ChangedFiles.Count == 0
            ? "none"
            : string.Join(",", report.ChangedFiles.Select(file => file.Path));
        string commands = report.Commands.Count == 0
            ? "none"
            : string.Join(",", report.Commands.Select(command => command.Command));
        string verification = report.Verification.Count == 0
            ? "none"
            : string.Join(",", report.Verification.Select(item => item.Status));
        string risks = report.Risks.Count == 0
            ? "none"
            : string.Join(" | ", report.Risks);
        string references = report.References.Count == 0
            ? "none"
            : string.Join(",", report.References.Select(reference => reference.ResolvedPath ?? reference.RequestedPath));

        string expert = report.Expert is null ? "none" : report.Expert.Name;
        string reportMode = report.Report is null ? "none" : report.Report.Mode;

        return $"status={report.Status} expert={expert} report={reportMode} references={references} changedFiles={changedFiles} commands={commands} verification={verification} risks={risks}";
    }

    private static string? FindPlanSummary(IReadOnlyList<AgentRunEvent> events)
    {
        return events.FirstOrDefault(agentEvent =>
            string.Equals(agentEvent.Type, "plan", StringComparison.Ordinal))?.Summary;
    }

    private static IReadOnlyList<AgentTaskCommandReport> CreateCommandReports(
        AgentRunResult result,
        SecretPresenceCollector secrets)
    {
        List<AgentTaskCommandReport> commands = [];
        foreach (ConversationToolCall toolCall in result.ToolCalls)
        {
            if (!string.Equals(toolCall.ToolName, ShellToolName, StringComparison.Ordinal))
            {
                continue;
            }

            if (TryReadJsonString(toolCall.ArgumentsJson, "command", out string? command) &&
                !string.IsNullOrWhiteSpace(command))
            {
                TryReadJsonString(toolCall.ArgumentsJson, "cwd", out string? cwd);
                commands.Add(new AgentTaskCommandReport(
                    Source: "tool",
                    Command: Bound(secrets.Sanitize(command, "commands.tool"), MaxItemCharacters) ?? string.Empty,
                    WorkingDirectory: Bound(secrets.Sanitize(cwd, "commands.cwd"), MaxItemCharacters),
                    Status: toolCall.Succeeded ? DiagnosticEventStatus.Success : DiagnosticEventStatus.Failure,
                    ErrorCode: Bound(secrets.Sanitize(toolCall.ErrorCode, "commands"), MaxItemCharacters)));
            }
        }

        foreach (VerificationResultSummary verification in result.VerificationResults)
        {
            if (string.IsNullOrWhiteSpace(verification.Command))
            {
                continue;
            }

            commands.Add(new AgentTaskCommandReport(
                Source: "verification",
                Command: Bound(secrets.Sanitize(verification.Command, "commands.verification"), MaxItemCharacters) ?? string.Empty,
                WorkingDirectory: Bound(secrets.Sanitize(verification.WorkingDirectory, "commands.cwd"), MaxItemCharacters),
                Status: Bound(secrets.Sanitize(verification.Status, "commands"), MaxItemCharacters),
                ErrorCode: Bound(secrets.Sanitize(verification.ErrorCode, "commands"), MaxItemCharacters)));
        }

        if (result.FailureSummary is not null)
        {
            foreach (string command in result.FailureSummary.Commands)
            {
                if (commands.Any(existing => string.Equals(existing.Command, command, StringComparison.Ordinal)))
                {
                    continue;
                }

                commands.Add(new AgentTaskCommandReport(
                    Source: "failure",
                    Command: Bound(secrets.Sanitize(command, "commands.failure"), MaxItemCharacters) ?? string.Empty));
            }
        }

        return commands
            .GroupBy(command => command.Command, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
    }

    private static IReadOnlyList<AgentTaskReferenceReport> CreateReferenceReports(
        WorkflowReferenceResolution? references,
        SecretPresenceCollector secrets)
    {
        references ??= WorkflowReferenceResolution.Empty;
        return references.References
            .Select(reference => new AgentTaskReferenceReport(
                Kind: Bound(secrets.Sanitize(reference.Kind, "references"), MaxItemCharacters) ?? string.Empty,
                RequestedPath: Bound(secrets.Sanitize(reference.RequestedPath, "references.path"), MaxItemCharacters) ?? string.Empty,
                ResolvedPath: Bound(secrets.Sanitize(reference.ResolvedPath, "references.path"), MaxItemCharacters),
                Status: Bound(secrets.Sanitize(reference.Status, "references"), MaxItemCharacters) ?? string.Empty,
                IncludedFileCount: reference.IncludedFileCount,
                SkippedFileCount: reference.SkippedFileCount,
                ByteCount: reference.ByteCount,
                Truncated: reference.Truncated,
                Warnings: reference.Warnings
                    .Select(warning => Bound(
                        secrets.Sanitize(
                            string.IsNullOrWhiteSpace(warning.Path)
                                ? warning.ErrorCode
                                : warning.ErrorCode + ":" + warning.Path,
                            "references.warning"),
                        MaxItemCharacters) ?? string.Empty)
                    .Where(warning => !string.IsNullOrWhiteSpace(warning))
                    .ToArray(),
                ErrorCode: Bound(secrets.Sanitize(reference.ErrorCode, "references"), MaxItemCharacters)))
            .ToArray();
    }

    private static IReadOnlyList<string> CreateRisks(
        AgentRunResult result,
        IReadOnlyList<AgentTaskVerificationReport> verification,
        AgentTaskReviewGateReport? reviewGate,
        SecretPresenceCollector secrets)
    {
        List<string> risks = [];
        AgentRunEvent? plan = result.Events.FirstOrDefault(agentEvent =>
            string.Equals(agentEvent.Type, "plan", StringComparison.Ordinal));
        if (plan?.Payload is not null && plan.Payload.TryGetValue("risks", out string? planRisks))
        {
            AddSplitRisks(risks, secrets.Sanitize(planRisks, "risks.plan"));
        }

        if (result.FailureSummary is not null)
        {
            AddDistinct(risks, secrets.Sanitize(result.FailureSummary.RemainingRisk, "risks.failure"));
        }
        else if (!result.IsSuccess && result.Error is not null)
        {
            AddDistinct(risks, secrets.Sanitize(result.Error.SafeMessage, "risks.error"));
        }

        if (result.ChangedFiles.Count > 0 && verification.Count == 0)
        {
            AddDistinct(risks, "No verification result was recorded for the changed files.");
        }

        foreach (AgentTaskVerificationReport item in verification.Where(item => !item.Succeeded && item.Status != "skipped"))
        {
            AddDistinct(risks, "Verification did not succeed: " + item.Status);
        }

        if (reviewGate is not null)
        {
            if (!string.Equals(reviewGate.Status, DiagnosticEventStatus.Success, StringComparison.Ordinal))
            {
                AddDistinct(risks, "Review gate did not complete cleanly: " + reviewGate.Status);
            }

            if (reviewGate.Truncated)
            {
                AddDistinct(risks, "Review gate diff summary was truncated.");
            }
        }

        return risks
            .Where(risk => !string.IsNullOrWhiteSpace(risk))
            .Select(risk => Bound(risk, MaxItemCharacters) ?? string.Empty)
            .ToArray();
    }

    private static void AddSplitRisks(List<string> risks, string? rawRisks)
    {
        if (string.IsNullOrWhiteSpace(rawRisks))
        {
            return;
        }

        foreach (string risk in rawRisks.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            AddDistinct(risks, risk);
        }
    }

    private static void AddDistinct(List<string> values, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!values.Contains(value, StringComparer.Ordinal))
        {
            values.Add(value);
        }
    }

    private static void AddIfPresent(Dictionary<string, string> payload, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            payload[key] = value;
        }
    }

    private static bool TryReadJsonString(string json, string propertyName, out string? value)
    {
        value = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty(propertyName, out JsonElement element) ||
                element.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            value = element.GetString();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? Bound(string? value, int maxCharacters)
    {
        if (value is null)
        {
            return null;
        }

        return value.Length <= maxCharacters
            ? value
            : value[..maxCharacters];
    }

    private sealed class SecretPresenceCollector
    {
        private const string SecretMarker = "[secret-present]";
        private const string SecretKeyNamePattern =
            @"(?:[A-Za-z0-9]+[_-]+)*(?:api[_-]?key|access[_-]?token|refresh[_-]?token|token|client[_-]?secret|secret[_-]?access[_-]?key|password|secret)" +
            "|apiKey|accessToken|refreshToken|clientSecret|awsSecretAccessKey";

        private static readonly Regex JsonSecretPattern = new(
            $$"""("(?i:{{SecretKeyNamePattern}})"\s*:\s*")(?:\\.|[^"\\])*(")""",
            RegexOptions.CultureInvariant);
        private static readonly Regex EscapedJsonSecretPattern = new(
            $$"""(\\"(?i:{{SecretKeyNamePattern}})\\"\s*:\s*\\")(?:\\\\\\"|\\\\.|\\(?!")|[^"\\])*(\\")""",
            RegexOptions.CultureInvariant);
        private static readonly Regex KeyValueSecretPattern = new(
            $$"""\b(?i:{{SecretKeyNamePattern}})\b(\s*[:=]\s*)(?:"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|Bearer\s+[A-Za-z0-9._~+/=-]+|[^\s,;]+)""",
            RegexOptions.CultureInvariant);
        private static readonly Regex EscapedKeyValueSecretPattern = new(
            $$"""\b(?i:{{SecretKeyNamePattern}})\b(\s*[:=]\s*)(?:\\"(?:\\\\\\"|\\\\.|\\(?!")|[^"\\])*\\"|\\'(?:\\\\\\'|\\\\.|\\(?!')|[^'\\])*\\')""",
            RegexOptions.CultureInvariant);
        private static readonly Regex SecretOptionPattern = new(
            $$"""(?<![A-Za-z0-9_-])(-{1,2}(?i:{{SecretKeyNamePattern}})(?![A-Za-z0-9_-])(?:[=\s]+))(?:"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|Bearer\s+[A-Za-z0-9._~+/=-]+|[^\s,;]+)""",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex BearerTokenPattern = new(
            @"\bBearer\s+[A-Za-z0-9._~+/=-]+",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex OpenAiKeyPattern = new(
            @"\bsk-[A-Za-z0-9._-]+",
            RegexOptions.CultureInvariant);
        private static readonly Regex GitHubTokenPattern = new(
            @"\b(?:gh[pousr]_|github_pat_)[A-Za-z0-9_]+",
            RegexOptions.CultureInvariant);

        private readonly List<AgentTaskSecretPresence> secrets = [];

        public string? Sanitize(string? value, string source)
        {
            if (value is null)
            {
                return null;
            }

            string sanitized = EscapedJsonSecretPattern.Replace(value, match =>
            {
                Add(source, "escaped-json");
                return match.Groups[1].Value + SecretMarker + match.Groups[2].Value;
            });
            sanitized = JsonSecretPattern.Replace(sanitized, match =>
            {
                Add(source, "json");
                return match.Groups[1].Value + SecretMarker + match.Groups[2].Value;
            });
            sanitized = RedactKeyValue(EscapedKeyValueSecretPattern, sanitized, source, "escaped-key-value");
            sanitized = RedactKeyValue(KeyValueSecretPattern, sanitized, source, "key-value");
            sanitized = RedactOptionSecret(sanitized, source);
            sanitized = BearerTokenPattern.Replace(sanitized, _ =>
            {
                Add(source, "bearer");
                return "Bearer " + SecretMarker;
            });
            sanitized = OpenAiKeyPattern.Replace(sanitized, _ =>
            {
                Add(source, "openai-key");
                return SecretMarker;
            });
            sanitized = GitHubTokenPattern.Replace(sanitized, _ =>
            {
                Add(source, "github-token");
                return SecretMarker;
            });

            return sanitized;
        }

        public IReadOnlyList<AgentTaskSecretPresence> ToPresenceList()
        {
            return secrets
                .Distinct()
                .OrderBy(secret => secret.Source, StringComparer.Ordinal)
                .ThenBy(secret => secret.Kind, StringComparer.Ordinal)
                .ToArray();
        }

        private string RedactKeyValue(Regex pattern, string value, string source, string kind)
        {
            return pattern.Replace(value, match =>
            {
                Group separator = match.Groups[1];
                if (!separator.Success)
                {
                    return match.Value;
                }

                Add(source, kind);
                int prefixLength = separator.Index - match.Index;
                return match.Value[..prefixLength] + separator.Value + SecretMarker;
            });
        }

        private string RedactOptionSecret(string value, string source)
        {
            return SecretOptionPattern.Replace(value, match =>
            {
                Group prefix = match.Groups[1];
                if (!prefix.Success)
                {
                    return match.Value;
                }

                Add(source, "cli-option");
                return prefix.Value + SecretMarker;
            });
        }

        private void Add(string source, string kind)
        {
            AgentTaskSecretPresence presence = new(source, kind);
            if (!secrets.Contains(presence))
            {
                secrets.Add(presence);
            }
        }
    }
}
