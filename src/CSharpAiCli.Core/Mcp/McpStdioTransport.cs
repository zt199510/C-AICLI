namespace CSharpAiCli.Core;

public sealed class McpStdioTransport
{
    private readonly IWorkspaceGuard workspaceGuard;

    public McpStdioTransport(IWorkspaceGuard workspaceGuard)
    {
        ArgumentNullException.ThrowIfNull(workspaceGuard);
        this.workspaceGuard = workspaceGuard;
    }

    public McpStdioSessionOpenResult OpenSession(
        WorkspaceContext workspace,
        McpStdioServerOptions options,
        CancellationToken cancellationToken = default)
    {
        return McpStdioSession.Open(workspace, options, workspaceGuard, cancellationToken);
    }

    public McpStdioTransportResult Send(
        WorkspaceContext workspace,
        McpStdioServerOptions options,
        McpJsonRpcRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        McpStdioSessionOpenResult openResult = OpenSession(workspace, options, cancellationToken);
        if (!openResult.Succeeded || openResult.Session is null)
        {
            return McpStdioTransportResult.Failure(
                openResult.ErrorCode ?? McpErrorCode.StartFailed,
                openResult.SafeMessage,
                stderrSnippet: openResult.StderrSnippet,
                stderrTruncated: openResult.StderrTruncated);
        }

        using McpStdioSession session = openResult.Session;
        return session.Send(request, cancellationToken);
    }
}
