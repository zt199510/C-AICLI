namespace CSharpAiCli.Core;

public sealed record McpStdioServerOptions
{
    public McpStdioServerOptions(
        string serverName,
        string command,
        IReadOnlyList<string>? arguments = null,
        string? workingDirectory = null,
        int timeoutMilliseconds = 30_000)
    {
        ServerName = serverName;
        Command = command;
        Args = arguments?.ToArray() ?? [];
        WorkingDirectory = workingDirectory;
        TimeoutMilliseconds = timeoutMilliseconds;
    }

    public string ServerName { get; }

    public string Command { get; }

    public IReadOnlyList<string> Args { get; }

    public string? WorkingDirectory { get; }

    public int TimeoutMilliseconds { get; }
}
