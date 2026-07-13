using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CSharpAiCli.Core;

public static class JobStatus
{
    public const string Planned = "planned";
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Canceled = "canceled";
    public const string ApprovalRequired = "approval-required";
    public const string DryRun = "dry-run";
}

public static class JobArtifactKind
{
    public const string TaskReport = "task-report";
    public const string MarkdownReport = "markdown-report";
    public const string Trace = "trace";
    public const string Session = "session";
    public const string SkillPlan = "skill-plan";
}

public sealed record JobSkillSummary
{
    public JobSkillSummary(
        string Name,
        string Version,
        string SourceKind,
        string? SourcePath,
        string EntryMode,
        string Expert,
        string Report,
        string SafetySummary,
        string? ValidationCommand,
        IReadOnlyList<string>? SuggestedReferences)
    {
        this.Name = Safe(Name);
        this.Version = Safe(Version);
        this.SourceKind = Safe(SourceKind);
        this.SourcePath = SafeOrNull(SourcePath);
        this.EntryMode = Safe(EntryMode);
        this.Expert = Safe(Expert);
        this.Report = Safe(Report);
        this.SafetySummary = Safe(SafetySummary);
        this.ValidationCommand = SafeOrNull(ValidationCommand);
        this.SuggestedReferences = new ReadOnlyCollection<string>(
            (SuggestedReferences ?? []).Take(64).Select(Safe).ToArray());
    }

    public string Name { get; }

    public string Version { get; }

    public string SourceKind { get; }

    public string? SourcePath { get; }

    public string EntryMode { get; }

    public string Expert { get; }

    public string Report { get; }

    public string SafetySummary { get; }

    public string? ValidationCommand { get; }

    public IReadOnlyList<string> SuggestedReferences { get; }

    public static JobSkillSummary FromMetadata(SkillRunMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        return new JobSkillSummary(
            metadata.Name,
            metadata.Version,
            metadata.SourceKind,
            metadata.SourcePath,
            metadata.EntryMode,
            metadata.Expert,
            metadata.Report,
            metadata.SafetySummary,
            metadata.ValidationCommand,
            metadata.SuggestedReferences);
    }

    private static string Safe(string? value)
    {
        string safe = DiagnosticSecretRedactor.Redact(value ?? string.Empty);
        const int maxLength = 1024;
        return safe.Length <= maxLength ? safe : safe[..maxLength];
    }

    private static string? SafeOrNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : Safe(value);
    }
}

public sealed record JobCommandSummary
{
    public JobCommandSummary(
        string Family,
        string? Task = null,
        string? Name = null,
        string? WorkspaceRoot = null,
        string? Cwd = null,
        string? Skill = null,
        string? Expert = null,
        string? ReportMode = null,
        string? OutputMode = null,
        bool? DryRun = null,
        JobSkillSummary? SkillMetadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Family);

        this.Family = Safe(Family);
        this.Task = SafeOrNull(Task);
        this.Name = SafeOrNull(Name);
        this.WorkspaceRoot = SafeOrNull(WorkspaceRoot);
        this.Cwd = SafeOrNull(Cwd);
        this.Skill = SafeOrNull(Skill);
        this.Expert = SafeOrNull(Expert);
        this.ReportMode = SafeOrNull(ReportMode);
        this.OutputMode = SafeOrNull(OutputMode);
        this.DryRun = DryRun;
        this.SkillMetadata = SkillMetadata;
    }

    public string Family { get; }

    public string? Task { get; }

    public string? Name { get; }

    public string? WorkspaceRoot { get; }

    public string? Cwd { get; }

    public string? Skill { get; }

    public string? Expert { get; }

    public string? ReportMode { get; }

    public string? OutputMode { get; }

    public bool? DryRun { get; }

    public JobSkillSummary? SkillMetadata { get; }

    private static string Safe(string value)
    {
        return Bound(DiagnosticSecretRedactor.Redact(value ?? string.Empty));
    }

    private static string? SafeOrNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : Safe(value);
    }

    private static string Bound(string value)
    {
        const int maxLength = 4096;
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}

public sealed record JobArtifact
{
    public JobArtifact(
        string Kind,
        string Path,
        bool Exists,
        string? Summary = null,
        string? Sha256 = null,
        DateTimeOffset? CreatedAtUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(Path);

        this.Kind = DiagnosticSecretRedactor.Redact(Kind);
        this.Path = DiagnosticSecretRedactor.Redact(Path);
        this.Exists = Exists;
        this.Summary = SafeOrNull(Summary);
        this.Sha256 = SafeOrNull(Sha256);
        this.CreatedAtUtc = CreatedAtUtc;
    }

    public string Kind { get; }

    public string Path { get; }

    public bool Exists { get; }

    public string? Summary { get; }

    public string? Sha256 { get; }

    public DateTimeOffset? CreatedAtUtc { get; }

    public static JobArtifact FromPath(string kind, string? path, string? summary = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);

        string safePath = string.IsNullOrWhiteSpace(path) ? "none" : path;
        bool exists = false;
        DateTimeOffset? createdAtUtc = null;
        string? sha256 = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                exists = true;
                createdAtUtc = File.GetCreationTimeUtc(path);
                sha256 = ComputeSha256(path);
            }
        }
        catch (Exception exception) when (IsBestEffortArtifactException(exception))
        {
            exists = false;
        }

        return new JobArtifact(kind, safePath, exists, summary, sha256, createdAtUtc);
    }

    private static string? SafeOrNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : DiagnosticSecretRedactor.Redact(value);
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }

    private static bool IsBestEffortArtifactException(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException;
    }
}

public sealed record JobTaskReportSummary
{
    public JobTaskReportSummary(
        string Status,
        string StopReason,
        string? Summary,
        string? ErrorCode,
        int ChangedFileCount,
        int CommandCount,
        int VerificationCount,
        int RiskCount,
        int ReferenceCount,
        int SecretPresenceCount,
        IReadOnlyList<string>? ChangedFiles = null,
        IReadOnlyList<string>? VerificationStatuses = null,
        IReadOnlyList<string>? Risks = null,
        IReadOnlyList<string>? Commands = null,
        IReadOnlyList<string>? Verification = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Status);
        ArgumentException.ThrowIfNullOrWhiteSpace(StopReason);

        this.Status = Safe(Status);
        this.StopReason = Safe(StopReason);
        this.Summary = SafeOrNull(Summary);
        this.ErrorCode = SafeOrNull(ErrorCode);
        this.ChangedFileCount = ChangedFileCount;
        this.CommandCount = CommandCount;
        this.VerificationCount = VerificationCount;
        this.RiskCount = RiskCount;
        this.ReferenceCount = ReferenceCount;
        this.SecretPresenceCount = SecretPresenceCount;
        this.ChangedFiles = new ReadOnlyCollection<string>((ChangedFiles ?? []).Select(Safe).ToArray());
        this.VerificationStatuses = new ReadOnlyCollection<string>((VerificationStatuses ?? []).Select(Safe).ToArray());
        this.Risks = new ReadOnlyCollection<string>((Risks ?? []).Select(Safe).ToArray());
        this.Commands = new ReadOnlyCollection<string>((Commands ?? []).Select(Safe).ToArray());
        this.Verification = new ReadOnlyCollection<string>((Verification ?? []).Select(Safe).ToArray());
    }

    public string Status { get; }

    public string StopReason { get; }

    public string? Summary { get; }

    public string? ErrorCode { get; }

    public int ChangedFileCount { get; }

    public int CommandCount { get; }

    public int VerificationCount { get; }

    public int RiskCount { get; }

    public int ReferenceCount { get; }

    public int SecretPresenceCount { get; }

    public IReadOnlyList<string> ChangedFiles { get; }

    public IReadOnlyList<string> VerificationStatuses { get; }

    public IReadOnlyList<string> Risks { get; }

    public IReadOnlyList<string> Commands { get; }

    public IReadOnlyList<string> Verification { get; }

    public static JobTaskReportSummary FromTaskReport(AgentTaskReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        return new JobTaskReportSummary(
            report.Status,
            report.StopReason,
            report.Summary,
            report.ErrorCode,
            report.ChangedFiles.Count,
            report.Commands.Count,
            report.Verification.Count,
            report.Risks.Count,
            report.References.Count,
            report.Secrets.Count,
            report.ChangedFiles.Select(file => $"{file.Path} status={file.Status}").ToArray(),
            report.Verification.Select(verification => verification.Status).ToArray(),
            report.Risks.ToArray(),
            report.Commands.Select(command =>
                $"source={command.Source} command={command.Command}" +
                (string.IsNullOrWhiteSpace(command.WorkingDirectory) ? string.Empty : $" cwd={command.WorkingDirectory}") +
                (string.IsNullOrWhiteSpace(command.Status) ? string.Empty : $" status={command.Status}") +
                (string.IsNullOrWhiteSpace(command.ErrorCode) ? string.Empty : $" errorCode={command.ErrorCode}")).ToArray(),
            report.Verification.Select(verification =>
                $"status={verification.Status} source={verification.Source}" +
                (string.IsNullOrWhiteSpace(verification.Command) ? string.Empty : $" command={verification.Command}") +
                $" succeeded={verification.Succeeded.ToString().ToLowerInvariant()}" +
                (string.IsNullOrWhiteSpace(verification.ErrorCode) ? string.Empty : $" errorCode={verification.ErrorCode}")).ToArray());
    }

    private static string Safe(string value)
    {
        string safe = DiagnosticSecretRedactor.Redact(value ?? string.Empty);
        const int maxLength = 1024;
        return safe.Length <= maxLength ? safe : safe[..maxLength];
    }

    private static string? SafeOrNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : Safe(value);
    }
}

public sealed record JobRedactionSummary(
    bool SecretsRedacted,
    bool RawReferencesStored,
    bool RawToolArgumentsStored,
    bool FullDiffStored,
    string Policy);

public sealed record JobRecord
{
    public const int CurrentSchemaVersion = 1;

    public JobRecord(
        int SchemaVersion,
        string JobId,
        string Status,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc,
        JobCommandSummary Command,
        string? JobName = null,
        DateTimeOffset? StartedAtUtc = null,
        DateTimeOffset? CompletedAtUtc = null,
        int? ExitCode = null,
        string? StopReason = null,
        string? ErrorCode = null,
        string? Summary = null,
        JobTaskReportSummary? TaskReport = null,
        IReadOnlyList<JobArtifact>? Artifacts = null,
        IReadOnlyList<string>? Warnings = null,
        JobRedactionSummary? Redaction = null)
    {
        if (SchemaVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(SchemaVersion));
        }

        if (!JobIdGenerator.IsValid(JobId))
        {
            throw new ArgumentException("Job id format is invalid.", nameof(JobId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(Status);
        ArgumentNullException.ThrowIfNull(Command);

        this.SchemaVersion = SchemaVersion;
        this.JobId = JobId;
        this.Status = Safe(Status);
        this.CreatedAtUtc = CreatedAtUtc;
        this.UpdatedAtUtc = UpdatedAtUtc;
        this.Command = Command;
        this.JobName = SafeOrNull(JobName);
        this.StartedAtUtc = StartedAtUtc;
        this.CompletedAtUtc = CompletedAtUtc;
        this.ExitCode = ExitCode;
        this.StopReason = SafeOrNull(StopReason);
        this.ErrorCode = SafeOrNull(ErrorCode);
        this.Summary = SafeOrNull(Summary);
        this.TaskReport = TaskReport;
        this.Artifacts = new ReadOnlyCollection<JobArtifact>((Artifacts ?? []).ToArray());
        this.Warnings = new ReadOnlyCollection<string>((Warnings ?? []).Select(Safe).ToArray());
        this.Redaction = Redaction ?? DefaultRedaction;
    }

    public int SchemaVersion { get; }

    public string JobId { get; }

    public string Status { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; }

    public JobCommandSummary Command { get; }

    public string? JobName { get; }

    public DateTimeOffset? StartedAtUtc { get; }

    public DateTimeOffset? CompletedAtUtc { get; }

    public int? ExitCode { get; }

    public string? StopReason { get; }

    public string? ErrorCode { get; }

    public string? Summary { get; }

    public JobTaskReportSummary? TaskReport { get; }

    public IReadOnlyList<JobArtifact> Artifacts { get; }

    public IReadOnlyList<string> Warnings { get; }

    public JobRedactionSummary Redaction { get; }

    public static JobRedactionSummary DefaultRedaction { get; } = new(
        SecretsRedacted: true,
        RawReferencesStored: false,
        RawToolArgumentsStored: false,
        FullDiffStored: false,
        Policy: "job records store redacted metadata and artifact pointers only");

    public static JobRecord CreateRunning(
        string jobId,
        DateTimeOffset nowUtc,
        JobCommandSummary command,
        string? jobName = null)
    {
        return new JobRecord(
            CurrentSchemaVersion,
            jobId,
            JobStatus.Running,
            nowUtc,
            nowUtc,
            command,
            jobName,
            StartedAtUtc: nowUtc);
    }

    public JobRecord WithStatus(
        string status,
        DateTimeOffset nowUtc,
        int? exitCode = null,
        string? stopReason = null,
        string? errorCode = null,
        string? summary = null,
        JobTaskReportSummary? taskReport = null,
        IReadOnlyList<JobArtifact>? artifacts = null,
        IReadOnlyList<string>? warnings = null)
    {
        return new JobRecord(
            SchemaVersion,
            JobId,
            status,
            CreatedAtUtc,
            nowUtc,
            Command,
            JobName,
            StartedAtUtc,
            CompletedAtUtc: status == JobStatus.Running ? null : nowUtc,
            exitCode,
            stopReason,
            errorCode,
            summary,
            taskReport,
            artifacts ?? Artifacts,
            warnings ?? Warnings,
            Redaction);
    }

    public static JobRecord FromExecResult(
        JobRecord current,
        ExecResult result,
        DateTimeOffset nowUtc,
        IReadOnlyList<JobArtifact>? artifacts = null,
        IReadOnlyList<string>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(result);

        string status = result.IsSuccess ? JobStatus.Succeeded : JobStatus.Failed;
        return current.WithStatus(
            status,
            nowUtc,
            result.ExitCode,
            result.StopReason,
            result.ErrorCode,
            result.Summary,
            result.TaskReport is null ? null : JobTaskReportSummary.FromTaskReport(result.TaskReport),
            artifacts,
            warnings);
    }

    private static string Safe(string value)
    {
        string safe = DiagnosticSecretRedactor.Redact(value ?? string.Empty);
        const int maxLength = 4096;
        return safe.Length <= maxLength ? safe : safe[..maxLength];
    }

    private static string? SafeOrNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : Safe(value);
    }
}

public static class JobIdGenerator
{
    private static readonly Regex JobIdPattern = new(
        @"^job_[0-9]{8}T[0-9]{9}Z_[a-f0-9]{8}$",
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
            .ToLowerInvariant();
        if (normalizedSuffix.Length < 8)
        {
            normalizedSuffix = normalizedSuffix.PadRight(8, '0');
        }

        normalizedSuffix = normalizedSuffix[..8];
        return "job_" + timestampUtc.UtcDateTime.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture) +
            "_" + normalizedSuffix;
    }

    public static bool IsValid(string? jobId)
    {
        return !string.IsNullOrWhiteSpace(jobId) && JobIdPattern.IsMatch(jobId);
    }
}

public sealed record JobRecordDiagnostic(
    string ErrorCode,
    string Summary,
    string? Path = null,
    string? JobId = null);

public sealed record JobRecordReadResult(
    bool Succeeded,
    JobRecord? Record,
    JobRecordDiagnostic? Diagnostic)
{
    public static JobRecordReadResult Success(JobRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new JobRecordReadResult(true, record, null);
    }

    public static JobRecordReadResult Failure(string errorCode, string summary, string? path = null, string? jobId = null)
    {
        return new JobRecordReadResult(false, null, new JobRecordDiagnostic(errorCode, summary, path, jobId));
    }
}

public sealed record JobRecordListResult(
    IReadOnlyList<JobRecord> Records,
    IReadOnlyList<JobRecordDiagnostic> Diagnostics);

public sealed class JobRecordStore
{
    private const string JobFileSuffix = ".job.json";
    private const string InvalidJobRecordSummary = "Job record is missing or uses an unsupported schema.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string jobDirectory;

    public JobRecordStore(string jobDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobDirectory);
        this.jobDirectory = jobDirectory;
    }

    public string JobDirectory => jobDirectory;

    public static JobRecordStore Create(CliEnvironmentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        string? userConfigDirectory = Path.GetDirectoryName(snapshot.UserConfigPath);
        string root = string.IsNullOrWhiteSpace(userConfigDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".caicli")
            : userConfigDirectory;
        return new JobRecordStore(Path.Combine(root, "jobs"));
    }

    public string Create(JobRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        Directory.CreateDirectory(jobDirectory);
        string path = GetPath(record.JobId);
        string json = JsonSerializer.Serialize(record, JsonOptions);
        WriteJsonAtomically(path, json, overwrite: false);
        return path;
    }

    public string Update(JobRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        Directory.CreateDirectory(jobDirectory);
        string path = GetPath(record.JobId);
        string json = JsonSerializer.Serialize(record, JsonOptions);
        WriteJsonAtomically(path, json, overwrite: true);
        return path;
    }

    public JobRecordReadResult Read(string jobId)
    {
        if (!JobIdGenerator.IsValid(jobId))
        {
            return JobRecordReadResult.Failure(
                "job-not-found",
                "Job record was not found.",
                jobId: jobId);
        }

        string path = GetPath(jobId);
        if (!File.Exists(path))
        {
            return JobRecordReadResult.Failure(
                "job-not-found",
                "Job record was not found.",
                path,
                jobId);
        }

        return TryLoad(path, expectedJobId: jobId);
    }

    public JobRecordListResult List(int? limit = null)
    {
        if (!Directory.Exists(jobDirectory))
        {
            return new JobRecordListResult([], []);
        }

        List<JobRecord> records = [];
        List<JobRecordDiagnostic> diagnostics = [];
        foreach (string path in EnumerateJobFilesBestEffort(jobDirectory))
        {
            JobRecordReadResult result = TryLoad(path);
            if (result.Succeeded && result.Record is not null)
            {
                records.Add(result.Record);
            }
            else if (result.Diagnostic is not null)
            {
                diagnostics.Add(result.Diagnostic);
            }
        }

        IOrderedEnumerable<JobRecord> ordered = records
            .OrderByDescending(record => record.CreatedAtUtc)
            .ThenByDescending(record => record.JobId, StringComparer.Ordinal);
        IReadOnlyList<JobRecord> bounded = limit is > 0
            ? ordered.Take(limit.Value).ToArray()
            : ordered.ToArray();

        return new JobRecordListResult(bounded, diagnostics);
    }

    private static IReadOnlyList<string> EnumerateJobFilesBestEffort(string directory)
    {
        List<string> paths = [];
        IEnumerator<string>? enumerator = null;
        try
        {
            enumerator = Directory.EnumerateFiles(directory, "*" + JobFileSuffix, SearchOption.TopDirectoryOnly)
                .GetEnumerator();
            while (true)
            {
                try
                {
                    if (!enumerator.MoveNext())
                    {
                        break;
                    }
                }
                catch (Exception exception) when (IsBestEffortStoreException(exception))
                {
                    break;
                }

                paths.Add(enumerator.Current);
            }
        }
        catch (Exception exception) when (IsBestEffortStoreException(exception))
        {
        }
        finally
        {
            enumerator?.Dispose();
        }

        paths.Sort(StringComparer.Ordinal);
        return paths;
    }

    private JobRecordReadResult TryLoad(string path, string? expectedJobId = null)
    {
        try
        {
            string json = File.ReadAllText(path);
            using JsonDocument document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("schemaVersion", out JsonElement schemaVersionElement) ||
                schemaVersionElement.ValueKind != JsonValueKind.Number ||
                !schemaVersionElement.TryGetInt32(out int schemaVersion) ||
                schemaVersion != JobRecord.CurrentSchemaVersion)
            {
                return JobRecordReadResult.Failure("corrupt-job-record", InvalidJobRecordSummary, path, expectedJobId);
            }

            JobRecord? record = JsonSerializer.Deserialize<JobRecord>(json, JsonOptions);
            if (record is null || !JobIdGenerator.IsValid(record.JobId))
            {
                return JobRecordReadResult.Failure("corrupt-job-record", InvalidJobRecordSummary, path, expectedJobId);
            }

            string pathJobId = Path.GetFileName(path);
            if (pathJobId.EndsWith(JobFileSuffix, StringComparison.OrdinalIgnoreCase))
            {
                pathJobId = pathJobId[..^JobFileSuffix.Length];
            }

            StringComparison comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!string.Equals(pathJobId, record.JobId, comparison) ||
                (expectedJobId is not null && !string.Equals(expectedJobId, record.JobId, comparison)))
            {
                return JobRecordReadResult.Failure(
                    "corrupt-job-record",
                    "Job record id does not match its file path.",
                    path,
                    expectedJobId ?? record.JobId);
            }

            return JobRecordReadResult.Success(record);
        }
        catch (JsonException exception)
        {
            return JobRecordReadResult.Failure(
                "corrupt-job-record",
                InvalidJobRecordSummary + " " + exception.GetType().Name,
                path,
                expectedJobId);
        }
        catch (Exception exception) when (IsBestEffortStoreException(exception))
        {
            return JobRecordReadResult.Failure(
                "job-record-unreadable",
                "Job record could not be read.",
                path,
                expectedJobId);
        }
    }

    private static void WriteJsonAtomically(string path, string json, bool overwrite)
    {
        string? directory = Path.GetDirectoryName(path);
        string fileName = Path.GetFileName(path);
        string temporaryPath = string.IsNullOrWhiteSpace(directory)
            ? $"{fileName}.{Guid.NewGuid():N}.tmp"
            : Path.Combine(directory, $"{fileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(temporaryPath, json, Encoding.UTF8);
            try
            {
                File.Move(temporaryPath, path, overwrite);
            }
            catch (IOException) when (!overwrite && File.Exists(path))
            {
                throw new IOException("Job record already exists.");
            }
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (IsBestEffortStoreException(exception))
        {
        }
    }

    private string GetPath(string jobId)
    {
        if (!JobIdGenerator.IsValid(jobId))
        {
            throw new ArgumentException("Job id format is invalid.", nameof(jobId));
        }

        string fullJobDirectory = Path.GetFullPath(jobDirectory);
        string path = Path.GetFullPath(Path.Combine(fullJobDirectory, jobId + JobFileSuffix));
        string rootedJobDirectory = fullJobDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? fullJobDirectory
            : fullJobDirectory + Path.DirectorySeparatorChar;

        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!path.StartsWith(rootedJobDirectory, comparison))
        {
            throw new InvalidOperationException("Job record path must remain inside the job directory.");
        }

        return path;
    }

    private static bool IsBestEffortStoreException(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or DirectoryNotFoundException
            or FileNotFoundException
            or NotSupportedException;
    }
}

public static class JobRecordJsonSchema
{
    public static string Render()
    {
        Dictionary<string, object?> schema = new(StringComparer.Ordinal)
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = "https://c-aicli.local/schemas/job-record.v1.json",
            ["title"] = "C-AICLI JobRecord v1",
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new[] { "schemaVersion", "jobId", "status", "createdAtUtc", "updatedAtUtc", "command", "artifacts", "warnings", "redaction" },
            ["properties"] = new Dictionary<string, object?>
            {
                ["schemaVersion"] = new Dictionary<string, object?> { ["const"] = JobRecord.CurrentSchemaVersion },
                ["jobId"] = new Dictionary<string, object?> { ["type"] = "string", ["pattern"] = "^job_[0-9]{8}T[0-9]{9}Z_[a-f0-9]{8}$" },
                ["jobName"] = NullableString(),
                ["status"] = new Dictionary<string, object?> { ["enum"] = new[] { JobStatus.Planned, JobStatus.Running, JobStatus.Succeeded, JobStatus.Failed, JobStatus.Canceled, JobStatus.ApprovalRequired, JobStatus.DryRun } },
                ["createdAtUtc"] = DateTimeString(),
                ["updatedAtUtc"] = DateTimeString(),
                ["startedAtUtc"] = NullableDateTimeString(),
                ["completedAtUtc"] = NullableDateTimeString(),
                ["exitCode"] = NullableInteger(),
                ["stopReason"] = NullableString(),
                ["errorCode"] = NullableString(),
                ["summary"] = NullableString(),
                ["command"] = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["required"] = new[] { "family" },
                    ["additionalProperties"] = true
                },
                ["taskReport"] = new Dictionary<string, object?>
                {
                    ["oneOf"] = new object[]
                    {
                        new Dictionary<string, object?> { ["type"] = "null" },
                        new Dictionary<string, object?> { ["type"] = "object", ["additionalProperties"] = true }
                    }
                },
                ["artifacts"] = ArrayOfObjects(),
                ["warnings"] = new Dictionary<string, object?> { ["type"] = "array", ["items"] = new Dictionary<string, object?> { ["type"] = "string" } },
                ["redaction"] = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["additionalProperties"] = true
                }
            }
        };

        return JsonSerializer.Serialize(schema, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
    }

    private static Dictionary<string, object?> NullableString()
    {
        return new Dictionary<string, object?> { ["type"] = new[] { "string", "null" } };
    }

    private static Dictionary<string, object?> NullableInteger()
    {
        return new Dictionary<string, object?> { ["type"] = new[] { "integer", "null" } };
    }

    private static Dictionary<string, object?> DateTimeString()
    {
        return new Dictionary<string, object?> { ["type"] = "string", ["format"] = "date-time" };
    }

    private static Dictionary<string, object?> NullableDateTimeString()
    {
        return new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["format"] = "date-time" };
    }

    private static Dictionary<string, object?> ArrayOfObjects()
    {
        return new Dictionary<string, object?>
        {
            ["type"] = "array",
            ["items"] = new Dictionary<string, object?> { ["type"] = "object", ["additionalProperties"] = true }
        };
    }
}

public sealed class JobsTextRenderer
{
    private readonly TextWriter writer;

    public JobsTextRenderer(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        this.writer = writer;
    }

    public void WriteList(JobRecordListResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        writer.WriteLine("C# AI CLI jobs");
        if (result.Records.Count == 0)
        {
            writer.WriteLine("status: empty");
        }
        else
        {
            foreach (JobRecord record in result.Records)
            {
                writer.WriteLine(
                    $"- {Safe(record.JobId)} status={Safe(record.Status)} command={Safe(record.Command.Family)} name={Safe(record.JobName ?? "none")} workspace={Safe(record.Command.WorkspaceRoot ?? "none")} created={record.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)} updated={record.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture)} artifacts={record.Artifacts.Count}");
            }
        }

        WriteDiagnostics(result.Diagnostics);
    }

    public void WriteShow(JobRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        writer.WriteLine("C# AI CLI job");
        writer.WriteLine("id: " + Safe(record.JobId));
        writer.WriteLine("status: " + Safe(record.Status));
        writer.WriteLine("name: " + Safe(record.JobName ?? "none"));
        writer.WriteLine("createdAtUtc: " + record.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        writer.WriteLine("updatedAtUtc: " + record.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        if (record.CompletedAtUtc.HasValue)
        {
            writer.WriteLine("completedAtUtc: " + record.CompletedAtUtc.Value.ToString("O", CultureInfo.InvariantCulture));
        }

        writer.WriteLine("command: " + Safe(record.Command.Family));
        writer.WriteLine("workspace: " + Safe(record.Command.WorkspaceRoot ?? "none"));
        writer.WriteLine("cwd: " + Safe(record.Command.Cwd ?? "none"));
        writer.WriteLine("skill: " + Safe(record.Command.Skill ?? "none"));
        writer.WriteLine("expert: " + Safe(record.Command.Expert ?? "none"));
        writer.WriteLine("reportMode: " + Safe(record.Command.ReportMode ?? "none"));
        if (record.Command.SkillMetadata is not null)
        {
            writer.WriteLine("skillVersion: " + Safe(record.Command.SkillMetadata.Version));
            writer.WriteLine("skillSource: " + Safe(FormatSkillSource(record.Command.SkillMetadata)));
            writer.WriteLine("skillEntry: " + Safe(record.Command.SkillMetadata.EntryMode));
            writer.WriteLine("skillSafety: " + Safe(record.Command.SkillMetadata.SafetySummary));
            writer.WriteLine("skillValidationCommand: " + Safe(record.Command.SkillMetadata.ValidationCommand ?? "none"));
            writer.WriteLine("skillSuggestedReferences: " + record.Command.SkillMetadata.SuggestedReferences.Count.ToString(CultureInfo.InvariantCulture));
        }

        writer.WriteLine("exitCode: " + (record.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "none"));
        writer.WriteLine("stopReason: " + Safe(record.StopReason ?? "none"));
        writer.WriteLine("errorCode: " + Safe(record.ErrorCode ?? "none"));
        writer.WriteLine("summary:");
        writer.WriteLine(Safe(record.Summary ?? "none"));
        WriteTaskReport(record.TaskReport);
        WriteArtifacts(record.Artifacts);
        WriteWarnings(record.Warnings);
        writer.WriteLine("redaction:");
        writer.WriteLine("- " + Safe(record.Redaction.Policy));
    }

    public void WriteMarkdown(JobRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        writer.WriteLine("# C# AI CLI Job");
        writer.WriteLine();
        writer.WriteLine("- ID: " + Safe(record.JobId));
        writer.WriteLine("- Status: " + Safe(record.Status));
        writer.WriteLine("- Name: " + Safe(record.JobName ?? "none"));
        writer.WriteLine("- Command: " + Safe(record.Command.Family));
        writer.WriteLine("- Workspace: " + Safe(record.Command.WorkspaceRoot ?? "none"));
        if (record.Command.SkillMetadata is not null)
        {
            writer.WriteLine("- Skill: " + Safe(record.Command.SkillMetadata.Name));
            writer.WriteLine("- Skill source: " + Safe(FormatSkillSource(record.Command.SkillMetadata)));
            writer.WriteLine("- Skill safety: " + Safe(record.Command.SkillMetadata.SafetySummary));
        }

        writer.WriteLine("- Created: " + record.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        writer.WriteLine("- Updated: " + record.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        if (record.CompletedAtUtc.HasValue)
        {
            writer.WriteLine("- Completed: " + record.CompletedAtUtc.Value.ToString("O", CultureInfo.InvariantCulture));
        }

        writer.WriteLine();
        writer.WriteLine("## Result");
        writer.WriteLine();
        writer.WriteLine("- Exit code: " + (record.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "none"));
        writer.WriteLine("- Stop reason: " + Safe(record.StopReason ?? "none"));
        writer.WriteLine("- Error code: " + Safe(record.ErrorCode ?? "none"));
        writer.WriteLine("- Summary: " + Safe(record.Summary ?? "none"));
        writer.WriteLine();
        writer.WriteLine("## Artifacts");
        if (record.Artifacts.Count == 0)
        {
            writer.WriteLine("- none");
        }
        else
        {
            foreach (JobArtifact artifact in record.Artifacts)
            {
                writer.WriteLine(
                    $"- {Safe(artifact.Kind)}: `{Safe(artifact.Path)}` exists={(artifact.Exists ? "true" : "false")} sha256={Safe(artifact.Sha256 ?? "none")}");
            }
        }

        writer.WriteLine();
        writer.WriteLine("## Redaction");
        writer.WriteLine();
        writer.WriteLine("- " + Safe(record.Redaction.Policy));
    }

    private void WriteTaskReport(JobTaskReportSummary? taskReport)
    {
        writer.WriteLine("taskReport:");
        if (taskReport is null)
        {
            writer.WriteLine("- none");
            return;
        }

        writer.WriteLine("- status: " + Safe(taskReport.Status));
        writer.WriteLine("- stopReason: " + Safe(taskReport.StopReason));
        writer.WriteLine("- changedFiles: " + taskReport.ChangedFileCount.ToString(CultureInfo.InvariantCulture));
        writer.WriteLine("- commands: " + taskReport.CommandCount.ToString(CultureInfo.InvariantCulture));
        writer.WriteLine("- verification: " + taskReport.VerificationCount.ToString(CultureInfo.InvariantCulture));
        writer.WriteLine("- risks: " + taskReport.RiskCount.ToString(CultureInfo.InvariantCulture));
        writer.WriteLine("- references: " + taskReport.ReferenceCount.ToString(CultureInfo.InvariantCulture));
        writer.WriteLine("- secretPresence: " + taskReport.SecretPresenceCount.ToString(CultureInfo.InvariantCulture));
        foreach (string command in taskReport.Commands)
        {
            writer.WriteLine("- command: " + Safe(command));
        }

        foreach (string verification in taskReport.Verification)
        {
            writer.WriteLine("- verificationDetail: " + Safe(verification));
        }
    }

    private void WriteArtifacts(IReadOnlyList<JobArtifact> artifacts)
    {
        writer.WriteLine("artifacts:");
        if (artifacts.Count == 0)
        {
            writer.WriteLine("- none");
            return;
        }

        foreach (JobArtifact artifact in artifacts)
        {
            writer.WriteLine(
                $"- kind={Safe(artifact.Kind)} path={Safe(artifact.Path)} exists={(artifact.Exists ? "true" : "false")} sha256={Safe(artifact.Sha256 ?? "none")} summary={Safe(artifact.Summary ?? "none")}");
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

    private void WriteDiagnostics(IReadOnlyList<JobRecordDiagnostic> diagnostics)
    {
        if (diagnostics.Count == 0)
        {
            return;
        }

        writer.WriteLine("diagnostics:");
        foreach (JobRecordDiagnostic diagnostic in diagnostics)
        {
            writer.WriteLine(
                $"- errorCode={Safe(diagnostic.ErrorCode)} job={Safe(diagnostic.JobId ?? "unknown")} path={Safe(diagnostic.Path ?? "unknown")} summary={Safe(diagnostic.Summary)}");
        }
    }

    private static string Safe(string value)
    {
        return DiagnosticSecretRedactor.Redact(value ?? string.Empty);
    }

    private static string FormatSkillSource(JobSkillSummary skill)
    {
        return string.IsNullOrWhiteSpace(skill.SourcePath)
            ? skill.SourceKind
            : skill.SourceKind + ":" + skill.SourcePath;
    }
}

public sealed class JobsJsonRenderer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    private readonly TextWriter writer;

    public JobsJsonRenderer(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        this.writer = writer;
    }

    public void WriteList(JobRecordListResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        writer.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["type"] = "jobs.list",
            ["records"] = result.Records.Select(ToJson).ToArray(),
            ["diagnostics"] = result.Diagnostics.Select(ToJson).ToArray()
        }, JsonOptions));
    }

    public void WriteShow(JobRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        writer.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["type"] = "jobs.show",
            ["record"] = ToJson(record)
        }, JsonOptions));
    }

    public void WriteExport(JobRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        writer.WriteLine(JsonSerializer.Serialize(ToJson(record), JsonOptions));
    }

    private static Dictionary<string, object?> ToJson(JobRecord record)
    {
        return new Dictionary<string, object?>
        {
            ["schemaVersion"] = record.SchemaVersion,
            ["jobId"] = Safe(record.JobId),
            ["jobName"] = SafeOrNull(record.JobName),
            ["status"] = Safe(record.Status),
            ["createdAtUtc"] = record.CreatedAtUtc,
            ["updatedAtUtc"] = record.UpdatedAtUtc,
            ["startedAtUtc"] = record.StartedAtUtc,
            ["completedAtUtc"] = record.CompletedAtUtc,
            ["exitCode"] = record.ExitCode,
            ["stopReason"] = SafeOrNull(record.StopReason),
            ["errorCode"] = SafeOrNull(record.ErrorCode),
            ["summary"] = SafeOrNull(record.Summary),
            ["command"] = new Dictionary<string, object?>
            {
                ["family"] = Safe(record.Command.Family),
                ["task"] = SafeOrNull(record.Command.Task),
                ["name"] = SafeOrNull(record.Command.Name),
                ["workspaceRoot"] = SafeOrNull(record.Command.WorkspaceRoot),
                ["cwd"] = SafeOrNull(record.Command.Cwd),
                ["skill"] = SafeOrNull(record.Command.Skill),
                ["expert"] = SafeOrNull(record.Command.Expert),
                ["reportMode"] = SafeOrNull(record.Command.ReportMode),
                ["outputMode"] = SafeOrNull(record.Command.OutputMode),
                ["dryRun"] = record.Command.DryRun,
                ["skillMetadata"] = record.Command.SkillMetadata is null
                    ? null
                    : ToJson(record.Command.SkillMetadata)
            },
            ["taskReport"] = record.TaskReport is null ? null : ToJson(record.TaskReport),
            ["artifacts"] = record.Artifacts.Select(ToJson).ToArray(),
            ["warnings"] = record.Warnings.Select(Safe).ToArray(),
            ["redaction"] = new Dictionary<string, object?>
            {
                ["secretsRedacted"] = record.Redaction.SecretsRedacted,
                ["rawReferencesStored"] = record.Redaction.RawReferencesStored,
                ["rawToolArgumentsStored"] = record.Redaction.RawToolArgumentsStored,
                ["fullDiffStored"] = record.Redaction.FullDiffStored,
                ["policy"] = Safe(record.Redaction.Policy)
            }
        };
    }

    private static Dictionary<string, object?> ToJson(JobTaskReportSummary report)
    {
        return new Dictionary<string, object?>
        {
            ["status"] = Safe(report.Status),
            ["stopReason"] = Safe(report.StopReason),
            ["summary"] = SafeOrNull(report.Summary),
            ["errorCode"] = SafeOrNull(report.ErrorCode),
            ["changedFileCount"] = report.ChangedFileCount,
            ["commandCount"] = report.CommandCount,
            ["verificationCount"] = report.VerificationCount,
            ["riskCount"] = report.RiskCount,
            ["referenceCount"] = report.ReferenceCount,
            ["secretPresenceCount"] = report.SecretPresenceCount,
            ["changedFiles"] = report.ChangedFiles.Select(Safe).ToArray(),
            ["verificationStatuses"] = report.VerificationStatuses.Select(Safe).ToArray(),
            ["risks"] = report.Risks.Select(Safe).ToArray(),
            ["commands"] = report.Commands.Select(Safe).ToArray(),
            ["verification"] = report.Verification.Select(Safe).ToArray()
        };
    }

    private static Dictionary<string, object?> ToJson(JobSkillSummary skill)
    {
        return new Dictionary<string, object?>
        {
            ["name"] = Safe(skill.Name),
            ["version"] = Safe(skill.Version),
            ["sourceKind"] = Safe(skill.SourceKind),
            ["sourcePath"] = SafeOrNull(skill.SourcePath),
            ["entryMode"] = Safe(skill.EntryMode),
            ["expert"] = Safe(skill.Expert),
            ["report"] = Safe(skill.Report),
            ["safetySummary"] = Safe(skill.SafetySummary),
            ["validationCommand"] = SafeOrNull(skill.ValidationCommand),
            ["suggestedReferences"] = skill.SuggestedReferences.Select(Safe).ToArray()
        };
    }

    private static Dictionary<string, object?> ToJson(JobArtifact artifact)
    {
        return new Dictionary<string, object?>
        {
            ["kind"] = Safe(artifact.Kind),
            ["path"] = Safe(artifact.Path),
            ["exists"] = artifact.Exists,
            ["summary"] = SafeOrNull(artifact.Summary),
            ["sha256"] = SafeOrNull(artifact.Sha256),
            ["createdAtUtc"] = artifact.CreatedAtUtc
        };
    }

    private static Dictionary<string, object?> ToJson(JobRecordDiagnostic diagnostic)
    {
        return new Dictionary<string, object?>
        {
            ["errorCode"] = Safe(diagnostic.ErrorCode),
            ["summary"] = Safe(diagnostic.Summary),
            ["path"] = SafeOrNull(diagnostic.Path),
            ["jobId"] = SafeOrNull(diagnostic.JobId)
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
