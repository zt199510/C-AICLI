namespace CSharpAiCli.Core;

public interface IAgentRunEventObserver
{
    void OnEvent(AgentRunEvent agentEvent);
}
