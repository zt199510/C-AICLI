namespace CSharpAiCli.Core;

public sealed class McpToolBridge
{
    private readonly IMcpToolInvoker invoker;
    private readonly IApprovalPolicy approvalPolicy;

    public McpToolBridge(IMcpToolInvoker invoker)
        : this(invoker, ApprovalPolicyResolver.Resolve(ApprovalMode.Never))
    {
    }

    public McpToolBridge(IMcpToolInvoker invoker, IApprovalPolicy approvalPolicy)
    {
        ArgumentNullException.ThrowIfNull(invoker);
        ArgumentNullException.ThrowIfNull(approvalPolicy);
        this.invoker = invoker;
        this.approvalPolicy = approvalPolicy;
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

            tools.Add(new McpExternalTool(server, invoker, approvalPolicy));
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
