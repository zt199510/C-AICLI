using CSharpAiCli.Core;

namespace CSharpAiCli.AgentFramework;

public sealed class MicrosoftAgentFrameworkRunner : IAgentRunner
{
    public AgentRunResult Run(
        AgentRunRequest request,
        ConversationTranscript? transcript = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return AgentRunResult.Failure(
            new AgentError(
                "agent-framework-unavailable",
                "Microsoft Agent Framework adapter is scaffolded but no framework package is enabled.",
                Retryable: false),
            []);
    }
}
