using System.Text.Json;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class McpStdioToolClientTests
{
    [Fact]
    public void Discover_and_invoke_use_real_stdio_fake_server()
    {
        using FakeMcpStdioServer server = FakeMcpStdioServer.CreateSuccessful();
        McpServerDefinition definition = server.CreateServerDefinition(timeoutMilliseconds: 2_000);
        WorkspaceContext workspace = server.CreateWorkspace();
        McpStdioClientSessionFactory sessionFactory = new(new McpStdioTransport(new WorkspaceGuard()));
        McpStdioToolDiscoverer discoverer = new(sessionFactory);
        McpStdioToolInvoker invoker = new(sessionFactory);

        McpToolsListResult tools = discoverer.DiscoverTools(definition, workspace);
        ToolExecutionResult call = invoker.Invoke(
            new McpToolRequest(definition, "echo", """{"text":"hello"}"""),
            workspace);

        Assert.True(tools.Succeeded, tools.SafeMessage);
        McpDiscoveredTool tool = Assert.Single(tools.Tools);
        Assert.Equal("echo", tool.Name);
        Assert.Equal("Echo input.", tool.Description);
        Assert.True(call.Succeeded, call.Summary);
        Assert.Equal("echo: hello", call.Summary);
        Assert.NotNull(call.StructuredPayload);
        Assert.Equal("hello", call.StructuredPayload["structuredContent"].GetProperty("echoed").GetString());

        Assert.True(server.WaitForObservationCount(6, TimeSpan.FromSeconds(2)));
        IReadOnlyList<JsonElement> observations = server.ReadObservations();
        Assert.Equal(
            [
                "initialize",
                "notifications/initialized",
                "tools/list",
                "initialize",
                "notifications/initialized",
                "tools/call"
            ],
            observations
                .Select(observation => observation.GetProperty("method").GetString() ?? string.Empty)
                .ToArray());
        Assert.Equal(McpProtocolClient.ProtocolVersion, observations[0].GetProperty("protocolVersion").GetString());
        Assert.False(observations[1].GetProperty("hasId").GetBoolean());
        Assert.Equal("echo", observations[5].GetProperty("toolName").GetString());
        Assert.Equal("hello", observations[5].GetProperty("textArgument").GetString());
    }

    [Fact]
    public void Discover_tools_initializes_session_then_lists_tools()
    {
        JsonElement toolsResult = JsonSerializer.SerializeToElement(new
        {
            tools = new[]
            {
                new
                {
                    name = "echo",
                    description = "Echo input.",
                    inputSchema = new { type = "object" }
                }
            }
        });
        FakeMcpClientSessionFactory factory = new(
            McpStdioTransportResult.Success(CreateInitializeResponse(1), "", false),
            McpStdioTransportResult.Success(
                new McpJsonRpcResponse
                {
                    Id = JsonSerializer.SerializeToElement(2),
                    Result = toolsResult
                },
                "",
                false));
        McpStdioToolDiscoverer discoverer = new(factory);

        McpToolsListResult result = discoverer.DiscoverTools(CreateServer(), CreateWorkspace());

        Assert.True(result.Succeeded, result.SafeMessage);
        McpDiscoveredTool tool = Assert.Single(result.Tools);
        Assert.Equal("echo", tool.Name);
        Assert.Equal(["initialize", "tools/list"], factory.Session.Requests.Select(request => request.Method).ToArray());
        Assert.Equal(["notifications/initialized"], factory.Session.Notifications.Select(notification => notification.Method).ToArray());
        Assert.True(factory.Session.Disposed);
    }

    [Fact]
    public void Invoke_initializes_session_and_calls_remote_tool_with_arguments_object()
    {
        JsonElement callResult = JsonSerializer.SerializeToElement(new
        {
            content = new[]
            {
                new
                {
                    type = "text",
                    text = "echo: hello"
                }
            },
            structuredContent = new
            {
                echoed = "hello"
            }
        });
        FakeMcpClientSessionFactory factory = new(
            McpStdioTransportResult.Success(CreateInitializeResponse(1), "", false),
            McpStdioTransportResult.Success(
                new McpJsonRpcResponse
                {
                    Id = JsonSerializer.SerializeToElement(2),
                    Result = callResult
                },
                "",
                false));
        McpStdioToolInvoker invoker = new(factory);

        ToolExecutionResult result = invoker.Invoke(
            new McpToolRequest(CreateServer(), "remote echo", """{"text":"hello"}"""),
            CreateWorkspace());

        Assert.True(result.Succeeded, result.Summary);
        Assert.Equal("echo: hello", result.Summary);
        Assert.NotNull(result.StructuredPayload);
        JsonElement structuredContent = result.StructuredPayload["structuredContent"];
        Assert.Equal("hello", structuredContent.GetProperty("echoed").GetString());
        McpJsonRpcRequest callRequest = Assert.Single(
            factory.Session.Requests,
            request => request.Method == "tools/call");
        JsonElement parameters = AssertJsonElement(callRequest.Params);
        Assert.Equal("remote echo", parameters.GetProperty("name").GetString());
        Assert.Equal("hello", parameters.GetProperty("arguments").GetProperty("text").GetString());
        Assert.True(factory.Session.Disposed);
    }

    [Fact]
    public void Invoke_maps_mcp_is_error_true_to_failed_tool_result()
    {
        JsonElement callResult = JsonSerializer.SerializeToElement(new
        {
            content = new[]
            {
                new
                {
                    type = "text",
                    text = "Remote validation failed."
                }
            },
            isError = true
        });
        FakeMcpClientSessionFactory factory = new(
            McpStdioTransportResult.Success(CreateInitializeResponse(1), "", false),
            McpStdioTransportResult.Success(
                new McpJsonRpcResponse
                {
                    Id = JsonSerializer.SerializeToElement(2),
                    Result = callResult
                },
                "",
                false));
        McpStdioToolInvoker invoker = new(factory);

        ToolExecutionResult result = invoker.Invoke(
            new McpToolRequest(CreateServer(), "validate", "{}"),
            CreateWorkspace());

        Assert.False(result.Succeeded);
        Assert.Equal(McpErrorCode.ToolError, result.ErrorCode);
        Assert.Equal("Remote validation failed.", result.Summary);
        Assert.False(result.Retryable);
    }

    [Fact]
    public void Invoke_maps_json_rpc_failure_to_failed_tool_result()
    {
        FakeMcpClientSessionFactory factory = new(
            McpStdioTransportResult.Success(CreateInitializeResponse(1), "", false),
            McpStdioTransportResult.Success(
                new McpJsonRpcResponse
                {
                    Id = JsonSerializer.SerializeToElement(2),
                    Error = new McpJsonRpcError
                    {
                        Code = -32602,
                        Message = "Invalid params"
                    }
                },
                "server stderr",
                false));
        McpStdioToolInvoker invoker = new(factory);

        ToolExecutionResult result = invoker.Invoke(
            new McpToolRequest(CreateServer(), "search", """{"query":"mcp"}"""),
            CreateWorkspace());

        Assert.False(result.Succeeded);
        Assert.Equal(McpErrorCode.JsonRpcError, result.ErrorCode);
        Assert.Contains("MCP tools/call returned a JSON-RPC error.", result.Summary, StringComparison.Ordinal);
        Assert.False(result.Retryable);
    }

    [Fact]
    public void Invoke_maps_open_failure_to_failed_tool_result()
    {
        FakeMcpClientSessionFactory factory = FakeMcpClientSessionFactory.FailingOpen(
            McpErrorCode.StartFailed,
            "MCP stdio server failed to start.");
        McpStdioToolInvoker invoker = new(factory);

        ToolExecutionResult result = invoker.Invoke(
            new McpToolRequest(CreateServer(), "search", """{"query":"mcp"}"""),
            CreateWorkspace());

        Assert.False(result.Succeeded);
        Assert.Equal(McpErrorCode.StartFailed, result.ErrorCode);
        Assert.Contains("failed to start", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("[]")]
    public void Invoke_rejects_invalid_arguments_without_opening_session(string argumentsJson)
    {
        FakeMcpClientSessionFactory factory = new();
        McpStdioToolInvoker invoker = new(factory);

        ToolExecutionResult result = invoker.Invoke(
            new McpToolRequest(CreateServer(), "search", argumentsJson),
            CreateWorkspace());

        Assert.False(result.Succeeded);
        Assert.Equal(ToolErrorCode.InvalidToolArguments, result.ErrorCode);
        Assert.Null(factory.LastServer);
    }

    private static McpJsonRpcResponse CreateInitializeResponse(int id)
    {
        return new McpJsonRpcResponse
        {
            Id = JsonSerializer.SerializeToElement(id),
            Result = JsonSerializer.SerializeToElement(new
            {
                protocolVersion = McpProtocolClient.ProtocolVersion,
                capabilities = new { },
                serverInfo = new
                {
                    name = "fixture",
                    version = "1.0.0"
                }
            })
        };
    }

    private static McpServerDefinition CreateServer()
    {
        return new McpServerDefinition(
            "active",
            Enabled: true,
            Status: "configured",
            TransportSummary: "stdio command: mcp-active",
            Source: "workspace config")
        {
            Transport = "stdio",
            Command = "mcp-active",
            Args = ["--flag"],
            Cwd = ".",
            TimeoutMilliseconds = 1234
        };
    }

    private static WorkspaceContext CreateWorkspace()
    {
        return new WorkspaceContext(
            RootPath: Path.GetTempPath(),
            ConfigPath: Path.Combine(Path.GetTempPath(), ".caicli", "config.json"),
            Status: WorkspaceStatus.Ready);
    }

    private static JsonElement AssertJsonElement(JsonElement? element)
    {
        Assert.True(element.HasValue);
        return element.Value;
    }

    private sealed class FakeMcpClientSessionFactory : IMcpClientSessionFactory
    {
        private readonly McpClientSessionOpenResult? openFailure;

        public FakeMcpClientSessionFactory(params McpStdioTransportResult[] requestResults)
        {
            Session = new FakeMcpSession(requestResults);
        }

        private FakeMcpClientSessionFactory(McpClientSessionOpenResult openFailure)
        {
            this.openFailure = openFailure;
            Session = new FakeMcpSession();
        }

        public FakeMcpSession Session { get; }

        public McpServerDefinition? LastServer { get; private set; }

        public WorkspaceContext? LastWorkspace { get; private set; }

        public static FakeMcpClientSessionFactory FailingOpen(string errorCode, string safeMessage)
        {
            return new FakeMcpClientSessionFactory(McpClientSessionOpenResult.Failure(errorCode, safeMessage));
        }

        public McpClientSessionOpenResult OpenSession(
            McpServerDefinition server,
            WorkspaceContext workspace,
            CancellationToken cancellationToken = default)
        {
            LastServer = server;
            LastWorkspace = workspace;
            return openFailure ?? McpClientSessionOpenResult.Success(Session);
        }
    }

    private sealed class FakeMcpSession : IMcpJsonRpcSession, IDisposable
    {
        private readonly Queue<McpStdioTransportResult> requestResults;

        public FakeMcpSession(params McpStdioTransportResult[] requestResults)
        {
            this.requestResults = new Queue<McpStdioTransportResult>(requestResults);
        }

        public List<McpJsonRpcRequest> Requests { get; } = [];

        public List<McpJsonRpcNotification> Notifications { get; } = [];

        public bool Disposed { get; private set; }

        public McpStdioTransportResult Send(
            McpJsonRpcRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (!requestResults.TryDequeue(out McpStdioTransportResult? result))
            {
                throw new InvalidOperationException("No fake MCP response was configured.");
            }

            return result;
        }

        public McpStdioTransportResult SendNotification(
            McpJsonRpcNotification notification,
            CancellationToken cancellationToken = default)
        {
            Notifications.Add(notification);
            return McpStdioTransportResult.NotificationSent(stderrSnippet: "", stderrTruncated: false);
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }
}
