namespace CSharpAiCli.Core;

public sealed record AgentModelTurn(
    string? FinalText,
    IReadOnlyList<AgentToolCallRequest> ToolCalls)
{
    public bool IsFinal => ToolCalls.Count == 0 && !string.IsNullOrWhiteSpace(FinalText);

    public static AgentModelTurn Final(string text)
    {
        return new AgentModelTurn(text, []);
    }

    public static AgentModelTurn RequestTools(params AgentToolCallRequest[] toolCalls)
    {
        return new AgentModelTurn(FinalText: null, toolCalls);
    }
}
