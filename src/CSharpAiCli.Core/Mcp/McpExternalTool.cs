using System.Text.Json;

namespace CSharpAiCli.Core;

public sealed class McpExternalTool : ITool
{
    private readonly McpServerDefinition server;
    private readonly string remoteToolName;
    private readonly IMcpToolInvoker invoker;
    private readonly IApprovalPolicy approvalPolicy;
    private readonly ToolDefinition definition;

    public McpExternalTool(McpServerDefinition server, IMcpToolInvoker invoker)
        : this(
            server,
            $"mcp.{server.Name}.call",
            "call",
            $"Call MCP server '{server.Name}' through {server.TransportSummary}.",
            """{"type":"object"}""",
            invoker,
            ApprovalPolicyResolver.Resolve(ApprovalMode.Never))
    {
    }

    public McpExternalTool(
        McpServerDefinition server,
        IMcpToolInvoker invoker,
        IApprovalPolicy approvalPolicy)
        : this(
            server,
            $"mcp.{server.Name}.call",
            "call",
            $"Call MCP server '{server.Name}' through {server.TransportSummary}.",
            """{"type":"object"}""",
            invoker,
            approvalPolicy)
    {
    }

    public McpExternalTool(
        McpServerDefinition server,
        string localToolName,
        string remoteToolName,
        string description,
        string parametersSchema,
        IMcpToolInvoker invoker,
        IApprovalPolicy approvalPolicy)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentException.ThrowIfNullOrWhiteSpace(localToolName);
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteToolName);
        ArgumentNullException.ThrowIfNull(invoker);
        ArgumentNullException.ThrowIfNull(approvalPolicy);

        this.server = server;
        this.remoteToolName = remoteToolName;
        this.invoker = invoker;
        this.approvalPolicy = approvalPolicy;
        definition = new ToolDefinition(
            localToolName,
            description,
            parametersSchema,
            ToolRiskLevel.Shell);
    }

    public ToolDefinition Definition => definition;

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

        ApprovalDecision approval = approvalPolicy.RequestApproval(new ApprovalRequest(
            Operation: definition.Name,
            Summary: $"Call MCP tool '{remoteToolName}' on server '{server.Name}'.",
            Diff: null,
            IsDirtyWorkspace: false,
            Metadata: new Dictionary<string, string>
            {
                ["server"] = server.Name,
                ["remoteTool"] = remoteToolName,
                ["localTool"] = definition.Name,
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

        if (!TryNormalizeArguments(context.ArgumentsJson, out string normalizedArguments, out ToolExecutionResult? failure))
        {
            return failure with { ApprovalStatus = approval.Status };
        }

        ToolExecutionResult result = invoker.Invoke(
            new McpToolRequest(server, remoteToolName, normalizedArguments),
            context.Workspace,
            cancellationToken);
        return result with { ApprovalStatus = approval.Status };
    }

    private static bool TryNormalizeArguments(
        string? argumentsJson,
        out string normalizedArguments,
        out ToolExecutionResult failure)
    {
        normalizedArguments = string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson;
        failure = null!;

        try
        {
            using JsonDocument document = JsonDocument.Parse(normalizedArguments);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                failure = ToolExecutionResult.Failure(
                    ToolErrorCode.InvalidToolArguments,
                    "MCP tool arguments must be a JSON object.");
                return false;
            }

            normalizedArguments = JsonSerializer.Serialize(document.RootElement);
            return true;
        }
        catch (JsonException)
        {
            failure = ToolExecutionResult.Failure(
                ToolErrorCode.InvalidToolArguments,
                "MCP tool arguments must be valid JSON.");
            return false;
        }
    }
}
