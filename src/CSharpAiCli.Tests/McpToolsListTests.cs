using System.Text.Json;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class McpToolsListTests
{
    [Fact]
    public void ListTools_sends_tools_list_and_parses_multiple_tools()
    {
        JsonElement responseResult = JsonSerializer.SerializeToElement(new
        {
            tools = new object[]
            {
                new
                {
                    name = "read_file",
                    description = "Read a file.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            path = new { type = "string" }
                        },
                        required = new[] { "path" }
                    }
                },
                new
                {
                    name = "search",
                    description = "Search workspace.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            query = new { type = "string" }
                        }
                    }
                }
            }
        });
        FakeMcpSession session = new(McpStdioTransportResult.Success(
            new McpJsonRpcResponse
            {
                Id = JsonSerializer.SerializeToElement(1),
                Result = responseResult
            },
            stderrSnippet: "",
            stderrTruncated: false));
        McpProtocolClient client = new(session);

        McpToolsListResult result = client.ListTools();

        Assert.True(result.Succeeded, result.SafeMessage);
        McpJsonRpcRequest request = Assert.Single(session.Requests);
        Assert.Equal("tools/list", request.Method);
        Assert.Null(request.Params);
        Assert.Equal(2, result.Tools.Count);
        Assert.Equal("read_file", result.Tools[0].Name);
        Assert.Equal("Read a file.", result.Tools[0].Description);
        Assert.Equal("object", result.Tools[0].InputSchema.GetProperty("type").GetString());
        Assert.True(result.Tools[0].InputSchema.GetProperty("properties").TryGetProperty("path", out _));
        Assert.Equal("search", result.Tools[1].Name);
        Assert.Equal("Search workspace.", result.Tools[1].Description);
        Assert.True(result.Tools[1].InputSchema.GetProperty("properties").TryGetProperty("query", out _));
    }

    [Fact]
    public void ListTools_defaults_missing_input_schema_to_object_schema()
    {
        JsonElement responseResult = JsonSerializer.SerializeToElement(new
        {
            tools = new[]
            {
                new
                {
                    name = "ping"
                }
            }
        });
        FakeMcpSession session = new(McpStdioTransportResult.Success(
            new McpJsonRpcResponse
            {
                Id = JsonSerializer.SerializeToElement(1),
                Result = responseResult
            },
            stderrSnippet: "",
            stderrTruncated: false));
        McpProtocolClient client = new(session);

        McpToolsListResult result = client.ListTools();

        Assert.True(result.Succeeded, result.SafeMessage);
        McpDiscoveredTool tool = Assert.Single(result.Tools);
        Assert.Equal("ping", tool.Name);
        Assert.Equal("", tool.Description);
        Assert.Equal("object", tool.InputSchema.GetProperty("type").GetString());
        Assert.Single(tool.InputSchema.EnumerateObject());
    }

    [Fact]
    public void ListTools_json_rpc_error_returns_safe_failure_with_error_details()
    {
        FakeMcpSession session = new(McpStdioTransportResult.Success(
            new McpJsonRpcResponse
            {
                Id = JsonSerializer.SerializeToElement(1),
                Error = new McpJsonRpcError
                {
                    Code = -32601,
                    Message = "Method not found: tools/list"
                }
            },
            stderrSnippet: "server stderr",
            stderrTruncated: false));
        McpProtocolClient client = new(session);

        McpToolsListResult result = client.ListTools();

        Assert.False(result.Succeeded);
        Assert.Equal(McpErrorCode.JsonRpcError, result.ErrorCode);
        Assert.Equal(-32601, result.JsonRpcErrorCode);
        Assert.Equal("Method not found: tools/list", result.JsonRpcErrorMessage);
        Assert.Contains("JSON-RPC error", result.SafeMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Tools);
        Assert.Equal("server stderr", result.StderrSnippet);
    }

    [Fact]
    public void ListTools_transport_failure_returns_safe_failure()
    {
        FakeMcpSession session = new(McpStdioTransportResult.Failure(
            McpErrorCode.Timeout,
            "MCP stdio request timed out.",
            timedOut: true,
            stderrSnippet: "partial stderr",
            stderrTruncated: true));
        McpProtocolClient client = new(session);

        McpToolsListResult result = client.ListTools();

        Assert.False(result.Succeeded);
        Assert.Equal(McpErrorCode.Timeout, result.ErrorCode);
        Assert.Equal("MCP stdio request timed out.", result.SafeMessage);
        Assert.True(result.TimedOut);
        Assert.Equal("partial stderr", result.StderrSnippet);
        Assert.True(result.StderrTruncated);
        Assert.Empty(result.Tools);
    }

    [Theory]
    [MemberData(nameof(InvalidResultShapes))]
    public void ListTools_invalid_result_or_tools_shape_returns_invalid_response(JsonElement responseResult)
    {
        FakeMcpSession session = new(McpStdioTransportResult.Success(
            new McpJsonRpcResponse
            {
                Id = JsonSerializer.SerializeToElement(1),
                Result = responseResult
            },
            stderrSnippet: "",
            stderrTruncated: false));
        McpProtocolClient client = new(session);

        McpToolsListResult result = client.ListTools();

        Assert.False(result.Succeeded);
        Assert.Equal(McpErrorCode.InvalidResponse, result.ErrorCode);
        Assert.Empty(result.Tools);
        Assert.Contains("invalid", result.SafeMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(InvalidToolEntries))]
    public void ListTools_invalid_tool_entry_returns_invalid_response(JsonElement toolEntry)
    {
        JsonElement responseResult = JsonSerializer.SerializeToElement(new
        {
            tools = new[] { toolEntry }
        });
        FakeMcpSession session = new(McpStdioTransportResult.Success(
            new McpJsonRpcResponse
            {
                Id = JsonSerializer.SerializeToElement(1),
                Result = responseResult
            },
            stderrSnippet: "",
            stderrTruncated: false));
        McpProtocolClient client = new(session);

        McpToolsListResult result = client.ListTools();

        Assert.False(result.Succeeded);
        Assert.Equal(McpErrorCode.InvalidResponse, result.ErrorCode);
        Assert.Empty(result.Tools);
    }

    public static TheoryData<JsonElement> InvalidResultShapes()
    {
        return new TheoryData<JsonElement>
        {
            JsonSerializer.SerializeToElement(new { }),
            JsonSerializer.SerializeToElement(new { tools = "not-array" }),
            JsonSerializer.SerializeToElement(new { tools = new object?[] { null } }),
            JsonSerializer.SerializeToElement(new object[] { })
        };
    }

    public static TheoryData<JsonElement> InvalidToolEntries()
    {
        return new TheoryData<JsonElement>
        {
            JsonSerializer.SerializeToElement(new { description = "missing name" }),
            JsonSerializer.SerializeToElement(new { name = "   " }),
            JsonSerializer.SerializeToElement(new { name = "bad_schema", inputSchema = "not-object" })
        };
    }

    private sealed class FakeMcpSession : IMcpJsonRpcSession
    {
        private readonly McpStdioTransportResult requestResult;

        public FakeMcpSession(McpStdioTransportResult requestResult)
        {
            this.requestResult = requestResult;
        }

        public List<McpJsonRpcRequest> Requests { get; } = [];

        public McpStdioTransportResult Send(
            McpJsonRpcRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return requestResult;
        }

        public McpStdioTransportResult SendNotification(
            McpJsonRpcNotification notification,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("tools/list should not send notifications.");
        }
    }
}
