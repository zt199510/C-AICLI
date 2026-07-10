namespace CSharpAiCli.Core;

public sealed class McpStdioToolDiscoverer : IMcpToolDiscoverer
{
    private readonly IMcpClientSessionFactory sessionFactory;
    private readonly Func<DateTimeOffset> utcNowProvider;

    public McpStdioToolDiscoverer(IMcpClientSessionFactory sessionFactory)
        : this(sessionFactory, null)
    {
    }

    public McpStdioToolDiscoverer(
        IMcpClientSessionFactory sessionFactory,
        Func<DateTimeOffset>? utcNowProvider)
    {
        ArgumentNullException.ThrowIfNull(sessionFactory);
        this.sessionFactory = sessionFactory;
        this.utcNowProvider = utcNowProvider ?? (() => DateTimeOffset.UtcNow);
    }

    public McpToolsListResult DiscoverTools(
        McpServerDefinition server,
        WorkspaceContext workspace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(workspace);
        cancellationToken.ThrowIfCancellationRequested();

        DateTimeOffset startedUtc = utcNowProvider();
        try
        {
            McpClientSessionOpenResult openResult = sessionFactory.OpenSession(server, workspace, cancellationToken);
            if (!openResult.Succeeded || openResult.Session is null)
            {
                return Complete(
                    McpToolsListResult.Failure(
                        openResult.ErrorCode ?? McpErrorCode.StartFailed,
                        openResult.SafeMessage,
                        stderrSnippet: openResult.StderrSnippet,
                        stderrTruncated: openResult.StderrTruncated),
                    startedUtc);
            }

            using IDisposable? sessionLease = openResult.Session as IDisposable;
            McpProtocolClient client = new(openResult.Session);
            McpInitializeResult initializeResult = client.Initialize(cancellationToken);
            if (!initializeResult.Succeeded)
            {
                return Complete(
                    McpToolsListResult.Failure(
                        initializeResult.ErrorCode ?? McpErrorCode.InvalidResponse,
                        initializeResult.SafeMessage,
                        initializeResult.JsonRpcErrorCode,
                        initializeResult.JsonRpcErrorMessage,
                        initializeResult.TimedOut,
                        initializeResult.StderrSnippet,
                        initializeResult.StderrTruncated),
                    startedUtc);
            }

            return Complete(client.ListTools(cancellationToken), startedUtc);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Complete(
                McpToolsListResult.Failure(
                    McpErrorCode.ClientFailed,
                    "MCP tool discovery failed."),
                startedUtc);
        }
    }

    private McpToolsListResult Complete(McpToolsListResult result, DateTimeOffset startedUtc)
    {
        string status = result.Succeeded
            ? DiagnosticEventStatus.Success
            : result.TimedOut
                ? DiagnosticEventStatus.Timeout
                : DiagnosticEventStatus.Failure;

        return result with
        {
            Status = status,
            DurationMs = CalculateDurationMs(startedUtc, utcNowProvider())
        };
    }

    private static long CalculateDurationMs(DateTimeOffset startedUtc, DateTimeOffset completedUtc)
    {
        return Math.Max(0, (long)(completedUtc - startedUtc).TotalMilliseconds);
    }
}
