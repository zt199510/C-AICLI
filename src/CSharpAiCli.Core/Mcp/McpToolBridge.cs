namespace CSharpAiCli.Core;

public sealed class McpToolBridge
{
    private readonly IMcpToolInvoker invoker;

    public McpToolBridge(IMcpToolInvoker invoker)
    {
        ArgumentNullException.ThrowIfNull(invoker);
        this.invoker = invoker;
    }

    public IReadOnlyList<ITool> CreateTools(McpConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        List<ITool> tools = [];
        foreach (McpServerDefinition server in configuration.Servers)
        {
            if (!server.Enabled || server.Status != "configured")
            {
                continue;
            }

            tools.Add(new McpExternalTool(server, invoker));
        }

        return tools;
    }

    public void RegisterTools(IToolRegistry registry, McpConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(registry);

        foreach (ITool tool in CreateTools(configuration))
        {
            registry.Register(tool);
        }
    }
}
