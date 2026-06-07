using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class McpConfigurationLoaderTests
{
    [Fact]
    public void Load_returns_empty_when_no_mcp_servers_are_configured()
    {
        EffectiveConfiguration configuration = CreateConfiguration([]);

        McpConfiguration mcp = McpConfigurationLoader.Load(configuration);

        Assert.Empty(mcp.Servers);
    }

    [Fact]
    public void Load_maps_enabled_and_disabled_servers()
    {
        CliConfigFile userConfig = new()
        {
            McpServers = new Dictionary<string, McpServerConfig>
            {
                ["docs"] = new()
                {
                    Enabled = true,
                    Transport = "stdio",
                    Command = "mcp-docs"
                },
                ["disabled"] = new()
                {
                    Enabled = false,
                    Transport = "stdio",
                    Command = "disabled-command"
                }
            }
        };

        McpConfiguration mcp = McpConfigurationLoader.Load(CreateConfiguration(
            [new CliConfigFileSource("user config", "user-config.json", userConfig)]));

        McpServerDefinition docs = Assert.Single(mcp.Servers, server => server.Name == "docs");
        Assert.True(docs.Enabled);
        Assert.Equal("configured", docs.Status);
        Assert.Equal("stdio command: mcp-docs", docs.TransportSummary);
        McpServerDefinition disabled = Assert.Single(mcp.Servers, server => server.Name == "disabled");
        Assert.False(disabled.Enabled);
        Assert.Equal("inactive", disabled.Status);
    }

    [Fact]
    public void Load_workspace_config_overrides_user_server_with_same_name()
    {
        CliConfigFile userConfig = new()
        {
            McpServers = new Dictionary<string, McpServerConfig>
            {
                ["docs"] = new() { Enabled = true, Transport = "stdio", Command = "user-docs" }
            }
        };
        CliConfigFile workspaceConfig = new()
        {
            McpServers = new Dictionary<string, McpServerConfig>
            {
                ["docs"] = new() { Enabled = false, Transport = "stdio", Command = "workspace-docs" }
            }
        };

        McpConfiguration mcp = McpConfigurationLoader.Load(CreateConfiguration(
            [
                new CliConfigFileSource("user config", "user-config.json", userConfig),
                new CliConfigFileSource("workspace config", "workspace-config.json", workspaceConfig)
            ]));

        McpServerDefinition docs = Assert.Single(mcp.Servers);
        Assert.Equal("workspace config", docs.Source);
        Assert.False(docs.Enabled);
        Assert.Equal("inactive", docs.Status);
        Assert.Equal("stdio command: workspace-docs", docs.TransportSummary);
    }

    private static EffectiveConfiguration CreateConfiguration(IReadOnlyList<CliConfigFileSource> sources)
    {
        return new EffectiveConfiguration(
            WorkspaceRoot: "workspace-root",
            UserConfigPath: "user-config.json",
            WorkspaceConfigPath: "workspace-config.json",
            Model: "not configured",
            ModelSource: "default",
            AgentBackend: "direct",
            AgentBackendSource: "default",
            DisabledTools: new HashSet<string>(StringComparer.Ordinal),
            ApiKey: null,
            ApiKeySource: "missing",
            LoadedConfigPaths: [],
            Warnings: [],
            ConfigSources: sources);
    }
}
