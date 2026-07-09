using System.Text.Json;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class McpToolBridgeTests
{
    [Fact]
    public void Register_tools_maps_discovered_stdio_tools_to_tool_definitions()
    {
        McpConfiguration configuration = new(
            [
                CreateServer("active"),
                CreateServer("disabled", enabled: false, status: "inactive"),
                CreateServer("http", transport: "http", command: null, url: "https://mcp.example.invalid")
            ]);
        JsonElement schema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                path = new { type = "string" }
            },
            required = new[] { "path" }
        });
        FakeMcpToolDiscoverer discoverer = new(
            new McpDiscoveredTool("Read File!", "Read a file from the server.", schema));
        ToolRegistry registry = new();

        new McpToolBridge(discoverer, new FakeMcpToolInvoker())
            .RegisterTools(registry, configuration, CreateWorkspace());

        ToolDefinition definition = Assert.Single(registry.List());
        Assert.Equal("mcp.active.read_file", definition.Name);
        Assert.Contains("Read a file from the server.", definition.Description, StringComparison.Ordinal);
        Assert.Contains("active", definition.Description, StringComparison.Ordinal);
        Assert.Contains("Read File!", definition.Description, StringComparison.Ordinal);
        Assert.Equal("""{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}""", definition.ParametersSchema);
        Assert.Equal(ToolRiskLevel.Shell, definition.RiskLevel);
        Assert.True(registry.TryGet("mcp.active.read_file", out ITool? _));
        Assert.Equal(["active"], discoverer.DiscoveredServerNames);
    }

    [Fact]
    public void Disabled_mcp_server_does_not_trigger_discovery_or_register_tools()
    {
        McpConfiguration configuration = new(
            [
                CreateServer("disabled", enabled: false, status: "inactive")
            ]);
        FakeMcpToolDiscoverer discoverer = new(new McpDiscoveredTool("echo", "Echo.", CreateObjectSchema()));
        ToolRegistry registry = new();

        new McpToolBridge(discoverer, new FakeMcpToolInvoker())
            .RegisterTools(registry, configuration, CreateWorkspace());

        Assert.Empty(registry.List());
        Assert.Empty(discoverer.DiscoveredServerNames);
    }

    [Fact]
    public void Duplicate_normalized_remote_tool_names_get_deterministic_suffixes()
    {
        McpConfiguration configuration = new([CreateServer("active")]);
        FakeMcpToolDiscoverer discoverer = new(
            new McpDiscoveredTool("read file", "First.", CreateObjectSchema()),
            new McpDiscoveredTool("read/file", "Second.", CreateObjectSchema()),
            new McpDiscoveredTool("read_file", "Third.", CreateObjectSchema()));
        ToolRegistry registry = new();

        new McpToolBridge(discoverer, new FakeMcpToolInvoker())
            .RegisterTools(registry, configuration, CreateWorkspace());

        string[] names = registry.List().Select(definition => definition.Name).ToArray();
        Assert.Equal(
            ["mcp.active.read_file", "mcp.active.read_file_2", "mcp.active.read_file_3"],
            names);
    }

    [Fact]
    public void Enabled_mcp_tool_can_be_invoked_through_tool_executor_with_remote_name_and_arguments()
    {
        McpConfiguration configuration = new([CreateServer("active")]);
        FakeMcpToolDiscoverer discoverer = new(
            new McpDiscoveredTool("echo tool", "Echo input.", CreateObjectSchema()));
        FakeMcpToolInvoker invoker = new();
        ToolRegistry registry = new();
        new McpToolBridge(discoverer, invoker, ApprovalPolicyResolver.Resolve(ApprovalMode.Always))
            .RegisterTools(registry, configuration, CreateWorkspace());
        ToolExecutor executor = new(registry);

        ToolExecutionResult result = executor.Execute(
            "mcp.active.echo_tool",
            CreateContext("""{"text":"hello"}"""));

        Assert.True(result.Succeeded);
        Assert.Equal("mcp active echo tool invoked with {\"text\":\"hello\"}", result.Summary);
        Assert.Equal("approved", result.ApprovalStatus);
        Assert.Equal("active", invoker.LastRequest?.ServerName);
        Assert.Equal("echo tool", invoker.LastRequest?.RemoteToolName);
        Assert.Equal("""{"text":"hello"}""", invoker.LastRequest?.ArgumentsJson);
    }

    [Fact]
    public void Enabled_mcp_tool_denies_on_request_approval_without_invoking_server()
    {
        McpConfiguration configuration = new([CreateServer("active")]);
        FakeMcpToolDiscoverer discoverer = new(
            new McpDiscoveredTool("echo", "Echo.", CreateObjectSchema()));
        FakeMcpToolInvoker invoker = new();
        ToolRegistry registry = new();
        new McpToolBridge(discoverer, invoker, ApprovalPolicyResolver.Resolve(ApprovalMode.OnRequest))
            .RegisterTools(registry, configuration, CreateWorkspace());
        ToolExecutor executor = new(registry);

        ToolExecutionResult result = executor.Execute(
            "mcp.active.echo",
            CreateContext("""{"text":"hello"}"""));

        Assert.False(result.Succeeded);
        Assert.Equal("approval-denied", result.ErrorCode);
        Assert.Equal("approval-required", result.ApprovalStatus);
        Assert.Equal(0, invoker.InvocationCount);
        Assert.Null(invoker.LastRequest);
    }

    [Fact]
    public void Enabled_mcp_tool_requests_approval_with_shell_risk_and_mcp_metadata()
    {
        McpConfiguration configuration = new([CreateServer("active")]);
        FakeMcpToolDiscoverer discoverer = new(
            new McpDiscoveredTool("echo", "Echo.", CreateObjectSchema()));
        FakeMcpToolInvoker invoker = new();
        RecordingApprovalPolicy approvalPolicy = new(ApprovalDecision.Deny("Denied by recording policy."));
        ToolRegistry registry = new();
        new McpToolBridge(discoverer, invoker, approvalPolicy)
            .RegisterTools(registry, configuration, CreateWorkspace());
        ToolExecutor executor = new(registry);

        ToolExecutionResult result = executor.Execute(
            "mcp.active.echo",
            CreateContext("""{"text":"hello"}"""));

        Assert.False(result.Succeeded);
        ApprovalRequest request = approvalPolicy.SingleRequest;
        Assert.Equal("mcp.active.echo", request.Operation);
        Assert.Equal(ToolRiskLevel.Shell, request.RiskLevel);
        Assert.Null(request.Diff);
        Assert.False(request.IsDirtyWorkspace);
        Assert.NotNull(request.Metadata);
        Assert.Equal("active", request.Metadata["server"]);
        Assert.Equal("echo", request.Metadata["remoteTool"]);
        Assert.Equal("mcp.active.echo", request.Metadata["localTool"]);
        Assert.Equal("MCP external tool invocation requires approval.", request.Metadata["reason"]);
        Assert.Equal(0, invoker.InvocationCount);
    }

    [Fact]
    public void Enabled_mcp_tool_reports_local_approval_status_when_invoker_sets_one()
    {
        McpConfiguration configuration = new([CreateServer("active")]);
        FakeMcpToolDiscoverer discoverer = new(
            new McpDiscoveredTool("echo", "Echo.", CreateObjectSchema()));
        FakeMcpToolInvoker invoker = new(ToolExecutionResult.Success("mcp active invoked", "mcp-invoker-approved"));
        ToolRegistry registry = new();
        new McpToolBridge(discoverer, invoker, ApprovalPolicyResolver.Resolve(ApprovalMode.Always))
            .RegisterTools(registry, configuration, CreateWorkspace());
        ToolExecutor executor = new(registry);

        ToolExecutionResult result = executor.Execute(
            "mcp.active.echo",
            CreateContext("""{"text":"hello"}"""));

        Assert.True(result.Succeeded);
        Assert.Equal("approved", result.ApprovalStatus);
        Assert.Equal(1, invoker.InvocationCount);
    }

    [Fact]
    public void Disabled_mcp_tool_is_not_executable()
    {
        McpConfiguration configuration = new(
            [
                CreateServer("disabled", enabled: false, status: "inactive")
            ]);
        ToolRegistry registry = new();
        new McpToolBridge(
                new FakeMcpToolDiscoverer(new McpDiscoveredTool("echo", "Echo.", CreateObjectSchema())),
                new FakeMcpToolInvoker())
            .RegisterTools(registry, configuration, CreateWorkspace());
        ToolExecutor executor = new(registry);

        ToolExecutionResult result = executor.Execute("mcp.disabled.echo", CreateContext("{}"));

        Assert.False(result.Succeeded);
        Assert.Equal("unknown-tool", result.ErrorCode);
    }

    [Fact]
    public void Mcp_tool_result_is_recorded_in_transcript_by_offline_runner()
    {
        McpConfiguration configuration = new([CreateServer("active")]);
        ToolRegistry registry = new();
        new McpToolBridge(
                new FakeMcpToolDiscoverer(new McpDiscoveredTool("echo", "Echo.", CreateObjectSchema())),
                new FakeMcpToolInvoker(),
                ApprovalPolicyResolver.Resolve(ApprovalMode.Always))
            .RegisterTools(registry, configuration, CreateWorkspace());
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
        Assert.Equal("mcp.active.echo", toolCall.ToolName);
        Assert.True(toolCall.Succeeded);
        Assert.Equal("approved", toolCall.ApprovalStatus);
        Assert.Contains("mcp active echo invoked", toolCall.OutputSummary, StringComparison.Ordinal);
    }

    private static McpServerDefinition CreateServer(
        string name,
        bool enabled = true,
        string status = "configured",
        string transport = "stdio",
        string? command = "mcp-active",
        string? url = null)
    {
        return new McpServerDefinition(
            name,
            enabled,
            status,
            command is null ? $"{transport} url: {url}" : $"stdio command: {command}",
            "workspace config")
        {
            Transport = transport,
            Command = command,
            Url = url
        };
    }

    private static JsonElement CreateObjectSchema()
    {
        return JsonSerializer.SerializeToElement(new { type = "object" });
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

    private sealed class FakeMcpToolDiscoverer(params McpDiscoveredTool[] tools) : IMcpToolDiscoverer
    {
        private readonly List<string> discoveredServerNames = [];

        public IReadOnlyList<string> DiscoveredServerNames => discoveredServerNames.ToArray();

        public McpToolsListResult DiscoverTools(
            McpServerDefinition server,
            WorkspaceContext workspace,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            discoveredServerNames.Add(server.Name);
            return McpToolsListResult.Success(tools, stderrSnippet: "", stderrTruncated: false);
        }
    }

    private sealed class FakeMcpToolInvoker(ToolExecutionResult? result = null) : IMcpToolInvoker
    {
        public McpToolRequest? LastRequest { get; private set; }
        public int InvocationCount { get; private set; }

        public ToolExecutionResult Invoke(
            McpToolRequest request,
            WorkspaceContext workspace,
            CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            LastRequest = request;
            return result ?? ToolExecutionResult.Success(
                $"mcp {request.ServerName} {request.RemoteToolName} invoked with {request.ArgumentsJson}");
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

    private sealed class McpToolCallingModel : IToolCallingModel
    {
        public AgentModelTurn Start(AgentRunRequest request, CancellationToken cancellationToken = default)
        {
            return AgentModelTurn.RequestTools(new AgentToolCallRequest(
                "call_mcp",
                "mcp.active.echo",
                """{"text":"hello"}"""));
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
