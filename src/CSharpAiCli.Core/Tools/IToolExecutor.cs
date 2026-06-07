namespace CSharpAiCli.Core;

public interface IToolExecutor
{
    ToolExecutionResult Execute(
        string toolName,
        ToolExecutionContext context,
        CancellationToken cancellationToken = default);
}
