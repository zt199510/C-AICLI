namespace CSharpAiCli.Core;

public sealed record ChatResponse(
    string Provider,
    string Model,
    string ResponseId,
    string Text);
