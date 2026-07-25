namespace CSharpAiCli.Core;

public sealed class FailedAgentRunner(
    AgentError error,
    IAgentRunEventObserver? eventObserver = null) : IAgentRunner
{
    public AgentRunResult Run(
        AgentRunRequest request,
        ConversationTranscript? transcript = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        string stopReason = AgentStopReason.FromErrorCode(error.LocalErrorCode);
        AgentRunEvent errorEvent = new(
            Type: "agent.error",
            Sequence: 0,
            Timestamp: DateTimeOffset.UtcNow,
            Message: error.SafeMessage,
            ErrorCode: error.LocalErrorCode,
            Status: DiagnosticEventStatus.Failure,
            StopReason: stopReason);
        eventObserver?.OnEvent(errorEvent);
        return AgentRunResult.Failure(
            error,
            [],
            [errorEvent],
            stopReason: stopReason,
            status: DiagnosticEventStatus.Failure);
    }
}
