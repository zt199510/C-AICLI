using System.Text.Json.Nodes;
using CSharpAiCli.Cli;
using CSharpAiCli.Core;
using CSharpAiCli.ProjectPacks;
using CSharpAiCli.ProjectPacks.GerberTiff;
using CSharpAiCli.ProjectPacks.Runtime;

namespace CSharpAiCli.Tests;

public sealed class ProjectPackCliTests
{
    [Fact]
    public void Packs_list_text_is_contract_only()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();

        int exitCode = CliCommandFactory.Create(output, path => CreateSnapshot(path, temp.Path))
            .Parse(["packs", "list", "--workspace", temp.Path])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Contains("pack: gerber-tiff", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("status: contract-only", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("real execution accepted", output.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Packs_list_json_has_stable_schema()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();

        int exitCode = CliCommandFactory.Create(output, path => CreateSnapshot(path, temp.Path))
            .Parse(["packs", "list", "--output", "json", "--workspace", temp.Path])
            .Invoke();

        JsonObject root = Assert.IsType<JsonObject>(JsonNode.Parse(output.ToString()));
        Assert.Equal(0, exitCode);
        Assert.Equal("packs.list", root["type"]?.GetValue<string>());
        Assert.Equal(1, root["schemaVersion"]?.GetValue<int>());
        Assert.Single(Assert.IsType<JsonArray>(root["packs"]));
    }

    [Fact]
    public void Packs_doctor_without_paths_is_static_and_reports_unavailable()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();

        int exitCode = CliCommandFactory.Create(output, path => CreateSnapshot(path, temp.Path))
            .Parse(["packs", "doctor", "gerber-tiff", "--output", "json", "--workspace", temp.Path])
            .Invoke();

        JsonObject root = Assert.IsType<JsonObject>(JsonNode.Parse(output.ToString()));
        Assert.Equal(1, exitCode);
        Assert.Equal("packs.doctor", root["type"]?.GetValue<string>());
        Assert.Equal("unavailable", root["status"]?.GetValue<string>());
        Assert.False(root["probeRequested"]?.GetValue<bool>());
    }

    [Fact]
    public void Packs_static_doctor_accepts_dependency_bindings_without_starting_files_or_rendering_paths()
    {
        using TempDirectory temp = TempDirectory.Create();
        string gerbv = Path.Combine(temp.Path, "gerbv.exe");
        string magick = Path.Combine(temp.Path, "magick.exe");
        File.WriteAllText(gerbv, "static-gerbv-fixture");
        File.WriteAllText(magick, "static-magick-fixture");
        using StringWriter output = new();

        int exitCode = CliCommandFactory.Create(output, path => CreateSnapshot(path, temp.Path))
            .Parse(
            [
                "packs", "doctor", "gerber-tiff",
                "--tool-path", "gerbv=" + gerbv, "imagemagick=" + magick,
                "--output", "json", "--workspace", temp.Path
            ])
            .Invoke();

        JsonObject root = Assert.IsType<JsonObject>(JsonNode.Parse(output.ToString()));
        Assert.Equal(0, exitCode);
        Assert.Equal("static-ok", root["status"]?.GetValue<string>());
        Assert.DoesNotContain(temp.Path, output.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Packs_probe_cannot_bypass_default_approval()
    {
        using TempDirectory temp = TempDirectory.Create();
        string gerbv = Path.Combine(temp.Path, "gerbv.exe");
        string magick = Path.Combine(temp.Path, "magick.exe");
        File.WriteAllText(gerbv, "not-executed");
        File.WriteAllText(magick, "not-executed");
        using StringWriter output = new();

        int exitCode = CliCommandFactory.Create(output, path => CreateSnapshot(path, temp.Path))
            .Parse(
            [
                "packs", "doctor", "gerber-tiff",
                "--tool-path", "gerbv=" + gerbv, "imagemagick=" + magick,
                "--probe", "--output", "json", "--workspace", temp.Path
            ])
            .Invoke();

        JsonObject root = Assert.IsType<JsonObject>(JsonNode.Parse(output.ToString()));
        Assert.Equal(1, exitCode);
        Assert.Equal("approval-required", root["status"]?.GetValue<string>());
        Assert.DoesNotContain(temp.Path, output.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Packs_doctor_rejects_unknown_dependency_binding()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();

        int exitCode = CliCommandFactory.Invoke(
            CliCommandFactory.Create(output, path => CreateSnapshot(path, temp.Path)),
            ["packs", "doctor", "gerber-tiff", "--tool-path", "unknown=C:\\tool.exe"],
            output);

        Assert.Equal(2, exitCode);
        Assert.Contains("Unknown project pack dependency", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Packs_doctor_json_redacts_secret_like_diagnostics()
    {
        ProjectPackDoctorReport report = new(
            "test-pack",
            ProjectPackDoctorStatus.Failed,
            ProbeRequested: false,
            Tools: [],
            Diagnostics:
            [
                new ProjectPackDiagnostic(
                    "test-diagnostic",
                    ProjectPackDiagnosticSeverity.Error,
                    "apiKey=sk-project-pack-secret")
            ]);

        string json = ProjectPackReportRenderer.RenderDoctorJson(report);

        Assert.Contains("apiKey=[redacted]", json, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-project-pack-secret", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Packs_doctor_json_does_not_render_raw_probe_output_or_paths()
    {
        string machinePath = Path.Combine(Path.GetTempPath(), "sensitive-tool", "tool.exe");
        ExternalToolIdentity identity = new(
            "test-tool",
            "tool.exe",
            1,
            DateTimeOffset.UnixEpoch,
            new string('A', 64),
            "--tool-path",
            ExternalToolTrustStatus.Untrusted,
            "succeeded",
            "tool 1.0.0");
        ExternalToolProbeResult probe = new(
            "succeeded",
            identity,
            "approved",
            0,
            0,
            10,
            "version 1.0 at " + machinePath,
            "stderr path " + machinePath,
            false,
            false,
            false,
            false,
            true,
            []);
        ProjectPackToolDoctorResult tool = new(
            "test-tool",
            "Test Tool",
            true,
            ProjectPackDoctorStatus.Ready,
            "--tool-path",
            identity,
            probe,
            []);
        ProjectPackDoctorReport report = new(
            "test-pack",
            ProjectPackDoctorStatus.Ready,
            true,
            [tool],
            []);

        string json = ProjectPackReportRenderer.RenderDoctorJson(report);

        Assert.DoesNotContain(machinePath, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("version 1.0 at", json, StringComparison.Ordinal);
        Assert.Contains("stdoutCharacters", json, StringComparison.Ordinal);
        Assert.Contains("stderrCharacters", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Packs_plan_json_is_static_deterministic_and_does_not_create_output_or_persistent_state()
    {
        using TempDirectory temp = TempDirectory.Create();
        string input = Path.Combine(temp.Path, "input");
        string outputDirectory = Path.Combine(temp.Path, "output");
        Directory.CreateDirectory(input);
        File.WriteAllText(Path.Combine(input, "board.GBR"), "private-gerber-content");
        File.WriteAllText(Path.Combine(input, "notes.txt"), "unknown-private-content");
        string gerbv = Path.Combine(temp.Path, "gerbv.exe");
        string magick = Path.Combine(temp.Path, "magick.exe");
        File.WriteAllText(gerbv, "static-gerbv-fixture");
        File.WriteAllText(magick, "static-magick-fixture");
        using StringWriter output = new();

        int exitCode = CliCommandFactory.Create(output, path => CreateSnapshot(path, temp.Path))
            .Parse(
            [
                "packs", "plan", "gerber-tiff",
                "--input", "input",
                "--output-dir", "output",
                "--tool-path", "gerbv=" + gerbv, "imagemagick=" + magick,
                "--output", "json",
                "--workspace", temp.Path
            ])
            .Invoke();

        JsonObject root = Assert.IsType<JsonObject>(JsonNode.Parse(output.ToString()));
        Assert.Equal(0, exitCode);
        Assert.Equal("packs.plan", root["type"]?.GetValue<string>());
        Assert.Equal("gerber-tiff.plan.v1", root["planSchema"]?.GetValue<string>());
        Assert.Equal("runnable", root["status"]?.GetValue<string>());
        Assert.True(root["readyForStaging"]?.GetValue<bool>());
        Assert.True(root["runnable"]?.GetValue<bool>());
        Assert.False(root["conversionExecuted"]?.GetValue<bool>());
        Assert.False(root["executionAuthorized"]?.GetValue<bool>());
        Assert.NotNull(root["fingerprint"]?.GetValue<string>());
        Assert.DoesNotContain(temp.Path, output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-gerber-content", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("unknown-private-content", output.ToString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(outputDirectory));
        Assert.False(Directory.Exists(Path.Combine(temp.Path, ".caicli")));
        Assert.False(Directory.Exists(Path.Combine(temp.Path, "profile")));
    }

    [Fact]
    public void Packs_plan_text_uses_output_dir_while_output_remains_renderer_mode()
    {
        using TempDirectory temp = TempDirectory.Create();
        Directory.CreateDirectory(Path.Combine(temp.Path, "input"));
        File.WriteAllText(Path.Combine(temp.Path, "input", "board.gbr"), "gerber");
        string gerbv = Path.Combine(temp.Path, "gerbv.exe");
        string magick = Path.Combine(temp.Path, "magick.exe");
        File.WriteAllText(gerbv, "static-gerbv-fixture");
        File.WriteAllText(magick, "static-magick-fixture");
        using StringWriter output = new();

        int exitCode = CliCommandFactory.Create(output, path => CreateSnapshot(path, temp.Path))
            .Parse(
            [
                "packs", "plan", "gerber-tiff",
                "--input", "input", "--output-dir", "planned-output", "--output", "text",
                "--tool-path", "gerbv=" + gerbv, "imagemagick=" + magick,
                "--workspace", temp.Path
            ])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Contains("outputDirectory: planned-output", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("outputSource: --output-dir", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("conversionExecuted: false", output.ToString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(temp.Path, "planned-output")));
    }

    [Fact]
    public void Packs_run_dry_run_stages_inputs_and_correlates_job_without_task_report_truth()
    {
        using TempDirectory temp = TempDirectory.Create();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(temp.Path, temp.Path);
        string planPath = WritePlan(temp.Path, snapshot.Workspace, toolPaths: null);
        using StringWriter output = new();

        int exitCode = CliCommandFactory.Create(output, _ => snapshot)
            .Parse(
            [
                "packs", "run", "gerber-tiff",
                "--plan", Path.GetFileName(planPath),
                "--dry-run", "--output", "json",
                "--workspace", temp.Path
            ])
            .Invoke();

        JsonObject root = Assert.IsType<JsonObject>(JsonNode.Parse(output.ToString()));
        Assert.Equal(0, exitCode);
        Assert.Equal("packs.run", root["type"]?.GetValue<string>());
        JsonObject run = Assert.IsType<JsonObject>(root["run"]);
        string runId = run["runId"]!.GetValue<string>();
        string jobId = Assert.IsType<JsonObject>(run["correlation"])["jobId"]!.GetValue<string>();
        Assert.Equal(ProjectPackRunState.Ready, run["state"]?.GetValue<string>());
        Assert.False(Assert.IsType<JsonObject>(root["checkpoint"])["approvalPersisted"]?.GetValue<bool>());

        ManagedProjectPackRunStore runStore = ManagedProjectPackRunStore.Create(snapshot);
        ManagedProjectPackRunLayout layout = runStore.GetLayout(runId);
        Assert.True(File.Exists(layout.RunRecordPath));
        Assert.Single(Directory.EnumerateFiles(layout.StagingPath));
        Assert.DoesNotContain("private-gerber", File.ReadAllText(layout.InputManifestPath), StringComparison.Ordinal);
        JobRecord job = Assert.IsType<JobRecord>(JobRecordStore.Create(snapshot).Read(jobId).Record);
        Assert.Equal(JobStatus.DryRun, job.Status);
        Assert.Null(job.TaskReport);
        Assert.Contains(job.Artifacts, artifact => artifact.Kind == JobArtifactKind.ProjectPackRun && artifact.Exists);
        Assert.Contains(job.Artifacts, artifact => artifact.Kind == JobArtifactKind.ProjectPackInputManifest && artifact.Exists);
    }

    [Fact]
    public void Packs_runs_show_resume_and_cancel_use_the_same_checkpoint()
    {
        using TempDirectory temp = TempDirectory.Create();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(temp.Path, temp.Path);
        string planPath = WritePlan(temp.Path, snapshot.Workspace, toolPaths: null);
        using StringWriter runOutput = new();
        int runExit = CliCommandFactory.Create(runOutput, _ => snapshot)
            .Parse(["packs", "run", "gerber-tiff", "--plan", Path.GetFileName(planPath), "--dry-run", "--output", "json", "--workspace", temp.Path])
            .Invoke();
        string runId = Assert.IsType<JsonObject>(Assert.IsType<JsonObject>(JsonNode.Parse(runOutput.ToString()))["run"])["runId"]!.GetValue<string>();

        using StringWriter showOutput = new();
        int showExit = CliCommandFactory.Create(showOutput, _ => snapshot)
            .Parse(["packs", "runs", "show", runId, "--output", "json", "--workspace", temp.Path])
            .Invoke();
        using StringWriter resumeOutput = new();
        int resumeExit = CliCommandFactory.Create(resumeOutput, _ => snapshot)
            .Parse(["packs", "resume", runId, "--dry-run", "--output", "json", "--workspace", temp.Path])
            .Invoke();
        using StringWriter cancelOutput = new();
        int cancelExit = CliCommandFactory.Create(cancelOutput, _ => snapshot)
            .Parse(["packs", "cancel", runId, "--output", "json", "--workspace", temp.Path])
            .Invoke();
        using StringWriter terminalResumeOutput = new();
        int terminalResumeExit = CliCommandFactory.Create(terminalResumeOutput, _ => snapshot)
            .Parse(["packs", "resume", runId, "--dry-run", "--output", "json", "--workspace", temp.Path])
            .Invoke();

        Assert.Equal(0, runExit);
        Assert.Equal(0, showExit);
        Assert.Equal(ProjectPackRunState.Ready,
            Assert.IsType<JsonObject>(Assert.IsType<JsonObject>(JsonNode.Parse(showOutput.ToString()))["run"])["state"]?.GetValue<string>());
        Assert.Equal(0, resumeExit);
        Assert.True(Assert.IsType<JsonObject>(JsonNode.Parse(resumeOutput.ToString()))["eligible"]?.GetValue<bool>());
        Assert.Equal(0, cancelExit);
        Assert.Equal(ProjectPackRunState.Canceled,
            Assert.IsType<JsonObject>(Assert.IsType<JsonObject>(JsonNode.Parse(cancelOutput.ToString()))["run"])["state"]?.GetValue<string>());
        Assert.Equal(1, terminalResumeExit);
        Assert.False(Assert.IsType<JsonObject>(JsonNode.Parse(terminalResumeOutput.ToString()))["eligible"]?.GetValue<bool>());
    }

    [Fact]
    public void Packs_run_without_dry_run_fails_without_creating_run_or_job_state()
    {
        using TempDirectory temp = TempDirectory.Create();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(temp.Path, temp.Path);
        string planPath = WritePlan(temp.Path, snapshot.Workspace, toolPaths: null);
        using StringWriter output = new();

        int exitCode = CliCommandFactory.Create(output, _ => snapshot)
            .Parse(["packs", "run", "gerber-tiff", "--plan", Path.GetFileName(planPath), "--output", "json", "--workspace", temp.Path])
            .Invoke();

        Assert.Equal(2, exitCode);
        Assert.Contains("pack-real-execution-deferred", output.ToString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(ManagedProjectPackRunStore.Create(snapshot).RunsRoot));
        Assert.False(Directory.Exists(JobRecordStore.Create(snapshot).JobDirectory));
    }

    [Fact]
    public void Run_correlation_validates_queue_and_job_ids_and_renderer_redacts_pointer_metadata()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-14T00:00:00Z");
        ProjectPackRunCorrelation correlation = new(TaskQueueIdGenerator.Create(now), JobIdGenerator.Create(now));
        ProjectPackRunArtifactPointer pointer = new(
            "evidence", "external-pointer", "external-pointer", "apiKey=secret-value", false);

        Assert.NotNull(correlation.QueueId);
        Assert.NotNull(correlation.JobId);
        Assert.Contains("[redacted]", pointer.Path, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => new ProjectPackRunCorrelation("../queue", correlation.JobId));
    }

    private static string WritePlan(
        string workspace,
        WorkspaceContext context,
        IReadOnlyDictionary<string, string>? toolPaths)
    {
        string input = Path.Combine(workspace, "input");
        Directory.CreateDirectory(input);
        File.WriteAllText(Path.Combine(input, "board.gbr"), "private-gerber");
        File.WriteAllText(Path.Combine(input, "unknown.txt"), "private-unknown");
        GerberTiffConversionPlan plan = new GerberTiffConversionPlanBuilder().Build(
            context, "input", "output", toolPaths);
        Assert.True(plan.ReadyForStaging);
        string path = Path.Combine(workspace, "plan.json");
        File.WriteAllText(path, GerberTiffPlanRenderer.RenderJson(plan, "--workspace"));
        return path;
    }

    private static CliEnvironmentSnapshot CreateSnapshot(string? workspacePath, string userProfileRoot)
    {
        string workspace = workspacePath ?? userProfileRoot;
        return CliEnvironmentSnapshot.Create(
            workspacePath: workspace,
            currentDirectory: workspace,
            userProfile: Path.Combine(userProfileRoot, "profile"),
            dotnetSdkVersion: "9.0.308",
            dotnetRuntime: ".NET 9",
            openAiApiKey: null,
            openAiModel: null,
            hasGlobalJson: false);
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempDirectory Create()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "caicli-pack-cli-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
