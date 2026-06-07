using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class McpListReportTests
{
    [Fact]
    public void Create_prints_none_when_no_servers_are_configured()
    {
        string text = McpListReport.Create(CreateSnapshot([])).ToDisplayText();

        Assert.Contains("C# AI CLI MCP servers", text);
        Assert.Contains("servers: none", text);
    }

    [Fact]
    public void Create_prints_server_status_transport_and_source()
    {
        CliConfigFile config = new()
        {
            McpServers = new Dictionary<string, McpServerConfig>
            {
                ["disabled"] = new()
                {
                    Enabled = false,
                    Transport = "stdio",
                    Command = "mcp-disabled"
                }
            }
        };

        string text = McpListReport.Create(CreateSnapshot(
            [new CliConfigFileSource("workspace config", "workspace-config.json", config)])).ToDisplayText();

        Assert.Contains("server: disabled", text);
        Assert.Contains("status: inactive", text);
        Assert.Contains("enabled: False", text);
        Assert.Contains("source: workspace config", text);
        Assert.Contains("transport: stdio command: mcp-disabled", text);
    }

    private static CliEnvironmentSnapshot CreateSnapshot(IReadOnlyList<CliConfigFileSource> sources)
    {
        WorkspaceContext workspace = new(
            RootPath: "workspace-root",
            ConfigPath: Path.Combine("workspace-root", ".caicli", "config.json"),
            Status: WorkspaceStatus.Ready);
        EffectiveConfiguration configuration = new(
            WorkspaceRoot: workspace.RootPath,
            UserConfigPath: Path.Combine("user-home", ".caicli", "config.json"),
            WorkspaceConfigPath: workspace.ConfigPath,
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

        return new CliEnvironmentSnapshot(
            Workspace: workspace,
            Configuration: configuration,
            DotnetSdkVersion: "9.0.308",
            DotnetRuntime: ".NET 9.0.0",
            TargetFramework: "net9.0",
            HasGlobalJson: false);
    }
}
