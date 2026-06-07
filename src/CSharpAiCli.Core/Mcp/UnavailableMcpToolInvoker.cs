namespace CSharpAiCli.Core;

public sealed class UnavailableMcpToolInvoker : IMcpToolInvoker
{
    public ToolExecutionResult Invoke(McpToolRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return ToolExecutionResult.Failure(
            "mcp-invoker-unavailable",
            "MCP tool invocation is not connected in this build.");
    }
}
