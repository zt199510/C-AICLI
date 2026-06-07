namespace CSharpAiCli.Core;

public sealed record McpListReport(IReadOnlyList<string> Lines)
{
    public static McpListReport Create(CliEnvironmentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        McpConfiguration configuration = McpConfigurationLoader.Load(snapshot.Configuration);
        List<string> lines =
        [
            $"{ProductInfo.DisplayName} MCP servers"
        ];

        if (configuration.Servers.Count == 0)
        {
            lines.Add("servers: none");
            return new McpListReport(lines);
        }

        foreach (McpServerDefinition server in configuration.Servers)
        {
            lines.Add($"server: {server.Name}");
            lines.Add($"  status: {server.Status}");
            lines.Add($"  enabled: {server.Enabled}");
            lines.Add($"  source: {server.Source}");
            lines.Add($"  transport: {server.TransportSummary}");
        }

        return new McpListReport(lines);
    }

    public string ToDisplayText()
    {
        return string.Join(Environment.NewLine, Lines);
    }
}
