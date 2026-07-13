using CSharpAiCli.Cli;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class JobRecordingCliTests
{
    private static readonly DateTimeOffset FixedUtc = DateTimeOffset.Parse("2026-07-13T08:00:00Z");

    [Fact]
    public void Exec_record_job_success_indexes_task_report_and_markdown_report()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspace = temp.CreateDirectory("workspace");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(temp.Path, workspace);
        RecordingAgentRunner runner = new(AgentRunResult.Success("completed", []));
        using StringWriter output = new();

        int exitCode = CreateCommand(output, snapshot, runner)
            .Parse([
                "exec",
                "--record-job",
                "--job-name",
                "review-job",
                "--report",
                "markdown",
                "--report-path",
                ".caicli/reports/job.md",
                "--workspace",
                workspace,
                "summarize apiKey=prompt-secret"
            ])
            .Invoke();

        JobRecord record = Assert.Single(JobRecordStore.Create(snapshot).List().Records);
        Assert.Equal(0, exitCode);
        Assert.Equal(JobStatus.Succeeded, record.Status);
        Assert.Equal("review-job", record.JobName);
        Assert.Equal("completed", record.Summary);
        Assert.Contains("[redacted]", record.Command.Task, StringComparison.Ordinal);
        Assert.DoesNotContain("prompt-secret", record.Command.Task, StringComparison.Ordinal);
        Assert.NotNull(record.TaskReport);
        Assert.Contains(record.Artifacts, artifact => artifact.Kind == JobArtifactKind.TaskReport);
        JobArtifact report = Assert.Single(record.Artifacts, artifact => artifact.Kind == JobArtifactKind.MarkdownReport);
        Assert.True(report.Exists);
        Assert.True(File.Exists(report.Path));
    }

    [Fact]
    public void Exec_record_job_agent_failure_reaches_failed_terminal_state()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspace = temp.CreateDirectory("workspace");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(temp.Path, workspace);
        RecordingAgentRunner runner = new(AgentRunResult.Failure(
            new AgentError("tool-failure", "Tool execution failed.", Retryable: false),
            []));
        using StringWriter output = new();

        int exitCode = CreateCommand(output, snapshot, runner)
            .Parse(["exec", "--record-job", "--workspace", workspace, "run task"])
            .Invoke();

        JobRecord record = Assert.Single(JobRecordStore.Create(snapshot).List().Records);
        Assert.Equal(1, exitCode);
        Assert.Equal(JobStatus.Failed, record.Status);
        Assert.Equal("tool-failure", record.ErrorCode);
        Assert.NotNull(record.CompletedAtUtc);
    }

    [Fact]
    public void Exec_record_job_reference_failure_records_metadata_without_content()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspace = temp.CreateDirectory("workspace");
        File.WriteAllText(Path.Combine(temp.Path, "outside.txt"), "raw-reference-content");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(temp.Path, workspace);
        RecordingAgentRunner runner = new(AgentRunResult.Success("must not run", []));
        using StringWriter output = new();

        int exitCode = CreateCommand(output, snapshot, runner)
            .Parse(["exec", "--record-job", "--workspace", workspace, "review @file:../outside.txt"])
            .Invoke();

        JobRecord record = Assert.Single(JobRecordStore.Create(snapshot).List().Records);
        string json = File.ReadAllText(Directory.EnumerateFiles(JobRecordStore.Create(snapshot).JobDirectory).Single());
        Assert.Equal(1, exitCode);
        Assert.Null(runner.LastRequest);
        Assert.Equal(JobStatus.Failed, record.Status);
        Assert.Equal("workflow-reference-boundary-denied", record.ErrorCode);
        Assert.Equal(1, record.TaskReport?.ReferenceCount);
        Assert.DoesNotContain("raw-reference-content", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Exec_record_job_early_validation_failure_does_not_remain_running()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspace = temp.CreateDirectory("workspace");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(temp.Path, workspace);
        RecordingAgentRunner runner = new(AgentRunResult.Success("must not run", []));
        using StringWriter output = new();

        int exitCode = CreateCommand(output, snapshot, runner)
            .Parse([
                "exec",
                "--record-job",
                "--session",
                "one",
                "--resume",
                "two",
                "--workspace",
                workspace,
                "run task"
            ])
            .Invoke();

        JobRecord record = Assert.Single(JobRecordStore.Create(snapshot).List().Records);
        Assert.Equal(1, exitCode);
        Assert.Null(runner.LastRequest);
        Assert.Equal(JobStatus.Failed, record.Status);
        Assert.Equal("session-option-conflict", record.ErrorCode);
        Assert.NotNull(record.CompletedAtUtc);
    }

    [Fact]
    public void Exec_record_job_does_not_persist_raw_tool_arguments_full_diff_or_secrets()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspace = temp.CreateDirectory("workspace");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(temp.Path, workspace);
        AgentRunEvent rawToolEvent = new(
            Type: "tool.call",
            Sequence: 0,
            Timestamp: FixedUtc,
            Message: "password=event-secret",
            Summary: "raw tool call",
            Payload: new Dictionary<string, string>
            {
                ["argumentsJson"] = "{\"password\":\"raw-argument-secret\",\"path\":\"note.txt\"}",
                ["diff"] = "@@ -1 +1 @@\n-before-private-content\n+after-private-content"
            });
        RecordingAgentRunner runner = new(AgentRunResult.Success(
            "finished authorization=Bearer result-secret",
            [],
            [rawToolEvent]));
        using StringWriter output = new();

        int exitCode = CreateCommand(output, snapshot, runner)
            .Parse(["exec", "--record-job", "--workspace", workspace, "run task"])
            .Invoke();

        string jobJson = File.ReadAllText(
            Directory.EnumerateFiles(JobRecordStore.Create(snapshot).JobDirectory, "*.job.json").Single());
        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("raw-argument-secret", jobJson, StringComparison.Ordinal);
        Assert.DoesNotContain("before-private-content", jobJson, StringComparison.Ordinal);
        Assert.DoesNotContain("after-private-content", jobJson, StringComparison.Ordinal);
        Assert.DoesNotContain("event-secret", jobJson, StringComparison.Ordinal);
        Assert.DoesNotContain("result-secret", jobJson, StringComparison.Ordinal);
        Assert.DoesNotContain("argumentsJson", jobJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"diff\"", jobJson, StringComparison.Ordinal);
        Assert.Contains("[redacted]", jobJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Skills_record_job_dry_run_saves_compact_plan_metadata_without_running_agent()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspace = temp.CreateDirectory("workspace");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(temp.Path, workspace);
        RecordingAgentRunner runner = new(AgentRunResult.Success("must not run", []));
        using StringWriter output = new();

        int exitCode = CreateCommand(output, snapshot, runner)
            .Parse([
                "skills",
                "run",
                "review-only",
                "--record-job",
                "--dry-run",
                "--workspace",
                workspace,
                "--",
                "Review",
                "@file:README.md"
            ])
            .Invoke();

        JobRecord record = Assert.Single(JobRecordStore.Create(snapshot).List().Records);
        JobSkillSummary skill = Assert.IsType<JobSkillSummary>(record.Command.SkillMetadata);
        Assert.Equal(0, exitCode);
        Assert.Null(runner.LastRequest);
        Assert.Equal(JobStatus.DryRun, record.Status);
        Assert.True(record.Command.DryRun);
        Assert.Equal("review-only", skill.Name);
        Assert.Equal("reviewer", skill.Expert);
        Assert.Contains("allowWrites=false", skill.SafetySummary, StringComparison.Ordinal);
        Assert.Contains(record.Artifacts, artifact => artifact.Kind == JobArtifactKind.SkillPlan);
    }

    [Fact]
    public void Skills_record_job_non_dry_run_reuses_exec_result_and_skill_metadata()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspace = temp.CreateDirectory("workspace");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(temp.Path, workspace);
        RecordingAgentRunner runner = new(AgentRunResult.Success("skill completed", []));
        using StringWriter output = new();

        int exitCode = CreateCommand(output, snapshot, runner)
            .Parse([
                "skills",
                "run",
                "test-fix",
                "--record-job",
                "--report",
                "none",
                "--workspace",
                workspace,
                "--",
                "Fix tests"
            ])
            .Invoke();

        JobRecord record = Assert.Single(JobRecordStore.Create(snapshot).List().Records);
        Assert.Equal(0, exitCode);
        Assert.NotNull(runner.LastRequest);
        Assert.Equal(JobStatus.Succeeded, record.Status);
        Assert.Equal("test-fix", record.Command.Skill);
        Assert.Equal("test-fix", record.Command.SkillMetadata?.Name);
        Assert.Equal("dotnet test", record.Command.SkillMetadata?.ValidationCommand);
        Assert.Equal("test-fix", runner.LastRequest?.Skill?.Name);
        Assert.NotNull(record.TaskReport);
    }

    private static System.CommandLine.RootCommand CreateCommand(
        StringWriter output,
        CliEnvironmentSnapshot snapshot,
        RecordingAgentRunner runner)
    {
        return CliCommandFactory.Create(
            output,
            _ => snapshot,
            (_, _) => { },
            _ => null!,
            _ => null!,
            _ => null!,
            () => FixedUtc,
            (_, _, _) => runner);
    }

    private static CliEnvironmentSnapshot CreateSnapshot(string stateRoot, string workspace)
    {
        string userConfigPath = Path.Combine(stateRoot, ".caicli", "config.json");
        WorkspaceContext workspaceContext = new(
            RootPath: workspace,
            ConfigPath: Path.Combine(workspace, ".caicli", "config.json"),
            Status: WorkspaceStatus.Ready);
        EffectiveConfiguration configuration = new(
            WorkspaceRoot: workspace,
            UserConfigPath: userConfigPath,
            WorkspaceConfigPath: workspaceContext.ConfigPath,
            Model: "gpt-test",
            ModelSource: "test",
            AgentBackend: "direct",
            AgentBackendSource: "test",
            DisabledTools: new HashSet<string>(StringComparer.Ordinal),
            ApiKey: SecretValue.From("sk-test-secret"),
            ApiKeySource: "test",
            LoadedConfigPaths: [],
            Warnings: [],
            ConfigSources: []);

        return new CliEnvironmentSnapshot(
            workspaceContext,
            configuration,
            "9.0.308",
            ".NET 9.0",
            "net9.0",
            HasGlobalJson: false);
    }

    private sealed class RecordingAgentRunner(AgentRunResult result) : IAgentRunner
    {
        public AgentRunRequest? LastRequest { get; private set; }

        public AgentRunResult Run(
            AgentRunRequest request,
            ConversationTranscript? transcript = null,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return result;
        }
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
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "caicli-job-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public string CreateDirectory(string name)
        {
            string path = System.IO.Path.Combine(Path, name);
            Directory.CreateDirectory(path);
            return path;
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
