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
        string? command = NormalizeOptional(config.Command);
        string? url = NormalizeOptional(config.Url);
        string? cwd = NormalizeOptional(config.Cwd);
        IReadOnlyList<string> args = NormalizeArgs(config.Args);
        string transport = string.IsNullOrWhiteSpace(config.Transport)
            ? InferTransport(config)
            : config.Transport.Trim().ToLowerInvariant();
        string summary = transport switch
        {
            "stdio" => command is null ? "stdio command: not configured" : $"stdio command: {command}",
            "sse" or "http" => url is null ? $"{transport} url: not configured" : $"{transport} url: {url}",
            _ => $"transport: {transport}"
        };

        bool hasTransportTarget = transport switch
        {
            "stdio" => command is not null,
            "sse" or "http" => url is not null,
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
            Source: source)
        {
            Transport = transport,
            Command = command,
            Args = args,
            Cwd = cwd,
            TimeoutMilliseconds = config.TimeoutMilliseconds,
            Url = url
        };
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

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static IReadOnlyList<string> NormalizeArgs(string[]? args)
    {
        if (args is null || args.Length == 0)
        {
            return [];
        }

        return args.Where(argument => argument is not null).ToArray();
    }
}
