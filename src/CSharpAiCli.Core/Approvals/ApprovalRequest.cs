namespace CSharpAiCli.Core;

public sealed record ApprovalRequest(
    string Operation,
    string Summary,
    string? Diff,
    bool IsDirtyWorkspace,
    IReadOnlyDictionary<string, string>? Metadata = null,
    ToolRiskLevel RiskLevel = ToolRiskLevel.Write);
