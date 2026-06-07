namespace CSharpAiCli.Core;

public interface IToolCallingModel
{
    AgentModelTurn Start(AgentRunRequest request, CancellationToken cancellationToken = default);

    AgentModelTurn Continue(
        AgentRunRequest request,
        IReadOnlyList<AgentToolCallResult> toolResults,
        CancellationToken cancellationToken = default);
}
