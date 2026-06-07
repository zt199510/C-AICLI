using CSharpAiCli.Core;

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
        Assert.Equal("approval-denied", result.ErrorCode);
        Assert.Equal("denied", result.ApprovalStatus);
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
}
