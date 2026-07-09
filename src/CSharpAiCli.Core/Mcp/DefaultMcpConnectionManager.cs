namespace CSharpAiCli.Core;

public sealed class DefaultMcpConnectionManager : IMcpConnectionManager
{
    public McpConnectionStatus Check(McpServerDefinition server)
    {
        ArgumentNullException.ThrowIfNull(server);

        if (!server.Enabled || server.Status == "inactive")
        {
            return new McpConnectionStatus(
                server.Name,
                "inactive",
                "Server is disabled.");
        }

        if (server.Status == "invalid")
        {
            return new McpConnectionStatus(
                server.Name,
                "invalid",
                "Server configuration is incomplete.");
        }

        if (server.Transport == "stdio")
        {
            if (string.IsNullOrWhiteSpace(server.Command))
            {
                return new McpConnectionStatus(server.Name, "invalid", "stdio command is not configured.");
            }

            if (!IsExecutableAvailable(server.Command))
            {
                return new McpConnectionStatus(
                    server.Name,
                    "unavailable",
                    "stdio command executable was not found on PATH.");
            }

            return new McpConnectionStatus(
                server.Name,
                "configured",
                "stdio command is configured; live MCP handshake is not attempted in this diagnostic.");
        }

        if (server.Transport is "sse" or "http")
        {
            return new McpConnectionStatus(
                server.Name,
                "configured",
                "remote transport is configured; live MCP handshake is not attempted in this diagnostic.");
        }

        return new McpConnectionStatus(server.Name, "invalid", "Transport is not supported.");
    }

    private static bool IsExecutableAvailable(string executable)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            return false;
        }

        if (Path.IsPathRooted(executable))
        {
            return File.Exists(executable);
        }

        string? pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathVariable))
        {
            return false;
        }

        foreach (string directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(directory, executable);
            if (File.Exists(candidate))
            {
                return true;
            }

            if (OperatingSystem.IsWindows())
            {
                foreach (string extension in new[] { ".exe", ".cmd", ".bat" })
                {
                    if (File.Exists(candidate + extension))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }
}
