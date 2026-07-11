namespace CSharpAiCli.Core;

public sealed record VerificationResultSummary(
    string Status,
    string Source,
    string? Command,
    string? WorkingDirectory,
    bool Succeeded,
    string ApprovalStatus,
    string? ErrorCode,
    int? ExitCode,
    bool TimedOut,
    bool StdoutTruncated,
    bool StderrTruncated,
    string? Stdout,
    string? Stderr,
    string Summary);
