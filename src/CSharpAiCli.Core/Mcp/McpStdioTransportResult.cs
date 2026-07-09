namespace CSharpAiCli.Core;

public sealed record McpStdioTransportResult(
    bool Succeeded,
    McpJsonRpcResponse? Response,
    string? ErrorCode,
    string SafeMessage,
    bool TimedOut,
    string StderrSnippet,
    bool StderrTruncated)
{
    public static McpStdioTransportResult Success(
        McpJsonRpcResponse response,
        string stderrSnippet,
        bool stderrTruncated)
    {
        return new McpStdioTransportResult(
            Succeeded: true,
            Response: response,
            ErrorCode: null,
            SafeMessage: "MCP stdio request completed.",
            TimedOut: false,
            StderrSnippet: stderrSnippet,
            StderrTruncated: stderrTruncated);
    }

    public static McpStdioTransportResult Failure(
        string errorCode,
        string safeMessage,
        bool timedOut = false,
        string stderrSnippet = "",
        bool stderrTruncated = false)
    {
        return new McpStdioTransportResult(
            Succeeded: false,
            Response: null,
            ErrorCode: errorCode,
            SafeMessage: safeMessage,
            TimedOut: timedOut,
            StderrSnippet: stderrSnippet,
            StderrTruncated: stderrTruncated);
    }
}
