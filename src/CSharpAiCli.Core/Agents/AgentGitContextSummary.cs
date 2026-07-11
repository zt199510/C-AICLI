namespace CSharpAiCli.Core;

public sealed record AgentGitContextSummary(
    string StatusSummary,
    bool StatusSucceeded,
    string? StatusErrorCode,
    bool IsDirty,
    bool StatusSummaryTruncated,
    string DiffSummary,
    bool DiffSucceeded,
    string? DiffErrorCode,
    bool DiffOutputTruncated,
    bool DiffSummaryTruncated);
