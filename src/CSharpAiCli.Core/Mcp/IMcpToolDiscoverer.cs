namespace CSharpAiCli.Core;

public interface IMcpToolDiscoverer
{
    McpToolsListResult DiscoverTools(
        McpServerDefinition server,
        WorkspaceContext workspace,
        CancellationToken cancellationToken = default);
}
