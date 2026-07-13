using System.CommandLine;
using System.Text.Json.Nodes;
using CSharpAiCli.Cli;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class TaskQueueCliTests
{
    private static readonly DateTimeOffset FixedUtc = DateTimeOffset.Parse("2026-07-13T08:00:00Z");

    [Fact]
    public void Queue_add_list_show_are_json_parseable_and_do_not_start_execution()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspace = temp.CreateDirectory("workspace");
        string userConfigPath = temp.UserConfigPath;
        List<string> loggedCommands = [];
        using StringWriter addOutput = new();
        RootCommand addCommand = CreateCommand(
            addOutput,
            userConfigPath,
            loggedCommands,
            (_, _, _) => throw new InvalidOperationException("queue metadata commands must not start an agent"));

        int addExitCode = addCommand.Parse([
            "queue", "add", "exec", "--workspace", workspace, "--expert", "reviewer",
            "--output", "json", "--", "Review apiKey=queue-secret"
        ]).Invoke();
        JsonObject added = Assert.IsType<JsonObject>(JsonNode.Parse(addOutput.ToString()));
        string queueId = added["item"]?["queueId"]?.GetValue<string>() ?? string.Empty;

        using StringWriter listOutput = new();
        int listExitCode = CreateCommand(listOutput, userConfigPath, loggedCommands)
            .Parse(["queue", "list", "--workspace", workspace, "--output", "json"])
            .Invoke();
        JsonObject list = Assert.IsType<JsonObject>(JsonNode.Parse(listOutput.ToString()));

        using StringWriter showOutput = new();
        int showExitCode = CreateCommand(showOutput, userConfigPath, loggedCommands)
            .Parse(["queue", "show", queueId, "--workspace", workspace, "--output", "json"])
            .Invoke();
        JsonObject show = Assert.IsType<JsonObject>(JsonNode.Parse(showOutput.ToString()));
        string queueJson = File.ReadAllText(Directory.EnumerateFiles(
            TaskQueueStore.Create(CreateSnapshot(workspace, userConfigPath)).QueueDirectory).Single());

        Assert.Equal(0, addExitCode);
        Assert.Equal("queue.add", added["type"]?.GetValue<string>());
        Assert.Equal("pending", added["item"]?["status"]?.GetValue<string>());
        Assert.Equal(0, listExitCode);
        Assert.Equal("queue.list", list["type"]?.GetValue<string>());
        Assert.Single(Assert.IsType<JsonArray>(list["items"]));
        Assert.Equal(0, showExitCode);
        Assert.Equal("queue.show", show["type"]?.GetValue<string>());
        Assert.DoesNotContain("queue-secret", queueJson, StringComparison.Ordinal);
        Assert.Contains("[redacted]", queueJson, StringComparison.Ordinal);
        Assert.Empty(loggedCommands);
        Assert.False(Directory.Exists(Path.Combine(workspace, ".caicli", "logs")));
    }

    [Fact]
    public void Queue_run_exec_uses_agent_path_and_records_queue_job_pointer()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspace = temp.CreateDirectory("workspace");
        RecordingAgentRunner runner = new(AgentRunResult.Success("completed", []));
        using StringWriter output = new();
        RootCommand command = CreateCommand(output, temp.UserConfigPath, execAgentRunnerFactory: (_, _, _) => runner);
        int addExitCode = command.Parse([
            "queue", "add", "exec", "--workspace", workspace, "--expert", "reviewer", "--", "Review workspace"
        ]).Invoke();
        TaskQueueStore queueStore = TaskQueueStore.Create(CreateSnapshot(workspace, temp.UserConfigPath));
        string queueId = Assert.Single(queueStore.List().Items).QueueId;

        int runExitCode = command.Parse(["queue", "run", queueId, "--workspace", workspace]).Invoke();

        TaskQueueItem item = Assert.Single(queueStore.List().Items);
        JobRecord job = Assert.Single(JobRecordStore.Create(CreateSnapshot(workspace, temp.UserConfigPath)).List().Records);
        Assert.Equal(0, addExitCode);
        Assert.Equal(0, runExitCode);
        Assert.Equal(TaskQueueStatus.Succeeded, item.Status);
        Assert.Equal(job.JobId, item.LatestJobId);
        Assert.Equal(job.JobId, Assert.Single(item.Attempts).JobId);
        Assert.Equal(queueId, job.JobName);
        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal("Review workspace", runner.LastRequest?.Prompt);
        Assert.Equal("reviewer", runner.LastRequest?.ExpertProfile?.Name);
        Assert.True(runner.LastRequest?.ExpertProfile?.IsReadOnly);
    }

    [Fact]
    public void Queue_run_failed_item_can_retry_and_tracks_each_job_attempt()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspace = temp.CreateDirectory("workspace");
        SequenceAgentRunner runner = new(
            AgentRunResult.Failure(new AgentError("first-failure", "first failed", false), []),
            AgentRunResult.Success("second completed", []));
        using StringWriter output = new();
        RootCommand command = CreateCommand(output, temp.UserConfigPath, execAgentRunnerFactory: (_, _, _) => runner);
        command.Parse(["queue", "add", "exec", "--workspace", workspace, "--", "retry task"]).Invoke();
        TaskQueueStore queueStore = TaskQueueStore.Create(CreateSnapshot(workspace, temp.UserConfigPath));
        string queueId = Assert.Single(queueStore.List().Items).QueueId;

        int firstExitCode = command.Parse(["queue", "run", queueId, "--workspace", workspace]).Invoke();
        int secondExitCode = command.Parse(["queue", "run", queueId, "--workspace", workspace]).Invoke();

        TaskQueueItem item = queueStore.Read(queueId).Item!;
        IReadOnlyList<JobRecord> jobs = JobRecordStore.Create(CreateSnapshot(workspace, temp.UserConfigPath)).List().Records;
        Assert.Equal(1, firstExitCode);
        Assert.Equal(0, secondExitCode);
        Assert.Equal(TaskQueueStatus.Succeeded, item.Status);
        Assert.Equal(2, item.Attempts.Count);
        Assert.Equal(TaskQueueStatus.Failed, item.Attempts[0].Status);
        Assert.Equal(TaskQueueStatus.Succeeded, item.Attempts[1].Status);
        Assert.Equal(2, jobs.Count);
        Assert.All(item.Attempts, attempt => Assert.Contains(jobs, job => job.JobId == attempt.JobId));
    }

    [Fact]
    public void Queue_run_unexpected_delegate_failure_reaches_failed_queue_and_job_state()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspace = temp.CreateDirectory("workspace");
        using StringWriter output = new();
        RootCommand command = CreateCommand(
            output,
            temp.UserConfigPath,
            execAgentRunnerFactory: (_, _, _) => new ThrowingAgentRunner());
        command.Parse(["queue", "add", "exec", "--workspace", workspace, "--", "throw task"]).Invoke();
        TaskQueueStore queueStore = TaskQueueStore.Create(CreateSnapshot(workspace, temp.UserConfigPath));
        string queueId = Assert.Single(queueStore.List().Items).QueueId;

        int exitCode = command.Parse(["queue", "run", queueId, "--workspace", workspace]).Invoke();

        TaskQueueItem item = queueStore.Read(queueId).Item!;
        JobRecord job = Assert.Single(JobRecordStore.Create(CreateSnapshot(workspace, temp.UserConfigPath)).List().Records);
        Assert.Equal(1, exitCode);
        Assert.Equal(TaskQueueStatus.Failed, item.Status);
        Assert.Equal(TaskQueueErrorCode.ExecutionFailed, item.ErrorCode);
        Assert.Equal(job.JobId, item.LatestJobId);
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal(TaskQueueErrorCode.ExecutionFailed, job.ErrorCode);
    }

    [Fact]
    public void Queue_run_does_not_promote_approval_for_write_tool()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspace = temp.CreateDirectory("workspace");
        string notePath = Path.Combine(workspace, "note.txt");
        File.WriteAllText(notePath, "before");
        ExecutorToolCallAgentRunner runner = new(
            "workspace.apply_patch",
            """{"path":"note.txt","find":"before","replace":"after"}""");
        using StringWriter output = new();
        RootCommand command = CreateCommand(
            output,
            temp.UserConfigPath,
            execAgentRunnerFactory: (_, _, executor) =>
            {
                runner.Executor = executor;
                return runner;
            });
        command.Parse(["queue", "add", "exec", "--workspace", workspace, "--", "change note"]).Invoke();
        TaskQueueStore queueStore = TaskQueueStore.Create(CreateSnapshot(workspace, temp.UserConfigPath));
        string queueId = Assert.Single(queueStore.List().Items).QueueId;

        int exitCode = command.Parse(["queue", "run", queueId, "--workspace", workspace]).Invoke();

        TaskQueueItem item = queueStore.Read(queueId).Item!;
        Assert.Equal(1, exitCode);
        Assert.Equal(TaskQueueStatus.Failed, item.Status);
        Assert.Equal(ToolErrorCode.ApprovalDenied, item.ErrorCode);
        Assert.Equal("before", File.ReadAllText(notePath));
    }

    [Fact]
    public void Queue_run_skill_preserves_read_only_boundary_and_disabled_tools()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspace = temp.CreateDirectory("workspace");
        string notePath = Path.Combine(workspace, "note.txt");
        File.WriteAllText(notePath, "before");
        ExecutorToolCallAgentRunner runner = new(
            "workspace.apply_patch",
            """{"path":"note.txt","find":"before","replace":"after"}""");
        using StringWriter output = new();
        RootCommand command = CreateCommand(
            output,
            temp.UserConfigPath,
            execAgentRunnerFactory: (_, _, executor) =>
            {
                runner.Executor = executor;
                return runner;
            });
        command.Parse(["queue", "add", "skill", "review-only", "--workspace", workspace, "--", "Review note"]).Invoke();
        TaskQueueStore queueStore = TaskQueueStore.Create(CreateSnapshot(workspace, temp.UserConfigPath));
        string queueId = Assert.Single(queueStore.List().Items).QueueId;

        int exitCode = command.Parse(["queue", "run", queueId, "--workspace", workspace]).Invoke();

        TaskQueueItem item = queueStore.Read(queueId).Item!;
        JobRecord job = Assert.Single(JobRecordStore.Create(CreateSnapshot(workspace, temp.UserConfigPath)).List().Records);
        Assert.Equal(1, exitCode);
        Assert.Equal(TaskQueueStatus.Failed, item.Status);
        Assert.Equal(ToolErrorCode.ToolDisabled, item.ErrorCode);
        Assert.Equal("review-only", runner.LastRequest?.Skill?.Name);
        Assert.Equal("reviewer", runner.LastRequest?.ExpertProfile?.Name);
        Assert.Equal(item.LatestJobId, job.JobId);
        Assert.Equal("before", File.ReadAllText(notePath));
    }

    [Fact]
    public void Queue_run_preserves_configured_disabled_tools()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspace = temp.CreateDirectory("workspace");
        File.WriteAllText(Path.Combine(workspace, "note.txt"), "content");
        ExecutorToolCallAgentRunner runner = new("workspace.read_text", """{"path":"note.txt"}""");
        using StringWriter output = new();
        RootCommand command = CreateCommand(
            output,
            temp.UserConfigPath,
            execAgentRunnerFactory: (_, _, executor) =>
            {
                runner.Executor = executor;
                return runner;
            },
            disabledTools: new HashSet<string>(StringComparer.Ordinal) { "workspace.read_text" });
        command.Parse(["queue", "add", "exec", "--workspace", workspace, "--", "read note"]).Invoke();
        TaskQueueStore queueStore = TaskQueueStore.Create(CreateSnapshot(workspace, temp.UserConfigPath));
        string queueId = Assert.Single(queueStore.List().Items).QueueId;

        int exitCode = command.Parse(["queue", "run", queueId, "--workspace", workspace]).Invoke();

        Assert.Equal(1, exitCode);
        Assert.Equal(ToolErrorCode.ToolDisabled, queueStore.Read(queueId).Item?.ErrorCode);
    }

    [Fact]
    public void Queue_run_preserves_shell_policy_even_when_configuration_always_approves()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspace = temp.CreateDirectory("workspace");
        string marker = Path.Combine(workspace, "queue-shell-marker.txt");
        ExecutorToolCallAgentRunner runner = new(
            "workspace.run_shell",
            """{"command":"dotnet --version > queue-shell-marker.txt"}""");
        ShellPolicyConfiguration shellPolicy = new(
            AllowedCommands: [],
            AllowedCommandsConfigured: false,
            AllowedCommandsSource: "default",
            DeniedCommands: ["dotnet"],
            MaxTimeoutMilliseconds: null,
            MaxTimeoutMillisecondsSource: "default");
        using StringWriter output = new();
        RootCommand command = CreateCommand(
            output,
            temp.UserConfigPath,
            execAgentRunnerFactory: (_, _, executor) =>
            {
                runner.Executor = executor;
                return runner;
            },
            approvalMode: ApprovalMode.Always,
            shellPolicy: shellPolicy);
        command.Parse(["queue", "add", "exec", "--workspace", workspace, "--", "run validation"]).Invoke();
        TaskQueueStore queueStore = TaskQueueStore.Create(CreateSnapshot(workspace, temp.UserConfigPath));
        string queueId = Assert.Single(queueStore.List().Items).QueueId;

        int exitCode = command.Parse(["queue", "run", queueId, "--workspace", workspace]).Invoke();

        Assert.Equal(1, exitCode);
        Assert.Equal("shell-policy-denied", queueStore.Read(queueId).Item?.ErrorCode);
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public void Queue_cancel_is_pending_only_and_cleanup_keeps_recent_item()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspace = temp.CreateDirectory("workspace");
        using StringWriter output = new();
        RootCommand command = CreateCommand(output, temp.UserConfigPath);
        command.Parse(["queue", "add", "exec", "--workspace", workspace, "--", "task"]).Invoke();
        TaskQueueStore queueStore = TaskQueueStore.Create(CreateSnapshot(workspace, temp.UserConfigPath));
        string queueId = Assert.Single(queueStore.List().Items).QueueId;

        int firstCancel = command.Parse(["queue", "cancel", queueId, "--workspace", workspace]).Invoke();
        int secondCancel = command.Parse(["queue", "cancel", queueId, "--workspace", workspace]).Invoke();
        int cleanup = command.Parse([
            "queue", "cleanup", "--status", "canceled", "--older-than-days", "30", "--workspace", workspace
        ]).Invoke();

        Assert.Equal(0, firstCancel);
        Assert.Equal(1, secondCancel);
        Assert.Equal(0, cleanup);
        Assert.Equal(TaskQueueStatus.Canceled, queueStore.Read(queueId).Item?.Status);
        Assert.Contains(TaskQueueErrorCode.InvalidState, output.ToString(), StringComparison.Ordinal);
    }

    private static RootCommand CreateCommand(
        StringWriter output,
        string userConfigPath,
        List<string>? loggedCommands = null,
        Func<CliEnvironmentSnapshot, ToolRegistry, IToolExecutor, IAgentRunner>? execAgentRunnerFactory = null,
        IReadOnlySet<string>? disabledTools = null,
        ApprovalMode approvalMode = ApprovalMode.OnRequest,
        ShellPolicyConfiguration? shellPolicy = null)
    {
        return CliCommandFactory.Create(
            output,
            workspacePath => CreateSnapshot(
                workspacePath,
                userConfigPath,
                disabledTools,
                approvalMode,
                shellPolicy),
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
        IReadOnlySet<string>? disabledTools = null,
        ApprovalMode approvalMode = ApprovalMode.OnRequest,
        ShellPolicyConfiguration? shellPolicy = null)
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
            ApprovalMode = approvalMode,
            ShellPolicy = shellPolicy ?? ShellPolicyConfiguration.Default,
        };
        return new CliEnvironmentSnapshot(
            workspace,
            configuration,
            "9.0.308",
            ".NET 9.0.0",
            "net9.0",
            false);
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

        public AgentRunRequest? LastRequest { get; private set; }

        public AgentRunResult Run(
            AgentRunRequest request,
            ConversationTranscript? transcript = null,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            ToolExecutionResult result = (Executor ?? throw new InvalidOperationException("Executor was not injected."))
                .Execute(
                    toolName,
                    new ToolExecutionContext("queue_tool_call", request.Workspace, argumentsJson),
                    cancellationToken);
            ConversationToolCall toolCall = ConversationToolCall.FromExecution(
                "queue_tool_call",
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

    private sealed class ThrowingAgentRunner : IAgentRunner
    {
        public AgentRunResult Run(
            AgentRunRequest request,
            ConversationTranscript? transcript = null,
            CancellationToken cancellationToken = default) =>
            throw new ApplicationException("unexpected test failure");
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
                "caicli-queue-cli-tests-" + Guid.NewGuid().ToString("N"));
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
