namespace CSharpAiCli.Core;

public sealed record McpStdioSessionOpenResult(
    bool Succeeded,
    McpStdioSession? Session,
    string? ErrorCode,
    string SafeMessage,
    string StderrSnippet,
    bool StderrTruncated)
{
    public static McpStdioSessionOpenResult Success(McpStdioSession session)
    {
        return new McpStdioSessionOpenResult(
            Succeeded: true,
            Session: session,
            ErrorCode: null,
            SafeMessage: "MCP stdio session started.",
            StderrSnippet: string.Empty,
            StderrTruncated: false);
    }

    public static McpStdioSessionOpenResult Failure(
        string errorCode,
        string safeMessage,
        string stderrSnippet = "",
        bool stderrTruncated = false)
    {
        return new McpStdioSessionOpenResult(
            Succeeded: false,
            Session: null,
            ErrorCode: errorCode,
            SafeMessage: safeMessage,
            StderrSnippet: stderrSnippet,
            StderrTruncated: stderrTruncated);
    }
}
