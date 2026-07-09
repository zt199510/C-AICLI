namespace CSharpAiCli.Core;

public interface IMcpJsonRpcSession
{
    McpStdioTransportResult Send(
        McpJsonRpcRequest request,
        CancellationToken cancellationToken = default);

    McpStdioTransportResult SendNotification(
        McpJsonRpcNotification notification,
        CancellationToken cancellationToken = default);
}
