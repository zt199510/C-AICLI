namespace CSharpAiCli.Core;

public sealed record ChatRequest(
    string Prompt,
    string? SessionName = null,
    string? Instructions = null)
{
    public bool HasPrompt => !string.IsNullOrWhiteSpace(Prompt);
    public bool HasSession => !string.IsNullOrWhiteSpace(SessionName);
    public bool HasInstructions => !string.IsNullOrWhiteSpace(Instructions);
}
