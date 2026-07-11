namespace CSharpAiCli.Core;

public sealed record ChangedFileSummary(
    string Path,
    string Status,
    string SourceToolCallId,
    string? DiffStat = null,
    bool DiffStatTruncated = false,
    string? ErrorCode = null);
