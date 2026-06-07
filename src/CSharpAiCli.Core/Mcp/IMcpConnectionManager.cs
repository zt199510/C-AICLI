namespace CSharpAiCli.Core;

public interface IMcpConnectionManager
{
    McpConnectionStatus Check(McpServerDefinition server);
}
