using CSharpAiCli.Core;
using System.Text.Json;

namespace CSharpAiCli.Tests;

public sealed class WorkspaceShellToolTests
{
    [Fact]
    public void Execute_runs_approved_harmless_command()
    {
        using TempDirectory temp = TempDirectory.Create();
        WorkspaceShellTool tool = new(
            new RestrictedShellRunner(new WorkspaceGuard()),
            new AlwaysApproveApprovalPolicy());

        ToolExecutionResult result = tool.Execute(CreateContext(
            temp.Path,
            """{"command":"dotnet --version","timeoutMilliseconds":10000}"""));

        Assert.True(result.Succeeded);
        Assert.Equal("approved", result.ApprovalStatus);
        Assert.Contains("exitCode: 0", result.Summary, StringComparison.Ordinal);
        Assert.Contains("stdout:", result.Summary, StringComparison.Ordinal);
        IReadOnlyDictionary<string, JsonElement> payload = AssertPayload(result);
        Assert.Equal("dotnet --version", payload["command"].GetString());
        Assert.Equal(".", payload["cwd"].GetString());
        Assert.Equal(0, payload["exitCode"].GetInt32());
        Assert.False(payload["timedOut"].GetBoolean());
        Assert.False(payload["stdoutTruncated"].GetBoolean());
        Assert.False(payload["stderrTruncated"].GetBoolean());
        Assert.Equal("approved", payload["approvalStatus"].GetString());
        Assert.False(payload.ContainsKey("stdout"));
        Assert.False(payload.ContainsKey("stderr"));
    }

    [Fact]
    public void Execute_refuses_when_approval_denied()
    {
        using TempDirectory temp = TempDirectory.Create();
        WorkspaceShellTool tool = new(
            new RestrictedShellRunner(new WorkspaceGuard()),
            new DefaultDenyApprovalPolicy());

        ToolExecutionResult result = tool.Execute(CreateContext(
            temp.Path,
            """{"command":"dotnet --version"}"""));

        Assert.False(result.Succeeded);
        Assert.Equal(ToolErrorCode.ApprovalDenied, result.ErrorCode);
        Assert.Equal("denied", result.ApprovalStatus);
        IReadOnlyDictionary<string, JsonElement> payload = AssertPayload(result);
        Assert.Equal("dotnet --version", payload["command"].GetString());
        Assert.Equal(".", payload["cwd"].GetString());
        Assert.Equal("denied", payload["approvalStatus"].GetString());
    }

    [Fact]
    public void Execute_requests_approval_with_shell_risk_metadata_and_reason_for_ordinary_command()
    {
        using TempDirectory temp = TempDirectory.Create();
        RecordingApprovalPolicy approvalPolicy = new(ApprovalDecision.Deny("Denied by recording policy."));
        WorkspaceShellTool tool = new(
            new AssertingShellRunner(),
            approvalPolicy);

        ToolExecutionResult result = tool.Execute(CreateContext(
            temp.Path,
            """{"command":"dotnet --version","cwd":"src"}"""));

        Assert.False(result.Succeeded);
        ApprovalRequest request = approvalPolicy.SingleRequest;
        Assert.Equal("workspace.run_shell", request.Operation);
        Assert.Equal(ToolRiskLevel.Shell, request.RiskLevel);
        Assert.NotNull(request.Metadata);
        IReadOnlyDictionary<string, string> metadata = request.Metadata;
        Assert.Equal("dotnet --version", metadata["command"]);
        Assert.Equal("src", metadata["cwd"]);
        Assert.Equal("Shell command execution requires approval.", metadata["reason"]);
    }

    [Fact]
    public void Execute_requests_approval_with_dangerous_shell_risk_and_detector_reason()
    {
        using TempDirectory temp = TempDirectory.Create();
        RecordingApprovalPolicy approvalPolicy = new(ApprovalDecision.Deny("Denied by recording policy."));
        WorkspaceShellTool tool = new(
            new AssertingShellRunner(),
            approvalPolicy);

        ToolExecutionResult result = tool.Execute(CreateContext(
            temp.Path,
            """{"command":"rm -rf ."}"""));

        Assert.False(result.Succeeded);
        ApprovalRequest request = approvalPolicy.SingleRequest;
        Assert.Equal(ToolRiskLevel.DangerousShell, request.RiskLevel);
        Assert.NotNull(request.Metadata);
        IReadOnlyDictionary<string, string> metadata = request.Metadata;
        Assert.Equal("rm -rf .", metadata["command"]);
        Assert.Equal(".", metadata["cwd"]);
        Assert.Equal("Command contains a destructive delete pattern.", metadata["reason"]);
    }

    [Fact]
    public void Execute_denies_dangerous_shell_by_policy_before_runner_executes()
    {
        using TempDirectory temp = TempDirectory.Create();
        CountingShellRunner shellRunner = new();
        WorkspaceShellTool tool = new(
            shellRunner,
            ApprovalPolicyResolver.Resolve(ApprovalMode.Always));

        ToolExecutionResult result = tool.Execute(CreateContext(
            temp.Path,
            """{"command":"rm -rf ."}"""));

        Assert.False(result.Succeeded);
        Assert.Equal("approval-denied", result.ErrorCode);
        Assert.Equal("dangerous-shell-denied", result.ApprovalStatus);
        Assert.Equal(0, shellRunner.RunCount);
    }

    [Fact]
    public void Execute_returns_failure_for_dangerous_command()
    {
        using TempDirectory temp = TempDirectory.Create();
        WorkspaceShellTool tool = new(
            new RestrictedShellRunner(new WorkspaceGuard()),
            new AlwaysApproveApprovalPolicy());

        ToolExecutionResult result = tool.Execute(CreateContext(
            temp.Path,
            """{"command":"rm -rf ."}"""));

        Assert.False(result.Succeeded);
        Assert.Equal("dangerous-command-denied", result.ErrorCode);
        Assert.Equal("approved", result.ApprovalStatus);
        IReadOnlyDictionary<string, JsonElement> payload = AssertPayload(result);
        Assert.Equal("rm -rf .", payload["command"].GetString());
        Assert.Equal(".", payload["cwd"].GetString());
        Assert.Equal(JsonValueKind.Null, payload["exitCode"].ValueKind);
        Assert.False(payload["timedOut"].GetBoolean());
        Assert.False(payload["stdoutTruncated"].GetBoolean());
        Assert.False(payload["stderrTruncated"].GetBoolean());
        Assert.Equal("approved", payload["approvalStatus"].GetString());
    }

    [Fact]
    public void Execute_returns_payload_for_invalid_arguments()
    {
        using TempDirectory temp = TempDirectory.Create();
        WorkspaceShellTool tool = new(
            new RestrictedShellRunner(new WorkspaceGuard()),
            new AlwaysApproveApprovalPolicy());

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path, "{}"));

        Assert.False(result.Succeeded);
        Assert.Equal(ToolErrorCode.InvalidToolArguments, result.ErrorCode);
        IReadOnlyDictionary<string, JsonElement> payload = AssertPayload(result);
        Assert.Equal(ToolErrorCode.InvalidToolArguments, payload["errorCode"].GetString());
        Assert.Equal("workspace.run_shell", payload["toolName"].GetString());
        Assert.Equal("command", payload["argument"].GetString());
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    public void Execute_returns_argument_failure_for_non_object_root(string argumentsJson)
    {
        using TempDirectory temp = TempDirectory.Create();
        WorkspaceShellTool tool = new(
            new RestrictedShellRunner(new WorkspaceGuard()),
            new AlwaysApproveApprovalPolicy());

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path, argumentsJson));

        Assert.False(result.Succeeded);
        Assert.Equal(ToolErrorCode.InvalidToolArguments, result.ErrorCode);
        Assert.Equal("Tool arguments must be a JSON object.", result.Summary);
        IReadOnlyDictionary<string, JsonElement> payload = AssertPayload(result);
        Assert.Equal(ToolErrorCode.InvalidToolArguments, payload["errorCode"].GetString());
        Assert.Equal("workspace.run_shell", payload["toolName"].GetString());
        Assert.Equal("arguments", payload["argument"].GetString());
    }

    [Fact]
    public void Execute_records_timeout_and_truncation_fields_in_summary()
    {
        using TempDirectory temp = TempDirectory.Create();
        WorkspaceShellTool tool = new(
            new RestrictedShellRunner(new WorkspaceGuard()),
            new AlwaysApproveApprovalPolicy());

        ToolExecutionResult result = tool.Execute(CreateContext(
            temp.Path,
            $$"""{"command":"{{CreateSleepCommand()}}","timeoutMilliseconds":200}"""));

        Assert.False(result.Succeeded);
        Assert.Equal("shell-timeout", result.ErrorCode);
        Assert.Contains("timedOut: True", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Shell_tool_records_approval_status_in_offline_agent_transcript()
    {
        using TempDirectory temp = TempDirectory.Create();
        ToolRegistry registry = new();
        registry.Register(new WorkspaceShellTool(
            new RestrictedShellRunner(new WorkspaceGuard()),
            new AlwaysApproveApprovalPolicy()));
        ToolExecutor executor = new(registry);
        OfflineAgentRunner runner = new(
            new ShellToolCallingModel(),
            executor,
            () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"));
        ConversationTranscript transcript = ConversationTranscript.Create(
            "smoke",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));

        AgentRunResult result = runner.Run(new AgentRunRequest(
            "run command",
            WorkspaceContext.Detect(temp.Path, temp.Path)), transcript);

        Assert.True(result.IsSuccess);
        ConversationToolCall toolCall = Assert.Single(transcript.ToolCalls);
        Assert.Equal("workspace.run_shell", toolCall.ToolName);
        Assert.Equal("approved", toolCall.ApprovalStatus);
        Assert.True(toolCall.Succeeded);
        Assert.Contains("exitCode: 0", toolCall.OutputSummary, StringComparison.Ordinal);
    }

    private static ToolExecutionContext CreateContext(string workspaceRoot, string argumentsJson)
    {
        return new ToolExecutionContext(
            "call_shell",
            WorkspaceContext.Detect(workspaceRoot, workspaceRoot),
            argumentsJson);
    }

    private static IReadOnlyDictionary<string, JsonElement> AssertPayload(ToolExecutionResult result)
    {
        return result.StructuredPayload ?? throw new InvalidOperationException("Structured payload was not set.");
    }

    private static string CreateSleepCommand()
    {
        return OperatingSystem.IsWindows()
            ? "ping -n 3 127.0.0.1 > nul"
            : "sleep 2";
    }

    private sealed class ShellToolCallingModel : IToolCallingModel
    {
        public AgentModelTurn Start(AgentRunRequest request, CancellationToken cancellationToken = default)
        {
            return AgentModelTurn.RequestTools(new AgentToolCallRequest(
                "call_shell",
                "workspace.run_shell",
                """{"command":"dotnet --version","timeoutMilliseconds":10000}"""));
        }

        public AgentModelTurn Continue(
            AgentRunRequest request,
            IReadOnlyList<AgentToolCallResult> toolResults,
            CancellationToken cancellationToken = default)
        {
            Assert.True(Assert.Single(toolResults).Result.Succeeded);
            return AgentModelTurn.Final("done");
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
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "caicli-tests-" + Guid.NewGuid().ToString("N"));
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

    private sealed class RecordingApprovalPolicy(ApprovalDecision decision) : IApprovalPolicy
    {
        private readonly List<ApprovalRequest> requests = [];

        public ApprovalRequest SingleRequest => Assert.Single(requests);

        public ApprovalDecision RequestApproval(ApprovalRequest request)
        {
            requests.Add(request);
            return decision;
        }
    }

    private sealed class AssertingShellRunner : IShellRunner
    {
        public ShellCommandResult Run(
            WorkspaceContext workspace,
            ShellCommandRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Shell runner should not execute for denied approval.");
        }
    }

    private sealed class CountingShellRunner : IShellRunner
    {
        public int RunCount { get; private set; }

        public ShellCommandResult Run(
            WorkspaceContext workspace,
            ShellCommandRequest request,
            CancellationToken cancellationToken = default)
        {
            RunCount++;
            return ShellCommandResult.Failure("unexpected-shell-run", "Shell runner was invoked.");
        }
    }
}
