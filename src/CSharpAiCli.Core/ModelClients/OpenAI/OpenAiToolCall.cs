namespace CSharpAiCli.Core;

public sealed record OpenAiToolCall(
    string CallId,
    string Name,
    string? ArgumentsJson);
