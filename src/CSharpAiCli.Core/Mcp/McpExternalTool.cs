namespace CSharpAiCli.Core;

public sealed class McpExternalTool : ITool
{
    private readonly McpServerDefinition server;
    private readonly IMcpToolInvoker invoker;

    public McpExternalTool(McpServerDefinition server, IMcpToolInvoker invoker)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(invoker);

        this.server = server;
        this.invoker = invoker;
    }

    public ToolDefinition Definition => new(
        $"mcp.{server.Name}.call",
        $"Call MCP server '{server.Name}' through {server.TransportSummary}.",
        """{"type":"object"}""");

    public ToolExecutionResult Execute(
        ToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!server.Enabled || server.Status != "configured")
        {
            return ToolExecutionResult.Failure(
                "mcp-server-inactive",
                "MCP server is not active.");
        }

        return invoker.Invoke(new McpToolRequest(server.Name, context.ArgumentsJson), cancellationToken);
    }
}
