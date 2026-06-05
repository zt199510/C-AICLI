namespace CSharpAiCli.Core;

public sealed record ChatRequest(string Prompt, string? SessionName = null)
{
    public bool HasPrompt => !string.IsNullOrWhiteSpace(Prompt);
    public bool HasSession => !string.IsNullOrWhiteSpace(SessionName);
}
