using CSharpAiCli.Core;

namespace CSharpAiCli.AgentFramework;

public sealed class MicrosoftToolBridge
{
    private readonly IToolRegistry toolRegistry;
    private readonly IToolExecutor toolExecutor;

    public MicrosoftToolBridge(IToolRegistry toolRegistry)
        : this(toolRegistry, new ToolExecutor(toolRegistry))
    {
    }

    public MicrosoftToolBridge(IToolRegistry toolRegistry, IToolExecutor toolExecutor)
    {
        ArgumentNullException.ThrowIfNull(toolRegistry);
        ArgumentNullException.ThrowIfNull(toolExecutor);
        this.toolRegistry = toolRegistry;
        this.toolExecutor = toolExecutor;
    }

    public IReadOnlyList<ToolDefinition> ListToolDefinitions()
    {
        return toolRegistry.List();
    }

    public IReadOnlyList<MicrosoftFrameworkToolDefinition> ListFrameworkToolDefinitions()
    {
        return toolRegistry
            .List()
            .Select(definition => new MicrosoftFrameworkToolDefinition(
                Name: definition.Name,
                Description: definition.Description,
                ParametersSchema: definition.ParametersSchema))
            .ToArray();
    }

    public MicrosoftFrameworkToolResult InvokeTool(
        WorkspaceContext workspace,
        MicrosoftFrameworkToolCall toolCall,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(toolCall);

        ToolExecutionResult result = toolExecutor.Execute(
            toolCall.ToolName,
            new ToolExecutionContext(
                CallId: toolCall.CallId,
                Workspace: workspace,
                ArgumentsJson: toolCall.ArgumentsJson),
            cancellationToken);

        return new MicrosoftFrameworkToolResult(
            CallId: toolCall.CallId,
            ToolName: toolCall.ToolName,
            Succeeded: result.Succeeded,
            Summary: result.Summary,
            ErrorCode: result.ErrorCode,
            Retryable: result.Retryable,
            ApprovalStatus: result.ApprovalStatus);
    }
}
