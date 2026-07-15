using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CSharpAiCli.Core;
using CSharpAiCli.ProjectPacks;
using CSharpAiCli.ProjectPacks.GerberTiff;
using CSharpAiCli.ProjectPacks.Runtime;

namespace CSharpAiCli.Tests;

public sealed class ProjectPackRunRuntimeTests
{
    [Fact]
    public void Transition_table_allows_only_declared_edges_and_terminals_are_closed()
    {
        Assert.True(ProjectPackRunTransitionTable.CanTransition(ProjectPackRunState.Created, ProjectPackRunState.Discovered));
        Assert.True(ProjectPackRunTransitionTable.CanTransition(ProjectPackRunState.Ready, ProjectPackRunState.Running));
        Assert.True(ProjectPackRunTransitionTable.CanTransition(ProjectPackRunState.Running, ProjectPackRunState.Interrupted));
        Assert.False(ProjectPackRunTransitionTable.CanTransition(ProjectPackRunState.Created, ProjectPackRunState.Running));
        Assert.Empty(ProjectPackRunTransitionTable.Targets(ProjectPackRunState.Accepted));
        Assert.Empty(ProjectPackRunTransitionTable.Targets(ProjectPackRunState.Rejected));
        Assert.Empty(ProjectPackRunTransitionTable.Targets(ProjectPackRunState.Failed));
        Assert.Empty(ProjectPackRunTransitionTable.Targets(ProjectPackRunState.Canceled));
    }

    [Fact]
    public void Store_round_trips_atomically_and_fails_closed_for_corrupt_unsupported_and_conflicting_updates()
    {
        using TestRunWorkspace test = TestRunWorkspace.Create();
        ProjectPackRunMutationResult created = test.CreateAndStage();
        Assert.True(created.Succeeded, created.Diagnostic?.Summary);
        ProjectPackRunReadResult restarted = new ManagedProjectPackRunStore(test.RunsRoot).Read(created.Record!.RunId);
        Assert.True(restarted.Succeeded, restarted.Diagnostic?.Summary);
        Assert.Equal(ProjectPackRunState.Ready, restarted.Record?.State);
        Assert.False(restarted.Checkpoint?.ApprovalPersisted);

        ManagedProjectPackRunLayout layout = test.Store.GetLayout(created.Record.RunId);
        using (FileStream runLock = new(layout.LockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            ProjectPackRunRecord next = created.Record.Transition(ProjectPackRunState.Running, test.Now.AddSeconds(1));
            ProjectPackRunCheckpoint checkpoint = new(
                1, next.RunId, next.Revision, next.State, next.PlanFingerprint, next.PolicyFingerprint,
                test.Now.AddSeconds(1), created.Checkpoint!.Stages);
            ProjectPackRunMutationResult conflict = test.Store.Update(next, checkpoint, created.Record.Revision);
            Assert.False(conflict.Succeeded);
            Assert.Equal(ProjectPackRunErrorCode.ConcurrentConflict, conflict.Diagnostic?.ErrorCode);
        }

        File.WriteAllText(layout.RunRecordPath, "{ not-json");
        Assert.Equal(ProjectPackRunErrorCode.RecordCorrupt, test.Store.Read(created.Record.RunId).Diagnostic?.ErrorCode);
        File.WriteAllText(layout.RunRecordPath, "{\"schemaVersion\":999}");
        Assert.Equal(ProjectPackRunErrorCode.SchemaUnsupported, test.Store.Read(created.Record.RunId).Diagnostic?.ErrorCode);
    }

    [Fact]
    public void Create_and_stage_copies_only_inventory_files_with_safe_names_and_preserves_source_hashes()
    {
        using TestRunWorkspace test = TestRunWorkspace.Create();
        string before = Hash(test.GerberPath);

        ProjectPackRunMutationResult result = test.CreateAndStage();

        Assert.True(result.Succeeded, result.Diagnostic?.Summary);
        Assert.Equal(ProjectPackRunState.Ready, result.Record?.State);
        Assert.Equal(before, Hash(test.GerberPath));
        ManagedProjectPackRunLayout layout = test.Store.GetLayout(result.Record!.RunId);
        string staged = Assert.Single(Directory.EnumerateFiles(layout.StagingPath));
        Assert.Equal("0001-input-0001.gbr", Path.GetFileName(staged));
        Assert.Equal(before, Hash(staged));
        Assert.DoesNotContain("unknown", Path.GetFileName(staged), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PRIVATE-GERBER-CONTENT", File.ReadAllText(layout.InputManifestPath), StringComparison.Ordinal);
        Assert.DoesNotContain(test.WorkspacePath, File.ReadAllText(layout.InputManifestPath), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(ProjectPackDriverBehavior.Failure, ProjectPackRunState.Failed, ProjectPackRunErrorCode.DriverFailed)]
    [InlineData(ProjectPackDriverBehavior.Timeout, ProjectPackRunState.Failed, ProjectPackRunErrorCode.DriverTimedOut)]
    [InlineData(ProjectPackDriverBehavior.Cancel, ProjectPackRunState.Canceled, ProjectPackRunErrorCode.DriverCanceled)]
    [InlineData(ProjectPackDriverBehavior.PartialOutput, ProjectPackRunState.Failed, ProjectPackRunErrorCode.DriverPartialOutput)]
    [InlineData(ProjectPackDriverBehavior.Interrupted, ProjectPackRunState.Interrupted, ProjectPackRunErrorCode.RestartRequired)]
    public void Fake_driver_terminal_behaviors_are_explicit(
        string behavior,
        string expectedState,
        string expectedErrorCode)
    {
        using TestRunWorkspace test = TestRunWorkspace.Create();
        ProjectPackRunMutationResult ready = test.CreateAndStage();

        ProjectPackRunMutationResult result = test.Service.ExecuteFake(
            ready.Record!.RunId,
            new FakeProjectPackRunDriver(behavior, () => test.Now.AddSeconds(1)),
            test.Now.AddSeconds(1));

        Assert.True(result.Succeeded, result.Diagnostic?.Summary);
        Assert.Equal(expectedState, result.Record?.State);
        Assert.Equal(expectedErrorCode, result.Record?.ErrorCode);
        Assert.NotEqual(ProjectPackRunState.Accepted, result.Record?.State);
        Assert.NotEqual("succeeded", result.Record?.State);
        if (behavior == ProjectPackDriverBehavior.PartialOutput)
        {
            Assert.Contains(result.Record!.Artifacts, artifact => artifact.Kind == "fake-partial-evidence" && artifact.Exists);
        }
    }

    [Fact]
    public void Fake_success_flows_through_verifying_to_awaiting_acceptance_without_claiming_business_validation()
    {
        using TestRunWorkspace test = TestRunWorkspace.Create();
        ProjectPackRunMutationResult ready = test.CreateAndStage();

        ProjectPackRunMutationResult result = test.Service.ExecuteFake(
            ready.Record!.RunId,
            new FakeProjectPackRunDriver(ProjectPackDriverBehavior.Success, () => test.Now.AddSeconds(1)),
            test.Now.AddSeconds(1));

        Assert.True(result.Succeeded, result.Diagnostic?.Summary);
        Assert.Equal(ProjectPackRunState.AwaitingAcceptance, result.Record?.State);
        Assert.Contains("real conversion, TIFF verification, and acceptance remain unproven", result.Record?.Summary, StringComparison.Ordinal);
        Assert.False(result.Checkpoint?.ApprovalPersisted);
        Assert.Equal(ProjectPackStageStatus.Succeeded,
            result.Checkpoint?.Stages.Single(stage => stage.StageId == "inspect").Status);
        Assert.Contains(result.Record!.Artifacts, artifact => artifact.Kind == "fake-driver-evidence");
    }

    [Fact]
    public void Controlled_execution_rejects_malicious_success_without_declared_outputs()
    {
        using TestRunWorkspace test = TestRunWorkspace.Create();
        ProjectPackRunMutationResult ready = test.CreateAndStage();

        ProjectPackRunMutationResult result = test.Service.ExecuteControlled(
            ready.Record!.RunId,
            test.Plan,
            test.Context,
            test.ToolPaths,
            test.PolicyFingerprint,
            new EmptySuccessDriver(test.Now.AddSeconds(1)),
            test.Now.AddSeconds(1));

        Assert.True(result.Succeeded, result.Diagnostic?.Summary);
        Assert.Equal(ProjectPackRunState.Failed, result.Record?.State);
        Assert.Equal(ProjectPackRunErrorCode.PartialOutput, result.Record?.ErrorCode);
        Assert.NotEqual(ProjectPackRunState.Verifying, result.Record?.State);
    }

    [Fact]
    public void Resume_revalidates_staging_input_tool_output_and_policy_and_never_replays_interrupted_execute()
    {
        using TestRunWorkspace test = TestRunWorkspace.Create();
        ProjectPackRunMutationResult ready = test.CreateAndStage();
        string runId = ready.Record!.RunId;

        ProjectPackResumeEligibility eligible = test.Service.EvaluateResume(
            runId, test.Context, test.ToolPaths, test.PolicyFingerprint);
        Assert.True(eligible.Eligible, eligible.Summary);
        Assert.True(eligible.RequiresApproval);
        Assert.Equal("continue-after-current-approval", eligible.NextAction);

        ProjectPackResumeEligibility policyChanged = test.Service.EvaluateResume(
            runId, test.Context, test.ToolPaths, ProjectPackRunPolicyFingerprint.Compute("changed-policy"));
        Assert.False(policyChanged.Eligible);
        Assert.Equal(ProjectPackRunErrorCode.PolicyChanged, policyChanged.ErrorCode);

        File.AppendAllText(test.GerberPath, "changed");
        ProjectPackResumeEligibility inputChanged = test.Service.EvaluateResume(
            runId, test.Context, test.ToolPaths, test.PolicyFingerprint);
        Assert.False(inputChanged.Eligible);
        Assert.Equal(ProjectPackRunErrorCode.InputChanged, inputChanged.ErrorCode);
    }

    [Fact]
    public void Interrupted_execute_requires_explicit_restart_and_cancel_is_terminal()
    {
        using TestRunWorkspace test = TestRunWorkspace.Create();
        ProjectPackRunMutationResult ready = test.CreateAndStage();
        ProjectPackRunMutationResult interrupted = test.Service.ExecuteFake(
            ready.Record!.RunId,
            new FakeProjectPackRunDriver(ProjectPackDriverBehavior.Interrupted, () => test.Now.AddSeconds(1)),
            test.Now.AddSeconds(1));

        ProjectPackResumeEligibility resume = test.Service.EvaluateResume(
            interrupted.Record!.RunId, test.Context, test.ToolPaths, test.PolicyFingerprint);
        Assert.False(resume.Eligible);
        Assert.True(resume.RestartRequired);
        Assert.True(resume.RequiresApproval);
        Assert.Equal(ProjectPackRunErrorCode.RestartRequired, resume.ErrorCode);

        ProjectPackRunMutationResult canceled = test.Service.Cancel(interrupted.Record.RunId, test.Now.AddSeconds(2));
        Assert.True(canceled.Succeeded, canceled.Diagnostic?.Summary);
        Assert.Equal(ProjectPackRunState.Canceled, canceled.Record?.State);
        Assert.False(test.Service.EvaluateResume(
            canceled.Record!.RunId, test.Context, test.ToolPaths, test.PolicyFingerprint).Eligible);
    }

    [Fact]
    public void Plan_loader_sanitizes_unknown_raw_fields_before_managed_persistence()
    {
        using TestRunWorkspace test = TestRunWorkspace.Create();
        JsonObject injected = Assert.IsType<JsonObject>(JsonNode.Parse(test.Plan.SourceJson));
        injected["rawInputContent"] = "DO-NOT-PERSIST-RAW-BOARD";
        injected["approvalToken"] = "DO-NOT-PERSIST-APPROVAL";

        GerberTiffRunPlanSnapshot loaded = GerberTiffRunPlanLoader.Load(injected.ToJsonString());

        Assert.DoesNotContain("rawInputContent", loaded.SourceJson, StringComparison.Ordinal);
        Assert.DoesNotContain("DO-NOT-PERSIST-RAW-BOARD", loaded.SourceJson, StringComparison.Ordinal);
        Assert.DoesNotContain("approvalToken", loaded.SourceJson, StringComparison.Ordinal);
        Assert.DoesNotContain("DO-NOT-PERSIST-APPROVAL", loaded.SourceJson, StringComparison.Ordinal);

        JsonObject unsafeToolPlan = Assert.IsType<JsonObject>(JsonNode.Parse(test.Plan.SourceJson));
        JsonObject firstTool = Assert.IsType<JsonObject>(Assert.IsType<JsonArray>(unsafeToolPlan["tools"])[0]);
        firstTool["fileName"] = Path.Combine(test.RootPath, "secret", "gerbv.exe");
        Assert.Throws<ProjectPackContractException>(() => GerberTiffRunPlanLoader.Load(unsafeToolPlan.ToJsonString()));
    }

    [Fact]
    public void Store_rejects_precreated_run_directory_even_when_record_files_are_absent()
    {
        using TestRunWorkspace test = TestRunWorkspace.Create();
        string runId = ProjectPackRunId.Create(test.Now);
        Directory.CreateDirectory(test.Store.GetLayout(runId).RunRoot);

        ProjectPackRunMutationResult result = test.Service.CreateAndStage(
            test.Plan,
            test.Context,
            test.ToolPaths,
            test.PolicyFingerprint,
            test.Now,
            runId: runId);

        Assert.False(result.Succeeded);
        Assert.Equal(ProjectPackRunErrorCode.AlreadyExists, result.Diagnostic?.ErrorCode);
    }

    [Fact]
    public void Store_rejects_unknown_approval_field_and_unsupported_checkpoint_schema()
    {
        using TestRunWorkspace first = TestRunWorkspace.Create();
        ProjectPackRunMutationResult firstReady = first.CreateAndStage();
        ManagedProjectPackRunLayout firstLayout = first.Store.GetLayout(firstReady.Record!.RunId);
        JsonObject record = Assert.IsType<JsonObject>(JsonNode.Parse(File.ReadAllText(firstLayout.RunRecordPath)));
        record["approvalToken"] = "forbidden";
        File.WriteAllText(firstLayout.RunRecordPath, record.ToJsonString());
        Assert.Equal(ProjectPackRunErrorCode.RecordCorrupt,
            first.Store.Read(firstReady.Record.RunId).Diagnostic?.ErrorCode);

        using TestRunWorkspace second = TestRunWorkspace.Create();
        ProjectPackRunMutationResult secondReady = second.CreateAndStage();
        ManagedProjectPackRunLayout secondLayout = second.Store.GetLayout(secondReady.Record!.RunId);
        JsonObject checkpoint = Assert.IsType<JsonObject>(JsonNode.Parse(File.ReadAllText(secondLayout.CheckpointPath)));
        checkpoint["schemaVersion"] = 999;
        File.WriteAllText(secondLayout.CheckpointPath, checkpoint.ToJsonString());
        Assert.Equal(ProjectPackRunErrorCode.SchemaUnsupported,
            second.Store.Read(secondReady.Record.RunId).Diagnostic?.ErrorCode);
    }

    [Fact]
    public void Resume_rejects_staged_tamper_tool_drift_and_output_conflict_with_stable_codes()
    {
        using TestRunWorkspace stagedTest = TestRunWorkspace.Create();
        ProjectPackRunMutationResult stagedReady = stagedTest.CreateAndStage();
        ManagedProjectPackRunLayout stagedLayout = stagedTest.Store.GetLayout(stagedReady.Record!.RunId);
        File.AppendAllText(Assert.Single(Directory.EnumerateFiles(stagedLayout.StagingPath)), "tamper");
        ProjectPackResumeEligibility stagedResult = stagedTest.Service.EvaluateResume(
            stagedReady.Record.RunId, stagedTest.Context, stagedTest.ToolPaths, stagedTest.PolicyFingerprint);
        Assert.False(stagedResult.Eligible);
        Assert.Equal(ProjectPackRunErrorCode.StagingHashMismatch, stagedResult.ErrorCode);

        using TestRunWorkspace toolTest = TestRunWorkspace.Create();
        ProjectPackRunMutationResult toolReady = toolTest.CreateAndStage();
        File.AppendAllText(toolTest.ToolPaths["gerbv"], "changed");
        ProjectPackResumeEligibility toolResult = toolTest.Service.EvaluateResume(
            toolReady.Record!.RunId, toolTest.Context, toolTest.ToolPaths, toolTest.PolicyFingerprint);
        Assert.False(toolResult.Eligible);
        Assert.Equal(ProjectPackRunErrorCode.ToolChanged, toolResult.ErrorCode);

        using TestRunWorkspace outputTest = TestRunWorkspace.Create();
        ProjectPackRunMutationResult outputReady = outputTest.CreateAndStage();
        Directory.CreateDirectory(Path.Combine(outputTest.WorkspacePath, "output"));
        ProjectPackResumeEligibility outputResult = outputTest.Service.EvaluateResume(
            outputReady.Record!.RunId, outputTest.Context, outputTest.ToolPaths, outputTest.PolicyFingerprint);
        Assert.False(outputResult.Eligible);
        Assert.Equal(ProjectPackRunErrorCode.OutputConflict, outputResult.ErrorCode);
    }

    [Fact]
    public void Managed_runs_root_reparse_point_is_rejected_without_writing_outside()
    {
        using TestRunWorkspace test = TestRunWorkspace.Create();
        string linkParent = Path.Combine(test.RootPath, "linked-profile", ".caicli");
        string runsLink = Path.Combine(linkParent, "runs");
        string outside = Path.Combine(test.RootPath, "outside-runs");
        Directory.CreateDirectory(linkParent);
        Directory.CreateDirectory(outside);
        try
        {
            Directory.CreateSymbolicLink(runsLink, outside);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        ManagedProjectPackRunStore linkedStore = new(runsLink);
        ProjectPackRunMutationResult result = new ProjectPackRunService(linkedStore).CreateAndStage(
            test.Plan,
            test.Context,
            test.ToolPaths,
            test.PolicyFingerprint,
            test.Now,
            runId: ProjectPackRunId.Create(test.Now));

        Assert.False(result.Succeeded);
        Assert.Equal(ProjectPackRunErrorCode.ReparsePoint, result.Diagnostic?.ErrorCode);
        Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
    }

    [Fact]
    public void Staging_toctou_mutation_fails_closed_and_removes_untrusted_copy()
    {
        using TestRunWorkspace test = TestRunWorkspace.Create();
        ProjectPackStagingService staging = new((source, _) =>
        {
            int length = checked((int)new FileInfo(source).Length);
            File.WriteAllText(source, new string('X', length), System.Text.Encoding.ASCII);
        });
        ProjectPackRunService service = new(test.Store, staging: staging);

        ProjectPackRunMutationResult result = service.CreateAndStage(
            test.Plan,
            test.Context,
            test.ToolPaths,
            test.PolicyFingerprint,
            test.Now,
            runId: ProjectPackRunId.Create(test.Now));

        Assert.True(result.Succeeded, result.Diagnostic?.Summary);
        Assert.Equal(ProjectPackRunState.Failed, result.Record?.State);
        Assert.Contains(result.Record?.ErrorCode, new[]
        {
            ProjectPackRunErrorCode.InputChanged,
            ProjectPackRunErrorCode.StagingHashMismatch
        });
        ManagedProjectPackRunLayout layout = test.Store.GetLayout(result.Record!.RunId);
        Assert.Empty(Directory.EnumerateFiles(layout.StagingPath));
        Assert.Empty(Directory.EnumerateFiles(layout.RunRoot, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void Store_distinguishes_disk_full_from_lock_contention_and_atomic_write_cleans_temp()
    {
        IOException diskFull = new("controlled disk full", unchecked((int)0x80070070));
        IOException sharingViolation = new("controlled sharing violation", unchecked((int)0x80070020));

        Assert.Equal(ProjectPackRunErrorCode.RecordWriteFailed,
            ManagedProjectPackRunStore.ClassifyWriteIOException(diskFull));
        Assert.Equal(ProjectPackRunErrorCode.ConcurrentConflict,
            ManagedProjectPackRunStore.ClassifyWriteIOException(sharingViolation));

        string root = Path.Combine(Path.GetTempPath(), "caicli-atomic-write-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string directoryAsDestination = Path.Combine(root, "run.json");
        Directory.CreateDirectory(directoryAsDestination);
        try
        {
            Exception writeFailure = Assert.ThrowsAny<Exception>(() =>
                ManagedProjectPackRunStore.WriteTextAtomically(directoryAsDestination, "{}", overwrite: true));
            Assert.True(writeFailure is IOException or UnauthorizedAccessException);
            Assert.Empty(Directory.EnumerateFiles(root, "*.tmp", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Store_rejects_split_write_and_unknown_state_as_corrupt_without_guessing()
    {
        using TestRunWorkspace split = TestRunWorkspace.Create();
        ProjectPackRunMutationResult splitReady = split.CreateAndStage();
        ManagedProjectPackRunLayout splitLayout = split.Store.GetLayout(splitReady.Record!.RunId);
        JsonObject checkpoint = Assert.IsType<JsonObject>(JsonNode.Parse(File.ReadAllText(splitLayout.CheckpointPath)));
        checkpoint["revision"] = splitReady.Record.Revision + 1;
        File.WriteAllText(splitLayout.CheckpointPath, checkpoint.ToJsonString());
        Assert.Equal(ProjectPackRunErrorCode.RecordCorrupt,
            split.Store.Read(splitReady.Record.RunId).Diagnostic?.ErrorCode);

        using TestRunWorkspace state = TestRunWorkspace.Create();
        ProjectPackRunMutationResult stateReady = state.CreateAndStage();
        ManagedProjectPackRunLayout stateLayout = state.Store.GetLayout(stateReady.Record!.RunId);
        JsonObject record = Assert.IsType<JsonObject>(JsonNode.Parse(File.ReadAllText(stateLayout.RunRecordPath)));
        record["state"] = "unknown-state";
        File.WriteAllText(stateLayout.RunRecordPath, record.ToJsonString());
        Assert.Equal(ProjectPackRunErrorCode.RecordCorrupt,
            state.Store.Read(stateReady.Record.RunId).Diagnostic?.ErrorCode);
    }

    [Fact]
    public void Restart_json_projects_redacted_diagnostic_instead_of_raw_exception_text()
    {
        ProjectPackRestartPreparation failure = new(
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            new ProjectPackRunDiagnostic(
                ProjectPackRunErrorCode.RecordCorrupt,
                "token=restart-super-secret",
                "apiKey=restart-path-secret"));

        string json = ProjectPackRunRenderer.RenderRestartJson(failure);

        Assert.DoesNotContain("restart-super-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("restart-path-secret", json, StringComparison.Ordinal);
        Assert.Contains("[redacted]", json, StringComparison.Ordinal);
    }

    private static string Hash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private sealed class EmptySuccessDriver(DateTimeOffset now) : IProjectPackRunDriver
    {
        public ProjectPackRunDriverResult Execute(
            ProjectPackRunExecutionContext context,
            CancellationToken cancellationToken = default) =>
            new(
                ProjectPackStageStatus.Succeeded,
                [
                    new ProjectPackDriverStageEvent("render", ProjectPackStageStatus.Succeeded, now, now),
                    new ProjectPackDriverStageEvent("encode", ProjectPackStageStatus.Succeeded, now, now)
                ],
                summary: "Malicious fake adapter claimed success without outputs.");
    }

    private sealed class TestRunWorkspace : IDisposable
    {
        private TestRunWorkspace(
            string rootPath,
            string workspacePath,
            string gerberPath,
            WorkspaceContext context,
            Dictionary<string, string> toolPaths,
            GerberTiffRunPlanSnapshot plan,
            ManagedProjectPackRunStore store,
            ProjectPackRunService service)
        {
            RootPath = rootPath;
            WorkspacePath = workspacePath;
            GerberPath = gerberPath;
            Context = context;
            ToolPaths = toolPaths;
            Plan = plan;
            Store = store;
            Service = service;
        }

        public DateTimeOffset Now { get; } = DateTimeOffset.Parse("2026-07-14T01:02:03Z");
        public string RootPath { get; }
        public string WorkspacePath { get; }
        public string GerberPath { get; }
        public string RunsRoot => Store.RunsRoot;
        public WorkspaceContext Context { get; }
        public Dictionary<string, string> ToolPaths { get; }
        public GerberTiffRunPlanSnapshot Plan { get; }
        public ManagedProjectPackRunStore Store { get; }
        public ProjectPackRunService Service { get; }
        public string PolicyFingerprint { get; } = ProjectPackRunPolicyFingerprint.Compute("approval=never;pack=gerber-tiff");

        public static TestRunWorkspace Create()
        {
            string root = Path.Combine(Path.GetTempPath(), "caicli-pack-run-tests-" + Guid.NewGuid().ToString("N"));
            string workspace = Path.Combine(root, "workspace");
            string input = Path.Combine(workspace, "input");
            Directory.CreateDirectory(input);
            string gerber = Path.Combine(input, "board.gbr");
            File.WriteAllText(gerber, "PRIVATE-GERBER-CONTENT");
            File.WriteAllText(Path.Combine(input, "unknown.txt"), "PRIVATE-UNKNOWN-CONTENT");
            string tools = Path.Combine(root, "tools");
            Directory.CreateDirectory(tools);
            string gerbv = Path.Combine(tools, "gerbv.exe");
            string magick = Path.Combine(tools, "magick.exe");
            File.WriteAllText(gerbv, "static-gerbv");
            File.WriteAllText(magick, "static-magick");
            Dictionary<string, string> toolPaths = new(StringComparer.Ordinal)
            {
                ["gerbv"] = gerbv,
                ["imagemagick"] = magick
            };
            WorkspaceContext context = WorkspaceContext.Detect(workspace, root);
            GerberTiffConversionPlan conversionPlan = new GerberTiffConversionPlanBuilder().Build(
                context, "input", "output", toolPaths);
            Assert.True(conversionPlan.Runnable, string.Join(" | ", conversionPlan.Diagnostics.Select(item => item.Code)));
            string planJson = GerberTiffPlanRenderer.RenderJson(conversionPlan, "--workspace");
            GerberTiffRunPlanSnapshot plan = GerberTiffRunPlanLoader.Load(planJson);
            ManagedProjectPackRunStore store = new(Path.Combine(root, "profile", ".caicli", "runs"));
            return new TestRunWorkspace(
                root, workspace, gerber, context, toolPaths, plan, store, new ProjectPackRunService(store));
        }

        public ProjectPackRunMutationResult CreateAndStage() => Service.CreateAndStage(
            Plan,
            Context,
            ToolPaths,
            PolicyFingerprint,
            Now,
            runId: ProjectPackRunId.Create(Now));

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(RootPath))
                {
                    Directory.Delete(RootPath, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
