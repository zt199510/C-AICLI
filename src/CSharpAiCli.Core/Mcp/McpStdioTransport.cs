namespace CSharpAiCli.Core;

public sealed class McpStdioTransport
{
    private readonly IWorkspaceGuard workspaceGuard;
    private readonly ShellPolicyConfiguration shellPolicy;

    public McpStdioTransport(
        IWorkspaceGuard workspaceGuard,
        ShellPolicyConfiguration? shellPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(workspaceGuard);
        this.workspaceGuard = workspaceGuard;
        this.shellPolicy = shellPolicy ?? ShellPolicyConfiguration.Default;
    }

    public McpStdioSessionOpenResult OpenSession(
        WorkspaceContext workspace,
        McpStdioServerOptions options,
        CancellationToken cancellationToken = default)
    {
        return McpStdioSession.Open(workspace, options, workspaceGuard, shellPolicy, cancellationToken);
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
