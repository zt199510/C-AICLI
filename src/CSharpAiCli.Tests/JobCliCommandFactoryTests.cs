using System.CommandLine;
using System.Text.Json.Nodes;
using CSharpAiCli.Cli;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class JobCliCommandFactoryTests
{
    [Fact]
    public void Jobs_read_commands_use_user_store_without_model_client_or_command_log()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspace = CreateWorkspace(temp.Path);
        string userConfigPath = CreateUserConfigPath(temp.Path);
        CliEnvironmentSnapshot snapshot = CreateSnapshot(workspace, userConfigPath);
        JobRecord record = JobRecord.CreateRunning(
            JobIdGenerator.Create(DateTimeOffset.Parse("2026-07-13T00:00:00Z")),
            DateTimeOffset.Parse("2026-07-13T00:00:00Z"),
            new JobCommandSummary("exec", Task: "summarize", WorkspaceRoot: workspace),
            "smoke");
        JobRecordStore.Create(snapshot).Create(record.WithStatus(
            JobStatus.Succeeded,
            DateTimeOffset.Parse("2026-07-13T00:00:01Z"),
            exitCode: 0,
            stopReason: "completed",
            summary: "done"));
        List<string> loggedCommands = [];

        using StringWriter listOutput = new();
        RootCommand command = CreateCommand(
            listOutput,
            userConfigPath,
            loggedCommands: loggedCommands,
            chatModelClientFactory: _ => throw new InvalidOperationException("jobs must not create a model client"),
            execAgentRunnerFactory: (_, _, _) => throw new InvalidOperationException("jobs must not create an agent runner"));

        int listExitCode = CliCommandFactory.Invoke(
            command,
            ["jobs", "list", "--output", "json", "--workspace", workspace],
            listOutput);
        JsonObject list = Assert.IsType<JsonObject>(JsonNode.Parse(listOutput.ToString()));

        using StringWriter showOutput = new();
        int showExitCode = CliCommandFactory.Invoke(
            CreateCommand(showOutput, userConfigPath, loggedCommands: loggedCommands),
            ["jobs", "show", record.JobId, "--output", "json", "--workspace", workspace],
            showOutput);
        JsonObject show = Assert.IsType<JsonObject>(JsonNode.Parse(showOutput.ToString()));

        using StringWriter exportOutput = new();
        int exportExitCode = CliCommandFactory.Invoke(
            CreateCommand(exportOutput, userConfigPath, loggedCommands: loggedCommands),
            ["jobs", "export", record.JobId, "--format", "markdown", "--workspace", workspace],
            exportOutput);

        Assert.Equal(0, listExitCode);
        Assert.Equal("jobs.list", list["type"]?.GetValue<string>());
        Assert.Single(Assert.IsType<JsonArray>(list["records"]));
        Assert.Equal(0, showExitCode);
        Assert.Equal("jobs.show", show["type"]?.GetValue<string>());
        Assert.Equal(0, exportExitCode);
        Assert.Contains("# C# AI CLI Job", exportOutput.ToString(), StringComparison.Ordinal);
        Assert.Empty(loggedCommands);
        Assert.False(Directory.Exists(Path.Combine(workspace, ".caicli", "logs")));
    }

    [Fact]
    public void Exec_record_job_writes_success_record_with_task_report_summary()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspace = CreateWorkspace(temp.Path);
        string userConfigPath = CreateUserConfigPath(temp.Path);
        using StringWriter output = new();
        FakeAgentRunner runner = new(AgentRunResult.Success(
            "agent completed task",
            [],
            [
                new AgentRunEvent(
                    Type: "plan",
                    Sequence: 0,
                    Timestamp: DateTimeOffset.Parse("2026-07-13T00:00:00Z"),
                    Message: "plan",
                    Summary: "inspect workspace")
            ]));
        RootCommand command = CreateCommand(
            output,
            userConfigPath,
            execAgentRunnerFactory: (_, _, _) => runner);

        int exitCode = CliCommandFactory.Invoke(
            command,
            ["exec", "--record-job", "--job-name", "success-smoke", "--workspace", workspace, "summarize workspace"],
            output);

        JobRecord record = Assert.Single(ReadJobs(workspace, userConfigPath));
        Assert.Equal(0, exitCode);
        Assert.Equal(JobStatus.Succeeded, record.Status);
        Assert.Equal("exec", record.Command.Family);
        Assert.Equal("success-smoke", record.JobName);
        Assert.NotNull(record.TaskReport);
        Assert.Equal("success", record.TaskReport?.Status);
        Assert.Contains(record.Artifacts, artifact => artifact.Kind == JobArtifactKind.TaskReport);
        Assert.Equal("summarize workspace", runner.Request?.Prompt);
    }

    [Fact]
    public void Exec_record_job_writes_failure_and_report_artifact_without_reference_content()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspace = CreateWorkspace(temp.Path);
        File.WriteAllText(Path.Combine(workspace, "note.txt"), "raw referenced content should not persist");
        string userConfigPath = CreateUserConfigPath(temp.Path);
        using StringWriter output = new();

        int exitCode = CliCommandFactory
            .Create(output, workspacePath => CreateSnapshot(workspacePath, userConfigPath, model: "not configured", apiKey: null))
            .Parse([
                "exec",
                "--record-job",
                "--report",
                "markdown",
                "--report-path",
                ".caicli/reports/job.md",
                "--workspace",
                workspace,
                "summarize @file:note.txt"
            ])
            .Invoke();

        JobRecord record = Assert.Single(ReadJobs(workspace, userConfigPath));
        string jobJson = File.ReadAllText(Directory.EnumerateFiles(Path.Combine(temp.Path, "user", ".caicli", "jobs")).Single());
        Assert.Equal(1, exitCode);
        Assert.Equal(JobStatus.Failed, record.Status);
        Assert.Equal("missing-model", record.ErrorCode);
        Assert.Contains(record.Artifacts, artifact => artifact.Kind == JobArtifactKind.MarkdownReport && artifact.Exists);
        Assert.NotNull(record.TaskReport);
        Assert.Equal(1, record.TaskReport?.ReferenceCount);
        Assert.DoesNotContain("raw referenced content should not persist", jobJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Exec_record_job_writes_reference_failure_record()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspace = CreateWorkspace(temp.Path);
        string userConfigPath = CreateUserConfigPath(temp.Path);
        using StringWriter output = new();

        int exitCode = CliCommandFactory
            .Create(output, workspacePath => CreateSnapshot(workspacePath, userConfigPath, model: "not configured", apiKey: null))
            .Parse([
                "exec",
                "--record-job",
                "--workspace",
                workspace,
                "summarize @file:missing.txt"
            ])
            .Invoke();

        JobRecord record = Assert.Single(ReadJobs(workspace, userConfigPath));
        Assert.Equal(1, exitCode);
        Assert.Equal(JobStatus.Failed, record.Status);
        Assert.NotNull(record.ErrorCode);
        Assert.NotNull(record.TaskReport);
        Assert.Equal(1, record.TaskReport?.ReferenceCount);
    }

    [Fact]
    public void Skills_run_record_job_dry_run_writes_dry_run_metadata()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspace = CreateWorkspace(temp.Path);
        File.WriteAllText(Path.Combine(workspace, "note.txt"), "note");
        string userConfigPath = CreateUserConfigPath(temp.Path);
        using StringWriter output = new();

        int exitCode = CliCommandFactory.Invoke(
            CreateCommand(output, userConfigPath),
            ["skills", "run", "review-only", "--record-job", "--dry-run", "--workspace", workspace, "--", "Review", "@file:note.txt"],
            output);

        JobRecord record = Assert.Single(ReadJobs(workspace, userConfigPath));
        Assert.Equal(0, exitCode);
        Assert.Equal(JobStatus.DryRun, record.Status);
        Assert.Equal("skills run", record.Command.Family);
        Assert.Equal("review-only", record.Command.Skill);
        Assert.Equal("reviewer", record.Command.Expert);
        Assert.True(record.Command.DryRun);
        Assert.Contains(record.Artifacts, artifact => artifact.Kind == JobArtifactKind.SkillPlan);
    }

    [Fact]
    public void Skills_run_record_job_non_dry_run_preserves_skill_metadata()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspace = CreateWorkspace(temp.Path);
        File.WriteAllText(Path.Combine(workspace, "note.txt"), "note");
        string userConfigPath = CreateUserConfigPath(temp.Path);
        using StringWriter output = new();
        FakeAgentRunner runner = new(AgentRunResult.Success("review complete", []));

        int exitCode = CliCommandFactory.Invoke(
            CreateCommand(output, userConfigPath, execAgentRunnerFactory: (_, _, _) => runner),
            ["skills", "run", "review-only", "--record-job", "--report", "none", "--workspace", workspace, "--", "Review", "@file:note.txt"],
            output);

        JobRecord record = Assert.Single(ReadJobs(workspace, userConfigPath));
        Assert.Equal(0, exitCode);
        Assert.Equal(JobStatus.Succeeded, record.Status);
        Assert.Equal("skills run", record.Command.Family);
        Assert.Equal("review-only", record.Command.Skill);
        Assert.Equal("reviewer", record.Command.Expert);
        Assert.False(record.Command.DryRun);
        Assert.NotNull(record.TaskReport);
        Assert.Equal("review-only", record.TaskReport is null ? null : runner.Request?.Skill?.Name);
    }

    private static RootCommand CreateCommand(
        StringWriter output,
        string userConfigPath,
        List<string>? loggedCommands = null,
        Func<CliEnvironmentSnapshot, IChatModelClient>? chatModelClientFactory = null,
        Func<CliEnvironmentSnapshot, ToolRegistry, IToolExecutor, IAgentRunner>? execAgentRunnerFactory = null)
    {
        return CliCommandFactory.Create(
            output,
            workspacePath => CreateSnapshot(workspacePath, userConfigPath),
            (commandName, _) => loggedCommands?.Add(commandName),
            chatModelClientFactory ?? (_ => throw new InvalidOperationException("model client not expected")),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => throw new InvalidOperationException("conversation store not expected"),
            () => DateTimeOffset.Parse("2026-07-13T00:00:00Z"),
            execAgentRunnerFactory ?? ((_, _, _) => throw new InvalidOperationException("agent runner not expected")));
    }

    private static IReadOnlyList<JobRecord> ReadJobs(string workspace, string userConfigPath)
    {
        return JobRecordStore.Create(CreateSnapshot(workspace, userConfigPath)).List().Records;
    }

    private static CliEnvironmentSnapshot CreateSnapshot(
        string? workspacePath,
        string userConfigPath,
        string model = "gpt-test",
        string? apiKey = "sk-test-secret")
    {
        string workspaceRoot = string.IsNullOrWhiteSpace(workspacePath) ? "workspace-root" : workspacePath;
        WorkspaceContext workspace = new(
            RootPath: workspaceRoot,
            ConfigPath: Path.Combine(workspaceRoot, ".caicli", "config.json"),
            Status: WorkspaceStatus.Ready);
        EffectiveConfiguration configuration = new(
            WorkspaceRoot: workspaceRoot,
            UserConfigPath: userConfigPath,
            WorkspaceConfigPath: Path.Combine(workspaceRoot, ".caicli", "config.json"),
            Model: model,
            ModelSource: model == "not configured" ? "default" : "test",
            AgentBackend: "direct",
            AgentBackendSource: "default",
            DisabledTools: new HashSet<string>(StringComparer.Ordinal),
            ApiKey: SecretValue.From(apiKey),
            ApiKeySource: apiKey is null ? "missing" : "OPENAI_API_KEY",
            LoadedConfigPaths: [],
            Warnings: [],
            ConfigSources: []);

        return new CliEnvironmentSnapshot(
            Workspace: workspace,
            Configuration: configuration,
            DotnetSdkVersion: "9.0.308",
            DotnetRuntime: ".NET 9.0.0",
            TargetFramework: "net9.0",
            HasGlobalJson: false);
    }

    private static string CreateWorkspace(string root)
    {
        string workspace = Path.Combine(root, "workspace");
        Directory.CreateDirectory(workspace);
        return workspace;
    }

    private static string CreateUserConfigPath(string root)
    {
        return Path.Combine(root, "user", ".caicli", "config.json");
    }

    private sealed class FakeAgentRunner(AgentRunResult result) : IAgentRunner
    {
        public AgentRunRequest? Request { get; private set; }

        public AgentRunResult Run(
            AgentRunRequest request,
            ConversationTranscript? transcript = null,
            CancellationToken cancellationToken = default)
        {
            Request = request;
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
                "caicli-job-cli-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
