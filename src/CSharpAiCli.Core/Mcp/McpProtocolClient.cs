using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CSharpAiCli.Core;

public sealed class McpProtocolClient
{
    public const string ProtocolVersion = "2025-03-26";

    private const string InitializeMethod = "initialize";
    private const string InitializedNotificationMethod = "notifications/initialized";
    private const string ToolsListMethod = "tools/list";
    private const string ToolsCallMethod = "tools/call";
    private const int MaxToolsListPages = 100;
    private const int MaxJsonRpcErrorMessageLength = 512;
    private const int MaxProtocolVersionMessageLength = 64;

    private readonly IMcpJsonRpcSession session;
    private long nextRequestId;

    public McpProtocolClient(McpStdioSession session)
        : this((IMcpJsonRpcSession)session)
    {
    }

    public McpProtocolClient(IMcpJsonRpcSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        this.session = session;
    }

    public McpInitializeResult Initialize(CancellationToken cancellationToken = default)
    {
        JsonElement requestId = JsonSerializer.SerializeToElement(
            Interlocked.Increment(ref nextRequestId));
        JsonElement parameters = JsonSerializer.SerializeToElement(new InitializeParams(
            ProtocolVersion,
            new ClientCapabilities(),
            new ClientInfo(ProductInfo.CommandName, ProductInfo.Version)));
        McpJsonRpcRequest request = new(requestId, InitializeMethod, parameters);

        McpStdioTransportResult responseResult = session.Send(request, cancellationToken);
        if (!responseResult.Succeeded)
        {
            return FromTransportFailure(responseResult);
        }

        McpJsonRpcResponse? response = responseResult.Response;
        if (response is null)
        {
            return InvalidInitializeResponse(responseResult);
        }

        if (response.Error is not null)
        {
            return McpInitializeResult.Failure(
                McpErrorCode.JsonRpcError,
                "MCP initialize returned a JSON-RPC error.",
                jsonRpcErrorCode: response.Error.Code,
                jsonRpcErrorMessage: SanitizeJsonRpcErrorMessage(response.Error.Message),
                stderrSnippet: responseResult.StderrSnippet,
                stderrTruncated: responseResult.StderrTruncated);
        }

        if (!TryParseInitializeResult(
            response.Result,
            out string protocolVersion,
            out JsonElement capabilities,
            out McpServerInfo? serverInfo))
        {
            return InvalidInitializeResponse(responseResult);
        }

        if (!string.Equals(protocolVersion, ProtocolVersion, StringComparison.Ordinal))
        {
            return ProtocolVersionMismatchFailure(protocolVersion, responseResult);
        }

        McpStdioTransportResult notificationResult = session.SendNotification(
            new McpJsonRpcNotification(InitializedNotificationMethod),
            cancellationToken);
        if (!notificationResult.Succeeded)
        {
            return FromTransportFailure(notificationResult);
        }

        return McpInitializeResult.Success(
            protocolVersion,
            capabilities,
            serverInfo,
            notificationResult.StderrSnippet,
            notificationResult.StderrTruncated);
    }

    public McpToolsListResult ListTools(CancellationToken cancellationToken = default)
    {
        List<McpDiscoveredTool> tools = [];
        string? cursor = null;
        McpStdioTransportResult? lastResponseResult = null;

        for (int page = 0; page < MaxToolsListPages; page++)
        {
            McpJsonRpcRequest request = CreateToolsListRequest(cursor);
            McpStdioTransportResult responseResult = session.Send(request, cancellationToken);
            lastResponseResult = responseResult;
            if (!responseResult.Succeeded)
            {
                return FromToolsListTransportFailure(responseResult);
            }

            McpJsonRpcResponse? response = responseResult.Response;
            if (response is null)
            {
                return InvalidToolsListResponse(responseResult);
            }

            if (response.Error is not null)
            {
                return McpToolsListResult.Failure(
                    McpErrorCode.JsonRpcError,
                    "MCP tools/list returned a JSON-RPC error.",
                    jsonRpcErrorCode: response.Error.Code,
                    jsonRpcErrorMessage: SanitizeJsonRpcErrorMessage(response.Error.Message),
                    stderrSnippet: responseResult.StderrSnippet,
                    stderrTruncated: responseResult.StderrTruncated);
            }

            if (!TryParseToolsListResult(
                response.Result,
                out IReadOnlyList<McpDiscoveredTool> pageTools,
                out string? nextCursor))
            {
                return InvalidToolsListResponse(responseResult);
            }

            tools.AddRange(pageTools);
            if (string.IsNullOrWhiteSpace(nextCursor))
            {
                return McpToolsListResult.Success(
                    tools,
                    responseResult.StderrSnippet,
                    responseResult.StderrTruncated);
            }

            cursor = nextCursor;
        }

        return ToolsListPaginationLimitFailure(lastResponseResult);
    }

    public McpToolCallResult CallTool(
        string name,
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return McpToolCallResult.Failure(
                McpErrorCode.InvalidResponse,
                "MCP tools/call tool name is required.");
        }

        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return McpToolCallResult.Failure(
                McpErrorCode.InvalidResponse,
                "MCP tools/call arguments must be a JSON object.");
        }

        JsonElement requestId = JsonSerializer.SerializeToElement(
            Interlocked.Increment(ref nextRequestId));
        JsonElement parameters = JsonSerializer.SerializeToElement(new ToolsCallParams(name, arguments));
        McpJsonRpcRequest request = new(requestId, ToolsCallMethod, parameters);

        McpStdioTransportResult responseResult = session.Send(request, cancellationToken);
        if (!responseResult.Succeeded)
        {
            return FromToolCallTransportFailure(responseResult);
        }

        McpJsonRpcResponse? response = responseResult.Response;
        if (response is null)
        {
            return InvalidToolCallResponse(responseResult);
        }

        if (response.Error is not null)
        {
            return McpToolCallResult.Failure(
                McpErrorCode.JsonRpcError,
                "MCP tools/call returned a JSON-RPC error.",
                jsonRpcErrorCode: response.Error.Code,
                jsonRpcErrorMessage: SanitizeJsonRpcErrorMessage(response.Error.Message),
                stderrSnippet: responseResult.StderrSnippet,
                stderrTruncated: responseResult.StderrTruncated);
        }

        if (!TryParseToolCallResult(
            response.Result,
            out IReadOnlyList<JsonElement> content,
            out JsonElement? structuredContent,
            out bool isToolError,
            out JsonElement rawResult))
        {
            return InvalidToolCallResponse(responseResult);
        }

        return McpToolCallResult.Success(
            content,
            structuredContent,
            isToolError,
            rawResult,
            responseResult.StderrSnippet,
            responseResult.StderrTruncated);
    }

    private McpJsonRpcRequest CreateToolsListRequest(string? cursor)
    {
        JsonElement requestId = JsonSerializer.SerializeToElement(
            Interlocked.Increment(ref nextRequestId));
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return new McpJsonRpcRequest(requestId, ToolsListMethod);
        }

        JsonElement parameters = JsonSerializer.SerializeToElement(new ToolsListParams(cursor));
        return new McpJsonRpcRequest(requestId, ToolsListMethod, parameters);
    }

    private static McpInitializeResult FromTransportFailure(McpStdioTransportResult result)
    {
        return McpInitializeResult.Failure(
            result.ErrorCode ?? McpErrorCode.InvalidResponse,
            result.SafeMessage,
            timedOut: result.TimedOut,
            stderrSnippet: result.StderrSnippet,
            stderrTruncated: result.StderrTruncated);
    }

    private static McpToolsListResult FromToolsListTransportFailure(McpStdioTransportResult result)
    {
        return McpToolsListResult.Failure(
            result.ErrorCode ?? McpErrorCode.InvalidResponse,
            result.SafeMessage,
            timedOut: result.TimedOut,
            stderrSnippet: result.StderrSnippet,
            stderrTruncated: result.StderrTruncated);
    }

    private static McpToolCallResult FromToolCallTransportFailure(McpStdioTransportResult result)
    {
        return McpToolCallResult.Failure(
            result.ErrorCode ?? McpErrorCode.InvalidResponse,
            result.SafeMessage,
            timedOut: result.TimedOut,
            stderrSnippet: result.StderrSnippet,
            stderrTruncated: result.StderrTruncated);
    }

    private static McpInitializeResult ProtocolVersionMismatchFailure(
        string protocolVersion,
        McpStdioTransportResult result)
    {
        string safeProtocolVersion = SanitizeProtocolVersion(protocolVersion);
        return McpInitializeResult.Failure(
            McpErrorCode.ProtocolVersionMismatch,
            $"MCP initialize returned unsupported protocol version '{safeProtocolVersion}'.",
            timedOut: result.TimedOut,
            stderrSnippet: result.StderrSnippet,
            stderrTruncated: result.StderrTruncated);
    }

    private static McpInitializeResult InvalidInitializeResponse(McpStdioTransportResult result)
    {
        return McpInitializeResult.Failure(
            McpErrorCode.InvalidResponse,
            "MCP initialize response was missing or invalid.",
            timedOut: result.TimedOut,
            stderrSnippet: result.StderrSnippet,
            stderrTruncated: result.StderrTruncated);
    }

    private static McpToolsListResult InvalidToolsListResponse(McpStdioTransportResult result)
    {
        return McpToolsListResult.Failure(
            McpErrorCode.InvalidResponse,
            "MCP tools/list response was missing or invalid.",
            timedOut: result.TimedOut,
            stderrSnippet: result.StderrSnippet,
            stderrTruncated: result.StderrTruncated);
    }

    private static McpToolCallResult InvalidToolCallResponse(McpStdioTransportResult result)
    {
        return McpToolCallResult.Failure(
            McpErrorCode.InvalidResponse,
            "MCP tools/call response was missing or invalid.",
            timedOut: result.TimedOut,
            stderrSnippet: result.StderrSnippet,
            stderrTruncated: result.StderrTruncated);
    }

    private static McpToolsListResult ToolsListPaginationLimitFailure(McpStdioTransportResult? result)
    {
        return McpToolsListResult.Failure(
            McpErrorCode.InvalidResponse,
            "MCP tools/list pagination exceeded the maximum page count.",
            timedOut: result?.TimedOut ?? false,
            stderrSnippet: result?.StderrSnippet ?? "",
            stderrTruncated: result?.StderrTruncated ?? false);
    }

    private static bool TryParseInitializeResult(
        JsonElement? result,
        out string protocolVersion,
        out JsonElement capabilities,
        out McpServerInfo? serverInfo)
    {
        protocolVersion = string.Empty;
        capabilities = default;
        serverInfo = null;

        if (!result.HasValue || result.Value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        JsonElement root = result.Value;
        if (!TryGetRequiredString(root, "protocolVersion", out protocolVersion))
        {
            return false;
        }

        if (!root.TryGetProperty("capabilities", out JsonElement capabilitiesElement) ||
            capabilitiesElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        capabilities = capabilitiesElement.Clone();
        if (!root.TryGetProperty("serverInfo", out JsonElement serverInfoElement))
        {
            return false;
        }

        if (serverInfoElement.ValueKind != JsonValueKind.Object ||
            !TryGetRequiredString(serverInfoElement, "name", out string name) ||
            !TryGetRequiredString(serverInfoElement, "version", out string version))
        {
            return false;
        }

        serverInfo = new McpServerInfo(name, version);
        return true;
    }

    private static bool TryParseToolsListResult(
        JsonElement? result,
        out IReadOnlyList<McpDiscoveredTool> tools,
        out string? nextCursor)
    {
        tools = [];
        nextCursor = null;

        if (!result.HasValue || result.Value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        JsonElement root = result.Value;
        if (!root.TryGetProperty("tools", out JsonElement toolsElement) ||
            toolsElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        List<McpDiscoveredTool> parsedTools = [];
        foreach (JsonElement toolElement in toolsElement.EnumerateArray())
        {
            if (!TryParseDiscoveredTool(toolElement, out McpDiscoveredTool? tool) ||
                tool is null)
            {
                return false;
            }

            parsedTools.Add(tool);
        }

        if (root.TryGetProperty("nextCursor", out JsonElement nextCursorElement) &&
            nextCursorElement.ValueKind != JsonValueKind.Null)
        {
            if (nextCursorElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            string? cursor = nextCursorElement.GetString();
            if (!string.IsNullOrWhiteSpace(cursor))
            {
                nextCursor = cursor;
            }
        }

        tools = parsedTools;
        return true;
    }

    private static bool TryParseToolCallResult(
        JsonElement? result,
        out IReadOnlyList<JsonElement> content,
        out JsonElement? structuredContent,
        out bool isToolError,
        out JsonElement rawResult)
    {
        content = [];
        structuredContent = null;
        isToolError = false;
        rawResult = default;

        if (!result.HasValue || result.Value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        JsonElement root = result.Value;
        if (!root.TryGetProperty("content", out JsonElement contentElement) ||
            contentElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        List<JsonElement> contentBlocks = [];
        foreach (JsonElement contentBlock in contentElement.EnumerateArray())
        {
            if (contentBlock.ValueKind != JsonValueKind.Object ||
                !TryGetRequiredString(contentBlock, "type", out string contentType))
            {
                return false;
            }

            if (string.Equals(contentType, "text", StringComparison.Ordinal) &&
                (!contentBlock.TryGetProperty("text", out JsonElement textElement) ||
                    textElement.ValueKind != JsonValueKind.String))
            {
                return false;
            }

            contentBlocks.Add(contentBlock.Clone());
        }

        content = contentBlocks;

        if (root.TryGetProperty("structuredContent", out JsonElement structuredContentElement))
        {
            structuredContent = structuredContentElement.Clone();
        }

        if (root.TryGetProperty("isError", out JsonElement isErrorElement))
        {
            if (isErrorElement.ValueKind != JsonValueKind.True &&
                isErrorElement.ValueKind != JsonValueKind.False)
            {
                return false;
            }

            isToolError = isErrorElement.GetBoolean();
        }

        rawResult = root.Clone();
        return true;
    }

    private static bool TryParseDiscoveredTool(
        JsonElement toolElement,
        out McpDiscoveredTool? tool)
    {
        tool = null;
        if (toolElement.ValueKind != JsonValueKind.Object ||
            !TryGetRequiredString(toolElement, "name", out string name) ||
            !TryGetOptionalString(toolElement, "description", out string description) ||
            !TryGetInputSchema(toolElement, out JsonElement inputSchema))
        {
            return false;
        }

        tool = new McpDiscoveredTool(name, description, inputSchema);
        return true;
    }

    private static bool TryGetRequiredString(
        JsonElement element,
        string propertyName,
        out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        string? text = property.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        value = text;
        return true;
    }

    private static bool TryGetOptionalString(
        JsonElement element,
        string propertyName,
        out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return true;
    }

    private static bool TryGetInputSchema(JsonElement toolElement, out JsonElement inputSchema)
    {
        if (!toolElement.TryGetProperty("inputSchema", out JsonElement schemaElement) ||
            schemaElement.ValueKind == JsonValueKind.Null)
        {
            inputSchema = CreateDefaultObjectSchema();
            return true;
        }

        if (schemaElement.ValueKind != JsonValueKind.Object)
        {
            inputSchema = default;
            return false;
        }

        if (!TryGetRequiredString(schemaElement, "type", out string schemaType) ||
            !string.Equals(schemaType, "object", StringComparison.Ordinal))
        {
            inputSchema = default;
            return false;
        }

        inputSchema = schemaElement.Clone();
        return true;
    }

    private static JsonElement CreateDefaultObjectSchema()
    {
        return JsonSerializer.SerializeToElement(new { type = "object" });
    }

    private static string SanitizeJsonRpcErrorMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "JSON-RPC error";
        }

        StringBuilder builder = new(Math.Min(message.Length, MaxJsonRpcErrorMessageLength));
        foreach (char character in message)
        {
            if (builder.Length >= MaxJsonRpcErrorMessageLength)
            {
                break;
            }

            builder.Append(char.IsControl(character) ? ' ' : character);
        }

        string sanitized = builder.ToString().Trim();
        return sanitized.Length == 0 ? "JSON-RPC error" : sanitized;
    }

    private static string SanitizeProtocolVersion(string protocolVersion)
    {
        if (string.IsNullOrWhiteSpace(protocolVersion))
        {
            return "unknown";
        }

        StringBuilder builder = new(Math.Min(protocolVersion.Length, MaxProtocolVersionMessageLength));
        foreach (char character in protocolVersion)
        {
            if (builder.Length >= MaxProtocolVersionMessageLength)
            {
                break;
            }

            builder.Append(char.IsControl(character) ? ' ' : character);
        }

        string sanitized = builder.ToString().Trim();
        return sanitized.Length == 0 ? "unknown" : sanitized;
    }

    private sealed record InitializeParams(
        [property: JsonPropertyName("protocolVersion")] string ProtocolVersion,
        [property: JsonPropertyName("capabilities")] ClientCapabilities Capabilities,
        [property: JsonPropertyName("clientInfo")] ClientInfo ClientInfo);

    private sealed record ToolsListParams(
        [property: JsonPropertyName("cursor")] string Cursor);

    private sealed record ToolsCallParams(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("arguments")] JsonElement Arguments);

    private sealed record ClientCapabilities;

    private sealed record ClientInfo(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("version")] string Version);
}
