namespace CSharpAiCli.Core;

public sealed record ConversationMessage(
    string Role,
    DateTimeOffset CreatedAtUtc,
    string Content,
    string? Provider,
    string? Model,
    string? ResponseId);
