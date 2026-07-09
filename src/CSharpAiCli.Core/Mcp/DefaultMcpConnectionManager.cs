namespace CSharpAiCli.Core;

public sealed class DefaultMcpConnectionManager : IMcpConnectionManager
{
    private readonly IMcpClientSessionFactory stdioSessionFactory;

    public DefaultMcpConnectionManager()
        : this(new McpStdioClientSessionFactory(new McpStdioTransport(new WorkspaceGuard())))
    {
    }

    public DefaultMcpConnectionManager(IMcpClientSessionFactory stdioSessionFactory)
    {
        ArgumentNullException.ThrowIfNull(stdioSessionFactory);
        this.stdioSessionFactory = stdioSessionFactory;
    }

    public McpConnectionStatus Check(
        McpServerDefinition server,
        WorkspaceContext workspace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(workspace);

        if (!server.Enabled || server.Status == "inactive")
        {
            return new McpConnectionStatus(
                server.Name,
                "inactive",
                "Server is disabled.");
        }

        if (server.Status == "invalid")
        {
            return new McpConnectionStatus(
                server.Name,
                "invalid",
                "Server configuration is incomplete.");
        }

        if (server.Transport == "stdio")
        {
            if (string.IsNullOrWhiteSpace(server.Command))
            {
                return new McpConnectionStatus(server.Name, "invalid", "stdio command is not configured.");
            }

            if (!IsExecutableAvailable(server.Command))
            {
                return new McpConnectionStatus(
                    server.Name,
                    "unavailable",
                    "stdio command executable was not found on PATH.");
            }

            return CheckStdio(server, workspace, cancellationToken);
        }

        if (server.Transport is "sse" or "http")
        {
            return new McpConnectionStatus(
                server.Name,
                "configured",
                "remote transport is configured; live MCP handshake is not attempted in this diagnostic.");
        }

        return new McpConnectionStatus(server.Name, "invalid", "Transport is not supported.");
    }

    private McpConnectionStatus CheckStdio(
        McpServerDefinition server,
        WorkspaceContext workspace,
        CancellationToken cancellationToken)
    {
        try
        {
            McpClientSessionOpenResult openResult = stdioSessionFactory.OpenSession(
                server,
                workspace,
                cancellationToken);
            if (!openResult.Succeeded || openResult.Session is null)
            {
                return new McpConnectionStatus(
                    server.Name,
                    "unavailable",
                    openResult.SafeMessage);
            }

            using IDisposable? sessionLease = openResult.Session as IDisposable;
            McpProtocolClient client = new(openResult.Session);
            McpInitializeResult initializeResult = client.Initialize(cancellationToken);
            if (!initializeResult.Succeeded)
            {
                return new McpConnectionStatus(
                    server.Name,
                    "unavailable",
                    initializeResult.SafeMessage);
            }

            return new McpConnectionStatus(
                server.Name,
                "active",
                "MCP stdio initialize completed.");
        }
        catch (OperationCanceledException)
        {
            return new McpConnectionStatus(
                server.Name,
                "unavailable",
                "MCP stdio request was cancelled.");
        }
        catch
        {
            return new McpConnectionStatus(
                server.Name,
                "unavailable",
                "MCP stdio initialize failed.");
        }
    }

    private static bool IsExecutableAvailable(string executable)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            return false;
        }

        if (Path.IsPathRooted(executable))
        {
            return File.Exists(executable);
        }

        string? pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathVariable))
        {
            return false;
        }

        foreach (string directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(directory, executable);
            if (File.Exists(candidate))
            {
                return true;
            }

            if (OperatingSystem.IsWindows())
            {
                foreach (string extension in new[] { ".exe", ".cmd", ".bat" })
                {
                    if (File.Exists(candidate + extension))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }
}
