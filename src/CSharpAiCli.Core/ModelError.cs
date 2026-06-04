namespace CSharpAiCli.Core;

public sealed record ModelError(
    string Provider,
    string Operation,
    int? StatusCode,
    string? LocalErrorCode,
    string SafeMessage,
    bool Retryable);
