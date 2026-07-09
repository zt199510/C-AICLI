using System.Text.Json;

namespace CSharpAiCli.Core;

public sealed record McpToolsListResult(
    bool Succeeded,
    IReadOnlyList<McpDiscoveredTool> Tools,
    string? ErrorCode,
    string SafeMessage,
    int? JsonRpcErrorCode,
    string? JsonRpcErrorMessage,
    bool TimedOut,
    string StderrSnippet,
    bool StderrTruncated)
{
    public static McpToolsListResult Success(
        IReadOnlyList<McpDiscoveredTool> tools,
        string stderrSnippet,
        bool stderrTruncated)
    {
        return new McpToolsListResult(
            Succeeded: true,
            Tools: tools.ToArray(),
            ErrorCode: null,
            SafeMessage: "MCP tools/list completed.",
            JsonRpcErrorCode: null,
            JsonRpcErrorMessage: null,
            TimedOut: false,
            StderrSnippet: stderrSnippet,
            StderrTruncated: stderrTruncated);
    }

    public static McpToolsListResult Failure(
        string errorCode,
        string safeMessage,
        int? jsonRpcErrorCode = null,
        string? jsonRpcErrorMessage = null,
        bool timedOut = false,
        string stderrSnippet = "",
        bool stderrTruncated = false)
    {
        return new McpToolsListResult(
            Succeeded: false,
            Tools: [],
            ErrorCode: errorCode,
            SafeMessage: safeMessage,
            JsonRpcErrorCode: jsonRpcErrorCode,
            JsonRpcErrorMessage: jsonRpcErrorMessage,
            TimedOut: timedOut,
            StderrSnippet: stderrSnippet,
            StderrTruncated: stderrTruncated);
    }
}

public sealed record McpDiscoveredTool(
    string Name,
    string Description,
    JsonElement InputSchema);
