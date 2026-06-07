namespace CSharpAiCli.Core;

internal sealed record GitCommandResult(
    bool Succeeded,
    int? ExitCode,
    string Stdout,
    string Stderr,
    string? ErrorCode,
    string Summary);
