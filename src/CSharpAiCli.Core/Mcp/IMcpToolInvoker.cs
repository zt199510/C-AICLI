namespace CSharpAiCli.Core;

public interface IMcpToolInvoker
{
    ToolExecutionResult Invoke(McpToolRequest request, CancellationToken cancellationToken = default);
}
