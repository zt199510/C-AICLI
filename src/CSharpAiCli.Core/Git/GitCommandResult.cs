namespace CSharpAiCli.Core;

internal sealed record GitCommandResult(
    bool Succeeded,
    int? ExitCode,
    string Stdout,
    string Stderr,
    bool StdoutTruncated,
    bool StderrTruncated,
    string? ErrorCode,
    string Summary);
