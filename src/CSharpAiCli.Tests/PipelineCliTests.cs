using System.CommandLine;
using System.Text.Json.Nodes;
using CSharpAiCli.Cli;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class PipelineCliTests
{
    private static readonly DateTimeOffset FixedUtc = DateTimeOffset.Parse("2026-07-13T08:00:00Z");

    [Fact]
    public void Pipeline_list_and_plan_are_json_parseable_and_do_not_start_execution_or_create_state()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspace = temp.CreateDirectory("workspace");
        List<string> loggedCommands = [];

        using StringWriter listOutput = new();
        int listExitCode = CreateCommand(
                listOutput,
                temp.UserConfigPath,
                loggedCommands,
                (_, _, _) => throw new InvalidOperationException("pipeline list must not start an agent"))
            .Parse(["pipeline", "list", "--output", "json"])
            .Invoke();
        JsonObject list = Assert.IsType<JsonObject>(JsonNode.Parse(listOutput.ToString()));

        using StringWriter planOutput = new();
        int planExitCode = CreateCommand(
                planOutput,
                temp.UserConfigPath,
                loggedCommands,
                (_, _, _) => throw new InvalidOperationException("pipeline plan must not start an agent"))
            .Parse(["pipeline", "plan", "security-review", "--workspace", workspace, "--output", "json", "--", "Review source"])
            .Invoke();
        JsonObject plan = Assert.IsType<JsonObject>(JsonNode.Parse(planOutput.ToString()));
        CliEnvironmentSnapshot snapshot = CreateSnapshot(workspace, temp.UserConfigPath);

        Assert.Equal(0, listExitCode);
        Assert.Equal("pipeline.list", list["type"]?.GetValue<string>());
        Assert.Equal(3, Assert.IsType<JsonArray>(list["pipelines"]).Count);
        Assert.Equal(0, planExitCode);
        Assert.Equal("pipeline.plan", plan["type"]?.GetValue<string>());
        Assert.Equal("security-review", plan["plan"]?["pipeline"]?["name"]?.GetValue<string>());
        Assert.Empty(loggedCommands);
        Assert.False(Directory.Exists(TaskQueueStore.Create(snapshot).QueueDirectory));
        Assert.False(Directory.Exists(JobRecordStore.Create(snapshot).JobDirectory));
    }

    [Fact]
    public void Pipeline_run_executes_fix_review_test_sequentially_and_records_each_queue_job()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspace = temp.CreateDirectory("workspace");
        SequenceAgentRunner runner = new(
            AgentRunResult.Success("implemented", []),
            AgentRunResult.Success("reviewed", []),
            AgentRunResult.Success("tested", []));
        List<string> loggedCommands = [];
        using StringWriter output = new();
        RootCommand command = CreateCommand(
            output,
            temp.UserConfigPath,
            loggedCommands,
            (_, _, _) => runner);

        int exitCode = command.Parse([
            "pipeline", "run", "fix-review-test", "--workspace", workspace,
            "--output", "json", "--", "Fix apiKey=pipeline-secret"
        ]).Invoke();

        JsonObject result = Assert.IsType<JsonObject>(JsonNode.Parse(output.ToString()));
        JsonArray roles = Assert.IsType<JsonArray>(result["report"]?["roles"]);
        CliEnvironmentSnapshot snapshot = CreateSnapshot(workspace, temp.UserConfigPath);
        IReadOnlyList<TaskQueueItem> queueItems = TaskQueueStore.Create(snapshot).List().Items;
        IReadOnlyList<JobRecord> jobs = JobRecordStore.Create(snapshot).List().Records;
        string persisted = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(Path.GetDirectoryName(temp.UserConfigPath)!, "*.json", SearchOption.AllDirectories)
                .Select(File.ReadAllText));

        Assert.Equal(0, exitCode);
        Assert.Equal("succeeded", result["status"]?.GetValue<string>());
        Assert.Equal(3, roles.Count);
        Assert.Equal(["implementer", "reviewer", "tester"], roles.Select(role => role?["role"]?.GetValue<string>()));
        Assert.Equal(3, queueItems.Count);
        Assert.Equal(3, jobs.Count);
        Assert.All(queueItems, item => Assert.Equal(TaskQueueStatus.Succeeded, item.Status));
        Assert.All(queueItems, item => Assert.NotNull(Assert.Single(item.Attempts).JobId));
        Assert.Equal(["bugfix", "reviewer", "tester"], runner.Requests.Select(request => request.ExpertProfile?.Name));
        Assert.Null(runner.Requests[0].Skill);
        Assert.Equal("review-only", runner.Requests[1].Skill?.Name);
        Assert.Null(runner.Requests[2].Skill);
        Assert.Contains("pipeline run", loggedCommands);
        Assert.Contains("skills run", loggedCommands);
        Assert.DoesNotContain("pipeline-secret", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("pipeline-secret", persisted, StringComparison.Ordinal);
        Assert.Contains("[redacted]", persisted, StringComparison.Ordinal);
    }

    [Fact]
    public void Pipeline_run_short_circuits_after_failure_and_keeps_prior_role_artifact()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspace = temp.CreateDirectory("workspace");
        SequenceAgentRunner runner = new(
            AgentRunResult.Success("implemented", []),
            AgentRunResult.Failure(new AgentError("review-failed", "review failed", false), []),
            AgentRunResult.Success("must not run", []));
        using StringWriter output = new();

        int exitCode = CreateCommand(output, temp.UserConfigPath, execAgentRunnerFactory: (_, _, _) => runner)
            .Parse(["pipeline", "run", "fix-review-test", "--workspace", workspace, "--output", "json", "--", "Fix tests"])
            .Invoke();

        JsonObject result = Assert.IsType<JsonObject>(JsonNode.Parse(output.ToString()));
        JsonArray roles = Assert.IsType<JsonArray>(result["report"]?["roles"]);
        JsonArray artifacts = Assert.IsType<JsonArray>(result["report"]?["artifacts"]);
        JsonArray risks = Assert.IsType<JsonArray>(result["report"]?["remainingRisks"]);
        CliEnvironmentSnapshot snapshot = CreateSnapshot(workspace, temp.UserConfigPath);

        Assert.Equal(1, exitCode);
        Assert.Equal("failed", result["status"]?.GetValue<string>());
        Assert.Equal(2, roles.Count);
        Assert.Equal(2, runner.Requests.Count);
        Assert.Equal(2, TaskQueueStore.Create(snapshot).List().Items.Count);
        Assert.Equal(2, JobRecordStore.Create(snapshot).List().Records.Count);
        Assert.Contains(artifacts, artifact => artifact?["role"]?.GetValue<string>() == "implementer");
        Assert.Contains(risks, risk => risk?.GetValue<string>().Contains("tester", StringComparison.Ordinal) == true);
    }

    [Theory]
    [InlineData("review-test", "reviewer")]
    [InlineData("security-review", "security")]
    public void Read_only_pipeline_roles_do_not_register_or_execute_patch_shell_or_mcp_tools(
        string pipeline,
        string expectedRole)
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspace = temp.CreateDirectory("workspace");
        string notePath = Path.Combine(workspace, "note.txt");
        File.WriteAllText(notePath, "before");
        ExecutorToolCallAgentRunner runner = new(
            "workspace.apply_patch",
            """{"path":"note.txt","find":"before","replace":"after"}""");
        List<IReadOnlyList<string>> registeredTools = [];
        using StringWriter output = new();
        RootCommand command = CreateCommand(
            output,
            temp.UserConfigPath,
            execAgentRunnerFactory: (_, registry, executor) =>
            {
                registeredTools.Add(registry.List().Select(tool => tool.Name).ToArray());
                runner.Executor = executor;
                return runner;
            });

        int exitCode = command.Parse([
            "pipeline", "run", pipeline, "--workspace", workspace, "--output", "json", "--", "Review note"
        ]).Invoke();

        JsonObject result = Assert.IsType<JsonObject>(JsonNode.Parse(output.ToString()));
        JsonNode role = Assert.Single(Assert.IsType<JsonArray>(result["report"]?["roles"]))!;
        IReadOnlyList<string> tools = Assert.Single(registeredTools);
        Assert.Equal(1, exitCode);
        Assert.Equal(expectedRole, role["role"]?.GetValue<string>());
        Assert.Equal(ToolErrorCode.ToolDisabled, role["errorCode"]?.GetValue<string>());
        Assert.True(role["boundary"]?["isReadOnly"]?.GetValue<bool>());
        Assert.DoesNotContain("workspace.apply_patch", tools);
        Assert.DoesNotContain("workspace.run_shell", tools);
        Assert.DoesNotContain(tools, tool => tool.StartsWith("mcp.", StringComparison.Ordinal));
        Assert.Equal("before", File.ReadAllText(notePath));
        Assert.True(runner.Requests.Single().ExpertProfile?.IsReadOnly);
    }

    private static RootCommand CreateCommand(
        StringWriter output,
        string userConfigPath,
        List<string>? loggedCommands = null,
        Func<CliEnvironmentSnapshot, ToolRegistry, IToolExecutor, IAgentRunner>? execAgentRunnerFactory = null)
    {
        return CliCommandFactory.Create(
            output,
            workspacePath => CreateSnapshot(workspacePath, userConfigPath),
            (commandName, _) => loggedCommands?.Add(commandName),
            _ => throw new InvalidOperationException("chat model client not expected"),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => throw new InvalidOperationException("conversation store not expected"),
            () => FixedUtc,
            execAgentRunnerFactory ?? ((_, _, _) => throw new InvalidOperationException("agent runner not expected")));
    }

    private static CliEnvironmentSnapshot CreateSnapshot(string? workspacePath, string userConfigPath)
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
            DisabledTools: new HashSet<string>(StringComparer.Ordinal),
            ApiKey: SecretValue.From("sk-test-secret"),
            ApiKeySource: "OPENAI_API_KEY",
            LoadedConfigPaths: [],
            Warnings: [],
            ConfigSources: [])
        {
            ApprovalMode = ApprovalMode.OnRequest,
            ShellPolicy = ShellPolicyConfiguration.Default,
        };
        return new CliEnvironmentSnapshot(
            workspace,
            configuration,
            "9.0.308",
            ".NET 9.0.0",
            "net9.0",
            false);
    }

    private sealed class SequenceAgentRunner(params AgentRunResult[] results) : IAgentRunner
    {
        private int index;

        public List<AgentRunRequest> Requests { get; } = [];

        public AgentRunResult Run(
            AgentRunRequest request,
            ConversationTranscript? transcript = null,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            int current = index++;
            return results[Math.Min(current, results.Length - 1)];
        }
    }

    private sealed class ExecutorToolCallAgentRunner(string toolName, string argumentsJson) : IAgentRunner
    {
        public IToolExecutor? Executor { get; set; }

        public List<AgentRunRequest> Requests { get; } = [];

        public AgentRunResult Run(
            AgentRunRequest request,
            ConversationTranscript? transcript = null,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            ToolExecutionResult result = (Executor ?? throw new InvalidOperationException("Executor was not injected."))
                .Execute(
                    toolName,
                    new ToolExecutionContext("pipeline_tool_call", request.Workspace, argumentsJson),
                    cancellationToken);
            ConversationToolCall toolCall = ConversationToolCall.FromExecution(
                "pipeline_tool_call",
                toolName,
                argumentsJson,
                result,
                FixedUtc);
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

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;

        public string Path { get; }

        public string UserConfigPath => System.IO.Path.Combine(Path, "user", ".caicli", "config.json");

        public static TempDirectory Create()
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "caicli-pipeline-cli-tests-" + Guid.NewGuid().ToString("N"));
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
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
            }
        }
    }
}
