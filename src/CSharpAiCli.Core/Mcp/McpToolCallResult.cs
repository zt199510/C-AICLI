using System.Text.Json;

namespace CSharpAiCli.Core;

public sealed record McpToolCallResult(
    bool Succeeded,
    IReadOnlyList<JsonElement> Content,
    JsonElement? StructuredContent,
    bool IsToolError,
    JsonElement? RawResult,
    string? ErrorCode,
    string SafeMessage,
    int? JsonRpcErrorCode,
    string? JsonRpcErrorMessage,
    bool TimedOut,
    string StderrSnippet,
    bool StderrTruncated)
{
    public static McpToolCallResult Success(
        IReadOnlyList<JsonElement> content,
        JsonElement? structuredContent,
        bool isToolError,
        JsonElement rawResult,
        string stderrSnippet,
        bool stderrTruncated)
    {
        return new McpToolCallResult(
            Succeeded: true,
            Content: content.Select(contentBlock => contentBlock.Clone()).ToArray(),
            StructuredContent: structuredContent.HasValue ? structuredContent.Value.Clone() : null,
            IsToolError: isToolError,
            RawResult: rawResult.Clone(),
            ErrorCode: null,
            SafeMessage: "MCP tools/call completed.",
            JsonRpcErrorCode: null,
            JsonRpcErrorMessage: null,
            TimedOut: false,
            StderrSnippet: stderrSnippet,
            StderrTruncated: stderrTruncated);
    }

    public static McpToolCallResult Failure(
        string errorCode,
        string safeMessage,
        int? jsonRpcErrorCode = null,
        string? jsonRpcErrorMessage = null,
        bool timedOut = false,
        string stderrSnippet = "",
        bool stderrTruncated = false)
    {
        return new McpToolCallResult(
            Succeeded: false,
            Content: [],
            StructuredContent: null,
            IsToolError: false,
            RawResult: null,
            ErrorCode: errorCode,
            SafeMessage: safeMessage,
            JsonRpcErrorCode: jsonRpcErrorCode,
            JsonRpcErrorMessage: jsonRpcErrorMessage,
            TimedOut: timedOut,
            StderrSnippet: stderrSnippet,
            StderrTruncated: stderrTruncated);
    }
}
