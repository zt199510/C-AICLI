using System.CommandLine;
using System.Text.Json.Nodes;
using CSharpAiCli.Cli;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class AutomationCliTests
{
    private static readonly DateTimeOffset FixedUtc = DateTimeOffset.Parse("2026-07-13T08:00:00Z");

    [Fact]
    public void List_validate_plan_and_dry_run_are_local_redacted_and_non_persistent()
    {
        using AutomationTests.TempDirectory temp = AutomationTests.TempDirectory.Create();
        string workspace = temp.CreateDirectory("workspace");
        AutomationTests.WriteManifest(
            workspace,
            "nightly-review.json",
            AutomationTests.ValidReviewManifest("Review apiKey=automation-secret"));
        List<string> loggedCommands = [];

        JsonObject list = InvokeJson(CreateCommand(
            temp.UserConfigPath,
            loggedCommands,
            (_, _, _) => throw new InvalidOperationException("local automation metadata must not start an agent")),
            ["automation", "list", "--workspace", workspace, "--output", "json"]);
        JsonObject validate = InvokeJson(CreateCommand(temp.UserConfigPath, loggedCommands),
            ["automation", "validate", "--workspace", workspace, "--output", "json"]);
        JsonObject plan = InvokeJson(CreateCommand(temp.UserConfigPath, loggedCommands),
            ["automation", "plan", "nightly-review", "--workspace", workspace, "--output", "json"]);
        JsonObject dryRun = InvokeJson(CreateCommand(temp.UserConfigPath, loggedCommands),
            ["automation", "run", "nightly-review", "--dry-run", "--workspace", workspace, "--output", "json"]);
        CliEnvironmentSnapshot snapshot = CreateSnapshot(workspace, temp.UserConfigPath);

        Assert.Equal("automation.list", list["type"]?.GetValue<string>());
        Assert.Equal("succeeded", validate["status"]?.GetValue<string>());
        Assert.Equal("automation.plan", plan["type"]?.GetValue<string>());
        Assert.False(plan["plan"]?["schedulePreview"]?["enabled"]?.GetValue<bool>());
        Assert.Equal("automation.run.dry-run", dryRun["type"]?.GetValue<string>());
        Assert.DoesNotContain("automation-secret", plan.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain("automation-secret", dryRun.ToJsonString(), StringComparison.Ordinal);
        Assert.Contains("[redacted]", dryRun.ToJsonString(), StringComparison.Ordinal);
        Assert.Empty(loggedCommands);
        Assert.False(Directory.Exists(TaskQueueStore.Create(snapshot).QueueDirectory));
        Assert.False(Directory.Exists(JobRecordStore.Create(snapshot).JobDirectory));
        Assert.False(Directory.Exists(Path.Combine(workspace, ".caicli", "logs")));
    }

    [Fact]
    public void Validate_reports_invalid_and_unsafe_manifests_without_execution()
    {
        using AutomationTests.TempDirectory temp = AutomationTests.TempDirectory.Create();
        string workspace = temp.CreateDirectory("workspace");
        AutomationTests.WriteManifest(workspace, "invalid.json", """
            {
              "schemaVersion": 1,
              "name": "unsafe",
              "description": "invalid direct script",
              "trigger": { "type": "manual" },
              "target": { "type": "queue", "family": "exec", "task": "run", "command": "pwsh bad.ps1" },
              "safety": { "manualOnly": false, "allowWrites": false, "allowShell": false, "allowMcp": false }
            }
            """);
        using StringWriter output = new();
        RootCommand command = CreateCommand(
            temp.UserConfigPath,
            execAgentRunnerFactory: (_, _, _) => throw new InvalidOperationException("validation must not start an agent"),
            output: output);

        int exitCode = command.Parse(["automation", "validate", "--workspace", workspace, "--output", "json"]).Invoke();
        JsonObject result = Assert.IsType<JsonObject>(JsonNode.Parse(output.ToString()));

        Assert.Equal(1, exitCode);
        Assert.Equal("failed", result["status"]?.GetValue<string>());
        Assert.NotEmpty(Assert.IsType<JsonArray>(result["diagnostics"]));
    }

    [Fact]
    public void Manual_skill_run_reuses_queue_job_path_and_records_redacted_automation_artifact()
    {
        using AutomationTests.TempDirectory temp = AutomationTests.TempDirectory.Create();
        string workspace = temp.CreateDirectory("workspace");
        AutomationTests.WriteManifest(
            workspace,
            "nightly-review.json",
            AutomationTests.ValidReviewManifest("Review password=automation-secret"));
        RecordingAgentRunner runner = new(AgentRunResult.Success("reviewed", []));
        using StringWriter output = new();
        RootCommand command = CreateCommand(
            temp.UserConfigPath,
            execAgentRunnerFactory: (_, _, _) => runner,
            output: output);

        int exitCode = command.Parse([
            "automation", "run", "nightly-review", "--manual", "--workspace", workspace, "--output", "json"
        ]).Invoke();

        JsonObject result = Assert.IsType<JsonObject>(JsonNode.Parse(output.ToString()));
        CliEnvironmentSnapshot snapshot = CreateSnapshot(workspace, temp.UserConfigPath);
        TaskQueueItem queue = Assert.Single(TaskQueueStore.Create(snapshot).List().Items);
        JobRecord job = Assert.Single(JobRecordStore.Create(snapshot).List().Records);
        string queueJson = File.ReadAllText(Directory.EnumerateFiles(TaskQueueStore.Create(snapshot).QueueDirectory).Single());
        string jobJson = File.ReadAllText(Directory.EnumerateFiles(JobRecordStore.Create(snapshot).JobDirectory, "*.json").Single());

        Assert.Equal(0, exitCode);
        Assert.Equal("automation.result", result["type"]?.GetValue<string>());
        Assert.Equal("succeeded", result["status"]?.GetValue<string>());
        Assert.Equal("nightly-review", queue.Request.Automation?.Automation);
        Assert.Equal(queue.Request.Automation, job.Command.Automation);
        Assert.Contains(job.Artifacts, artifact => artifact.Kind == JobArtifactKind.Automation && artifact.Exists);
        Assert.Equal("review-only", runner.LastRequest?.Skill?.Name);
        Assert.True(runner.LastRequest?.ExpertProfile?.IsReadOnly);
        Assert.DoesNotContain("automation-secret", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("automation-secret", queueJson, StringComparison.Ordinal);
        Assert.DoesNotContain("automation-secret", jobJson, StringComparison.Ordinal);
        Assert.Contains("[redacted]", queueJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Manual_exec_preserves_configured_disabled_tools()
    {
        using AutomationTests.TempDirectory temp = AutomationTests.TempDirectory.Create();
        string workspace = temp.CreateDirectory("workspace");
        File.WriteAllText(Path.Combine(workspace, "note.txt"), "content");
        AutomationTests.WriteManifest(workspace, "manual-check.json", AutomationTests.ValidExecManifest("Read note"));
        ExecutorToolCallAgentRunner runner = new("workspace.read_text", """{"path":"note.txt"}""");
        using StringWriter output = new();
        RootCommand command = CreateCommand(
            temp.UserConfigPath,
            execAgentRunnerFactory: (_, _, executor) =>
            {
                runner.Executor = executor;
                return runner;
            },
            disabledTools: new HashSet<string>(StringComparer.Ordinal) { "workspace.read_text" },
            output: output);

        int exitCode = command.Parse([
            "automation", "run", "manual-check", "--manual", "--workspace", workspace, "--output", "json"
        ]).Invoke();

        TaskQueueItem item = Assert.Single(TaskQueueStore.Create(CreateSnapshot(
            workspace,
            temp.UserConfigPath,
            new HashSet<string>(StringComparer.Ordinal) { "workspace.read_text" })).List().Items);
        Assert.Equal(1, exitCode);
        Assert.Equal(ToolErrorCode.ToolDisabled, item.ErrorCode);
    }

    [Fact]
    public void Manual_exec_does_not_promote_write_approval()
    {
        using AutomationTests.TempDirectory temp = AutomationTests.TempDirectory.Create();
        string workspace = temp.CreateDirectory("workspace");
        string notePath = Path.Combine(workspace, "note.txt");
        File.WriteAllText(notePath, "before");
        AutomationTests.WriteManifest(workspace, "manual-check.json", AutomationTests.ValidExecManifest("Change note"));
        ExecutorToolCallAgentRunner runner = new(
            "workspace.apply_patch",
            """{"path":"note.txt","find":"before","replace":"after"}""");
        using StringWriter output = new();
        RootCommand command = CreateCommand(
            temp.UserConfigPath,
            execAgentRunnerFactory: (_, _, executor) =>
            {
                runner.Executor = executor;
                return runner;
            },
            output: output);

        int exitCode = command.Parse([
            "automation", "run", "manual-check", "--manual", "--workspace", workspace, "--output", "json"
        ]).Invoke();

        TaskQueueItem item = Assert.Single(TaskQueueStore.Create(CreateSnapshot(workspace, temp.UserConfigPath)).List().Items);
        Assert.Equal(1, exitCode);
        Assert.Equal(ToolErrorCode.ApprovalDenied, item.ErrorCode);
        Assert.Equal("before", File.ReadAllText(notePath));
    }

    [Fact]
    public void Manual_pipeline_correlates_every_role_queue_and_job()
    {
        using AutomationTests.TempDirectory temp = AutomationTests.TempDirectory.Create();
        string workspace = temp.CreateDirectory("workspace");
        AutomationTests.WriteManifest(workspace, "pipeline-check.json", AutomationTests.ValidPipelineManifest("Review and test"));
        SequenceAgentRunner runner = new(
            AgentRunResult.Success("reviewed", []),
            AgentRunResult.Success("tested", []));
        using StringWriter output = new();
        RootCommand command = CreateCommand(
            temp.UserConfigPath,
            execAgentRunnerFactory: (_, _, _) => runner,
            output: output);

        int exitCode = command.Parse([
            "automation", "run", "pipeline-check", "--manual", "--workspace", workspace, "--output", "json"
        ]).Invoke();

        CliEnvironmentSnapshot snapshot = CreateSnapshot(workspace, temp.UserConfigPath);
        IReadOnlyList<TaskQueueItem> queues = TaskQueueStore.Create(snapshot).List().Items;
        IReadOnlyList<JobRecord> jobs = JobRecordStore.Create(snapshot).List().Records;
        Assert.Equal(0, exitCode);
        Assert.Equal(2, queues.Count);
        Assert.Equal(2, jobs.Count);
        Assert.All(queues, queue => Assert.Equal("pipeline-check", queue.Request.Automation?.Automation));
        Assert.Single(queues.Select(queue => queue.Request.Automation?.RunId).Distinct());
        Assert.All(jobs, job => Assert.Contains(job.Artifacts, artifact => artifact.Kind == JobArtifactKind.Automation));
    }

    private static JsonObject InvokeJson(RootCommand command, string[] arguments)
    {
        StringWriter output = Assert.IsType<StringWriter>(CommandOutput.Value);
        output.GetStringBuilder().Clear();
        int exitCode = command.Parse(arguments).Invoke();
        Assert.Equal(0, exitCode);
        return Assert.IsType<JsonObject>(JsonNode.Parse(output.ToString()));
    }

    private static readonly AsyncLocal<TextWriter?> CommandOutput = new();

    private static RootCommand CreateCommand(
        string userConfigPath,
        List<string>? loggedCommands = null,
        Func<CliEnvironmentSnapshot, ToolRegistry, IToolExecutor, IAgentRunner>? execAgentRunnerFactory = null,
        IReadOnlySet<string>? disabledTools = null,
        StringWriter? output = null)
    {
        output ??= new StringWriter();
        CommandOutput.Value = output;
        return CliCommandFactory.Create(
            output,
            workspacePath => CreateSnapshot(workspacePath, userConfigPath, disabledTools),
            (commandName, _) => loggedCommands?.Add(commandName),
            _ => throw new InvalidOperationException("chat model client not expected"),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => throw new InvalidOperationException("conversation store not expected"),
            () => FixedUtc,
            execAgentRunnerFactory ?? ((_, _, _) => throw new InvalidOperationException("agent runner not expected")));
    }

    private static CliEnvironmentSnapshot CreateSnapshot(
        string? workspacePath,
        string userConfigPath,
        IReadOnlySet<string>? disabledTools = null)
    {
        string workspaceRoot = string.IsNullOrWhiteSpace(workspacePath) ? "workspace-root" : workspacePath;
        WorkspaceContext workspace = new(
            workspaceRoot,
            Path.Combine(workspaceRoot, ".caicli", "config.json"),
            WorkspaceStatus.Ready);
        EffectiveConfiguration configuration = new(
            WorkspaceRoot: workspaceRoot,
            UserConfigPath: userConfigPath,
            WorkspaceConfigPath: Path.Combine(workspaceRoot, ".caicli", "config.json"),
            Model: "gpt-test",
            ModelSource: "test",
            AgentBackend: "direct",
            AgentBackendSource: "default",
            DisabledTools: disabledTools ?? new HashSet<string>(StringComparer.Ordinal),
            ApiKey: SecretValue.From("sk-test-secret"),
            ApiKeySource: "OPENAI_API_KEY",
            LoadedConfigPaths: [],
            Warnings: [],
            ConfigSources: [])
        {
            ApprovalMode = ApprovalMode.OnRequest,
            ShellPolicy = ShellPolicyConfiguration.Default,
        };
        return new CliEnvironmentSnapshot(workspace, configuration, "9.0.308", ".NET 9.0.0", "net9.0", false);
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

    private sealed class SequenceAgentRunner(params AgentRunResult[] results) : IAgentRunner
    {
        private int index;

        public AgentRunResult Run(
            AgentRunRequest request,
            ConversationTranscript? transcript = null,
            CancellationToken cancellationToken = default)
        {
            int current = index++;
            return results[Math.Min(current, results.Length - 1)];
        }
    }

    private sealed class ExecutorToolCallAgentRunner(string toolName, string argumentsJson) : IAgentRunner
    {
        public IToolExecutor? Executor { get; set; }

        public AgentRunResult Run(
            AgentRunRequest request,
            ConversationTranscript? transcript = null,
            CancellationToken cancellationToken = default)
        {
            ToolExecutionResult result = (Executor ?? throw new InvalidOperationException("Executor was not injected."))
                .Execute(toolName, new ToolExecutionContext("automation_tool_call", request.Workspace, argumentsJson), cancellationToken);
            ConversationToolCall toolCall = ConversationToolCall.FromExecution(
                "automation_tool_call", toolName, argumentsJson, result, FixedUtc);
            AgentRunEvent toolEvent = new(
                result.Succeeded ? "tool.completed" : "tool.failed",
                0,
                FixedUtc,
                Summary: result.Summary,
                ErrorCode: result.ErrorCode,
                ApprovalStatus: result.ApprovalStatus);
            return result.Succeeded
                ? AgentRunResult.Success(result.Summary, [toolCall], [toolEvent])
                : AgentRunResult.Failure(
                    new AgentError(result.ErrorCode ?? "tool-call-failed", result.Summary, result.Retryable),
                    [toolCall],
                    [toolEvent]);
        }
    }
}
