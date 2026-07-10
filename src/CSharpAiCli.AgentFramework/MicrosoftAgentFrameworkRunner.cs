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

        AgentError error = new(
            "agent-framework-unavailable",
            "Microsoft Agent Framework adapter is scaffolded but no framework package is enabled.",
            Retryable: false);
        string stopReason = AgentStopReason.FromErrorCode(error.LocalErrorCode);
        AgentRunEvent errorEvent = new(
            Type: "agent.error",
            Sequence: 0,
            Timestamp: DateTimeOffset.UtcNow,
            Message: error.SafeMessage,
            ErrorCode: error.LocalErrorCode,
            Status: "failure",
            StopReason: stopReason);

        return AgentRunResult.Failure(
            error,
            [],
            [errorEvent],
            stopReason: stopReason,
            status: "failure");
    }
}
