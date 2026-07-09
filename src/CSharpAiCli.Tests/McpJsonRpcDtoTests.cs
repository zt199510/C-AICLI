using System.Text.Json;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class McpJsonRpcDtoTests
{
    [Fact]
    public void Request_serializes_jsonrpc_id_method_and_params()
    {
        using JsonDocument parameters = JsonDocument.Parse(
            """
            {
              "protocolVersion": "2025-03-26",
              "capabilities": {},
              "clientInfo": {
                "name": "c-aicli",
                "version": "test"
              }
            }
            """);
        McpJsonRpcRequest request = new(
            JsonSerializer.SerializeToElement(1),
            "initialize",
            parameters.RootElement.Clone());

        string json = JsonSerializer.Serialize(request);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.Equal("2.0", root.GetProperty("jsonrpc").GetString());
        Assert.Equal(1, root.GetProperty("id").GetInt32());
        Assert.Equal("initialize", root.GetProperty("method").GetString());
        JsonElement requestParams = root.GetProperty("params");
        Assert.Equal("2025-03-26", requestParams.GetProperty("protocolVersion").GetString());
        Assert.Equal("c-aicli", requestParams.GetProperty("clientInfo").GetProperty("name").GetString());
        Assert.False(root.TryGetProperty("jsonRpc", out _));
    }

    [Fact]
    public void Request_omits_params_when_not_provided()
    {
        McpJsonRpcRequest request = new(JsonSerializer.SerializeToElement("list-1"), "tools/list");

        string json = JsonSerializer.Serialize(request);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.Equal("2.0", root.GetProperty("jsonrpc").GetString());
        Assert.Equal("list-1", root.GetProperty("id").GetString());
        Assert.Equal("tools/list", root.GetProperty("method").GetString());
        Assert.False(root.TryGetProperty("params", out _));
    }

    [Fact]
    public void Response_deserializes_result_payload()
    {
        const string json = """
            {
              "jsonrpc": "2.0",
              "id": 2,
              "result": {
                "tools": [
                  {
                    "name": "echo",
                    "description": "Echo input"
                  }
                ]
              }
            }
            """;

        McpJsonRpcResponse? response = JsonSerializer.Deserialize<McpJsonRpcResponse>(json);

        Assert.NotNull(response);
        Assert.Equal("2.0", response.JsonRpc);
        Assert.Equal(2, response.Id.GetInt32());
        Assert.Null(response.Error);
        JsonElement result = AssertJsonElement(response.Result);
        Assert.Equal("echo", result.GetProperty("tools")[0].GetProperty("name").GetString());
    }

    [Fact]
    public void Response_deserializes_error_payload_with_data()
    {
        const string json = """
            {
              "jsonrpc": "2.0",
              "id": "call-1",
              "error": {
                "code": -32601,
                "message": "Method not found",
                "data": {
                  "method": "missing/method"
                }
              }
            }
            """;

        McpJsonRpcResponse? response = JsonSerializer.Deserialize<McpJsonRpcResponse>(json);

        Assert.NotNull(response);
        Assert.Equal("2.0", response.JsonRpc);
        Assert.Equal("call-1", response.Id.GetString());
        Assert.Null(response.Result);
        Assert.NotNull(response.Error);
        Assert.Equal(-32601, response.Error.Code);
        Assert.Equal("Method not found", response.Error.Message);
        JsonElement data = AssertJsonElement(response.Error.Data);
        Assert.Equal("missing/method", data.GetProperty("method").GetString());
    }

    private static JsonElement AssertJsonElement(JsonElement? element)
    {
        Assert.True(element.HasValue);
        return element.Value;
    }
}
