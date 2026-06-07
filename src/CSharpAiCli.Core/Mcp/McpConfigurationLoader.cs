namespace CSharpAiCli.Core;

public static class McpConfigurationLoader
{
    public static McpConfiguration Load(EffectiveConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        Dictionary<string, McpServerDefinition> servers = new(StringComparer.Ordinal);
        foreach (CliConfigFileSource source in configuration.ConfigSources)
        {
            if (source.Config.McpServers is null)
            {
                continue;
            }

            foreach ((string name, McpServerConfig serverConfig) in source.Config.McpServers)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                servers[name] = ToDefinition(name, serverConfig, source.SourceName);
            }
        }

        return servers.Count == 0
            ? McpConfiguration.Empty
            : new McpConfiguration(servers.Values.OrderBy(server => server.Name, StringComparer.Ordinal).ToArray());
    }

    private static McpServerDefinition ToDefinition(
        string name,
        McpServerConfig config,
        string source)
    {
        string transport = string.IsNullOrWhiteSpace(config.Transport)
            ? InferTransport(config)
            : config.Transport.Trim().ToLowerInvariant();
        string summary = transport switch
        {
            "stdio" => string.IsNullOrWhiteSpace(config.Command) ? "stdio command: not configured" : $"stdio command: {config.Command}",
            "sse" or "http" => string.IsNullOrWhiteSpace(config.Url) ? $"{transport} url: not configured" : $"{transport} url: {config.Url}",
            _ => $"transport: {transport}"
        };

        bool hasTransportTarget = transport switch
        {
            "stdio" => !string.IsNullOrWhiteSpace(config.Command),
            "sse" or "http" => !string.IsNullOrWhiteSpace(config.Url),
            _ => false
        };

        string status = !config.Enabled
            ? "inactive"
            : hasTransportTarget ? "configured" : "invalid";

        return new McpServerDefinition(
            Name: name,
            Enabled: config.Enabled,
            Status: status,
            TransportSummary: summary,
            Source: source);
    }

    private static string InferTransport(McpServerConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.Command))
        {
            return "stdio";
        }

        if (!string.IsNullOrWhiteSpace(config.Url))
        {
            return "http";
        }

        return "unknown";
    }
}
