using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CSharpAiCli.Core;

namespace CSharpAiCli.ProjectPacks.Runtime;

public static class ProjectPackDriverBehavior
{
    public const string Success = "success";
    public const string Failure = "failure";
    public const string Timeout = "timeout";
    public const string Cancel = "cancel";
    public const string PartialOutput = "partial-output";
    public const string Interrupted = "interrupted";
}

public sealed record ProjectPackDriverStageEvent(
    string StageId,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string? ErrorCode = null,
    string? Summary = null,
    IReadOnlyList<ProjectPackRunArtifactPointer>? Outputs = null);

public sealed record ProjectPackRunDriverResult
{
    public ProjectPackRunDriverResult(
        string status,
        IReadOnlyList<ProjectPackDriverStageEvent>? events,
        IReadOnlyList<ProjectPackRunArtifactPointer>? outputs = null,
        string? errorCode = null,
        string? summary = null)
    {
        if (!ProjectPackStageStatus.IsKnown(status))
        {
            throw new ArgumentException("Project pack driver status is invalid.", nameof(status));
        }

        Status = status;
        Events = new ReadOnlyCollection<ProjectPackDriverStageEvent>((events ?? []).ToArray());
        Outputs = new ReadOnlyCollection<ProjectPackRunArtifactPointer>((outputs ?? []).ToArray());
        ErrorCode = errorCode;
        Summary = summary;
    }

    public string Status { get; }
    public IReadOnlyList<ProjectPackDriverStageEvent> Events { get; }
    public IReadOnlyList<ProjectPackRunArtifactPointer> Outputs { get; }
    public string? ErrorCode { get; }
    public string? Summary { get; }
}

public sealed record ProjectPackRunExecutionContext(
    ProjectPackRunRecord Record,
    ProjectPackRunCheckpoint Checkpoint,
    ManagedProjectPackRunLayout Layout);

public interface IProjectPackRunDriver
{
    ProjectPackRunDriverResult Execute(
        ProjectPackRunExecutionContext context,
        CancellationToken cancellationToken = default);
}

public sealed class FakeProjectPackRunDriver : IProjectPackRunDriver
{
    private readonly string behavior;
    private readonly Func<DateTimeOffset> utcNowProvider;

    public FakeProjectPackRunDriver(string behavior, Func<DateTimeOffset>? utcNowProvider = null)
    {
        if (behavior is not ProjectPackDriverBehavior.Success and
            not ProjectPackDriverBehavior.Failure and
            not ProjectPackDriverBehavior.Timeout and
            not ProjectPackDriverBehavior.Cancel and
            not ProjectPackDriverBehavior.PartialOutput and
            not ProjectPackDriverBehavior.Interrupted)
        {
            throw new ArgumentException("Fake project pack driver behavior is invalid.", nameof(behavior));
        }

        this.behavior = behavior;
        this.utcNowProvider = utcNowProvider ?? (() => DateTimeOffset.UtcNow);
    }

    public ProjectPackRunDriverResult Execute(
        ProjectPackRunExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (cancellationToken.IsCancellationRequested || behavior == ProjectPackDriverBehavior.Cancel)
        {
            return Result(ProjectPackStageStatus.Canceled, "render", ProjectPackRunErrorCode.DriverCanceled,
                "Fake driver cancellation was observed.");
        }

        return behavior switch
        {
            ProjectPackDriverBehavior.Success => Success(context),
            ProjectPackDriverBehavior.Failure => Result(ProjectPackStageStatus.Failed, "render",
                ProjectPackRunErrorCode.DriverFailed, "Fake driver returned a controlled failure."),
            ProjectPackDriverBehavior.Timeout => Result(ProjectPackStageStatus.TimedOut, "render",
                ProjectPackRunErrorCode.DriverTimedOut, "Fake driver returned a controlled timeout."),
            ProjectPackDriverBehavior.PartialOutput => PartialOutput(context),
            ProjectPackDriverBehavior.Interrupted => Result(ProjectPackStageStatus.Interrupted, "render",
                ProjectPackRunErrorCode.RestartRequired, "Fake driver simulated an interrupted execute stage."),
            _ => throw new InvalidOperationException("Unsupported fake driver behavior.")
        };
    }

    private ProjectPackRunDriverResult Success(ProjectPackRunExecutionContext context)
    {
        ProjectPackRunArtifactPointer output = WriteEvidence(context.Layout, "fake-driver-success.bin", "fake-driver-evidence");
        DateTimeOffset started = utcNowProvider();
        DateTimeOffset completed = utcNowProvider();
        return new ProjectPackRunDriverResult(
            ProjectPackStageStatus.Succeeded,
            [
                new ProjectPackDriverStageEvent("render", ProjectPackStageStatus.Succeeded, started, completed),
                new ProjectPackDriverStageEvent("encode", ProjectPackStageStatus.Succeeded, started, completed, Outputs: [output]),
                new ProjectPackDriverStageEvent("inspect", ProjectPackStageStatus.Succeeded, started, completed)
            ],
            [output],
            summary: "Fake driver completed; no real Gerber/TIFF tool or verifier was used.");
    }

    private ProjectPackRunDriverResult PartialOutput(ProjectPackRunExecutionContext context)
    {
        ProjectPackRunArtifactPointer output = WriteEvidence(context.Layout, "fake-driver-partial.bin", "fake-partial-evidence");
        DateTimeOffset started = utcNowProvider();
        DateTimeOffset completed = utcNowProvider();
        return new ProjectPackRunDriverResult(
            ProjectPackStageStatus.PartialOutput,
            [new ProjectPackDriverStageEvent(
                "render",
                ProjectPackStageStatus.PartialOutput,
                started,
                completed,
                ProjectPackRunErrorCode.DriverPartialOutput,
                "Fake driver retained partial output as failure evidence.",
                [output])],
            [output],
            ProjectPackRunErrorCode.DriverPartialOutput,
            "Fake driver returned controlled partial output; the run failed.");
    }

    private ProjectPackRunDriverResult Result(string status, string stageId, string errorCode, string summary)
    {
        DateTimeOffset started = utcNowProvider();
        DateTimeOffset completed = utcNowProvider();
        return new ProjectPackRunDriverResult(
            status,
            [new ProjectPackDriverStageEvent(stageId, status, started, completed, errorCode, summary)],
            errorCode: errorCode,
            summary: summary);
    }

    private static ProjectPackRunArtifactPointer WriteEvidence(
        ManagedProjectPackRunLayout layout,
        string fileName,
        string kind)
    {
        ManagedProjectPackRunStore.EnsureNoReparseInExistingChain(layout.ArtifactsPath);
        string path = Path.GetFullPath(Path.Combine(layout.ArtifactsPath, fileName));
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(layout.ArtifactsPath)) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new ProjectPackContractException(ProjectPackRunErrorCode.PathUnsafe, "Fake driver output escaped the managed run.");
        }

        byte[] content = Encoding.ASCII.GetBytes("C-AICLI CONTROLLED FAKE DRIVER EVIDENCE\n");
        using (FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            stream.Write(content);
            stream.Flush(flushToDisk: true);
        }

        return new ProjectPackRunArtifactPointer(
            Path.GetFileNameWithoutExtension(fileName),
            kind,
            "managed-run",
            "artifacts/" + fileName,
            exists: true,
            size: content.Length,
            sha256: Convert.ToHexString(SHA256.HashData(content)));
    }
}

public sealed record ProjectPackResumeEligibility(
    bool Eligible,
    string State,
    string NextAction,
    bool RequiresApproval,
    bool RestartRequired,
    string? ErrorCode = null,
    string? Summary = null);

public sealed class ProjectPackRunService
{
    private readonly ManagedProjectPackRunStore store;
    private readonly GerberTiffRunRevalidator revalidator;
    private readonly ProjectPackStagingService staging;

    public ProjectPackRunService(
        ManagedProjectPackRunStore store,
        GerberTiffRunRevalidator? revalidator = null,
        ProjectPackStagingService? staging = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.revalidator = revalidator ?? new GerberTiffRunRevalidator();
        this.staging = staging ?? new ProjectPackStagingService();
    }

    public ProjectPackRunMutationResult CreateAndStage(
        GerberTiffRunPlanSnapshot plan,
        WorkspaceContext workspace,
        IReadOnlyDictionary<string, string>? toolPaths,
        string policyFingerprint,
        DateTimeOffset nowUtc,
        ProjectPackRunCorrelation? correlation = null,
        string? runId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(workspace);
        runId ??= ProjectPackRunId.Create(nowUtc);
        ProjectPackInputManifest manifest = staging.CreateManifest(plan, runId);
        string manifestJson = ProjectPackStagingService.RenderManifest(manifest);
        ProjectPackRunArtifactPointer[] initialArtifacts =
        [
            JsonPointer("plan", "project-pack-plan", "plan.json", plan.SourceJson),
            JsonPointer("input-manifest", "input-manifest", "input-manifest.json", manifestJson)
        ];
        ProjectPackRunRecord record = new(
            ProjectPackRunRecord.CurrentSchemaVersion,
            runId,
            revision: 0,
            plan.PackId,
            plan.PackVersion,
            plan.PlanId,
            plan.Fingerprint,
            policyFingerprint,
            ProjectPackRunState.Created,
            nowUtc,
            nowUtc,
            correlation,
            initialArtifacts);
        ProjectPackRunCheckpoint checkpoint = new(
            ProjectPackRunCheckpoint.CurrentSchemaVersion,
            runId,
            revision: 0,
            ProjectPackRunState.Created,
            plan.Fingerprint,
            policyFingerprint,
            nowUtc,
            plan.Stages.Select(stage => new ProjectPackStageCheckpoint(stage.Id, ProjectPackStageStatus.Pending, 0)).ToArray());
        ProjectPackRunMutationResult created = store.CreateRun(record, checkpoint, plan.SourceJson, manifestJson);
        if (!created.Succeeded)
        {
            return created;
        }

        ProjectPackRunValidationResult validation = revalidator.Revalidate(
            plan, workspace, toolPaths, policyFingerprint, policyFingerprint, cancellationToken);
        if (!validation.Succeeded)
        {
            return Fail(record, checkpoint, validation.Diagnostic?.ErrorCode ?? ProjectPackRunErrorCode.PlanInvalid,
                validation.Diagnostic?.Summary ?? "Pre-run validation failed.", nowUtc);
        }

        ProjectPackRunMutationResult discovered = Advance(record, checkpoint, ProjectPackRunState.Discovered, nowUtc);
        if (!discovered.Succeeded || discovered.Record is null || discovered.Checkpoint is null)
        {
            return discovered;
        }

        record = discovered.Record;
        checkpoint = discovered.Checkpoint;
        ManagedProjectPackRunLayout layout = store.GetLayout(runId);
        ProjectPackStagingResult staged = staging.Stage(manifest, workspace, layout, cancellationToken);
        if (!staged.Succeeded)
        {
            return Fail(record, checkpoint, staged.Diagnostic?.ErrorCode ?? ProjectPackRunErrorCode.StagingCopyFailed,
                staged.Diagnostic?.Summary ?? "Staging failed.", nowUtc);
        }

        ProjectPackStageCheckpoint[] stagedCheckpoints = ReplaceStage(
            checkpoint.Stages,
            "inventory",
            new ProjectPackStageCheckpoint(
                "inventory",
                ProjectPackStageStatus.Succeeded,
                1,
                nowUtc,
                nowUtc,
                summary: "Bounded inventory staging completed with post-copy SHA256 verification.",
                outputs: [initialArtifacts[1]]));
        ProjectPackRunMutationResult stagedState = Advance(
            record, checkpoint, ProjectPackRunState.Staged, nowUtc, stages: stagedCheckpoints);
        if (!stagedState.Succeeded || stagedState.Record is null || stagedState.Checkpoint is null)
        {
            return stagedState;
        }

        record = stagedState.Record;
        checkpoint = stagedState.Checkpoint;
        validation = revalidator.Revalidate(
            plan, workspace, toolPaths, policyFingerprint, policyFingerprint, cancellationToken);
        if (!validation.Succeeded)
        {
            return Fail(record, checkpoint, validation.Diagnostic?.ErrorCode ?? ProjectPackRunErrorCode.PlanInvalid,
                validation.Diagnostic?.Summary ?? "Post-staging validation failed.", nowUtc);
        }

        return Advance(record, checkpoint, ProjectPackRunState.Ready, nowUtc,
            summary: "Run is staged and ready; execution still requires current approval.");
    }

    public ProjectPackRunMutationResult ExecuteFake(
        string runId,
        IProjectPackRunDriver driver,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ProjectPackRunReadResult read = store.Read(runId);
        if (!read.Succeeded || read.Record is null || read.Checkpoint is null)
        {
            return FromReadFailure(read, runId);
        }

        ProjectPackRunRecord record = read.Record;
        ProjectPackRunCheckpoint checkpoint = read.Checkpoint;
        if (record.State != ProjectPackRunState.Ready)
        {
            return ProjectPackRunMutationResult.Failure(
                ProjectPackRunErrorCode.TransitionInvalid,
                "Fake execution only starts from ready state.",
                runId: runId);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Cancel(runId, nowUtc);
        }

        ProjectPackStageCheckpoint[] runningStages = ReplaceStage(
            checkpoint.Stages,
            "render",
            new ProjectPackStageCheckpoint("render", ProjectPackStageStatus.Running, 1, nowUtc));
        ProjectPackRunMutationResult running = Advance(
            record, checkpoint, ProjectPackRunState.Running, nowUtc, stages: runningStages);
        if (!running.Succeeded || running.Record is null || running.Checkpoint is null)
        {
            return running;
        }

        record = running.Record;
        checkpoint = running.Checkpoint;
        ProjectPackRunDriverResult result;
        try
        {
            result = driver.Execute(
                new ProjectPackRunExecutionContext(record, checkpoint, store.GetLayout(runId)),
                cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Fail(record, checkpoint, ProjectPackRunErrorCode.DriverFailed,
                "Fake driver failed without producing a trusted result.", nowUtc);
        }

        ProjectPackStageCheckpoint[] resultStages = ApplyEvents(checkpoint.Stages, result.Events);
        IReadOnlyList<ProjectPackRunArtifactPointer> artifacts = record.Artifacts.Concat(result.Outputs).ToArray();
        if (result.Status == ProjectPackStageStatus.Succeeded)
        {
            ProjectPackStageCheckpoint? inspect = resultStages.FirstOrDefault(stage => stage.StageId == "inspect");
            ProjectPackStageCheckpoint[] verifyingStages = inspect is null
                ? resultStages
                : ReplaceStage(resultStages, "inspect", new ProjectPackStageCheckpoint(
                    "inspect", ProjectPackStageStatus.Running, Math.Max(1, inspect.Attempt), inspect.StartedAtUtc ?? nowUtc));
            ProjectPackRunMutationResult verifying = Advance(
                record, checkpoint, ProjectPackRunState.Verifying, nowUtc,
                summary: "Fake stage flow entered verifying; no TIFF verifier was run.",
                artifacts: artifacts,
                stages: verifyingStages);
            if (!verifying.Succeeded || verifying.Record is null || verifying.Checkpoint is null)
            {
                return verifying;
            }

            return Advance(
                verifying.Record,
                verifying.Checkpoint,
                ProjectPackRunState.AwaitingAcceptance,
                nowUtc,
                summary: "Fake driver stage flow completed; real conversion, TIFF verification, and acceptance remain unproven.",
                artifacts: artifacts,
                stages: resultStages);
        }

        if (result.Status == ProjectPackStageStatus.Canceled)
        {
            return Advance(record, checkpoint, ProjectPackRunState.Canceled, nowUtc,
                result.ErrorCode ?? ProjectPackRunErrorCode.DriverCanceled, result.Summary,
                cancellationRequested: true, artifacts: artifacts, stages: resultStages);
        }

        if (result.Status == ProjectPackStageStatus.Interrupted)
        {
            return Advance(record, checkpoint, ProjectPackRunState.Interrupted, nowUtc,
                ProjectPackRunErrorCode.RestartRequired, result.Summary,
                restartRequired: true, artifacts: artifacts, stages: resultStages);
        }

        string errorCode = result.Status switch
        {
            ProjectPackStageStatus.TimedOut => ProjectPackRunErrorCode.DriverTimedOut,
            ProjectPackStageStatus.PartialOutput => ProjectPackRunErrorCode.DriverPartialOutput,
            _ => ProjectPackRunErrorCode.DriverFailed
        };
        return Advance(record, checkpoint, ProjectPackRunState.Failed, nowUtc,
            result.ErrorCode ?? errorCode, result.Summary, artifacts: artifacts, stages: resultStages);
    }

    public ProjectPackRunMutationResult ExecuteControlled(
        string runId,
        GerberTiffRunPlanSnapshot plan,
        WorkspaceContext workspace,
        IReadOnlyDictionary<string, string>? toolPaths,
        string currentPolicyFingerprint,
        IProjectPackRunDriver driver,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(driver);
        ProjectPackRunReadResult read = store.Read(runId);
        if (!read.Succeeded || read.Record is null || read.Checkpoint is null)
        {
            return FromReadFailure(read, runId);
        }

        ProjectPackRunRecord record = read.Record;
        ProjectPackRunCheckpoint checkpoint = read.Checkpoint;
        if (record.State != ProjectPackRunState.Ready ||
            !string.Equals(record.PlanId, plan.PlanId, StringComparison.Ordinal) ||
            !string.Equals(record.PlanFingerprint, plan.Fingerprint, StringComparison.OrdinalIgnoreCase))
        {
            return ProjectPackRunMutationResult.Failure(
                ProjectPackRunErrorCode.TransitionInvalid,
                "Controlled conversion only starts from the matching ready run state.",
                runId: runId);
        }

        ProjectPackRunValidationResult validation = revalidator.Revalidate(
            plan,
            workspace,
            toolPaths,
            record.PolicyFingerprint,
            currentPolicyFingerprint,
            cancellationToken);
        if (!validation.Succeeded)
        {
            return Fail(
                record,
                checkpoint,
                validation.Diagnostic?.ErrorCode ?? ProjectPackRunErrorCode.PlanInvalid,
                validation.Diagnostic?.Summary ?? "Controlled conversion pre-run validation failed.",
                nowUtc);
        }

        ManagedProjectPackRunLayout layout = store.GetLayout(runId);
        ProjectPackInputManifest manifest;
        try
        {
            manifest = ProjectPackStagingService.LoadManifest(
                ManagedProjectPackRunStore.ReadTextBounded(layout.InputManifestPath, 2 * 1024 * 1024));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ProjectPackContractException)
        {
            return Fail(record, checkpoint, ProjectPackRunErrorCode.RecordCorrupt,
                "Controlled conversion input manifest could not be read safely.", nowUtc);
        }

        ProjectPackRunDiagnostic? stagedDiagnostic = staging.VerifyStaged(manifest, layout);
        if (stagedDiagnostic is not null)
        {
            return Fail(record, checkpoint, stagedDiagnostic.ErrorCode, stagedDiagnostic.Summary, nowUtc);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Cancel(runId, nowUtc);
        }

        ProjectPackStageCheckpoint[] runningStages = ReplaceStage(
            checkpoint.Stages,
            "render",
            new ProjectPackStageCheckpoint("render", ProjectPackStageStatus.Running, 1, nowUtc));
        ProjectPackRunMutationResult running = Advance(
            record,
            checkpoint,
            ProjectPackRunState.Running,
            nowUtc,
            summary: "Controlled Gerber/TIFF conversion started after current revalidation; approvals are invocation-local.",
            stages: runningStages);
        if (!running.Succeeded || running.Record is null || running.Checkpoint is null)
        {
            return running;
        }

        record = running.Record;
        checkpoint = running.Checkpoint;
        ProjectPackRunDriverResult result;
        try
        {
            result = driver.Execute(new ProjectPackRunExecutionContext(record, checkpoint, layout), cancellationToken);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or ProjectPackContractException)
        {
            return Advance(
                record,
                checkpoint,
                ProjectPackRunState.Interrupted,
                nowUtc,
                ProjectPackRunErrorCode.ExecutionFailed,
                "Controlled conversion terminated without a trusted result; inspect partial outputs before restart.",
                restartRequired: true,
                stages: ReplaceRunningStages(checkpoint.Stages, nowUtc, ProjectPackRunErrorCode.ExecutionFailed));
        }

        ProjectPackStageCheckpoint[] resultStages;
        try
        {
            resultStages = ApplyEvents(checkpoint.Stages, result.Events);
        }
        catch (ProjectPackContractException exception)
        {
            return Advance(
                record,
                checkpoint,
                ProjectPackRunState.Interrupted,
                nowUtc,
                exception.ErrorCode,
                "Controlled conversion returned invalid stage evidence; inspect outputs before restart.",
                restartRequired: true,
                stages: ReplaceRunningStages(checkpoint.Stages, nowUtc, exception.ErrorCode));
        }

        IReadOnlyList<ProjectPackRunArtifactPointer> artifacts = record.Artifacts.Concat(result.Outputs).ToArray();
        if (result.Status == ProjectPackStageStatus.Succeeded)
        {
            int expected = manifest.Inputs.Count(input => input.PassedToExternalTool);
            int renderCount = result.Outputs.Count(output => output.Kind == "render-intermediate" && output.Exists);
            int tiffCount = result.Outputs.Count(output => output.Kind == "tiff-output" && output.Exists);
            bool stagesComplete = resultStages.Any(stage => stage.StageId == "render" && stage.Status == ProjectPackStageStatus.Succeeded) &&
                resultStages.Any(stage => stage.StageId == "encode" && stage.Status == ProjectPackStageStatus.Succeeded);
            if (!stagesComplete || expected == 0 || renderCount != expected || tiffCount != expected)
            {
                return Advance(
                    record,
                    checkpoint,
                    ProjectPackRunState.Failed,
                    nowUtc,
                    ProjectPackRunErrorCode.PartialOutput,
                    "Controlled conversion did not produce the complete declared output inventory.",
                    artifacts: artifacts,
                    stages: resultStages);
            }

            return Advance(
                record,
                checkpoint,
                ProjectPackRunState.Verifying,
                nowUtc,
                summary: "Gerber to TIFF conversion executed and declared output hashes were recorded; TIFF engineering verification is pending Week 62.",
                artifacts: artifacts,
                stages: resultStages);
        }

        if (result.Status == ProjectPackStageStatus.Canceled)
        {
            return Advance(
                record,
                checkpoint,
                ProjectPackRunState.Canceled,
                nowUtc,
                result.ErrorCode ?? ProjectPackRunErrorCode.ExecutionCanceled,
                result.Summary,
                cancellationRequested: true,
                artifacts: artifacts,
                stages: resultStages);
        }

        if (result.Status == ProjectPackStageStatus.Interrupted)
        {
            return Advance(
                record,
                checkpoint,
                ProjectPackRunState.Interrupted,
                nowUtc,
                result.ErrorCode ?? ProjectPackRunErrorCode.ProcessCleanupFailed,
                result.Summary,
                restartRequired: true,
                artifacts: artifacts,
                stages: resultStages);
        }

        string errorCode = result.ErrorCode ?? result.Status switch
        {
            ProjectPackStageStatus.TimedOut => ProjectPackRunErrorCode.ExecutionTimeout,
            ProjectPackStageStatus.PartialOutput => ProjectPackRunErrorCode.PartialOutput,
            _ => ProjectPackRunErrorCode.ExecutionFailed
        };
        return Advance(
            record,
            checkpoint,
            ProjectPackRunState.Failed,
            nowUtc,
            errorCode,
            result.Summary,
            artifacts: artifacts,
            stages: resultStages);
    }

    public ProjectPackRunMutationResult CompleteVerification(
        string runId,
        bool succeeded,
        IReadOnlyList<ProjectPackRunArtifactPointer> evidence,
        DateTimeOffset nowUtc,
        string? errorCode,
        string summary)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ProjectPackRunReadResult read = store.Read(runId);
        if (!read.Succeeded || read.Record is null || read.Checkpoint is null)
        {
            return FromReadFailure(read, runId);
        }

        ProjectPackRunRecord record = read.Record;
        ProjectPackRunCheckpoint checkpoint = read.Checkpoint;
        if (record.State != ProjectPackRunState.Verifying)
        {
            return ProjectPackRunMutationResult.Failure(
                TiffVerificationErrorCode.RunStateInvalid,
                "TIFF verification can complete only from the verifying state.",
                runId: runId);
        }

        ProjectPackRunArtifactPointer[] artifacts;
        try
        {
            artifacts = record.Artifacts.Concat(evidence).ToArray();
            if (artifacts.Select(artifact => artifact.Id).Distinct(StringComparer.Ordinal).Count() != artifacts.Length)
            {
                throw new ProjectPackContractException(
                    ProjectPackRunErrorCode.RecordCorrupt,
                    "TIFF verification evidence contains a duplicate artifact id.");
            }
        }
        catch (ProjectPackContractException exception)
        {
            return ProjectPackRunMutationResult.Failure(exception.ErrorCode, exception.Message, runId: runId);
        }

        ProjectPackStageCheckpoint[] stages = ReplaceStage(
            checkpoint.Stages,
            "inspect",
            new ProjectPackStageCheckpoint(
                "inspect",
                succeeded ? ProjectPackStageStatus.Succeeded : ProjectPackStageStatus.Failed,
                1,
                nowUtc,
                nowUtc,
                succeeded ? null : errorCode ?? TiffVerificationErrorCode.DecodeFailed,
                summary,
                evidence));
        return Advance(
            record,
            checkpoint,
            succeeded ? ProjectPackRunState.AwaitingAcceptance : ProjectPackRunState.Failed,
            nowUtc,
            succeeded ? null : errorCode ?? TiffVerificationErrorCode.DecodeFailed,
            summary,
            artifacts: artifacts,
            stages: stages);
    }

    public ProjectPackRunMutationResult AttachVerificationEvidence(
        string runId,
        IReadOnlyList<ProjectPackRunArtifactPointer> evidence,
        DateTimeOffset nowUtc,
        string summary)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ProjectPackRunReadResult read = store.Read(runId);
        if (!read.Succeeded || read.Record is null || read.Checkpoint is null)
        {
            return FromReadFailure(read, runId);
        }

        if (read.Record.State != ProjectPackRunState.AwaitingAcceptance)
        {
            return ProjectPackRunMutationResult.Failure(
                TiffVerificationErrorCode.RunStateInvalid,
                "Preview evidence can be attached only after hard verification.",
                runId: runId);
        }

        try
        {
            ProjectPackRunArtifactPointer[] artifacts = read.Record.Artifacts.Concat(evidence).ToArray();
            ProjectPackRunRecord updated = read.Record.WithArtifacts(artifacts, nowUtc, summary);
            ProjectPackRunCheckpoint updatedCheckpoint = new(
                read.Checkpoint.SchemaVersion,
                read.Checkpoint.RunId,
                updated.Revision,
                updated.State,
                read.Checkpoint.PlanFingerprint,
                read.Checkpoint.PolicyFingerprint,
                nowUtc,
                read.Checkpoint.Stages,
                approvalPersisted: false);
            return store.Update(updated, updatedCheckpoint, read.Record.Revision);
        }
        catch (ProjectPackContractException exception)
        {
            return ProjectPackRunMutationResult.Failure(exception.ErrorCode, exception.Message, runId: runId);
        }
    }

    public ProjectPackRunMutationResult Cancel(string runId, DateTimeOffset nowUtc)
    {
        ProjectPackRunReadResult read = store.Read(runId);
        if (!read.Succeeded || read.Record is null || read.Checkpoint is null)
        {
            return FromReadFailure(read, runId);
        }

        if (ProjectPackRunState.IsTerminal(read.Record.State))
        {
            return ProjectPackRunMutationResult.Failure(
                ProjectPackRunErrorCode.TransitionInvalid,
                "Terminal project pack runs cannot be canceled again.",
                runId: runId);
        }

        ProjectPackStageCheckpoint[] stages = read.Checkpoint.Stages.Select(stage =>
            stage.Status == ProjectPackStageStatus.Running
                ? new ProjectPackStageCheckpoint(
                    stage.StageId, ProjectPackStageStatus.Canceled, stage.Attempt,
                    stage.StartedAtUtc, nowUtc, ProjectPackRunErrorCode.DriverCanceled, "Run cancellation was recorded.", stage.Outputs)
                : stage).ToArray();
        return Advance(read.Record, read.Checkpoint, ProjectPackRunState.Canceled, nowUtc,
            ProjectPackRunErrorCode.DriverCanceled, "Project pack run was canceled.",
            cancellationRequested: true, stages: stages);
    }

    public ProjectPackRunMutationResult MarkInterrupted(string runId, DateTimeOffset nowUtc)
    {
        ProjectPackRunReadResult read = store.Read(runId);
        if (!read.Succeeded || read.Record is null || read.Checkpoint is null)
        {
            return FromReadFailure(read, runId);
        }

        if (read.Record.State != ProjectPackRunState.Running)
        {
            return ProjectPackRunMutationResult.Failure(
                ProjectPackRunErrorCode.TransitionInvalid,
                "Only a running execute stage can be marked interrupted.",
                runId: runId);
        }

        ProjectPackStageCheckpoint[] stages = read.Checkpoint.Stages.Select(stage =>
            stage.Status == ProjectPackStageStatus.Running
                ? new ProjectPackStageCheckpoint(
                    stage.StageId, ProjectPackStageStatus.Interrupted, stage.Attempt,
                    stage.StartedAtUtc, nowUtc, ProjectPackRunErrorCode.RestartRequired,
                    "Execute stage was interrupted and cannot be replayed automatically.", stage.Outputs)
                : stage).ToArray();
        return Advance(read.Record, read.Checkpoint, ProjectPackRunState.Interrupted, nowUtc,
            ProjectPackRunErrorCode.RestartRequired,
            "Execute was interrupted; inspect partial outputs and make an explicit restart decision.",
            restartRequired: true,
            stages: stages);
    }

    public ProjectPackResumeEligibility EvaluateResume(
        string runId,
        WorkspaceContext workspace,
        IReadOnlyDictionary<string, string>? toolPaths,
        string currentPolicyFingerprint,
        CancellationToken cancellationToken = default)
    {
        ProjectPackRunReadResult read = store.Read(runId);
        if (!read.Succeeded || read.Record is null || read.Checkpoint is null)
        {
            return new ProjectPackResumeEligibility(
                false, "unknown", "none", false, false,
                read.Diagnostic?.ErrorCode ?? ProjectPackRunErrorCode.NotFound,
                read.Diagnostic?.Summary ?? "Project pack run could not be read.");
        }

        ProjectPackRunRecord record = read.Record;
        if (ProjectPackRunState.IsTerminal(record.State))
        {
            return Ineligible(record.State, ProjectPackRunErrorCode.ResumeNotEligible, "Terminal runs cannot resume.");
        }

        GerberTiffRunPlanSnapshot plan;
        ProjectPackInputManifest manifest;
        ManagedProjectPackRunLayout layout = store.GetLayout(runId);
        try
        {
            plan = GerberTiffRunPlanLoader.Load(ManagedProjectPackRunStore.ReadTextBounded(layout.PlanPath, 2 * 1024 * 1024));
            manifest = ProjectPackStagingService.LoadManifest(
                ManagedProjectPackRunStore.ReadTextBounded(layout.InputManifestPath, 2 * 1024 * 1024));
        }
        catch (ProjectPackContractException exception)
        {
            return Ineligible(record.State, exception.ErrorCode, exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return Ineligible(record.State, ProjectPackRunErrorCode.RecordCorrupt, "Run plan or input manifest is corrupt.");
        }

        bool postExecution = record.State is ProjectPackRunState.Running
            or ProjectPackRunState.Interrupted
            or ProjectPackRunState.Verifying
            or ProjectPackRunState.AwaitingAcceptance;
        ProjectPackRunValidationResult validation = revalidator.Revalidate(
            plan,
            workspace,
            toolPaths,
            record.PolicyFingerprint,
            currentPolicyFingerprint,
            cancellationToken,
            allowExistingOutputDirectory: postExecution);
        if (!validation.Succeeded)
        {
            return Ineligible(record.State,
                validation.Diagnostic?.ErrorCode ?? ProjectPackRunErrorCode.ResumeNotEligible,
                validation.Diagnostic?.Summary ?? "Resume revalidation failed.");
        }

        if (record.State is ProjectPackRunState.Running or ProjectPackRunState.Interrupted)
        {
            return new ProjectPackResumeEligibility(
                false,
                record.State,
                "explicit-restart-decision",
                true,
                true,
                ProjectPackRunErrorCode.RestartRequired,
                "Running or interrupted execute stages are never replayed automatically; inspect partial outputs first.");
        }

        if (record.State is ProjectPackRunState.Staged or ProjectPackRunState.Ready)
        {
            ProjectPackRunDiagnostic? stagedDiagnostic = staging.VerifyStaged(manifest, layout);
            if (stagedDiagnostic is not null)
            {
                return Ineligible(record.State, stagedDiagnostic.ErrorCode, stagedDiagnostic.Summary);
            }

            return new ProjectPackResumeEligibility(
                true, record.State, "continue-after-current-approval", true, false,
                Summary: "Staged inputs are valid; execution requires a new approval decision.");
        }

        if (record.State == ProjectPackRunState.Verifying)
        {
            ProjectPackRunDiagnostic? artifactDiagnostic = VerifyDeclaredArtifacts(record, workspace);
            if (artifactDiagnostic is not null)
            {
                return Ineligible(record.State, artifactDiagnostic.ErrorCode, artifactDiagnostic.Summary);
            }

            return new ProjectPackResumeEligibility(
                true, record.State, "rerun-read-only-verifier", false, false,
                Summary: "Tool, input, policy, staging, and declared output identities are current; the bounded in-process verifier may be rerun.");
        }

        if (record.State == ProjectPackRunState.AwaitingAcceptance)
        {
            ProjectPackRunDiagnostic? artifactDiagnostic = VerifyDeclaredArtifacts(record, workspace);
            if (artifactDiagnostic is not null)
            {
                return Ineligible(record.State, artifactDiagnostic.ErrorCode, artifactDiagnostic.Summary);
            }

            ProjectPackRunDiagnostic? verificationDiagnostic = new ProjectPackAcceptanceService(store)
                .ValidateHardVerification(runId);
            if (verificationDiagnostic is not null)
            {
                return Ineligible(record.State, verificationDiagnostic.ErrorCode, verificationDiagnostic.Summary);
            }

            return new ProjectPackResumeEligibility(
                true, record.State, "continue-human-decision", false, false,
                Summary: "Current hard verification evidence was revalidated; the run may continue only at the explicit human decision gate.");
        }

        return Ineligible(record.State, ProjectPackRunErrorCode.ResumeNotEligible,
            "This pre-staging state is not resumable; create a new run.");
    }

    private ProjectPackRunMutationResult Fail(
        ProjectPackRunRecord record,
        ProjectPackRunCheckpoint checkpoint,
        string errorCode,
        string summary,
        DateTimeOffset nowUtc) =>
        Advance(record, checkpoint, ProjectPackRunState.Failed, nowUtc, errorCode, summary);

    private ProjectPackRunMutationResult Advance(
        ProjectPackRunRecord record,
        ProjectPackRunCheckpoint checkpoint,
        string state,
        DateTimeOffset nowUtc,
        string? errorCode = null,
        string? summary = null,
        bool? cancellationRequested = null,
        bool? restartRequired = null,
        IReadOnlyList<ProjectPackRunArtifactPointer>? artifacts = null,
        IReadOnlyList<ProjectPackStageCheckpoint>? stages = null)
    {
        try
        {
            ProjectPackRunRecord updated = record.Transition(
                state, nowUtc, errorCode, summary, cancellationRequested, restartRequired, artifacts);
            ProjectPackRunCheckpoint updatedCheckpoint = new(
                checkpoint.SchemaVersion,
                checkpoint.RunId,
                updated.Revision,
                state,
                checkpoint.PlanFingerprint,
                checkpoint.PolicyFingerprint,
                nowUtc,
                stages ?? checkpoint.Stages,
                approvalPersisted: false);
            return store.Update(updated, updatedCheckpoint, record.Revision);
        }
        catch (ProjectPackContractException exception)
        {
            return ProjectPackRunMutationResult.Failure(exception.ErrorCode, exception.Message, runId: record.RunId);
        }
    }

    private static ProjectPackStageCheckpoint[] ApplyEvents(
        IReadOnlyList<ProjectPackStageCheckpoint> stages,
        IReadOnlyList<ProjectPackDriverStageEvent> events)
    {
        ProjectPackStageCheckpoint[] result = stages.ToArray();
        foreach (ProjectPackDriverStageEvent stageEvent in events)
        {
            ProjectPackStageCheckpoint existing = result.FirstOrDefault(stage => stage.StageId == stageEvent.StageId)
                ?? throw new ProjectPackContractException(ProjectPackRunErrorCode.DriverFailed, "Driver returned an unknown stage id.");
            result = ReplaceStage(result, stageEvent.StageId, new ProjectPackStageCheckpoint(
                stageEvent.StageId,
                stageEvent.Status,
                Math.Max(1, existing.Attempt),
                stageEvent.StartedAtUtc,
                stageEvent.CompletedAtUtc,
                stageEvent.ErrorCode,
                stageEvent.Summary,
                stageEvent.Outputs));
        }

        return result;
    }

    private static ProjectPackStageCheckpoint[] ReplaceStage(
        IReadOnlyList<ProjectPackStageCheckpoint> stages,
        string stageId,
        ProjectPackStageCheckpoint replacement) =>
        stages.Select(stage => stage.StageId == stageId ? replacement : stage).ToArray();

    private static ProjectPackStageCheckpoint[] ReplaceRunningStages(
        IReadOnlyList<ProjectPackStageCheckpoint> stages,
        DateTimeOffset completedAtUtc,
        string errorCode) =>
        stages.Select(stage => stage.Status == ProjectPackStageStatus.Running
            ? new ProjectPackStageCheckpoint(
                stage.StageId,
                ProjectPackStageStatus.Interrupted,
                Math.Max(1, stage.Attempt),
                stage.StartedAtUtc,
                completedAtUtc,
                errorCode,
                "Controlled conversion stage ended without trusted terminal evidence.",
                stage.Outputs)
            : stage).ToArray();

    private static ProjectPackRunMutationResult FromReadFailure(ProjectPackRunReadResult read, string runId) =>
        ProjectPackRunMutationResult.Failure(
            read.Diagnostic?.ErrorCode ?? ProjectPackRunErrorCode.NotFound,
            read.Diagnostic?.Summary ?? "Project pack run could not be read.",
            read.Diagnostic?.Path,
            runId);

    private static ProjectPackRunArtifactPointer JsonPointer(string id, string kind, string path, string json)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        return new ProjectPackRunArtifactPointer(
            id, kind, "managed-run", path, true, bytes.Length,
            Convert.ToHexString(SHA256.HashData(bytes)));
    }

    private static ProjectPackResumeEligibility Ineligible(string state, string errorCode, string summary) =>
        new(false, state, "none", false, false, errorCode, summary);

    private ProjectPackRunDiagnostic? VerifyDeclaredArtifacts(
        ProjectPackRunRecord record,
        WorkspaceContext workspace)
    {
        ProjectPackRunArtifactPointer[] outputs = record.Artifacts
            .Where(artifact => artifact.Scope is "managed-run" or "workspace-output" or "source")
            .ToArray();
        if (outputs.Length == 0 || outputs.Any(output => output.Sha256 is null || output.Size is null))
        {
            return new ProjectPackRunDiagnostic(
                ProjectPackRunErrorCode.ResumeNotEligible,
                "Resumable run does not have complete declared artifact hashes.",
                store.GetLayout(record.RunId).RunRoot,
                RunId: record.RunId);
        }

        ManagedArtifactStore artifacts = new(store);
        foreach (ProjectPackRunArtifactPointer output in outputs)
        {
            ManagedArtifactVerificationResult verification = artifacts.Verify(
                ManagedArtifactId.Create(record.RunId, output.Id),
                workspace);
            if (!verification.Succeeded || verification.Availability != ManagedArtifactAvailability.Available)
            {
                return new ProjectPackRunDiagnostic(
                    verification.Diagnostic?.ErrorCode ?? ProjectPackRunErrorCode.ResumeNotEligible,
                    verification.Diagnostic?.Summary ?? "Declared artifact identity is incomplete or changed.",
                    output.Path,
                    record.RunId);
            }
        }

        return null;
    }
}
