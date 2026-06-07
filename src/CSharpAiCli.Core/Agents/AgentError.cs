namespace CSharpAiCli.Core;

public sealed record AgentError(
    string LocalErrorCode,
    string SafeMessage,
    bool Retryable);
