namespace CSharpAiCli.Core;

public sealed record ShellCommandRequest(
    string Command,
    string WorkingDirectory,
    int TimeoutMilliseconds,
    int MaxStdoutBytes,
    int MaxStderrBytes);
