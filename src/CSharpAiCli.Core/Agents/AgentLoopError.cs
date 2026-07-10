namespace CSharpAiCli.Core;

public sealed record AgentLoopError(
    string StopReason,
    string ErrorCode,
    string SafeMessage,
    bool Retryable,
    string Status = DiagnosticEventStatus.Failure)
{
    public AgentError ToAgentError()
    {
        return new AgentError(ErrorCode, SafeMessage, Retryable);
    }
}
