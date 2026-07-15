using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using CSharpAiCli.Core;

namespace CSharpAiCli.ProjectPacks.Runtime;

public static class ProjectPackRunState
{
    public const string Created = "created";
    public const string Discovered = "discovered";
    public const string Staged = "staged";
    public const string Ready = "ready";
    public const string Running = "running";
    public const string Verifying = "verifying";
    public const string AwaitingAcceptance = "awaiting-acceptance";
    public const string Accepted = "accepted";
    public const string Rejected = "rejected";
    public const string Failed = "failed";
    public const string Canceled = "canceled";
    public const string Interrupted = "interrupted";

    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        Created, Discovered, Staged, Ready, Running, Verifying, AwaitingAcceptance,
        Accepted, Rejected, Failed, Canceled, Interrupted
    };

    private static readonly HashSet<string> Terminal = new(StringComparer.Ordinal)
    {
        Accepted, Rejected, Failed, Canceled
    };

    public static bool IsKnown(string? state) => state is not null && Known.Contains(state);

    public static bool IsTerminal(string? state) => state is not null && Terminal.Contains(state);
}

public static class ProjectPackStageStatus
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string TimedOut = "timed-out";
    public const string Canceled = "canceled";
    public const string PartialOutput = "partial-output";
    public const string Interrupted = "interrupted";

    public static bool IsKnown(string? status) => status is Pending or Running or Succeeded or Failed
        or TimedOut or Canceled or PartialOutput or Interrupted;
}

public static class ProjectPackRunErrorCode
{
    public const string NotFound = "pack-run-not-found";
    public const string IdInvalid = "pack-run-id-invalid";
    public const string PathUnsafe = "pack-run-path-unsafe";
    public const string ReparsePoint = "pack-run-reparse-point";
    public const string AlreadyExists = "pack-run-already-exists";
    public const string ConcurrentConflict = "pack-run-concurrent-conflict";
    public const string RecordCorrupt = "pack-run-record-corrupt";
    public const string SchemaUnsupported = "pack-run-schema-unsupported";
    public const string RecordUnreadable = "pack-run-record-unreadable";
    public const string RecordWriteFailed = "pack-run-record-write-failed";
    public const string RevisionConflict = "pack-run-revision-conflict";
    public const string TransitionInvalid = "pack-run-transition-invalid";
    public const string PlanInvalid = "pack-run-plan-invalid";
    public const string PlanFingerprintChanged = "pack-run-plan-fingerprint-changed";
    public const string InputChanged = "pack-run-input-changed";
    public const string InputOutsideWorkspace = "pack-run-input-outside-workspace";
    public const string InputReparsePoint = "pack-run-input-reparse-point";
    public const string StagingLimitExceeded = "pack-run-staging-limit-exceeded";
    public const string StagingCopyFailed = "pack-run-staging-copy-failed";
    public const string StagingHashMismatch = "pack-run-staging-hash-mismatch";
    public const string ToolChanged = "pack-run-tool-changed";
    public const string OutputConflict = "pack-run-output-conflict";
    public const string PolicyChanged = "pack-run-policy-changed";
    public const string DriverFailed = "pack-run-driver-failed";
    public const string DriverTimedOut = "pack-run-driver-timeout";
    public const string DriverCanceled = "pack-run-driver-canceled";
    public const string DriverPartialOutput = "pack-run-driver-partial-output";
    public const string ToolNotFound = "pack-tool-not-found";
    public const string ToolVersionUnsupported = "pack-tool-version-unsupported";
    public const string ToolIdentityChanged = "pack-tool-identity-changed";
    public const string ApprovalRequired = "pack-approval-required";
    public const string ExecutionTimeout = "pack-execution-timeout";
    public const string ExecutionCanceled = "pack-execution-canceled";
    public const string ExecutionFailed = "pack-execution-failed";
    public const string PartialOutput = "pack-partial-output";
    public const string ConversionOutputConflict = "pack-output-conflict";
    public const string OutputBoundaryViolation = "pack-output-boundary-violation";
    public const string OutputLimitExceeded = "pack-output-limit-exceeded";
    public const string ProcessCleanupFailed = "pack-process-cleanup-failed";
    public const string ResidualProcessDetected = "pack-residual-process-detected";
    public const string RestartRequired = "pack-run-restart-required";
    public const string ResumeNotEligible = "pack-run-resume-not-eligible";
    public const string AcceptanceNotEligible = "pack-acceptance-not-eligible";
    public const string HardVerificationRequired = "pack-hard-verification-required";
    public const string DecisionConflict = "pack-acceptance-decision-conflict";
    public const string HumanRejected = "pack-human-rejected";
}

public static class ProjectPackRunTransitionTable
{
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Allowed =
        new ReadOnlyDictionary<string, IReadOnlySet<string>>(
            new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
            {
                [ProjectPackRunState.Created] = Set(ProjectPackRunState.Discovered, ProjectPackRunState.Failed, ProjectPackRunState.Canceled),
                [ProjectPackRunState.Discovered] = Set(ProjectPackRunState.Staged, ProjectPackRunState.Failed, ProjectPackRunState.Canceled),
                [ProjectPackRunState.Staged] = Set(ProjectPackRunState.Ready, ProjectPackRunState.Failed, ProjectPackRunState.Canceled),
                [ProjectPackRunState.Ready] = Set(ProjectPackRunState.Running, ProjectPackRunState.Failed, ProjectPackRunState.Canceled),
                [ProjectPackRunState.Running] = Set(ProjectPackRunState.Verifying, ProjectPackRunState.Failed, ProjectPackRunState.Canceled, ProjectPackRunState.Interrupted),
                [ProjectPackRunState.Verifying] = Set(ProjectPackRunState.AwaitingAcceptance, ProjectPackRunState.Failed, ProjectPackRunState.Canceled, ProjectPackRunState.Interrupted),
                [ProjectPackRunState.AwaitingAcceptance] = Set(ProjectPackRunState.Accepted, ProjectPackRunState.Rejected, ProjectPackRunState.Canceled),
                [ProjectPackRunState.Interrupted] = Set(ProjectPackRunState.Failed, ProjectPackRunState.Canceled),
                [ProjectPackRunState.Accepted] = Set(),
                [ProjectPackRunState.Rejected] = Set(),
                [ProjectPackRunState.Failed] = Set(),
                [ProjectPackRunState.Canceled] = Set()
            });

    public static bool CanTransition(string from, string to) =>
        Allowed.TryGetValue(from, out IReadOnlySet<string>? targets) && targets.Contains(to);

    public static IReadOnlySet<string> Targets(string state) =>
        Allowed.TryGetValue(state, out IReadOnlySet<string>? targets) ? targets : Set();

    private static IReadOnlySet<string> Set(params string[] values) => new HashSet<string>(values, StringComparer.Ordinal);
}

public static partial class ProjectPackRunId
{
    [GeneratedRegex("^run_[0-9]{8}T[0-9]{9}Z_[a-f0-9]{8}$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public static string Create(DateTimeOffset timestampUtc)
    {
        Span<byte> bytes = stackalloc byte[4];
        RandomNumberGenerator.Fill(bytes);
        return Create(timestampUtc, Convert.ToHexString(bytes));
    }

    internal static string Create(DateTimeOffset timestampUtc, string suffix)
    {
        string normalized = Regex.Replace(suffix ?? string.Empty, "[^a-fA-F0-9]", string.Empty).ToLowerInvariant();
        normalized = normalized.PadRight(8, '0')[..8];
        return "run_" + timestampUtc.UtcDateTime.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture) +
            "_" + normalized;
    }

    public static bool IsValid(string? runId) => !string.IsNullOrWhiteSpace(runId) && Pattern().IsMatch(runId);
}

public sealed record ProjectPackRunCorrelation
{
    public ProjectPackRunCorrelation(
        string? queueId = null,
        string? jobId = null,
        string? taskReportPointer = null,
        string? rootRunId = null,
        string? parentRunId = null,
        int attempt = 1,
        string? restartedByRunId = null)
    {
        if (queueId is not null && !TaskQueueIdGenerator.IsValid(queueId))
        {
            throw new ArgumentException("Project pack run queue correlation id is invalid.", nameof(queueId));
        }

        if (jobId is not null && !JobIdGenerator.IsValid(jobId))
        {
            throw new ArgumentException("Project pack run job correlation id is invalid.", nameof(jobId));
        }

        if (rootRunId is not null && !ProjectPackRunId.IsValid(rootRunId) ||
            parentRunId is not null && !ProjectPackRunId.IsValid(parentRunId) ||
            restartedByRunId is not null && !ProjectPackRunId.IsValid(restartedByRunId) ||
            attempt <= 0)
        {
            throw new ArgumentException("Project pack restart correlation is invalid.");
        }

        QueueId = queueId;
        JobId = jobId;
        TaskReportPointer = string.IsNullOrWhiteSpace(taskReportPointer)
            ? null
            : Safe(taskReportPointer, 1_024);
        RootRunId = rootRunId;
        ParentRunId = parentRunId;
        Attempt = attempt;
        RestartedByRunId = restartedByRunId;
    }

    public string? QueueId { get; }
    public string? JobId { get; }
    public string? TaskReportPointer { get; }
    public string? RootRunId { get; }
    public string? ParentRunId { get; }
    public int Attempt { get; }
    public string? RestartedByRunId { get; }

    private static string Safe(string value, int maxLength)
    {
        string safe = DiagnosticSecretRedactor.Redact(value);
        return safe.Length <= maxLength ? safe : safe[..maxLength];
    }
}

public sealed record ProjectPackRunArtifactPointer
{
    public ProjectPackRunArtifactPointer(
        string id,
        string kind,
        string scope,
        string path,
        bool exists,
        long? size = null,
        string? sha256 = null)
    {
        ProjectPackContractGuard.RequireId(id, nameof(id));
        ProjectPackContractGuard.RequireId(kind, nameof(kind));
        if (scope is not "managed-run" and not "workspace-output" and not "source" and not "external-pointer")
        {
            throw new ArgumentException("Project pack artifact scope is invalid.", nameof(scope));
        }

        if (size < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }

        if (sha256 is not null)
        {
            ProjectPackContractGuard.RequireSha256(sha256, nameof(sha256));
        }

        Id = id;
        Kind = kind;
        Scope = scope;
        Path = Safe(path, 4_096);
        Exists = exists;
        Size = size;
        Sha256 = sha256?.ToUpperInvariant();
    }

    public string Id { get; }
    public string Kind { get; }
    public string Scope { get; }
    public string Path { get; }
    public bool Exists { get; }
    public long? Size { get; }
    public string? Sha256 { get; }

    private static string Safe(string value, int maxLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string safe = DiagnosticSecretRedactor.Redact(value);
        return safe.Length <= maxLength ? safe : safe[..maxLength];
    }
}

public sealed record ProjectPackStageCheckpoint
{
    public ProjectPackStageCheckpoint(
        string stageId,
        string status,
        int attempt,
        DateTimeOffset? startedAtUtc = null,
        DateTimeOffset? completedAtUtc = null,
        string? errorCode = null,
        string? summary = null,
        IReadOnlyList<ProjectPackRunArtifactPointer>? outputs = null)
    {
        StageId = ProjectPackContractGuard.RequireId(stageId, nameof(stageId));
        if (!ProjectPackStageStatus.IsKnown(status) || attempt < 0)
        {
            throw new ArgumentException("Project pack stage checkpoint is invalid.");
        }

        Status = status;
        Attempt = attempt;
        StartedAtUtc = startedAtUtc;
        CompletedAtUtc = completedAtUtc;
        ErrorCode = SafeOptional(errorCode, 128);
        Summary = SafeOptional(summary, 4_096);
        Outputs = new ReadOnlyCollection<ProjectPackRunArtifactPointer>((outputs ?? []).ToArray());
    }

    public string StageId { get; }
    public string Status { get; }
    public int Attempt { get; }
    public DateTimeOffset? StartedAtUtc { get; }
    public DateTimeOffset? CompletedAtUtc { get; }
    public string? ErrorCode { get; }
    public string? Summary { get; }
    public IReadOnlyList<ProjectPackRunArtifactPointer> Outputs { get; }

    private static string? SafeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string safe = DiagnosticSecretRedactor.Redact(value);
        return safe.Length <= maxLength ? safe : safe[..maxLength];
    }
}

public sealed record ProjectPackRunRedaction(
    bool SecretsRedacted,
    bool RawInputStored,
    bool RawToolArgumentsStored,
    bool ApprovalStored,
    string Policy);

public sealed record ProjectPackAcceptanceDecision
{
    public ProjectPackAcceptanceDecision(
        string outcome,
        string actor,
        string? reason,
        DateTimeOffset decidedAtUtc,
        long basedOnRunRevision,
        string verificationArtifactId,
        string verificationSha256)
    {
        if (outcome is not ProjectPackRunState.Accepted and not ProjectPackRunState.Rejected ||
            basedOnRunRevision < 0)
        {
            throw new ArgumentException("Project pack acceptance decision is invalid.");
        }

        if (!ManagedArtifactId.IsValid(verificationArtifactId))
        {
            throw new ArgumentException("Project pack verification artifact id is invalid.", nameof(verificationArtifactId));
        }
        ProjectPackContractGuard.RequireSha256(verificationSha256, nameof(verificationSha256));
        Outcome = outcome;
        Actor = Safe(actor, 256);
        Reason = SafeOptional(reason, 2_048);
        DecidedAtUtc = decidedAtUtc;
        BasedOnRunRevision = basedOnRunRevision;
        VerificationArtifactId = verificationArtifactId;
        VerificationSha256 = verificationSha256.ToUpperInvariant();
    }

    public string Outcome { get; }
    public string Actor { get; }
    public string? Reason { get; }
    public DateTimeOffset DecidedAtUtc { get; }
    public long BasedOnRunRevision { get; }
    public string VerificationArtifactId { get; }
    public string VerificationSha256 { get; }

    private static string Safe(string? value, int maxLength)
    {
        string safe = DiagnosticSecretRedactor.Redact(value ?? string.Empty).Trim();
        if (safe.Length == 0)
        {
            throw new ArgumentException("Project pack acceptance actor is required.");
        }

        return safe.Length <= maxLength ? safe : safe[..maxLength];
    }

    private static string? SafeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string safe = DiagnosticSecretRedactor.Redact(value).Trim();
        return safe.Length <= maxLength ? safe : safe[..maxLength];
    }
}

public sealed record ProjectPackRestartPlan
{
    public ProjectPackRestartPlan(
        string newRunId,
        int attempt,
        string outputDirectory,
        DateTimeOffset plannedAtUtc)
    {
        if (!ProjectPackRunId.IsValid(newRunId) || attempt < 2)
        {
            throw new ArgumentException("Project pack restart plan is invalid.");
        }

        NewRunId = newRunId;
        Attempt = attempt;
        OutputDirectory = Safe(outputDirectory, 1_024);
        PlannedAtUtc = plannedAtUtc;
    }

    public string NewRunId { get; }
    public int Attempt { get; }
    public string OutputDirectory { get; }
    public DateTimeOffset PlannedAtUtc { get; }

    private static string Safe(string? value, int maxLength)
    {
        string safe = DiagnosticSecretRedactor.Redact(value ?? string.Empty).Trim();
        if (safe.Length == 0)
        {
            throw new ArgumentException("Project pack restart output directory is required.");
        }

        return safe.Length <= maxLength ? safe : safe[..maxLength];
    }
}

public sealed record ProjectPackRunRecord
{
    public const int CurrentSchemaVersion = 1;

    public ProjectPackRunRecord(
        int schemaVersion,
        string runId,
        long revision,
        string packId,
        string packVersion,
        string planId,
        string planFingerprint,
        string policyFingerprint,
        string state,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        ProjectPackRunCorrelation? correlation = null,
        IReadOnlyList<ProjectPackRunArtifactPointer>? artifacts = null,
        string? errorCode = null,
        string? summary = null,
        bool cancellationRequested = false,
        bool restartRequired = false,
        ProjectPackRunRedaction? redaction = null,
        ProjectPackAcceptanceDecision? acceptance = null,
        ProjectPackRestartPlan? restartPlan = null)
    {
        if (schemaVersion <= 0 || revision < 0 || !ProjectPackRunId.IsValid(runId) ||
            !ProjectPackRunState.IsKnown(state))
        {
            throw new ArgumentException("Project pack run record is invalid.");
        }

        ProjectPackContractGuard.RequireId(packId, nameof(packId));
        ProjectPackContractGuard.RequireId(planId, nameof(planId));
        ProjectPackContractGuard.RequireSha256(planFingerprint, nameof(planFingerprint));
        ProjectPackContractGuard.RequireSha256(policyFingerprint, nameof(policyFingerprint));
        SchemaVersion = schemaVersion;
        RunId = runId;
        Revision = revision;
        PackId = packId;
        PackVersion = Safe(packVersion, 64);
        PlanId = planId;
        PlanFingerprint = planFingerprint.ToUpperInvariant();
        PolicyFingerprint = policyFingerprint.ToUpperInvariant();
        State = state;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
        Correlation = correlation ?? new ProjectPackRunCorrelation();
        Artifacts = new ReadOnlyCollection<ProjectPackRunArtifactPointer>((artifacts ?? []).ToArray());
        ErrorCode = SafeOptional(errorCode, 128);
        Summary = SafeOptional(summary, 4_096);
        CancellationRequested = cancellationRequested;
        RestartRequired = restartRequired;
        if (acceptance is not null && (state != acceptance.Outcome || !ProjectPackRunState.IsTerminal(state)))
        {
            throw new ArgumentException("Project pack acceptance decision does not match run state.", nameof(acceptance));
        }

        Acceptance = acceptance;
        RestartPlan = restartPlan;
        Redaction = redaction ?? new ProjectPackRunRedaction(
            SecretsRedacted: true,
            RawInputStored: false,
            RawToolArgumentsStored: false,
            ApprovalStored: false,
            Policy: "run records store redacted evidence and artifact pointers only");
    }

    public int SchemaVersion { get; }
    public string RunId { get; }
    public long Revision { get; }
    public string PackId { get; }
    public string PackVersion { get; }
    public string PlanId { get; }
    public string PlanFingerprint { get; }
    public string PolicyFingerprint { get; }
    public string State { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset UpdatedAtUtc { get; }
    public ProjectPackRunCorrelation Correlation { get; }
    public IReadOnlyList<ProjectPackRunArtifactPointer> Artifacts { get; }
    public string? ErrorCode { get; }
    public string? Summary { get; }
    public bool CancellationRequested { get; }
    public bool RestartRequired { get; }
    public ProjectPackAcceptanceDecision? Acceptance { get; }
    public ProjectPackRestartPlan? RestartPlan { get; }
    public ProjectPackRunRedaction Redaction { get; }

    public ProjectPackRunRecord Transition(
        string state,
        DateTimeOffset nowUtc,
        string? errorCode = null,
        string? summary = null,
        bool? cancellationRequested = null,
        bool? restartRequired = null,
        IReadOnlyList<ProjectPackRunArtifactPointer>? artifacts = null)
    {
        if (!ProjectPackRunTransitionTable.CanTransition(State, state))
        {
            throw new ProjectPackContractException(
                ProjectPackRunErrorCode.TransitionInvalid,
                $"Run state cannot transition from '{State}' to '{state}'.");
        }

        return Copy(
            revision: Revision + 1,
            state: state,
            nowUtc: nowUtc,
            errorCode: errorCode,
            summary: summary,
            cancellationRequested: cancellationRequested ?? CancellationRequested,
            restartRequired: restartRequired ?? RestartRequired,
            artifacts: artifacts ?? Artifacts);
    }

    public ProjectPackRunRecord WithCorrelation(ProjectPackRunCorrelation correlation, DateTimeOffset nowUtc) =>
        new(
            SchemaVersion, RunId, Revision + 1, PackId, PackVersion, PlanId, PlanFingerprint,
            PolicyFingerprint, State, CreatedAtUtc, nowUtc, correlation, Artifacts, ErrorCode, Summary,
            CancellationRequested, RestartRequired, Redaction, Acceptance, RestartPlan);

    public ProjectPackRunRecord WithRestartPlan(
        ProjectPackRestartPlan restartPlan,
        ProjectPackRunCorrelation correlation,
        DateTimeOffset nowUtc) =>
        new(
            SchemaVersion, RunId, Revision + 1, PackId, PackVersion, PlanId, PlanFingerprint,
            PolicyFingerprint, State, CreatedAtUtc, nowUtc, correlation, Artifacts, ErrorCode, Summary,
            CancellationRequested, RestartRequired, Redaction, Acceptance, restartPlan);

    public ProjectPackRunRecord Decide(
        ProjectPackAcceptanceDecision decision,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(decision);
        if (State != ProjectPackRunState.AwaitingAcceptance || Acceptance is not null ||
            decision.BasedOnRunRevision != Revision)
        {
            throw new ProjectPackContractException(
                ProjectPackRunErrorCode.TransitionInvalid,
                "Acceptance decision does not match the current awaiting-acceptance revision.");
        }

        ProjectPackRunRecord transitioned = Transition(
            decision.Outcome,
            nowUtc,
            summary: decision.Outcome == ProjectPackRunState.Accepted
                ? "Run was explicitly accepted by a human reviewer."
                : "Run was explicitly rejected by a human reviewer.");
        return new ProjectPackRunRecord(
            transitioned.SchemaVersion,
            transitioned.RunId,
            transitioned.Revision,
            transitioned.PackId,
            transitioned.PackVersion,
            transitioned.PlanId,
            transitioned.PlanFingerprint,
            transitioned.PolicyFingerprint,
            transitioned.State,
            transitioned.CreatedAtUtc,
            transitioned.UpdatedAtUtc,
            transitioned.Correlation,
            transitioned.Artifacts,
            transitioned.ErrorCode,
            transitioned.Summary,
            transitioned.CancellationRequested,
            transitioned.RestartRequired,
            transitioned.Redaction,
            decision,
            transitioned.RestartPlan);
    }

    public ProjectPackRunRecord WithArtifacts(
        IReadOnlyList<ProjectPackRunArtifactPointer> artifacts,
        DateTimeOffset nowUtc,
        string? summary = null)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        if (artifacts.Select(artifact => artifact.Id).Distinct(StringComparer.Ordinal).Count() != artifacts.Count)
        {
            throw new ProjectPackContractException(
                ProjectPackRunErrorCode.RecordCorrupt,
                "Project pack run artifact ids must remain unique.");
        }

        return Copy(
            revision: Revision + 1,
            state: State,
            nowUtc: nowUtc,
            errorCode: ErrorCode,
            summary: summary ?? Summary,
            cancellationRequested: CancellationRequested,
            restartRequired: RestartRequired,
            artifacts: artifacts);
    }

    private ProjectPackRunRecord Copy(
        long revision,
        string state,
        DateTimeOffset nowUtc,
        string? errorCode,
        string? summary,
        bool cancellationRequested,
        bool restartRequired,
        IReadOnlyList<ProjectPackRunArtifactPointer> artifacts) =>
        new(
            SchemaVersion, RunId, revision, PackId, PackVersion, PlanId, PlanFingerprint,
            PolicyFingerprint, state, CreatedAtUtc, nowUtc, Correlation, artifacts, errorCode, summary,
            cancellationRequested, restartRequired, Redaction, Acceptance, RestartPlan);

    private static string Safe(string value, int maxLength)
    {
        string safe = DiagnosticSecretRedactor.Redact(value ?? string.Empty);
        return safe.Length <= maxLength ? safe : safe[..maxLength];
    }

    private static string? SafeOptional(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Safe(value, maxLength);
}

public sealed record ProjectPackRunCheckpoint
{
    public const int CurrentSchemaVersion = 1;

    public ProjectPackRunCheckpoint(
        int schemaVersion,
        string runId,
        long revision,
        string state,
        string planFingerprint,
        string policyFingerprint,
        DateTimeOffset updatedAtUtc,
        IReadOnlyList<ProjectPackStageCheckpoint>? stages = null,
        bool approvalPersisted = false)
    {
        if (schemaVersion <= 0 || revision < 0 || !ProjectPackRunId.IsValid(runId) ||
            !ProjectPackRunState.IsKnown(state) || approvalPersisted)
        {
            throw new ArgumentException("Project pack checkpoint is invalid.");
        }

        ProjectPackContractGuard.RequireSha256(planFingerprint, nameof(planFingerprint));
        ProjectPackContractGuard.RequireSha256(policyFingerprint, nameof(policyFingerprint));
        ProjectPackStageCheckpoint[] stageArray = (stages ?? []).ToArray();
        if (stageArray.Any(stage => string.IsNullOrWhiteSpace(stage.StageId) ||
            !ProjectPackStageStatus.IsKnown(stage.Status) || stage.Attempt < 0) ||
            stageArray.Select(stage => stage.StageId).Distinct(StringComparer.Ordinal).Count() != stageArray.Length)
        {
            throw new ArgumentException("Project pack checkpoint stages are invalid.");
        }

        SchemaVersion = schemaVersion;
        RunId = runId;
        Revision = revision;
        State = state;
        PlanFingerprint = planFingerprint.ToUpperInvariant();
        PolicyFingerprint = policyFingerprint.ToUpperInvariant();
        UpdatedAtUtc = updatedAtUtc;
        Stages = new ReadOnlyCollection<ProjectPackStageCheckpoint>(stageArray);
        ApprovalPersisted = false;
    }

    public int SchemaVersion { get; }
    public string RunId { get; }
    public long Revision { get; }
    public string State { get; }
    public string PlanFingerprint { get; }
    public string PolicyFingerprint { get; }
    public DateTimeOffset UpdatedAtUtc { get; }
    public IReadOnlyList<ProjectPackStageCheckpoint> Stages { get; }
    public bool ApprovalPersisted { get; }
}

public sealed record ProjectPackRunDiagnostic(string ErrorCode, string Summary, string? Path = null, string? RunId = null);

public sealed record ProjectPackRunReadResult(
    bool Succeeded,
    ProjectPackRunRecord? Record,
    ProjectPackRunCheckpoint? Checkpoint,
    ProjectPackRunDiagnostic? Diagnostic)
{
    public static ProjectPackRunReadResult Success(ProjectPackRunRecord record, ProjectPackRunCheckpoint checkpoint) =>
        new(true, record, checkpoint, null);

    public static ProjectPackRunReadResult Failure(string code, string summary, string? path = null, string? runId = null) =>
        new(false, null, null, new ProjectPackRunDiagnostic(code, summary, path, runId));
}

public sealed record ProjectPackRunMutationResult(
    bool Succeeded,
    ProjectPackRunRecord? Record,
    ProjectPackRunCheckpoint? Checkpoint,
    ProjectPackRunDiagnostic? Diagnostic)
{
    public static ProjectPackRunMutationResult Success(ProjectPackRunRecord record, ProjectPackRunCheckpoint checkpoint) =>
        new(true, record, checkpoint, null);

    public static ProjectPackRunMutationResult Failure(string code, string summary, string? path = null, string? runId = null) =>
        new(false, null, null, new ProjectPackRunDiagnostic(code, summary, path, runId));
}
