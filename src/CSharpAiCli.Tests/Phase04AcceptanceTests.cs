using CSharpAiCli.AgentFramework;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class Phase04AcceptanceTests
{
    [Fact]
    public void Direct_backend_offline_runner_remains_available()
    {
        ToolRegistry registry = new();
        registry.Register(new EchoTool());
        OfflineAgentRunner runner = new(
            new EchoToolCallingModel(),
            new ToolExecutor(registry),
            () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"));

        AgentRunResult result = runner.Run(new AgentRunRequest(
            "call direct tool",
            CreateWorkspace()));

        Assert.True(result.IsSuccess);
        Assert.Equal("direct complete", result.Text);
        ConversationToolCall toolCall = Assert.Single(result.ToolCalls);
        Assert.Equal("test.echo", toolCall.ToolName);
        Assert.True(toolCall.Succeeded);
    }

    [Fact]
    public void Framework_backend_is_deferred_with_safe_unavailable_result()
    {
        IAgentRunner runner = new MicrosoftAgentFrameworkRunner();

        AgentRunResult result = runner.Run(new AgentRunRequest(
            "try framework",
            CreateWorkspace()));

        Assert.False(result.IsSuccess);
        Assert.Equal("agent-framework-unavailable", result.Error?.LocalErrorCode);
        Assert.Contains("no framework package is enabled", result.Error?.SafeMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Framework_tool_bridge_can_call_shared_tools_without_duplicate_registry()
    {
        ToolRegistry registry = new();
        registry.Register(new EchoTool());
        MicrosoftToolBridge bridge = new(registry);

        MicrosoftFrameworkToolResult result = bridge.InvokeTool(
            CreateWorkspace(),
            new MicrosoftFrameworkToolCall(
                "call_echo",
                "test.echo",
                """{"text":"hello"}"""));

        Assert.True(result.Succeeded);
        Assert.Equal("test.echo", Assert.Single(bridge.ListFrameworkToolDefinitions()).Name);
        Assert.Equal("""{"text":"hello"}""", result.Summary);
    }

    [Fact]
    public void Framework_backend_configuration_is_selectable_and_doctor_reports_deferred_status()
    {
        WorkspaceContext workspace = CreateWorkspace();
        EffectiveConfiguration configuration = new(
            WorkspaceRoot: workspace.RootPath,
            UserConfigPath: Path.Combine("user-home", ".caicli", "config.json"),
            WorkspaceConfigPath: workspace.ConfigPath,
            Model: "not configured",
            ModelSource: "default",
            AgentBackend: "framework",
            AgentBackendSource: "workspace config",
            DisabledTools: new HashSet<string>(StringComparer.Ordinal),
            ApiKey: null,
            ApiKeySource: "missing",
            LoadedConfigPaths: [],
            Warnings: [],
            ConfigSources: []);
        CliEnvironmentSnapshot snapshot = new(
            Workspace: workspace,
            Configuration: configuration,
            DotnetSdkVersion: "9.0.308",
            DotnetRuntime: ".NET 9.0.0",
            TargetFramework: "net9.0",
            HasGlobalJson: false);

        string doctor = DoctorReport.Create(snapshot).ToDisplayText();

        Assert.Contains("agent backend: framework (workspace config)", doctor);
        Assert.Contains("experimental stub", doctor);
    }

    private static WorkspaceContext CreateWorkspace()
    {
        return new WorkspaceContext(
            RootPath: Path.GetTempPath(),
            ConfigPath: Path.Combine(Path.GetTempPath(), ".caicli", "config.json"),
            Status: WorkspaceStatus.Ready);
    }

    private sealed class EchoTool : ITool
    {
        public ToolDefinition Definition { get; } = new(
            "test.echo",
            "Echo tool.",
            """{"type":"object"}""");

        public ToolExecutionResult Execute(
            ToolExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            return ToolExecutionResult.Success(context.ArgumentsJson);
        }
    }

    private sealed class EchoToolCallingModel : IToolCallingModel
    {
        public AgentModelTurn Start(
            AgentRunRequest request,
            CancellationToken cancellationToken = default)
        {
            return AgentModelTurn.RequestTools(new AgentToolCallRequest(
                "call_echo",
                "test.echo",
                """{"text":"hello"}"""));
        }

        public AgentModelTurn Continue(
            AgentRunRequest request,
            IReadOnlyList<AgentToolCallResult> toolResults,
            CancellationToken cancellationToken = default)
        {
            Assert.True(Assert.Single(toolResults).Result.Succeeded);
            return AgentModelTurn.Final("direct complete");
        }
    }
}
