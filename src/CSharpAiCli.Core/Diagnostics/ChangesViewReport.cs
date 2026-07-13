using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;

namespace CSharpAiCli.Core;

public sealed record ChangesViewChangedFile(
    string Path,
    string Status);

public sealed record ChangesViewSession(
    string Source,
    string? Name,
    string? Path,
    AgentTaskReport? TaskReport);

public sealed record ChangesViewReport
{
    public ChangesViewReport(
        string Status,
        int ExitCode,
        string WorkspaceRoot,
        string GitStatusSummary,
        bool GitStatusSucceeded,
        string? GitStatusErrorCode,
        bool Dirty,
        string DiffStatSummary,
        bool DiffSucceeded,
        string? DiffErrorCode,
        bool DiffTruncated,
        IReadOnlyList<ChangesViewChangedFile>? ChangedFiles = null,
        ChangesViewSession? Session = null,
        IReadOnlyList<string>? Warnings = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Status);
        ArgumentException.ThrowIfNullOrWhiteSpace(WorkspaceRoot);

        this.Status = Status;
        this.ExitCode = ExitCode;
        this.WorkspaceRoot = WorkspaceRoot;
        this.GitStatusSummary = GitStatusSummary ?? string.Empty;
        this.GitStatusSucceeded = GitStatusSucceeded;
        this.GitStatusErrorCode = GitStatusErrorCode;
        this.Dirty = Dirty;
        this.DiffStatSummary = DiffStatSummary ?? string.Empty;
        this.DiffSucceeded = DiffSucceeded;
        this.DiffErrorCode = DiffErrorCode;
        this.DiffTruncated = DiffTruncated;
        this.ChangedFiles = new ReadOnlyCollection<ChangesViewChangedFile>((ChangedFiles ?? []).ToArray());
        this.Session = Session;
        this.Warnings = new ReadOnlyCollection<string>((Warnings ?? []).ToArray());
    }

    public string Status { get; }

    public int ExitCode { get; }

    public string WorkspaceRoot { get; }

    public string GitStatusSummary { get; }

    public bool GitStatusSucceeded { get; }

    public string? GitStatusErrorCode { get; }

    public bool Dirty { get; }

    public string DiffStatSummary { get; }

    public bool DiffSucceeded { get; }

    public string? DiffErrorCode { get; }

    public bool DiffTruncated { get; }

    public IReadOnlyList<ChangesViewChangedFile> ChangedFiles { get; }

    public ChangesViewSession? Session { get; }

    public IReadOnlyList<string> Warnings { get; }

    public string Summary
    {
        get
        {
            string changes = ChangedFiles.Count == 0
                ? "no changed files"
                : ChangedFiles.Count.ToString(CultureInfo.InvariantCulture) + " changed file(s)";
            return $"{Status}: {changes}";
        }
    }

    public static ChangesViewReport Create(
        WorkspaceContext workspace,
        ToolExecutionResult gitStatus,
        ToolExecutionResult gitDiffStat,
        ConversationTranscript? transcript = null,
        string? sessionName = null,
        string? sessionPath = null,
        string? sessionWarning = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(gitStatus);
        ArgumentNullException.ThrowIfNull(gitDiffStat);

        List<string> warnings = [];
        bool nonGit = string.Equals(gitStatus.ErrorCode, ToolErrorCode.GitNotRepository, StringComparison.Ordinal) ||
            string.Equals(gitDiffStat.ErrorCode, ToolErrorCode.GitNotRepository, StringComparison.Ordinal);
        if (nonGit)
        {
            warnings.Add("Workspace is not a git repository.");
        }

        if (gitDiffStat.Succeeded && ReadBool(gitDiffStat.StructuredPayload, "truncated"))
        {
            warnings.Add("Git diff stat output was truncated.");
        }

        if (!string.IsNullOrWhiteSpace(sessionWarning))
        {
            warnings.Add(sessionWarning!);
        }

        ChangesViewSession? session = CreateSessionReport(transcript, sessionName, sessionPath, warnings);
        IReadOnlyList<ChangesViewChangedFile> changedFiles = ParseChangedFiles(gitStatus.Summary);
        bool dirty = gitStatus.Succeeded && !IsCleanStatus(gitStatus.Summary);
        bool hardGitFailure = !gitStatus.Succeeded && !nonGit;
        string status = hardGitFailure
            ? "failed"
            : warnings.Count > 0
                ? "warning"
                : dirty
                    ? "dirty"
                    : "clean";

        return new ChangesViewReport(
            Status: status,
            ExitCode: hardGitFailure ? 1 : 0,
            WorkspaceRoot: workspace.RootPath,
            GitStatusSummary: gitStatus.Summary,
            GitStatusSucceeded: gitStatus.Succeeded,
            GitStatusErrorCode: gitStatus.ErrorCode,
            Dirty: dirty,
            DiffStatSummary: gitDiffStat.Summary,
            DiffSucceeded: gitDiffStat.Succeeded,
            DiffErrorCode: gitDiffStat.ErrorCode,
            DiffTruncated: ReadBool(gitDiffStat.StructuredPayload, "truncated"),
            ChangedFiles: changedFiles,
            Session: session,
            Warnings: warnings);
    }

    private static ChangesViewSession? CreateSessionReport(
        ConversationTranscript? transcript,
        string? sessionName,
        string? sessionPath,
        List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(sessionName))
        {
            return new ChangesViewSession("none", null, null, null);
        }

        if (transcript is null)
        {
            warnings.Add("Session transcript was not found.");
            return new ChangesViewSession("missing", sessionName, sessionPath, null);
        }

        ConversationAgentRun? lastRun = transcript.AgentRuns.LastOrDefault();
        if (lastRun?.TaskReport is null)
        {
            warnings.Add("Session transcript does not contain an agent task report.");
            return new ChangesViewSession("session:" + sessionName, sessionName, sessionPath, null);
        }

        return new ChangesViewSession("session:" + sessionName, sessionName, sessionPath, lastRun.TaskReport);
    }

    private static IReadOnlyList<ChangesViewChangedFile> ParseChangedFiles(string statusSummary)
    {
        if (IsCleanStatus(statusSummary) || string.IsNullOrWhiteSpace(statusSummary))
        {
            return [];
        }

        List<ChangesViewChangedFile> files = [];
        foreach (string rawLine in statusSummary.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (rawLine.Length < 3)
            {
                continue;
            }

            string status = rawLine[..2].Trim();
            string path = rawLine[2..].Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            files.Add(new ChangesViewChangedFile(path, status));
        }

        return files;
    }

    private static bool IsCleanStatus(string summary)
    {
        return string.Equals(summary.Trim(), "working tree clean", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ReadBool(
        IReadOnlyDictionary<string, JsonElement>? structuredPayload,
        string key)
    {
        if (structuredPayload is null ||
            !structuredPayload.TryGetValue(key, out JsonElement value))
        {
            return false;
        }

        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean();
        }

        return value.ValueKind == JsonValueKind.String &&
            bool.TryParse(value.GetString(), out bool parsed) &&
            parsed;
    }
}

public sealed class ChangesTextRenderer
{
    private readonly TextWriter writer;

    public ChangesTextRenderer(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        this.writer = writer;
    }

    public void Write(ChangesViewReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        writer.WriteLine("C# AI CLI changes");
        writer.WriteLine($"status: {report.Status}");
        writer.WriteLine($"workspace: {Safe(report.WorkspaceRoot)}");
        writer.WriteLine($"dirty: {(report.Dirty ? "true" : "false")}");
        writer.WriteLine($"gitStatusSucceeded: {(report.GitStatusSucceeded ? "true" : "false")}");
        if (!string.IsNullOrWhiteSpace(report.GitStatusErrorCode))
        {
            writer.WriteLine($"gitStatusErrorCode: {report.GitStatusErrorCode}");
        }

        writer.WriteLine("gitStatus:");
        writer.WriteLine(Safe(report.GitStatusSummary));
        writer.WriteLine($"gitDiffSucceeded: {(report.DiffSucceeded ? "true" : "false")}");
        if (!string.IsNullOrWhiteSpace(report.DiffErrorCode))
        {
            writer.WriteLine($"gitDiffErrorCode: {report.DiffErrorCode}");
        }

        writer.WriteLine($"gitDiffTruncated: {(report.DiffTruncated ? "true" : "false")}");
        writer.WriteLine("diffStat:");
        writer.WriteLine(Safe(report.DiffStatSummary));
        writer.WriteLine("changedFiles:");
        if (report.ChangedFiles.Count == 0)
        {
            writer.WriteLine("- none");
        }
        else
        {
            foreach (ChangesViewChangedFile file in report.ChangedFiles)
            {
                writer.WriteLine($"- {Safe(file.Path)} status={Safe(file.Status)}");
            }
        }

        WriteSession(report.Session);
        WriteWarnings(report.Warnings);
    }

    private void WriteSession(ChangesViewSession? session)
    {
        if (session is null)
        {
            return;
        }

        writer.WriteLine($"taskReportSource: {Safe(session.Source)}");
        if (!string.IsNullOrWhiteSpace(session.Name))
        {
            writer.WriteLine($"session: {Safe(session.Name)}");
        }

        if (!string.IsNullOrWhiteSpace(session.Path))
        {
            writer.WriteLine($"sessionPath: {Safe(session.Path)}");
        }

        AgentTaskReport? taskReport = session.TaskReport;
        if (taskReport is null)
        {
            return;
        }

        writer.WriteLine($"taskReportStatus: {Safe(taskReport.Status)}");
        writer.WriteLine($"stopReason: {Safe(taskReport.StopReason)}");
        writer.WriteLine("commands: " + (taskReport.Commands.Count == 0
            ? "none"
            : Safe(string.Join(",", taskReport.Commands.Select(command => command.Command)))));
        writer.WriteLine("verification: " + (taskReport.Verification.Count == 0
            ? "none"
            : Safe(string.Join(",", taskReport.Verification.Select(verification => verification.Status)))));
        writer.WriteLine("remainingRisks: " + (taskReport.Risks.Count == 0
            ? "none"
            : Safe(string.Join(" | ", taskReport.Risks))));
        writer.WriteLine("references: " + (taskReport.References.Count == 0
            ? "none"
            : Safe(string.Join(",", taskReport.References.Select(reference => reference.ResolvedPath ?? reference.RequestedPath)))));
        if (!string.IsNullOrWhiteSpace(taskReport.TracePath))
        {
            writer.WriteLine($"tracePath: {Safe(taskReport.TracePath)}");
        }
    }

    private void WriteWarnings(IReadOnlyList<string> warnings)
    {
        if (warnings.Count == 0)
        {
            return;
        }

        writer.WriteLine("warnings:");
        foreach (string warning in warnings)
        {
            writer.WriteLine("- " + Safe(warning));
        }
    }

    private static string Safe(string value)
    {
        return DiagnosticSecretRedactor.Redact(value ?? string.Empty);
    }
}

public sealed class ChangesJsonRenderer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TextWriter writer;

    public ChangesJsonRenderer(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        this.writer = writer;
    }

    public void Write(ChangesViewReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        writer.WriteLine(JsonSerializer.Serialize(ToJson(report), JsonOptions));
    }

    private static IReadOnlyDictionary<string, object?> ToJson(ChangesViewReport report)
    {
        return new Dictionary<string, object?>
        {
            ["type"] = "changes.view",
            ["status"] = Safe(report.Status),
            ["workspace"] = Safe(report.WorkspaceRoot),
            ["git"] = new Dictionary<string, object?>
            {
                ["statusSucceeded"] = report.GitStatusSucceeded,
                ["statusErrorCode"] = SafeOrNull(report.GitStatusErrorCode),
                ["statusSummary"] = Safe(report.GitStatusSummary),
                ["dirty"] = report.Dirty,
                ["diffSucceeded"] = report.DiffSucceeded,
                ["diffErrorCode"] = SafeOrNull(report.DiffErrorCode),
                ["diffStatSummary"] = Safe(report.DiffStatSummary),
                ["diffTruncated"] = report.DiffTruncated
            },
            ["changedFiles"] = report.ChangedFiles.Select(file => new Dictionary<string, object?>
            {
                ["path"] = Safe(file.Path),
                ["status"] = Safe(file.Status)
            }).ToArray(),
            ["taskReport"] = CreateTaskReportJson(report.Session),
            ["session"] = CreateSessionJson(report.Session),
            ["warnings"] = report.Warnings.Select(Safe).ToArray()
        };
    }

    private static IReadOnlyDictionary<string, object?>? CreateTaskReportJson(ChangesViewSession? session)
    {
        if (session?.TaskReport is null)
        {
            return null;
        }

        Dictionary<string, object?> payload = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, object?> pair in AgentTaskReportBuilder.ToJsonPayload(session.TaskReport))
        {
            payload[Safe(pair.Key)] = SafeJsonValue(pair.Value);
        }

        return payload;
    }

    private static IReadOnlyDictionary<string, object?>? CreateSessionJson(ChangesViewSession? session)
    {
        if (session is null)
        {
            return null;
        }

        return new Dictionary<string, object?>
        {
            ["source"] = Safe(session.Source),
            ["name"] = SafeOrNull(session.Name),
            ["path"] = SafeOrNull(session.Path),
            ["hasTaskReport"] = session.TaskReport is not null
        };
    }

    private static object? SafeJsonValue(object? value)
    {
        return value switch
        {
            null => null,
            string text => Safe(text),
            Dictionary<string, object?> dictionary => dictionary.ToDictionary(
                pair => Safe(pair.Key),
                pair => SafeJsonValue(pair.Value),
                StringComparer.Ordinal),
            IReadOnlyDictionary<string, object?> dictionary => dictionary.ToDictionary(
                pair => Safe(pair.Key),
                pair => SafeJsonValue(pair.Value),
                StringComparer.Ordinal),
            IEnumerable<Dictionary<string, object?>> dictionaries => dictionaries
                .Select(dictionary => SafeJsonValue(dictionary))
                .ToArray(),
            IEnumerable<IReadOnlyDictionary<string, object?>> dictionaries => dictionaries
                .Select(dictionary => SafeJsonValue(dictionary))
                .ToArray(),
            IEnumerable<string> strings => strings.Select(Safe).ToArray(),
            System.Collections.IEnumerable values when value is not string => values
                .Cast<object?>()
                .Select(SafeJsonValue)
                .ToArray(),
            _ => value
        };
    }

    private static string Safe(string value)
    {
        return DiagnosticSecretRedactor.Redact(value ?? string.Empty);
    }

    private static string? SafeOrNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : Safe(value);
    }
}
