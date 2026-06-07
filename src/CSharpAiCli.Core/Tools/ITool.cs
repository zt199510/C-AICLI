namespace CSharpAiCli.Core;

public interface ITool
{
    ToolDefinition Definition { get; }

    ToolExecutionResult Execute(
        ToolExecutionContext context,
        CancellationToken cancellationToken = default);
}
