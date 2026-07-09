namespace CSharpAiCli.Core;

public interface IMcpClientSessionFactory
{
    McpClientSessionOpenResult OpenSession(
        McpServerDefinition server,
        WorkspaceContext workspace,
        CancellationToken cancellationToken = default);
}

public sealed record McpClientSessionOpenResult(
    bool Succeeded,
    IMcpJsonRpcSession? Session,
    string? ErrorCode,
    string SafeMessage,
    string StderrSnippet,
    bool StderrTruncated)
{
    public static McpClientSessionOpenResult Success(IMcpJsonRpcSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new McpClientSessionOpenResult(
            Succeeded: true,
            Session: session,
            ErrorCode: null,
            SafeMessage: "MCP client session started.",
            StderrSnippet: string.Empty,
            StderrTruncated: false);
    }

    public static McpClientSessionOpenResult Failure(
        string errorCode,
        string safeMessage,
        string stderrSnippet = "",
        bool stderrTruncated = false)
    {
        return new McpClientSessionOpenResult(
            Succeeded: false,
            Session: null,
            ErrorCode: errorCode,
            SafeMessage: safeMessage,
            StderrSnippet: stderrSnippet,
            StderrTruncated: stderrTruncated);
    }
}
