namespace CSharpAiCli.Core;

public sealed class McpStdioClientSessionFactory : IMcpClientSessionFactory
{
    private const int DefaultTimeoutMilliseconds = 30_000;

    private readonly McpStdioTransport transport;

    public McpStdioClientSessionFactory(McpStdioTransport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);
        this.transport = transport;
    }

    public McpClientSessionOpenResult OpenSession(
        McpServerDefinition server,
        WorkspaceContext workspace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(workspace);

        if (!string.Equals(server.Transport, "stdio", StringComparison.Ordinal))
        {
            return McpClientSessionOpenResult.Failure(
                McpErrorCode.StartFailed,
                "MCP server transport is not stdio.");
        }

        if (string.IsNullOrWhiteSpace(server.Command))
        {
            return McpClientSessionOpenResult.Failure(
                McpErrorCode.StartFailed,
                "MCP stdio command is not configured.");
        }

        McpStdioSessionOpenResult openResult = transport.OpenSession(
            workspace,
            new McpStdioServerOptions(
                server.Name,
                server.Command,
                server.Args,
                server.Cwd,
                server.TimeoutMilliseconds ?? DefaultTimeoutMilliseconds),
            cancellationToken);

        if (!openResult.Succeeded || openResult.Session is null)
        {
            return McpClientSessionOpenResult.Failure(
                openResult.ErrorCode ?? McpErrorCode.StartFailed,
                openResult.SafeMessage,
                openResult.StderrSnippet,
                openResult.StderrTruncated);
        }

        return McpClientSessionOpenResult.Success(openResult.Session);
    }
}
