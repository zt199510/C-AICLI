using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class McpDoctorReportTests
{
    [Fact]
    public void Create_prints_none_when_no_servers_are_configured()
    {
        string text = McpDoctorReport.Create(CreateSnapshot([])).ToDisplayText();

        Assert.Contains("C# AI CLI MCP doctor", text);
        Assert.Contains("servers: none", text);
    }

    [Fact]
    public void Create_reports_disabled_server_as_inactive()
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

        string text = McpDoctorReport.Create(CreateSnapshot(
            [new CliConfigFileSource("workspace config", "workspace-config.json", config)])).ToDisplayText();

        Assert.Contains("server: disabled", text);
        Assert.Contains("configStatus: inactive", text);
        Assert.Contains("connectionStatus: inactive", text);
        Assert.Contains("Server is disabled.", text);
    }

    [Fact]
    public void Create_reports_missing_stdio_command_as_unavailable()
    {
        CliConfigFile config = new()
        {
            McpServers = new Dictionary<string, McpServerConfig>
            {
                ["missing"] = new()
                {
                    Enabled = true,
                    Transport = "stdio",
                    Command = "definitely-missing-caicli-mcp"
                }
            }
        };

        string text = McpDoctorReport.Create(CreateSnapshot(
            [new CliConfigFileSource("workspace config", "workspace-config.json", config)])).ToDisplayText();

        Assert.Contains("server: missing", text);
        Assert.Contains("connectionStatus: unavailable", text);
        Assert.Contains("stdio command executable was not found on PATH.", text);
        Assert.DoesNotContain("definitely-missing-caicli-mcp", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_reports_remote_transport_as_configured_without_live_handshake()
    {
        CliConfigFile config = new()
        {
            McpServers = new Dictionary<string, McpServerConfig>
            {
                ["remote"] = new()
                {
                    Enabled = true,
                    Transport = "http",
                    Url = "https://example.invalid/mcp"
                }
            }
        };

        string text = McpDoctorReport.Create(CreateSnapshot(
            [new CliConfigFileSource("workspace config", "workspace-config.json", config)])).ToDisplayText();

        Assert.Contains("server: remote", text);
        Assert.Contains("connectionStatus: configured", text);
        Assert.Contains("live MCP handshake is not attempted", text);
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
