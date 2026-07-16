using System.Diagnostics;
using System.Text.Json;
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
    public void Create_does_not_start_disabled_stdio_server()
    {
        using FakeMcpStdioServer server = FakeMcpStdioServer.CreateSuccessful();
        CliConfigFile config = new()
        {
            McpServers = new Dictionary<string, McpServerConfig>
            {
                ["disabled"] = server.CreateConfig(enabled: false, timeoutMilliseconds: 10_000)
            }
        };

        string text = McpDoctorReport.Create(CreateSnapshot(
            [new CliConfigFileSource("workspace config", "workspace-config.json", config)],
            server.WorkspacePath)).ToDisplayText();

        Assert.Contains("server: disabled", text);
        Assert.Contains("connectionStatus: inactive", text);
        Assert.False(server.HasStarted);
    }

    [Fact]
    public void Create_marks_enabled_stdio_server_active_after_initialize_handshake()
    {
        using FakeMcpStdioServer server = FakeMcpStdioServer.CreateSuccessful();
        CliConfigFile config = new()
        {
            McpServers = new Dictionary<string, McpServerConfig>
            {
                ["active"] = server.CreateConfig(timeoutMilliseconds: 10_000)
            }
        };

        string text = McpDoctorReport.Create(CreateSnapshot(
            [new CliConfigFileSource("workspace config", "workspace-config.json", config)],
            server.WorkspacePath)).ToDisplayText();

        Assert.Contains("server: active", text);
        Assert.Contains("configStatus: configured", text);
        Assert.Contains("connectionStatus: active", text);
        Assert.Contains("MCP stdio initialize completed.", text);
        Assert.True(server.WaitForObservationCount(2, TimeSpan.FromSeconds(2)));
        IReadOnlyList<JsonElement> observations = server.ReadObservations();
        Assert.Equal("initialize", observations[0].GetProperty("method").GetString());
        Assert.Equal(McpProtocolClient.ProtocolVersion, observations[0].GetProperty("protocolVersion").GetString());
        Assert.Equal("notifications/initialized", observations[1].GetProperty("method").GetString());
        Assert.False(observations[1].GetProperty("hasId").GetBoolean());
    }

    [Fact]
    public void Create_uses_configured_shell_policy_for_stdio_startup_timeout()
    {
        using FakeMcpStdioServer server = FakeMcpStdioServer.CreateSuccessful();
        CliConfigFile config = new()
        {
            McpServers = new Dictionary<string, McpServerConfig>
            {
                ["blocked-timeout"] = server.CreateConfig(timeoutMilliseconds: 10_000)
            }
        };
        ShellPolicyConfiguration shellPolicy = ShellPolicyConfiguration.Default with
        {
            MaxTimeoutMilliseconds = 1_000,
            MaxTimeoutMillisecondsSource = "workspace config"
        };

        string text = McpDoctorReport.Create(CreateSnapshot(
            [new CliConfigFileSource("workspace config", "workspace-config.json", config)],
            server.WorkspacePath,
            shellPolicy)).ToDisplayText();

        Assert.Contains("server: blocked-timeout", text);
        Assert.Contains("connectionStatus: unavailable", text);
        Assert.Contains("shell policy", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("timeout", text, StringComparison.OrdinalIgnoreCase);
        Assert.False(server.HasStarted);
    }

    [Fact]
    public void Create_reports_invalid_stdio_initialize_response_as_unavailable()
    {
        using FakeMcpStdioServer server = FakeMcpStdioServer.CreateInvalidJson();
        CliConfigFile config = new()
        {
            McpServers = new Dictionary<string, McpServerConfig>
            {
                ["invalid-response"] = server.CreateConfig(timeoutMilliseconds: 10_000)
            }
        };

        string text = McpDoctorReport.Create(CreateSnapshot(
            [new CliConfigFileSource("workspace config", "workspace-config.json", config)],
            server.WorkspacePath)).ToDisplayText();

        Assert.Contains("server: invalid-response", text);
        Assert.Contains("connectionStatus: unavailable", text);
        Assert.Contains("invalid JSON-RPC response", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_reports_stdio_initialize_timeout_as_unavailable()
    {
        using FakeMcpStdioServer server = FakeMcpStdioServer.CreateTimeout();
        CliConfigFile config = new()
        {
            McpServers = new Dictionary<string, McpServerConfig>
            {
                ["timeout"] = server.CreateConfig(timeoutMilliseconds: 200)
            }
        };

        Stopwatch stopwatch = Stopwatch.StartNew();
        string text = McpDoctorReport.Create(CreateSnapshot(
            [new CliConfigFileSource("workspace config", "workspace-config.json", config)],
            server.WorkspacePath)).ToDisplayText();
        stopwatch.Stop();

        Assert.Contains("server: timeout", text);
        Assert.Contains("connectionStatus: unavailable", text);
        Assert.Contains("timed out after 200 ms", text, StringComparison.OrdinalIgnoreCase);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(8), $"Elapsed: {stopwatch.Elapsed}");
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

    private static CliEnvironmentSnapshot CreateSnapshot(
        IReadOnlyList<CliConfigFileSource> sources,
        string? workspaceRoot = null,
        ShellPolicyConfiguration? shellPolicy = null)
    {
        string rootPath = workspaceRoot ?? "workspace-root";
        WorkspaceContext workspace = new(
            RootPath: rootPath,
            ConfigPath: Path.Combine(rootPath, ".caicli", "config.json"),
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
            ConfigSources: sources)
        {
            ShellPolicy = shellPolicy ?? ShellPolicyConfiguration.Default
        };

        return new CliEnvironmentSnapshot(
            Workspace: workspace,
            Configuration: configuration,
            DotnetSdkVersion: "9.0.308",
            DotnetRuntime: ".NET 9.0.0",
            TargetFramework: "net9.0",
            HasGlobalJson: false);
    }

}
