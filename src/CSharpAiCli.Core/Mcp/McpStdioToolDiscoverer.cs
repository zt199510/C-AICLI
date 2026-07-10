namespace CSharpAiCli.Core;

public sealed class McpStdioToolDiscoverer : IMcpToolDiscoverer
{
    private readonly IMcpClientSessionFactory sessionFactory;
    private readonly DiagnosticDurationClock durationClock;

    public McpStdioToolDiscoverer(IMcpClientSessionFactory sessionFactory)
        : this(sessionFactory, null)
    {
    }

    public McpStdioToolDiscoverer(
        IMcpClientSessionFactory sessionFactory,
        Func<DateTimeOffset>? utcNowProvider,
        Func<long>? timestampProvider = null)
    {
        ArgumentNullException.ThrowIfNull(sessionFactory);
        this.sessionFactory = sessionFactory;
        durationClock = new DiagnosticDurationClock(timestampProvider);
    }

    public McpToolsListResult DiscoverTools(
        McpServerDefinition server,
        WorkspaceContext workspace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(workspace);
        cancellationToken.ThrowIfCancellationRequested();

        long startedTimestamp = durationClock.GetTimestamp();
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
                    startedTimestamp);
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
                    startedTimestamp);
            }

            return Complete(client.ListTools(cancellationToken), startedTimestamp);
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
                startedTimestamp);
        }
    }

    private McpToolsListResult Complete(McpToolsListResult result, long startedTimestamp)
    {
        string status = result.Succeeded
            ? DiagnosticEventStatus.Success
            : result.TimedOut
                ? DiagnosticEventStatus.Timeout
                : DiagnosticEventStatus.Failure;

        return result with
        {
            Status = status,
            DurationMs = durationClock.GetElapsedMilliseconds(startedTimestamp)
        };
    }
}
