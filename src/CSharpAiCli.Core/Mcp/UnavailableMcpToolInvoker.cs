namespace CSharpAiCli.Core;

public sealed class UnavailableMcpToolInvoker : IMcpToolInvoker
{
    public ToolExecutionResult Invoke(
        McpToolRequest request,
        WorkspaceContext workspace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(workspace);
        cancellationToken.ThrowIfCancellationRequested();

        return ToolExecutionResult.Failure(
            "mcp-invoker-unavailable",
            "MCP tool invocation is not connected in this build.");
    }
}
