namespace CSharpAiCli.Core;

public sealed record McpDoctorReport(IReadOnlyList<string> Lines)
{
    public static McpDoctorReport Create(
        CliEnvironmentSnapshot snapshot,
        IMcpConnectionManager? connectionManager = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        connectionManager ??= new DefaultMcpConnectionManager();

        McpConfiguration configuration = McpConfigurationLoader.Load(snapshot.Configuration);
        List<string> lines =
        [
            $"{ProductInfo.DisplayName} MCP doctor"
        ];

        if (configuration.Servers.Count == 0)
        {
            lines.Add("servers: none");
            return new McpDoctorReport(lines);
        }

        foreach (McpServerDefinition server in configuration.Servers)
        {
            McpConnectionStatus status = connectionManager.Check(server);
            lines.Add($"server: {server.Name}");
            lines.Add($"  configStatus: {server.Status}");
            lines.Add($"  connectionStatus: {status.Status}");
            lines.Add($"  message: {status.SafeMessage}");
        }

        return new McpDoctorReport(lines);
    }

    public string ToDisplayText()
    {
        return string.Join(Environment.NewLine, Lines);
    }
}
