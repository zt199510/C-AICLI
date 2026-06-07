namespace CSharpAiCli.Core;

public sealed record ShellCommandResult(
    bool Succeeded,
    int? ExitCode,
    string Stdout,
    string Stderr,
    bool TimedOut,
    bool StdoutTruncated,
    bool StderrTruncated,
    string? ErrorCode,
    string Summary)
{
    public static ShellCommandResult Failure(string errorCode, string summary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        return new ShellCommandResult(
            Succeeded: false,
            ExitCode: null,
            Stdout: string.Empty,
            Stderr: string.Empty,
            TimedOut: false,
            StdoutTruncated: false,
            StderrTruncated: false,
            ErrorCode: errorCode,
            Summary: summary);
    }
}
