namespace CSharpAiCli.Core;

public sealed record OpenAiResponseEnvelope(
    string ResponseId,
    string Model,
    string Text);
