namespace CSharpAiCli.Core;

internal sealed record AgentStartupPlan(
    string Goal,
    IReadOnlyList<string> CandidateFiles,
    IReadOnlyList<string> ExpectedTools,
    IReadOnlyList<string> Risks,
    string Summary,
    bool Truncated);
