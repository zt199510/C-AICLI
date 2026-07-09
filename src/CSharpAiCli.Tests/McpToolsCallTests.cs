using System.Text.Json;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class McpToolsCallTests
{
    [Fact]
    public void CallTool_sends_tools_call_params_with_name_and_arguments_object()
    {
        JsonElement arguments = JsonSerializer.SerializeToElement(new
        {
            path = "README.md",
            maxBytes = 4096
        });
        JsonElement responseResult = JsonSerializer.SerializeToElement(new
        {
            content = new[]
            {
                new
                {
                    type = "text",
                    text = "file contents"
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

        McpToolCallResult result = client.CallTool("read_file", arguments);

        Assert.True(result.Succeeded, result.SafeMessage);
        McpJsonRpcRequest request = Assert.Single(session.Requests);
        Assert.Equal("tools/call", request.Method);
        JsonElement parameters = AssertJsonElement(request.Params);
        Assert.Equal("read_file", parameters.GetProperty("name").GetString());
        JsonElement sentArguments = parameters.GetProperty("arguments");
        Assert.Equal(JsonValueKind.Object, sentArguments.ValueKind);
        Assert.Equal("README.md", sentArguments.GetProperty("path").GetString());
        Assert.Equal(4096, sentArguments.GetProperty("maxBytes").GetInt32());
    }

    [Fact]
    public void CallTool_parses_content_structured_content_and_defaults_is_error_false()
    {
        JsonElement responseResult = JsonSerializer.SerializeToElement(new
        {
            content = new object[]
            {
                new
                {
                    type = "text",
                    text = "ok"
                },
                new
                {
                    type = "resource",
                    resource = new
                    {
                        uri = "file:///workspace/report.json",
                        mimeType = "application/json"
                    }
                }
            },
            structuredContent = new
            {
                count = 2,
                status = "complete"
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

        McpToolCallResult result = client.CallTool("summarize", JsonSerializer.SerializeToElement(new { }));

        Assert.True(result.Succeeded, result.SafeMessage);
        Assert.False(result.IsToolError);
        Assert.Equal(2, result.Content.Count);
        Assert.Equal("text", result.Content[0].GetProperty("type").GetString());
        Assert.Equal("ok", result.Content[0].GetProperty("text").GetString());
        Assert.Equal("resource", result.Content[1].GetProperty("type").GetString());
        JsonElement structuredContent = AssertJsonElement(result.StructuredContent);
        Assert.Equal(2, structuredContent.GetProperty("count").GetInt32());
        Assert.Equal("complete", structuredContent.GetProperty("status").GetString());
        JsonElement rawResult = AssertJsonElement(result.RawResult);
        Assert.True(rawResult.TryGetProperty("content", out _));
        Assert.True(rawResult.TryGetProperty("structuredContent", out _));
    }

    [Fact]
    public void CallTool_is_error_true_is_successful_protocol_result_with_tool_error_state()
    {
        JsonElement responseResult = JsonSerializer.SerializeToElement(new
        {
            content = new[]
            {
                new
                {
                    type = "text",
                    text = "Tool failed with a domain error."
                }
            },
            isError = true
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

        McpToolCallResult result = client.CallTool("failing_tool", JsonSerializer.SerializeToElement(new { }));

        Assert.True(result.Succeeded, result.SafeMessage);
        Assert.True(result.IsToolError);
        Assert.Null(result.ErrorCode);
        JsonElement content = Assert.Single(result.Content);
        Assert.Equal("Tool failed with a domain error.", content.GetProperty("text").GetString());
    }

    [Fact]
    public void CallTool_json_rpc_error_returns_safe_failure_with_error_details()
    {
        FakeMcpSession session = new(McpStdioTransportResult.Success(
            new McpJsonRpcResponse
            {
                Id = JsonSerializer.SerializeToElement(1),
                Error = new McpJsonRpcError
                {
                    Code = -32602,
                    Message = "Invalid tool arguments"
                }
            },
            stderrSnippet: "server stderr",
            stderrTruncated: true));
        McpProtocolClient client = new(session);

        McpToolCallResult result = client.CallTool("search", JsonSerializer.SerializeToElement(new { query = "mcp" }));

        Assert.False(result.Succeeded);
        Assert.Equal(McpErrorCode.JsonRpcError, result.ErrorCode);
        Assert.Equal(-32602, result.JsonRpcErrorCode);
        Assert.Equal("Invalid tool arguments", result.JsonRpcErrorMessage);
        Assert.Contains("JSON-RPC error", result.SafeMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Content);
        Assert.Equal("server stderr", result.StderrSnippet);
        Assert.True(result.StderrTruncated);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CallTool_invalid_name_fails_before_sending_request(string name)
    {
        FakeMcpSession session = new();
        McpProtocolClient client = new(session);

        McpToolCallResult result = client.CallTool(name, JsonSerializer.SerializeToElement(new { }));

        Assert.False(result.Succeeded);
        Assert.Equal(McpErrorCode.InvalidResponse, result.ErrorCode);
        Assert.Contains("name", result.SafeMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(session.Requests);
    }

    [Theory]
    [MemberData(nameof(InvalidArguments))]
    public void CallTool_invalid_arguments_fail_before_sending_request(JsonElement arguments)
    {
        FakeMcpSession session = new();
        McpProtocolClient client = new(session);

        McpToolCallResult result = client.CallTool("search", arguments);

        Assert.False(result.Succeeded);
        Assert.Equal(McpErrorCode.InvalidResponse, result.ErrorCode);
        Assert.Contains("arguments", result.SafeMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(session.Requests);
    }

    [Theory]
    [MemberData(nameof(InvalidResultShapes))]
    public void CallTool_invalid_result_shape_returns_invalid_response(JsonElement responseResult)
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

        McpToolCallResult result = client.CallTool("search", JsonSerializer.SerializeToElement(new { query = "mcp" }));

        Assert.False(result.Succeeded);
        Assert.Equal(McpErrorCode.InvalidResponse, result.ErrorCode);
        Assert.Contains("invalid", result.SafeMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Content);
        Assert.Null(result.RawResult);
    }

    [Fact]
    public void CallTool_missing_result_returns_invalid_response()
    {
        FakeMcpSession session = new(McpStdioTransportResult.Success(
            new McpJsonRpcResponse
            {
                Id = JsonSerializer.SerializeToElement(1)
            },
            stderrSnippet: "",
            stderrTruncated: false));
        McpProtocolClient client = new(session);

        McpToolCallResult result = client.CallTool("search", JsonSerializer.SerializeToElement(new { query = "mcp" }));

        Assert.False(result.Succeeded);
        Assert.Equal(McpErrorCode.InvalidResponse, result.ErrorCode);
        Assert.Contains("invalid", result.SafeMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Content);
        Assert.Null(result.RawResult);
    }

    public static TheoryData<JsonElement> InvalidArguments()
    {
        return new TheoryData<JsonElement>
        {
            default,
            JsonSerializer.SerializeToElement((object?)null),
            JsonSerializer.SerializeToElement(new[] { "not-object" }),
            JsonSerializer.SerializeToElement("not-object")
        };
    }

    public static TheoryData<JsonElement> InvalidResultShapes()
    {
        return new TheoryData<JsonElement>
        {
            JsonSerializer.SerializeToElement(new object[] { }),
            JsonSerializer.SerializeToElement(new { content = "not-array" }),
            JsonSerializer.SerializeToElement(new { content = Array.Empty<object>(), isError = "not-bool" })
        };
    }

    private static JsonElement AssertJsonElement(JsonElement? element)
    {
        Assert.True(element.HasValue);
        return element.Value;
    }

    private sealed class FakeMcpSession : IMcpJsonRpcSession
    {
        private readonly Queue<McpStdioTransportResult> requestResults;

        public FakeMcpSession(params McpStdioTransportResult[] requestResults)
        {
            this.requestResults = new Queue<McpStdioTransportResult>(requestResults);
        }

        public List<McpJsonRpcRequest> Requests { get; } = [];

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
            throw new NotSupportedException("tools/call should not send notifications.");
        }
    }
}
