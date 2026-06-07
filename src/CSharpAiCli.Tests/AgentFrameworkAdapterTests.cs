using CSharpAiCli.AgentFramework;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class AgentFrameworkAdapterTests
{
    [Fact]
    public void Runner_implements_product_agent_contract_without_framework_dependency()
    {
        IAgentRunner runner = new MicrosoftAgentFrameworkRunner();
        WorkspaceContext workspace = new(
            RootPath: Path.GetTempPath(),
            ConfigPath: Path.Combine(Path.GetTempPath(), ".caicli", "config.json"),
            Status: WorkspaceStatus.Ready);

        AgentRunResult result = runner.Run(new AgentRunRequest("hello", workspace));

        Assert.False(result.IsSuccess);
        Assert.Equal("agent-framework-unavailable", result.Error?.LocalErrorCode);
        Assert.Empty(result.ToolCalls);
    }

    [Fact]
    public void Tool_bridge_reuses_core_tool_registry_definitions()
    {
        ToolRegistry registry = new();
        registry.Register(new EchoTool());
        MicrosoftToolBridge bridge = new(registry);

        ToolDefinition definition = Assert.Single(bridge.ListToolDefinitions());

        Assert.Equal("test.echo", definition.Name);
    }

    [Fact]
    public void Tool_bridge_maps_core_metadata_to_framework_tool_definitions()
    {
        ToolRegistry registry = new();
        WorkspaceGuard guard = new();
        registry.Register(new WorkspaceFileReadTool(guard));
        registry.Register(new WorkspaceSearchTool(guard));
        MicrosoftToolBridge bridge = new(registry);

        IReadOnlyList<MicrosoftFrameworkToolDefinition> definitions = bridge.ListFrameworkToolDefinitions();

        Assert.Contains(definitions, definition => definition.Name == "workspace.read_text");
        Assert.Contains(definitions, definition => definition.Name == "workspace.search_text");
        Assert.All(definitions, definition => Assert.Contains("\"type\":\"object\"", definition.ParametersSchema));
    }

    [Fact]
    public void Tool_bridge_invokes_read_and_search_tools_through_core_executor()
    {
        using TempDirectory temp = TempDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "README.md"), "needle in workspace");
        ToolRegistry registry = new();
        WorkspaceGuard guard = new();
        registry.Register(new WorkspaceFileReadTool(guard));
        registry.Register(new WorkspaceSearchTool(guard));
        MicrosoftToolBridge bridge = new(registry);
        WorkspaceContext workspace = WorkspaceContext.Detect(temp.Path, temp.Path);

        MicrosoftFrameworkToolResult read = bridge.InvokeTool(
            workspace,
            new MicrosoftFrameworkToolCall(
                "call_read",
                "workspace.read_text",
                """{"path":"README.md"}"""));
        MicrosoftFrameworkToolResult search = bridge.InvokeTool(
            workspace,
            new MicrosoftFrameworkToolCall(
                "call_search",
                "workspace.search_text",
                """{"query":"needle"}"""));

        Assert.True(read.Succeeded);
        Assert.Equal("needle in workspace", read.Summary);
        Assert.True(search.Succeeded);
        Assert.Contains("README.md:1: needle in workspace", search.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Tool_bridge_preserves_tool_failure_shape()
    {
        ToolRegistry registry = new();
        MicrosoftToolBridge bridge = new(registry);
        WorkspaceContext workspace = new(
            RootPath: Path.GetTempPath(),
            ConfigPath: Path.Combine(Path.GetTempPath(), ".caicli", "config.json"),
            Status: WorkspaceStatus.Ready);

        MicrosoftFrameworkToolResult result = bridge.InvokeTool(
            workspace,
            new MicrosoftFrameworkToolCall(
                "call_missing",
                "missing.tool",
                "{}"));

        Assert.False(result.Succeeded);
        Assert.Equal("unknown-tool", result.ErrorCode);
        Assert.Equal("not-required", result.ApprovalStatus);
    }

    [Fact]
    public void Capability_report_marks_adapter_as_experimental_stub()
    {
        AgentFrameworkCapabilityReport report = AgentFrameworkAdapterInfo.CreateReport();

        Assert.Equal("maf", report.BackendName);
        Assert.False(report.IsAvailable);
        Assert.Equal("experimental-stub", report.Status);
        Assert.Contains("scaffolded", report.SafeMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Cli_project_does_not_reference_agent_framework_adapter()
    {
        string cliProjectPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "CSharpAiCli.Cli",
            "CSharpAiCli.Cli.csproj"));

        string projectXml = File.ReadAllText(cliProjectPath);

        Assert.DoesNotContain("CSharpAiCli.AgentFramework", projectXml, StringComparison.Ordinal);
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
