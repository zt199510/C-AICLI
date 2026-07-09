namespace CSharpAiCli.Core;

public sealed class McpStdioToolDiscoverer : IMcpToolDiscoverer
{
    private readonly IMcpClientSessionFactory sessionFactory;

    public McpStdioToolDiscoverer(IMcpClientSessionFactory sessionFactory)
    {
        ArgumentNullException.ThrowIfNull(sessionFactory);
        this.sessionFactory = sessionFactory;
    }

    public McpToolsListResult DiscoverTools(
        McpServerDefinition server,
        WorkspaceContext workspace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(workspace);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            McpClientSessionOpenResult openResult = sessionFactory.OpenSession(server, workspace, cancellationToken);
            if (!openResult.Succeeded || openResult.Session is null)
            {
                return McpToolsListResult.Failure(
                    openResult.ErrorCode ?? McpErrorCode.StartFailed,
                    openResult.SafeMessage,
                    stderrSnippet: openResult.StderrSnippet,
                    stderrTruncated: openResult.StderrTruncated);
            }

            using IDisposable? sessionLease = openResult.Session as IDisposable;
            McpProtocolClient client = new(openResult.Session);
            McpInitializeResult initializeResult = client.Initialize(cancellationToken);
            if (!initializeResult.Succeeded)
            {
                return McpToolsListResult.Failure(
                    initializeResult.ErrorCode ?? McpErrorCode.InvalidResponse,
                    initializeResult.SafeMessage,
                    initializeResult.JsonRpcErrorCode,
                    initializeResult.JsonRpcErrorMessage,
                    initializeResult.TimedOut,
                    initializeResult.StderrSnippet,
                    initializeResult.StderrTruncated);
            }

            return client.ListTools(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return McpToolsListResult.Failure(
                McpErrorCode.ClientFailed,
                "MCP tool discovery failed.");
        }
    }
}
