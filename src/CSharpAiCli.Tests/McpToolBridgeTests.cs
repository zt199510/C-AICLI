using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class McpToolBridgeTests
{
    [Fact]
    public void Register_tools_adds_only_enabled_configured_servers_to_registry()
    {
        McpConfiguration configuration = new(
            [
                new McpServerDefinition("active", true, "configured", "stdio command: mcp-active", "workspace config"),
                new McpServerDefinition("disabled", false, "inactive", "stdio command: mcp-disabled", "workspace config"),
                new McpServerDefinition("invalid", true, "invalid", "transport: unknown", "workspace config")
            ]);
        ToolRegistry registry = new();

        new McpToolBridge(new FakeMcpToolInvoker()).RegisterTools(registry, configuration);

        ToolDefinition definition = Assert.Single(registry.List());
        Assert.Equal("mcp.active.call", definition.Name);
        Assert.True(registry.TryGet("mcp.active.call", out ITool? _));
        Assert.False(registry.TryGet("mcp.disabled.call", out ITool? _));
        Assert.False(registry.TryGet("mcp.invalid.call", out ITool? _));
    }

    [Fact]
    public void Enabled_mcp_tool_can_be_invoked_through_tool_executor()
    {
        McpConfiguration configuration = new(
            [
                new McpServerDefinition("active", true, "configured", "stdio command: mcp-active", "workspace config")
            ]);
        FakeMcpToolInvoker invoker = new();
        ToolRegistry registry = new();
        new McpToolBridge(invoker).RegisterTools(registry, configuration);
        ToolExecutor executor = new(registry);

        ToolExecutionResult result = executor.Execute(
            "mcp.active.call",
            CreateContext("""{"tool":"echo","arguments":{"text":"hello"}}"""));

        Assert.True(result.Succeeded);
        Assert.Equal("mcp active invoked with {\"tool\":\"echo\",\"arguments\":{\"text\":\"hello\"}}", result.Summary);
        Assert.Equal("active", invoker.LastRequest?.ServerName);
    }

    [Fact]
    public void Disabled_mcp_tool_is_not_executable()
    {
        McpConfiguration configuration = new(
            [
                new McpServerDefinition("disabled", false, "inactive", "stdio command: mcp-disabled", "workspace config")
            ]);
        ToolRegistry registry = new();
        new McpToolBridge(new FakeMcpToolInvoker()).RegisterTools(registry, configuration);
        ToolExecutor executor = new(registry);

        ToolExecutionResult result = executor.Execute("mcp.disabled.call", CreateContext("{}"));

        Assert.False(result.Succeeded);
        Assert.Equal("unknown-tool", result.ErrorCode);
    }

    [Fact]
    public void Mcp_tool_result_is_recorded_in_transcript_by_offline_runner()
    {
        McpConfiguration configuration = new(
            [
                new McpServerDefinition("active", true, "configured", "stdio command: mcp-active", "workspace config")
            ]);
        ToolRegistry registry = new();
        new McpToolBridge(new FakeMcpToolInvoker()).RegisterTools(registry, configuration);
        OfflineAgentRunner runner = new(
            new McpToolCallingModel(),
            new ToolExecutor(registry),
            () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"));
        ConversationTranscript transcript = ConversationTranscript.Create(
            "mcp",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));

        AgentRunResult result = runner.Run(new AgentRunRequest("call mcp", CreateWorkspace()), transcript);

        Assert.True(result.IsSuccess);
        ConversationToolCall toolCall = Assert.Single(transcript.ToolCalls);
        Assert.Equal("mcp.active.call", toolCall.ToolName);
        Assert.True(toolCall.Succeeded);
        Assert.Contains("mcp active invoked", toolCall.OutputSummary, StringComparison.Ordinal);
    }

    private static ToolExecutionContext CreateContext(string argumentsJson)
    {
        return new ToolExecutionContext("call_mcp", CreateWorkspace(), argumentsJson);
    }

    private static WorkspaceContext CreateWorkspace()
    {
        return new WorkspaceContext(
            RootPath: Path.GetTempPath(),
            ConfigPath: Path.Combine(Path.GetTempPath(), ".caicli", "config.json"),
            Status: WorkspaceStatus.Ready);
    }

    private sealed class FakeMcpToolInvoker : IMcpToolInvoker
    {
        public McpToolRequest? LastRequest { get; private set; }

        public ToolExecutionResult Invoke(McpToolRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return ToolExecutionResult.Success($"mcp {request.ServerName} invoked with {request.ArgumentsJson}");
        }
    }

    private sealed class McpToolCallingModel : IToolCallingModel
    {
        public AgentModelTurn Start(AgentRunRequest request, CancellationToken cancellationToken = default)
        {
            return AgentModelTurn.RequestTools(new AgentToolCallRequest(
                "call_mcp",
                "mcp.active.call",
                """{"tool":"echo"}"""));
        }

        public AgentModelTurn Continue(
            AgentRunRequest request,
            IReadOnlyList<AgentToolCallResult> toolResults,
            CancellationToken cancellationToken = default)
        {
            Assert.True(Assert.Single(toolResults).Result.Succeeded);
            return AgentModelTurn.Final("mcp complete");
        }
    }
}
