namespace CSharpAiCli.Core;

public sealed class McpExternalTool : ITool
{
    private readonly McpServerDefinition server;
    private readonly IMcpToolInvoker invoker;
    private readonly IApprovalPolicy approvalPolicy;

    public McpExternalTool(McpServerDefinition server, IMcpToolInvoker invoker)
        : this(server, invoker, ApprovalPolicyResolver.Resolve(ApprovalMode.Never))
    {
    }

    public McpExternalTool(
        McpServerDefinition server,
        IMcpToolInvoker invoker,
        IApprovalPolicy approvalPolicy)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(invoker);
        ArgumentNullException.ThrowIfNull(approvalPolicy);

        this.server = server;
        this.invoker = invoker;
        this.approvalPolicy = approvalPolicy;
    }

    public ToolDefinition Definition => new(
        $"mcp.{server.Name}.call",
        $"Call MCP server '{server.Name}' through {server.TransportSummary}.",
        """{"type":"object"}""",
        ToolRiskLevel.Shell);

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

        ToolDefinition definition = Definition;
        ApprovalDecision approval = approvalPolicy.RequestApproval(new ApprovalRequest(
            Operation: definition.Name,
            Summary: $"Call MCP server '{server.Name}'.",
            Diff: null,
            IsDirtyWorkspace: false,
            Metadata: new Dictionary<string, string>
            {
                ["server"] = server.Name,
                ["reason"] = "MCP external tool invocation requires approval."
            },
            RiskLevel: definition.RiskLevel));

        if (!approval.Approved)
        {
            return ToolExecutionResult.Failure(
                "approval-denied",
                approval.SafeMessage,
                approvalStatus: approval.Status);
        }

        ToolExecutionResult result = invoker.Invoke(new McpToolRequest(server.Name, context.ArgumentsJson), cancellationToken);
        return result.ApprovalStatus == "not-required"
            ? result with { ApprovalStatus = approval.Status }
            : result;
    }
}
