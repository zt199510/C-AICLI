using System.Text.Json;

namespace CSharpAiCli.Core;

public sealed record McpInitializeResult(
    bool Succeeded,
    string? ProtocolVersion,
    JsonElement? Capabilities,
    McpServerInfo? ServerInfo,
    string? ErrorCode,
    string SafeMessage,
    int? JsonRpcErrorCode,
    string? JsonRpcErrorMessage,
    bool TimedOut,
    string StderrSnippet,
    bool StderrTruncated)
{
    public static McpInitializeResult Success(
        string protocolVersion,
        JsonElement capabilities,
        McpServerInfo? serverInfo,
        string stderrSnippet,
        bool stderrTruncated)
    {
        return new McpInitializeResult(
            Succeeded: true,
            ProtocolVersion: protocolVersion,
            Capabilities: capabilities.Clone(),
            ServerInfo: serverInfo,
            ErrorCode: null,
            SafeMessage: "MCP initialize completed.",
            JsonRpcErrorCode: null,
            JsonRpcErrorMessage: null,
            TimedOut: false,
            StderrSnippet: stderrSnippet,
            StderrTruncated: stderrTruncated);
    }

    public static McpInitializeResult Failure(
        string errorCode,
        string safeMessage,
        int? jsonRpcErrorCode = null,
        string? jsonRpcErrorMessage = null,
        bool timedOut = false,
        string stderrSnippet = "",
        bool stderrTruncated = false)
    {
        return new McpInitializeResult(
            Succeeded: false,
            ProtocolVersion: null,
            Capabilities: null,
            ServerInfo: null,
            ErrorCode: errorCode,
            SafeMessage: safeMessage,
            JsonRpcErrorCode: jsonRpcErrorCode,
            JsonRpcErrorMessage: jsonRpcErrorMessage,
            TimedOut: timedOut,
            StderrSnippet: stderrSnippet,
            StderrTruncated: stderrTruncated);
    }
}

public sealed record McpServerInfo(string Name, string Version);
