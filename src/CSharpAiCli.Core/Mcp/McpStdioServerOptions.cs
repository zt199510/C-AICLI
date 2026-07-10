namespace CSharpAiCli.Core;

public sealed record McpStdioServerOptions
{
    public const int DefaultTimeoutMilliseconds = 30_000;

    public McpStdioServerOptions(
        string serverName,
        string command,
        IReadOnlyList<string>? arguments = null,
        string? workingDirectory = null,
        int timeoutMilliseconds = DefaultTimeoutMilliseconds)
    {
        ServerName = serverName;
        Command = command;
        Args = arguments?.ToArray() ?? [];
        WorkingDirectory = workingDirectory;
        TimeoutMilliseconds = NormalizeTimeoutMilliseconds(timeoutMilliseconds);
    }

    public string ServerName { get; }

    public string Command { get; }

    public IReadOnlyList<string> Args { get; }

    public string? WorkingDirectory { get; }

    public int TimeoutMilliseconds { get; }

    public static int NormalizeTimeoutMilliseconds(int timeoutMilliseconds)
    {
        return timeoutMilliseconds > 0
            ? timeoutMilliseconds
            : DefaultTimeoutMilliseconds;
    }
}
