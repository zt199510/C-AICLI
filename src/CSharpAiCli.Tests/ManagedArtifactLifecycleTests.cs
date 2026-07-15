using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CSharpAiCli.Cli;
using CSharpAiCli.Core;
using CSharpAiCli.ProjectPacks.GerberTiff;
using CSharpAiCli.ProjectPacks.Runtime;

namespace CSharpAiCli.Tests;

[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class ManagedArtifactLifecycleTestCollection
{
    public const string CollectionName = "managed-artifact-lifecycle-serial";
}

[Collection(ManagedArtifactLifecycleTestCollection.CollectionName)]
public sealed class ManagedArtifactLifecycleTests
{
    [Fact]
    public void Artifact_retention_default_is_user_level_and_is_frozen_into_new_manifests()
    {
        string root = Path.Combine(Path.GetTempPath(), "caicli-artifact-config-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            string workspacePath = Path.Combine(root, "workspace");
            string profilePath = Path.Combine(root, "profile");
            Directory.CreateDirectory(Path.Combine(workspacePath, ".caicli"));
            Directory.CreateDirectory(Path.Combine(profilePath, ".caicli"));
            File.WriteAllText(
                Path.Combine(workspacePath, ".caicli", "config.json"),
                "{\"artifactRetention\":{\"defaultMinimumAgeDays\":1}}");
            File.WriteAllText(
                Path.Combine(profilePath, ".caicli", "config.json"),
                "{\"artifactRetention\":{\"defaultMinimumAgeDays\":45}}");
            CliEnvironmentSnapshot snapshot = CliEnvironmentSnapshot.Create(
                workspacePath,
                currentDirectory: workspacePath,
                userProfile: profilePath,
                dotnetSdkVersion: "9.0.308",
                dotnetRuntime: ".NET 9",
                openAiApiKey: null,
                openAiModel: null,
                hasGlobalJson: false);
            Assert.Equal(45, snapshot.Configuration.ArtifactRetention.DefaultMinimumAgeDays);
            Assert.Equal("user config", snapshot.Configuration.ArtifactRetention.Source);
            Assert.Contains(snapshot.Configuration.Warnings, warning => warning.Contains("workspace config", StringComparison.Ordinal));

            ManagedProjectPackRunStore store = ManagedProjectPackRunStore.Create(snapshot);
            DateTimeOffset now = DateTimeOffset.Parse("2026-07-15T07:00:00Z");
            string runId = ProjectPackRunId.Create(now);
            string plan = TestHash("plan");
            string policy = TestHash("policy");
            ProjectPackRunRecord record = new(
                1, runId, 0, "gerber-tiff", "1.0.0", "plan", plan, policy,
                ProjectPackRunState.Failed, now, now);
            ProjectPackRunCheckpoint checkpoint = new(1, runId, 0, ProjectPackRunState.Failed, plan, policy, now);
            ProjectPackRunMutationResult created = store.CreateRun(record, checkpoint, "{}", "{}");
            ManagedProjectPackRunLayout layout = store.GetLayout(runId);
            string artifactPath = Path.Combine(layout.ReportsPath, "retention.txt");
            File.WriteAllText(artifactPath, "retention");
            byte[] bytes = File.ReadAllBytes(artifactPath);
            ProjectPackRunRecord updated = created.Record!.WithArtifacts(
                [new ProjectPackRunArtifactPointer(
                    "retention-report", "report", "managed-run", "reports/retention.txt", true,
                    bytes.LongLength, Convert.ToHexString(SHA256.HashData(bytes)))],
                now);
            ProjectPackRunCheckpoint updatedCheckpoint = new(1, runId, updated.Revision, updated.State, plan, policy, now);
            Assert.True(store.Update(updated, updatedCheckpoint, created.Record.Revision).Succeeded);
            ManagedArtifactManifest manifest = Assert.IsType<ManagedArtifactManifest>(new ManagedArtifactStore(store).ReadManifest(runId).Manifest);
            Assert.Equal(45, Assert.Single(manifest.Artifacts).Retention.DefaultMinimumAgeDays);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void Artifact_identity_manifest_store_and_renderers_are_stable_and_read_only()
    {
        using LifecycleFixture test = LifecycleFixture.Create();
        ProjectPackRunMutationResult run = test.CreateRun(ProjectPackRunState.Failed, test.Now.AddDays(-40));
        (ProjectPackRunMutationResult updated, ProjectPackRunArtifactPointer pointer, string path) = test.AddManagedArtifact(
            run,
            "report",
            "inspection-report",
            "reports/result.txt",
            "stable-evidence",
            test.Now.AddDays(-40));
        ManagedArtifactStore artifacts = new(test.Store);
        string artifactId = ManagedArtifactId.Create(updated.Record!.RunId, pointer.Id);

        ManagedArtifactListResult first = artifacts.List(updated.Record.RunId);
        ManagedArtifactListResult second = artifacts.List(updated.Record.RunId);
        ManagedArtifactReadResult read = artifacts.Read(artifactId);
        ManagedArtifactVerificationResult verified = artifacts.Verify(artifactId, test.Workspace);

        Assert.Single(first.Artifacts);
        Assert.Equal(ManagedArtifactRenderer.RenderListJson(first), ManagedArtifactRenderer.RenderListJson(second));
        Assert.True(read.Succeeded);
        Assert.Equal(artifactId, read.Artifact?.ArtifactId);
        Assert.True(verified.Succeeded);
        Assert.Equal(File.ReadAllBytes(path).LongLength, verified.ObservedSize);
        Assert.Equal(
            ManagedArtifactRenderer.RenderShowJson(read.Manifest!, read.Artifact!),
            ManagedArtifactRenderer.RenderShowJson(read.Manifest!, read.Artifact!));
        Assert.Contains("# C# AI CLI Managed Artifact", ManagedArtifactRenderer.RenderExport(read.Manifest!, read.Artifact!, "markdown"));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Artifact_store_reports_missing_corrupt_and_unsupported_manifests_without_repair()
    {
        using LifecycleFixture missing = LifecycleFixture.Create();
        ProjectPackRunMutationResult missingRun = missing.CreateRun(ProjectPackRunState.Failed, missing.Now);
        ManagedProjectPackRunLayout missingLayout = missing.Store.GetLayout(missingRun.Record!.RunId);
        File.Delete(missingLayout.ArtifactManifestPath);
        Assert.Equal(
            ManagedArtifactErrorCode.ManifestMissing,
            new ManagedArtifactStore(missing.Store).ReadManifest(missingRun.Record.RunId).Diagnostic?.ErrorCode);

        using LifecycleFixture corrupt = LifecycleFixture.Create();
        ProjectPackRunMutationResult corruptRun = corrupt.CreateRun(ProjectPackRunState.Failed, corrupt.Now);
        ManagedProjectPackRunLayout corruptLayout = corrupt.Store.GetLayout(corruptRun.Record!.RunId);
        File.WriteAllText(corruptLayout.ArtifactManifestPath, "{not-json");
        Assert.Equal(
            ManagedArtifactErrorCode.ManifestCorrupt,
            new ManagedArtifactStore(corrupt.Store).ReadManifest(corruptRun.Record.RunId).Diagnostic?.ErrorCode);

        using LifecycleFixture unsupported = LifecycleFixture.Create();
        ProjectPackRunMutationResult unsupportedRun = unsupported.CreateRun(ProjectPackRunState.Failed, unsupported.Now);
        ManagedProjectPackRunLayout unsupportedLayout = unsupported.Store.GetLayout(unsupportedRun.Record!.RunId);
        string json = File.ReadAllText(unsupportedLayout.ArtifactManifestPath)
            .Replace("\"schemaVersion\": 1", "\"schemaVersion\": 99", StringComparison.Ordinal);
        File.WriteAllText(unsupportedLayout.ArtifactManifestPath, json);
        Assert.Equal(
            ManagedArtifactErrorCode.SchemaUnsupported,
            new ManagedArtifactStore(unsupported.Store).ReadManifest(unsupportedRun.Record.RunId).Diagnostic?.ErrorCode);
    }

    [Fact]
    public void Human_accept_requires_current_hard_verification_and_rejects_double_decision()
    {
        using LifecycleFixture test = LifecycleFixture.Create();
        ProjectPackRunMutationResult awaiting = test.CreateRun(
            ProjectPackRunState.AwaitingAcceptance,
            test.Now,
            inspectStatus: ProjectPackStageStatus.Succeeded);
        ProjectPackRunMutationResult withReport = test.AddVerificationReport(awaiting, hardPassed: true);
        ProjectPackAcceptanceService service = new(test.Store);

        ProjectPackRunMutationResult accepted = service.Accept(
            withReport.Record!.RunId,
            "human-reviewer",
            "looks correct",
            test.Now.AddMinutes(1));
        ProjectPackRunMutationResult second = service.Reject(
            withReport.Record.RunId,
            "human-reviewer",
            "changed mind",
            test.Now.AddMinutes(2));

        Assert.True(accepted.Succeeded);
        Assert.Equal(ProjectPackRunState.Accepted, accepted.Record?.State);
        Assert.Equal(ManagedArtifactId.Create(withReport.Record.RunId, "inspection-report"), accepted.Record?.Acceptance?.VerificationArtifactId);
        Assert.Equal(ProjectPackRunErrorCode.DecisionConflict, second.Diagnostic?.ErrorCode);
    }

    [Fact]
    public void Human_gate_blocks_failed_or_fake_awaiting_verification_and_redacts_reject_reason()
    {
        using LifecycleFixture failed = LifecycleFixture.Create();
        ProjectPackRunMutationResult failedAwaiting = failed.CreateRun(
            ProjectPackRunState.AwaitingAcceptance,
            failed.Now,
            inspectStatus: ProjectPackStageStatus.Failed);
        ProjectPackRunMutationResult failedReport = failed.AddVerificationReport(failedAwaiting, hardPassed: false);
        ProjectPackRunMutationResult blocked = new ProjectPackAcceptanceService(failed.Store).Accept(
            failedReport.Record!.RunId,
            "reviewer",
            null,
            failed.Now.AddMinutes(1));
        Assert.Equal(ProjectPackRunErrorCode.HardVerificationRequired, blocked.Diagnostic?.ErrorCode);

        using LifecycleFixture fake = LifecycleFixture.Create();
        ProjectPackRunMutationResult fakeAwaiting = fake.CreateRun(
            ProjectPackRunState.AwaitingAcceptance,
            fake.Now,
            inspectStatus: ProjectPackStageStatus.Succeeded);
        ProjectPackRunMutationResult fakeBlocked = new ProjectPackAcceptanceService(fake.Store).Accept(
            fakeAwaiting.Record!.RunId,
            "reviewer",
            null,
            fake.Now.AddMinutes(1));
        Assert.Equal(ProjectPackRunErrorCode.HardVerificationRequired, fakeBlocked.Diagnostic?.ErrorCode);

        using LifecycleFixture rejected = LifecycleFixture.Create();
        ProjectPackRunMutationResult rejectAwaiting = rejected.CreateRun(
            ProjectPackRunState.AwaitingAcceptance,
            rejected.Now,
            inspectStatus: ProjectPackStageStatus.Succeeded);
        ProjectPackRunMutationResult rejectReport = rejected.AddVerificationReport(rejectAwaiting, hardPassed: true);
        ProjectPackRunMutationResult decision = new ProjectPackAcceptanceService(rejected.Store).Reject(
            rejectReport.Record!.RunId,
            "reviewer",
            "token=super-secret-value",
            rejected.Now.AddMinutes(1));
        Assert.True(decision.Succeeded);
        Assert.DoesNotContain("super-secret-value", decision.Record?.Acceptance?.Reason, StringComparison.Ordinal);
        Assert.Contains("[redacted]", decision.Record?.Acceptance?.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Human_gate_rejects_verification_report_mutation()
    {
        using LifecycleFixture test = LifecycleFixture.Create();
        ProjectPackRunMutationResult awaiting = test.CreateRun(
            ProjectPackRunState.AwaitingAcceptance,
            test.Now,
            inspectStatus: ProjectPackStageStatus.Succeeded);
        ProjectPackRunMutationResult withReport = test.AddVerificationReport(awaiting, hardPassed: true);
        string reportPath = Path.Combine(test.Store.GetLayout(withReport.Record!.RunId).ReportsPath, "verification.json");
        File.AppendAllText(reportPath, " ");

        ProjectPackRunMutationResult blocked = new ProjectPackAcceptanceService(test.Store).Accept(
            withReport.Record.RunId,
            "reviewer",
            null,
            test.Now.AddMinutes(1));

        Assert.Equal(ProjectPackRunErrorCode.HardVerificationRequired, blocked.Diagnostic?.ErrorCode);
    }

    [Fact]
    public void Prune_dry_run_and_apply_delete_only_owned_terminal_managed_artifacts_and_retain_tombstone()
    {
        using LifecycleFixture test = LifecycleFixture.Create();
        DateTimeOffset old = test.Now.AddDays(-40);
        ProjectPackRunMutationResult run = test.CreateRun(ProjectPackRunState.Failed, old);
        (run, ProjectPackRunArtifactPointer managed, string managedPath) = test.AddManagedArtifact(
            run, "managed-report", "inspection-report", "reports/old.txt", "managed", old);
        (run, _, string workspacePath) = test.AddWorkspaceArtifact(
            run, "workspace-output", "tiff-output", "output/board.tiff", "workspace", old, "workspace-output");
        (run, _, string sourcePath) = test.AddWorkspaceArtifact(
            run, "source-input", "source-input", "input/board.gbr", "source", old, "source");
        ProjectPackRunArtifactPointer outside = new(
            "outside-pointer", "external-output", "external-pointer", "C:/outside/output.bin", true, 10, TestHash("outside"));
        run = test.UpdateArtifacts(run, run.Record!.Artifacts.Concat([outside]).ToArray(), old);
        ManagedArtifactStore store = new(test.Store);
        ManagedArtifactPruneFilter filter = new(test.Now.AddDays(-30));

        ManagedArtifactPruneResult dryRun = store.Prune(filter, apply: false, test.Workspace, test.Now);
        ManagedArtifactPruneResult applied = store.Prune(filter, apply: true, test.Workspace, test.Now);
        ManagedArtifactReadResult manifest = store.ReadManifest(run.Record!.RunId);

        Assert.Single(dryRun.Items);
        Assert.Equal(ManagedArtifactId.Create(run.Record.RunId, managed.Id), dryRun.Items[0].ArtifactId);
        Assert.False(dryRun.Items[0].Deleted);
        Assert.Single(applied.Items);
        Assert.True(applied.Items[0].Deleted);
        Assert.False(File.Exists(managedPath));
        Assert.True(File.Exists(workspacePath));
        Assert.True(File.Exists(sourcePath));
        Assert.NotNull(manifest.Manifest?.Artifacts.Single(artifact => artifact.PointerId == managed.Id).Tombstone);
        Assert.True(store.Verify(applied.Items[0].ArtifactId, test.Workspace).Succeeded);
        Assert.Equal(ManagedArtifactAvailability.Pruned, store.Verify(applied.Items[0].ArtifactId, test.Workspace).Availability);
    }

    [Fact]
    public void Prune_filters_age_status_and_size_and_preserves_interrupted_runs()
    {
        using LifecycleFixture test = LifecycleFixture.Create();
        DateTimeOffset old = test.Now.AddDays(-40);
        ProjectPackRunMutationResult failed = test.CreateRun(ProjectPackRunState.Failed, old);
        (failed, _, _) = test.AddManagedArtifact(failed, "small", "report", "reports/small.bin", "12345", old);
        ProjectPackRunMutationResult interrupted = test.CreateRun(ProjectPackRunState.Interrupted, old);
        (interrupted, _, string interruptedPath) = test.AddManagedArtifact(
            interrupted, "partial", "render-intermediate", "artifacts/partial.bin", "partial", old);
        ManagedArtifactStore store = new(test.Store);

        ManagedArtifactPruneResult tooLarge = store.Prune(
            new ManagedArtifactPruneFilter(test.Now.AddDays(-30), MinimumSize: 10),
            false,
            test.Workspace,
            test.Now);
        ManagedArtifactPruneResult wrongStatus = store.Prune(
            new ManagedArtifactPruneFilter(
                test.Now.AddDays(-30),
                new HashSet<string>([ProjectPackRunState.Accepted], StringComparer.Ordinal)),
            false,
            test.Workspace,
            test.Now);

        Assert.Empty(tooLarge.Items);
        Assert.Empty(wrongStatus.Items);
        Assert.True(File.Exists(interruptedPath));
    }

    [Fact]
    public void Prune_detects_identity_race_and_does_not_delete_changed_content()
    {
        using LifecycleFixture test = LifecycleFixture.Create();
        DateTimeOffset old = test.Now.AddDays(-40);
        ProjectPackRunMutationResult run = test.CreateRun(ProjectPackRunState.Failed, old);
        (run, _, string path) = test.AddManagedArtifact(
            run, "race", "report", "reports/race.bin", "original", old);
        ManagedArtifactStore store = new(test.Store, selectedPath => File.WriteAllText(selectedPath, "changed-during-prune"));

        ManagedArtifactPruneResult result = store.Prune(
            new ManagedArtifactPruneFilter(test.Now.AddDays(-30)),
            true,
            test.Workspace,
            test.Now);

        Assert.Single(result.Items);
        Assert.False(result.Items[0].Deleted);
        Assert.Equal(ManagedArtifactErrorCode.PruneRace, result.Items[0].ErrorCode);
        Assert.True(File.Exists(path));
        Assert.Equal("changed-during-prune", File.ReadAllText(path));
        Assert.Null(store.ReadManifest(run.Record!.RunId).Manifest?.Artifacts.Single().Tombstone);
    }

    [Fact]
    public void Prune_preserves_locked_files_and_outside_root_pointers()
    {
        using LifecycleFixture test = LifecycleFixture.Create();
        DateTimeOffset old = test.Now.AddDays(-40);
        string outsidePath = Path.Combine(test.Root, "outside.bin");
        File.WriteAllText(outsidePath, "outside");
        ProjectPackRunMutationResult run = test.CreateRun(ProjectPackRunState.Failed, old);
        (run, _, string lockedPath) = test.AddManagedArtifact(
            run, "locked", "report", "reports/locked.bin", "locked", old);
        ProjectPackRunArtifactPointer escaped = new(
            "escaped", "report", "managed-run", "../outside.bin", true,
            new FileInfo(outsidePath).Length, TestHash("outside"));
        run = test.UpdateArtifacts(run, run.Record!.Artifacts.Concat([escaped]).ToArray(), old);
        using FileStream locked = new(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None);

        ManagedArtifactPruneResult result = new ManagedArtifactStore(test.Store).Prune(
            new ManagedArtifactPruneFilter(test.Now.AddDays(-30)),
            true,
            test.Workspace,
            test.Now);

        Assert.True(File.Exists(lockedPath));
        Assert.True(File.Exists(outsidePath));
        Assert.DoesNotContain(result.Items, item => item.Path == escaped.Path);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.ArtifactId == ManagedArtifactId.Create(run.Record!.RunId, "locked"));
    }

    [Fact]
    public void Prune_refuses_reparse_artifact_paths_without_deleting_target()
    {
        using LifecycleFixture test = LifecycleFixture.Create();
        DateTimeOffset old = test.Now.AddDays(-40);
        ProjectPackRunMutationResult run = test.CreateRun(ProjectPackRunState.Failed, old);
        ManagedProjectPackRunLayout layout = test.Store.GetLayout(run.Record!.RunId);
        string outsideDirectory = Path.Combine(test.Root, "outside-reparse");
        Directory.CreateDirectory(outsideDirectory);
        string outsidePath = Path.Combine(outsideDirectory, "target.bin");
        File.WriteAllText(outsidePath, "outside-target");
        string link = Path.Combine(layout.ArtifactsPath, "linked");
        try
        {
            Directory.CreateSymbolicLink(link, outsideDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        ProjectPackRunArtifactPointer pointer = new(
            "reparse-target",
            "render-intermediate",
            "managed-run",
            "artifacts/linked/target.bin",
            true,
            new FileInfo(outsidePath).Length,
            TestHash("outside-target"));
        run = test.UpdateArtifacts(run, [pointer], old);

        ManagedArtifactPruneResult result = new ManagedArtifactStore(test.Store).Prune(
            new ManagedArtifactPruneFilter(test.Now.AddDays(-30)),
            true,
            test.Workspace,
            test.Now);

        Assert.Empty(result.Items);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.ErrorCode == ManagedArtifactErrorCode.ReparsePoint);
        Assert.True(File.Exists(outsidePath));
    }

    [Fact]
    public void Safe_resume_invalidates_tool_input_and_policy_changes()
    {
        using RestartFixture tool = RestartFixture.Create();
        ProjectPackRunMutationResult toolRun = tool.CreateInterrupted();
        File.AppendAllText(tool.ToolPaths["gerbv"], "changed");
        ProjectPackResumeEligibility toolResult = tool.Service.EvaluateResume(
            toolRun.Record!.RunId, tool.Workspace, tool.ToolPaths, tool.Policy);
        Assert.Equal(ProjectPackRunErrorCode.ToolChanged, toolResult.ErrorCode);

        using RestartFixture input = RestartFixture.Create();
        ProjectPackRunMutationResult inputRun = input.CreateInterrupted();
        File.AppendAllText(input.GerberPath, "changed");
        ProjectPackResumeEligibility inputResult = input.Service.EvaluateResume(
            inputRun.Record!.RunId, input.Workspace, input.ToolPaths, input.Policy);
        Assert.Equal(ProjectPackRunErrorCode.InputChanged, inputResult.ErrorCode);

        using RestartFixture policy = RestartFixture.Create();
        ProjectPackRunMutationResult policyRun = policy.CreateInterrupted();
        ProjectPackResumeEligibility policyResult = policy.Service.EvaluateResume(
            policyRun.Record!.RunId,
            policy.Workspace,
            policy.ToolPaths,
            ProjectPackRunPolicyFingerprint.Compute("changed-policy"));
        Assert.Equal(ProjectPackRunErrorCode.PolicyChanged, policyResult.ErrorCode);
    }

    [Fact]
    public void Interrupted_restart_reserves_new_attempt_output_and_preserves_parent_partial_evidence()
    {
        using RestartFixture test = RestartFixture.Create();
        ProjectPackRunMutationResult interrupted = test.CreateInterrupted(withPartialEvidence: true);
        string parentPartial = Path.Combine(test.Store.GetLayout(interrupted.Record!.RunId).ArtifactsPath, "partial.bin");

        ProjectPackRestartPreparation preparation = new ProjectPackRestartService(test.Store).PrepareExecuteRestart(
            interrupted.Record.RunId,
            test.Workspace,
            test.ToolPaths,
            test.Policy,
            test.Now.AddMinutes(1));
        ProjectPackRunCorrelation childCorrelation = new(
            rootRunId: interrupted.Record.RunId,
            parentRunId: interrupted.Record.RunId,
            attempt: preparation.Attempt!.Value);
        ProjectPackRunMutationResult child = test.Service.CreateAndStage(
            preparation.Plan!,
            test.Workspace,
            test.ToolPaths,
            test.Policy,
            test.Now.AddMinutes(2),
            childCorrelation,
            preparation.NewRunId);

        Assert.True(preparation.Succeeded);
        Assert.Equal(2, preparation.Attempt);
        Assert.EndsWith(".attempt-0002", preparation.OutputDirectory, StringComparison.Ordinal);
        Assert.NotEqual(interrupted.Record.RunId, preparation.NewRunId);
        Assert.True(File.Exists(parentPartial));
        Assert.Equal(ProjectPackRunState.Interrupted, test.Store.Read(interrupted.Record.RunId).Record?.State);
        Assert.Equal(ProjectPackRunState.Ready, child.Record?.State);
        Assert.Equal(2, child.Record?.Correlation.Attempt);
        Assert.False(child.Checkpoint?.ApprovalPersisted);

        ProjectPackRestartPreparation duplicate = new ProjectPackRestartService(test.Store).PrepareExecuteRestart(
            interrupted.Record.RunId,
            test.Workspace,
            test.ToolPaths,
            test.Policy,
            test.Now.AddMinutes(3));
        Assert.False(duplicate.Succeeded);
        Assert.Equal(ProjectPackRunErrorCode.DecisionConflict, duplicate.Diagnostic?.ErrorCode);
    }

    [Fact]
    public void Artifact_cli_list_show_verify_export_and_prune_default_are_stable()
    {
        using LifecycleFixture test = LifecycleFixture.Create();
        ProjectPackRunMutationResult run = test.CreateRun(ProjectPackRunState.Failed, test.Now.AddDays(-40));
        (run, ProjectPackRunArtifactPointer pointer, _) = test.AddManagedArtifact(
            run, "cli-report", "report", "reports/cli.txt", "cli", test.Now.AddDays(-40));
        string artifactId = ManagedArtifactId.Create(run.Record!.RunId, pointer.Id);

        string list = Invoke(test.Snapshot, ["artifacts", "list", "--run", run.Record.RunId, "--output", "json", "--workspace", test.Workspace.RootPath], out int listExit);
        string show = Invoke(test.Snapshot, ["artifacts", "show", artifactId, "--output", "json", "--workspace", test.Workspace.RootPath], out int showExit);
        string verify = Invoke(test.Snapshot, ["artifacts", "verify", artifactId, "--output", "json", "--workspace", test.Workspace.RootPath], out int verifyExit);
        string export = Invoke(test.Snapshot, ["artifacts", "export", artifactId, "--format", "markdown", "--workspace", test.Workspace.RootPath], out int exportExit);
        string prune = Invoke(test.Snapshot, ["artifacts", "prune", "--older-than", "30d", "--output", "json", "--workspace", test.Workspace.RootPath], out int pruneExit);

        Assert.Equal(0, listExit);
        Assert.Equal("artifacts.list", JsonNode.Parse(list)?["type"]?.GetValue<string>());
        Assert.Equal(0, showExit);
        Assert.Equal("artifacts.show", JsonNode.Parse(show)?["type"]?.GetValue<string>());
        Assert.Equal(0, verifyExit);
        Assert.Equal("verified", JsonNode.Parse(verify)?["status"]?.GetValue<string>());
        Assert.Equal(0, exportExit);
        Assert.Contains("# C# AI CLI Managed Artifact", export);
        Assert.Equal(0, pruneExit);
        Assert.Equal("dry-run", JsonNode.Parse(prune)?["mode"]?.GetValue<string>());
    }

    [Fact]
    public void Human_accept_cli_updates_correlated_job_without_task_report()
    {
        using LifecycleFixture test = LifecycleFixture.Create();
        string jobId = JobIdGenerator.Create(test.Now);
        JobRecordStore jobStore = JobRecordStore.Create(test.Snapshot);
        jobStore.Create(JobRecord.CreateRunning(
            jobId,
            test.Now,
            new JobCommandSummary("packs run", WorkspaceRoot: test.Workspace.RootPath)));
        ProjectPackRunMutationResult awaiting = test.CreateRun(
            ProjectPackRunState.AwaitingAcceptance,
            test.Now,
            ProjectPackStageStatus.Succeeded,
            new ProjectPackRunCorrelation(jobId: jobId));
        ProjectPackRunMutationResult report = test.AddVerificationReport(awaiting, hardPassed: true);

        string output = Invoke(
            test.Snapshot,
            ["packs", "accept", report.Record!.RunId, "--actor", "reviewer", "--output", "json", "--workspace", test.Workspace.RootPath],
            out int exitCode);
        JobRecord job = Assert.IsType<JobRecord>(jobStore.Read(jobId).Record);

        Assert.Equal(0, exitCode);
        Assert.Equal(ProjectPackRunState.Accepted, JsonNode.Parse(output)?["run"]?["state"]?.GetValue<string>());
        Assert.Null(job.TaskReport);
        Assert.Contains(job.Artifacts, artifact => artifact.Kind == JobArtifactKind.ProjectPackAcceptance);
        Assert.Contains(job.Warnings, warning => warning.Contains("Human acceptance is explicit", StringComparison.Ordinal));
    }

    [Fact]
    public void Manual_stale_recovery_correlates_running_run_job_and_queue_without_replay()
    {
        using LifecycleFixture test = LifecycleFixture.Create();
        string jobId = JobIdGenerator.Create(test.Now);
        string queueId = TaskQueueIdGenerator.Create(test.Now);
        JobRecordStore jobStore = JobRecordStore.Create(test.Snapshot);
        jobStore.Create(JobRecord.CreateRunning(
            jobId,
            test.Now,
            new JobCommandSummary("packs run", WorkspaceRoot: test.Workspace.RootPath)));
        TaskQueueStore queueStore = TaskQueueStore.Create(test.Snapshot);
        queueStore.Create(TaskQueueItem.CreatePending(
            queueId,
            test.Now,
            new TaskQueueRequest(TaskQueueCommandFamily.Exec, "pack", test.Workspace.RootPath)));
        Assert.True(queueStore.Start(queueId, test.Now).Succeeded);
        ProjectPackRunMutationResult running = test.CreateRun(
            ProjectPackRunState.Running,
            test.Now,
            correlation: new ProjectPackRunCorrelation(queueId, jobId),
            stages: [new ProjectPackStageCheckpoint("render", ProjectPackStageStatus.Running, 1, test.Now)]);

        string output = Invoke(
            test.Snapshot,
            ["packs", "recover", running.Record!.RunId, "--mark-interrupted", "--output", "json", "--workspace", test.Workspace.RootPath],
            out int exitCode);
        JobRecord job = Assert.IsType<JobRecord>(jobStore.Read(jobId).Record);
        TaskQueueItem queue = Assert.IsType<TaskQueueItem>(queueStore.Read(queueId).Item);

        Assert.Equal(0, exitCode);
        Assert.Equal(ProjectPackRunState.Interrupted, JsonNode.Parse(output)?["run"]?["state"]?.GetValue<string>());
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal(ProjectPackRunErrorCode.RestartRequired, job.ErrorCode);
        Assert.Equal(TaskQueueStatus.Failed, queue.Status);
        Assert.Equal(ProjectPackRunErrorCode.RestartRequired, queue.ErrorCode);
    }

    [Fact]
    public void Prune_apply_updates_job_pointer_and_retains_job_tombstone()
    {
        using LifecycleFixture test = LifecycleFixture.Create();
        DateTimeOffset old = test.Now.AddDays(-40);
        string jobId = JobIdGenerator.Create(old);
        ProjectPackRunMutationResult run = test.CreateRun(
            ProjectPackRunState.Failed,
            old,
            correlation: new ProjectPackRunCorrelation(jobId: jobId));
        (run, _, string path) = test.AddManagedArtifact(
            run, "job-prune", "report", "reports/job-prune.bin", "job-prune", old);
        JobRecordStore jobStore = JobRecordStore.Create(test.Snapshot);
        jobStore.Create(JobRecord.CreateRunning(
            jobId,
            old,
            new JobCommandSummary("packs run", WorkspaceRoot: test.Workspace.RootPath))
            .WithStatus(
                JobStatus.Failed,
                old,
                exitCode: 1,
                artifacts: [JobArtifact.FromPath(JobArtifactKind.ProjectPackVerificationReport, path)]));

        _ = Invoke(
            test.Snapshot,
            ["artifacts", "prune", "--older-than", "30d", "--apply", "--output", "json", "--workspace", test.Workspace.RootPath],
            out int exitCode);
        JobRecord job = Assert.IsType<JobRecord>(jobStore.Read(jobId).Record);

        Assert.Equal(0, exitCode);
        Assert.Contains(job.Artifacts, artifact => artifact.Kind == JobArtifactKind.ProjectPackVerificationReport && !artifact.Exists);
        Assert.Contains(job.Artifacts, artifact => artifact.Kind == JobArtifactKind.ProjectPackArtifactTombstone && artifact.Exists);
        Assert.Null(job.TaskReport);
    }

    private static string Invoke(CliEnvironmentSnapshot snapshot, string[] args, out int exitCode)
    {
        using StringWriter output = new();
        exitCode = CliCommandFactory.Create(output, _ => snapshot).Parse(args).Invoke();
        return output.ToString();
    }

    private static string TestHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class LifecycleFixture : IDisposable
    {
        private LifecycleFixture(string root, WorkspaceContext workspace, CliEnvironmentSnapshot snapshot)
        {
            Root = root;
            Workspace = workspace;
            Snapshot = snapshot;
            Store = ManagedProjectPackRunStore.Create(snapshot);
        }

        public DateTimeOffset Now { get; } = DateTimeOffset.Parse("2026-07-15T08:00:00Z");
        public string Root { get; }
        public WorkspaceContext Workspace { get; }
        public CliEnvironmentSnapshot Snapshot { get; }
        public ManagedProjectPackRunStore Store { get; }

        public static LifecycleFixture Create()
        {
            string root = Path.Combine(Path.GetTempPath(), "caicli-artifact-lifecycle-tests-" + Guid.NewGuid().ToString("N"));
            string workspacePath = Path.Combine(root, "workspace");
            Directory.CreateDirectory(workspacePath);
            CliEnvironmentSnapshot snapshot = CliEnvironmentSnapshot.Create(
                workspacePath,
                currentDirectory: workspacePath,
                userProfile: Path.Combine(root, "profile"),
                dotnetSdkVersion: "9.0.308",
                dotnetRuntime: ".NET 9",
                openAiApiKey: null,
                openAiModel: null,
                hasGlobalJson: false);
            return new LifecycleFixture(root, snapshot.Workspace, snapshot);
        }

        public ProjectPackRunMutationResult CreateRun(
            string state,
            DateTimeOffset timestamp,
            string inspectStatus = ProjectPackStageStatus.Pending,
            ProjectPackRunCorrelation? correlation = null,
            IReadOnlyList<ProjectPackStageCheckpoint>? stages = null)
        {
            string runId = ProjectPackRunId.Create(timestamp.AddTicks(Random.Shared.Next(1, 10_000)));
            string planFingerprint = TestHash("plan-" + runId);
            string policyFingerprint = TestHash("policy");
            ProjectPackRunRecord record = new(
                ProjectPackRunRecord.CurrentSchemaVersion,
                runId,
                0,
                "gerber-tiff",
                "1.0.0",
                "gerber-tiff-test",
                planFingerprint,
                policyFingerprint,
                state,
                timestamp,
                timestamp,
                correlation);
            ProjectPackRunCheckpoint checkpoint = new(
                ProjectPackRunCheckpoint.CurrentSchemaVersion,
                runId,
                0,
                state,
                planFingerprint,
                policyFingerprint,
                timestamp,
                stages ?? [new ProjectPackStageCheckpoint("inspect", inspectStatus, inspectStatus == ProjectPackStageStatus.Pending ? 0 : 1)]);
            return Store.CreateRun(record, checkpoint, "{}", "{}");
        }

        public (ProjectPackRunMutationResult Run, ProjectPackRunArtifactPointer Pointer, string Path) AddManagedArtifact(
            ProjectPackRunMutationResult current,
            string id,
            string kind,
            string relativePath,
            string content,
            DateTimeOffset timestamp)
        {
            ManagedProjectPackRunLayout layout = Store.GetLayout(current.Record!.RunId);
            string path = Path.Combine(layout.RunRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            ProjectPackRunArtifactPointer pointer = Pointer(id, kind, "managed-run", relativePath, path);
            ProjectPackRunMutationResult updated = UpdateArtifacts(
                current,
                current.Record.Artifacts.Concat([pointer]).ToArray(),
                timestamp);
            return (updated, pointer, path);
        }

        public (ProjectPackRunMutationResult Run, ProjectPackRunArtifactPointer Pointer, string Path) AddWorkspaceArtifact(
            ProjectPackRunMutationResult current,
            string id,
            string kind,
            string relativePath,
            string content,
            DateTimeOffset timestamp,
            string scope)
        {
            string path = Path.Combine(Workspace.RootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            ProjectPackRunArtifactPointer pointer = Pointer(id, kind, scope, relativePath, path);
            ProjectPackRunMutationResult updated = UpdateArtifacts(
                current,
                current.Record!.Artifacts.Concat([pointer]).ToArray(),
                timestamp);
            return (updated, pointer, path);
        }

        public ProjectPackRunMutationResult AddVerificationReport(
            ProjectPackRunMutationResult current,
            bool hardPassed)
        {
            string status = hardPassed ? "passed" : "failed";
            object payload = new
            {
                type = TiffVerificationSchema.ResultType,
                schemaVersion = TiffVerificationSchema.CurrentVersion,
                runId = current.Record!.RunId,
                status,
                hardVerificationPassed = hardPassed,
                humanReviewRequired = hardPassed,
                correctnessProof = false,
                levels = new[]
                {
                    new { level = TiffVerificationLevel.FileValid, status, summary = "file" },
                    new { level = TiffVerificationLevel.MetadataValid, status, summary = "metadata" },
                    new { level = TiffVerificationLevel.ContentCompared, status = TiffVerificationStatus.NotRequested, summary = "content" },
                    new { level = TiffVerificationLevel.HumanReviewRequired, status = hardPassed ? TiffVerificationStatus.Required : TiffVerificationStatus.NotRequested, summary = "human" }
                },
                artifacts = Array.Empty<object>(),
                pixelComparisons = Array.Empty<object>(),
                diagnostics = hardPassed ? Array.Empty<object>() : [new { code = "failed", severity = "error", summary = "failed" }],
                summary = status
            };
            string json = JsonSerializer.Serialize(payload);
            return AddManagedArtifact(
                current,
                "inspection-report",
                "tiff-verification-json",
                "reports/verification.json",
                json,
                Now).Run;
        }

        public ProjectPackRunMutationResult UpdateArtifacts(
            ProjectPackRunMutationResult current,
            IReadOnlyList<ProjectPackRunArtifactPointer> artifacts,
            DateTimeOffset timestamp)
        {
            ProjectPackRunRecord updated = current.Record!.WithArtifacts(artifacts, timestamp);
            ProjectPackRunCheckpoint checkpoint = new(
                current.Checkpoint!.SchemaVersion,
                current.Checkpoint.RunId,
                updated.Revision,
                updated.State,
                current.Checkpoint.PlanFingerprint,
                current.Checkpoint.PolicyFingerprint,
                timestamp,
                current.Checkpoint.Stages);
            return Store.Update(updated, checkpoint, current.Record.Revision);
        }

        private static ProjectPackRunArtifactPointer Pointer(
            string id,
            string kind,
            string scope,
            string relativePath,
            string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            return new ProjectPackRunArtifactPointer(
                id,
                kind,
                scope,
                relativePath.Replace('\\', '/'),
                true,
                bytes.LongLength,
                Convert.ToHexString(SHA256.HashData(bytes)));
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch
            {
            }
        }
    }

    private sealed class RestartFixture : IDisposable
    {
        private RestartFixture(
            string root,
            WorkspaceContext workspace,
            string gerberPath,
            Dictionary<string, string> toolPaths,
            GerberTiffRunPlanSnapshot plan,
            ManagedProjectPackRunStore store)
        {
            Root = root;
            Workspace = workspace;
            GerberPath = gerberPath;
            ToolPaths = toolPaths;
            Plan = plan;
            Store = store;
            Service = new ProjectPackRunService(store);
        }

        public DateTimeOffset Now { get; } = DateTimeOffset.Parse("2026-07-15T09:00:00Z");
        public string Root { get; }
        public WorkspaceContext Workspace { get; }
        public string GerberPath { get; }
        public Dictionary<string, string> ToolPaths { get; }
        public GerberTiffRunPlanSnapshot Plan { get; }
        public ManagedProjectPackRunStore Store { get; }
        public ProjectPackRunService Service { get; }
        public string Policy { get; } = ProjectPackRunPolicyFingerprint.Compute("restart-test-policy");

        public static RestartFixture Create()
        {
            string root = Path.Combine(Path.GetTempPath(), "caicli-restart-tests-" + Guid.NewGuid().ToString("N"));
            string workspacePath = Path.Combine(root, "workspace");
            string inputPath = Path.Combine(workspacePath, "input");
            Directory.CreateDirectory(inputPath);
            string gerberPath = Path.Combine(inputPath, "board.gbr");
            File.WriteAllText(gerberPath, "gerber");
            string toolsPath = Path.Combine(root, "tools");
            Directory.CreateDirectory(toolsPath);
            Dictionary<string, string> tools = new(StringComparer.Ordinal)
            {
                ["gerbv"] = Path.Combine(toolsPath, "gerbv.exe"),
                ["imagemagick"] = Path.Combine(toolsPath, "magick.exe")
            };
            File.WriteAllText(tools["gerbv"], "gerbv");
            File.WriteAllText(tools["imagemagick"], "magick");
            WorkspaceContext workspace = WorkspaceContext.Detect(workspacePath, root);
            GerberTiffConversionPlan plan = new GerberTiffConversionPlanBuilder().Build(
                workspace,
                "input",
                "output",
                tools);
            Assert.True(plan.Runnable);
            GerberTiffRunPlanSnapshot snapshot = GerberTiffRunPlanLoader.Load(
                GerberTiffPlanRenderer.RenderJson(plan, "test"));
            return new RestartFixture(
                root,
                workspace,
                gerberPath,
                tools,
                snapshot,
                new ManagedProjectPackRunStore(Path.Combine(root, "profile", ".caicli", "runs")));
        }

        public ProjectPackRunMutationResult CreateInterrupted(bool withPartialEvidence = false)
        {
            ProjectPackRunMutationResult ready = Service.CreateAndStage(
                Plan,
                Workspace,
                ToolPaths,
                Policy,
                Now,
                runId: ProjectPackRunId.Create(Now));
            return Service.ExecuteFake(
                ready.Record!.RunId,
                withPartialEvidence ? new InterruptedEvidenceDriver() : new FakeProjectPackRunDriver(ProjectPackDriverBehavior.Interrupted),
                Now.AddSeconds(1));
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch
            {
            }
        }

        private sealed class InterruptedEvidenceDriver : IProjectPackRunDriver
        {
            public ProjectPackRunDriverResult Execute(
                ProjectPackRunExecutionContext context,
                CancellationToken cancellationToken = default)
            {
                string path = Path.Combine(context.Layout.ArtifactsPath, "partial.bin");
                byte[] bytes = Encoding.UTF8.GetBytes("partial-evidence");
                File.WriteAllBytes(path, bytes);
                ProjectPackRunArtifactPointer pointer = new(
                    "partial-evidence",
                    "render-intermediate",
                    "managed-run",
                    "artifacts/partial.bin",
                    true,
                    bytes.LongLength,
                    Convert.ToHexString(SHA256.HashData(bytes)));
                return new ProjectPackRunDriverResult(
                    ProjectPackStageStatus.Interrupted,
                    [new ProjectPackDriverStageEvent(
                        "render",
                        ProjectPackStageStatus.Interrupted,
                        DateTimeOffset.UtcNow,
                        DateTimeOffset.UtcNow,
                        ProjectPackRunErrorCode.RestartRequired,
                        "interrupted",
                        [pointer])],
                    [pointer],
                    ProjectPackRunErrorCode.RestartRequired,
                    "interrupted");
            }
        }
    }
}
