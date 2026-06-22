namespace CSharpAiCli.Core;

public sealed record AgentRunRequest(
    string Prompt,
    WorkspaceContext Workspace,
    string? Instructions = null,
    string? SessionName = null,
    AgentRunLimits? Limits = null)
{
    public AgentRunLimits EffectiveLimits => Limits ?? AgentRunLimits.Default;
}
