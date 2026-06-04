namespace CSharpAiCli.Core;

public sealed record ChatRequest(string Prompt)
{
    public bool HasPrompt => !string.IsNullOrWhiteSpace(Prompt);
}
