namespace CSharpAiCli.Core;

public sealed record ConversationError(
    DateTimeOffset CreatedAtUtc,
    string Provider,
    string Operation,
    int? StatusCode,
    string? LocalErrorCode,
    string SafeMessage,
    bool Retryable)
{
    public static ConversationError FromModelError(ModelError error, DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new ConversationError(
            CreatedAtUtc: createdAtUtc,
            Provider: error.Provider,
            Operation: error.Operation,
            StatusCode: error.StatusCode,
            LocalErrorCode: error.LocalErrorCode,
            SafeMessage: error.SafeMessage,
            Retryable: error.Retryable);
    }
}
