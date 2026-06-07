namespace CSharpAiCli.Core;

public interface IAgentRunner
{
    AgentRunResult Run(
        AgentRunRequest request,
        ConversationTranscript? transcript = null,
        CancellationToken cancellationToken = default);
}
