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
        using TempDirectory temp = TempDirectory.Create();
        string markerPath = Path.Combine(temp.Path, "disabled-started.txt");
        string scriptPath = WritePowerShellScript(
            temp.Path,
            $$"""
            [System.IO.File]::WriteAllText({{ToPowerShellStringLiteral(markerPath)}}, 'started')
            """);
        CliConfigFile config = new()
        {
            McpServers = new Dictionary<string, McpServerConfig>
            {
                ["disabled"] = new()
                {
                    Enabled = false,
                    Transport = "stdio",
                    Command = PowerShellExecutable,
                    Args = CreatePowerShellScriptArgs(scriptPath),
                    TimeoutMilliseconds = 10_000
                }
            }
        };

        string text = McpDoctorReport.Create(CreateSnapshot(
            [new CliConfigFileSource("workspace config", "workspace-config.json", config)],
            temp.Path)).ToDisplayText();

        Assert.Contains("server: disabled", text);
        Assert.Contains("connectionStatus: inactive", text);
        Assert.False(File.Exists(markerPath));
    }

    [Fact]
    public void Create_marks_enabled_stdio_server_active_after_initialize_handshake()
    {
        using TempDirectory temp = TempDirectory.Create();
        string markerPath = Path.Combine(temp.Path, "doctor-handshake-observed.json");
        string scriptPath = WritePowerShellScript(
            temp.Path,
            $$"""
            $requestLine = [Console]::In.ReadLine()
            $request = $requestLine | ConvertFrom-Json
            $response = [ordered]@{
                jsonrpc = '2.0'
                id = $request.id
                result = [ordered]@{
                    protocolVersion = '2025-03-26'
                    capabilities = [ordered]@{}
                    serverInfo = [ordered]@{
                        name = 'doctor-fixture'
                        version = '1.0.0'
                    }
                }
            } | ConvertTo-Json -Compress -Depth 10
            [Console]::Out.WriteLine($response)

            $notificationLine = [Console]::In.ReadLine()
            $notification = $notificationLine | ConvertFrom-Json
            $observation = [ordered]@{
                requestMethod = $request.method
                protocolVersion = $request.params.protocolVersion
                notificationMethod = $notification.method
            } | ConvertTo-Json -Compress -Depth 5
            [System.IO.File]::WriteAllText({{ToPowerShellStringLiteral(markerPath)}}, $observation)
            Start-Sleep -Milliseconds 100
            """);
        CliConfigFile config = new()
        {
            McpServers = new Dictionary<string, McpServerConfig>
            {
                ["active"] = CreateStdioServerConfig(scriptPath, timeoutMilliseconds: 10_000)
            }
        };

        string text = McpDoctorReport.Create(CreateSnapshot(
            [new CliConfigFileSource("workspace config", "workspace-config.json", config)],
            temp.Path)).ToDisplayText();

        Assert.Contains("server: active", text);
        Assert.Contains("configStatus: configured", text);
        Assert.Contains("connectionStatus: active", text);
        Assert.Contains("MCP stdio initialize completed.", text);
        Assert.True(WaitForFile(markerPath, TimeSpan.FromSeconds(2)));
        using JsonDocument observation = JsonDocument.Parse(File.ReadAllText(markerPath));
        JsonElement root = observation.RootElement;
        Assert.Equal("initialize", root.GetProperty("requestMethod").GetString());
        Assert.Equal(McpProtocolClient.ProtocolVersion, root.GetProperty("protocolVersion").GetString());
        Assert.Equal("notifications/initialized", root.GetProperty("notificationMethod").GetString());
    }

    [Fact]
    public void Create_reports_invalid_stdio_initialize_response_as_unavailable()
    {
        using TempDirectory temp = TempDirectory.Create();
        string scriptPath = WritePowerShellScript(
            temp.Path,
            """
            $null = [Console]::In.ReadLine()
            [Console]::Out.WriteLine('not-json')
            Start-Sleep -Milliseconds 200
            """);
        CliConfigFile config = new()
        {
            McpServers = new Dictionary<string, McpServerConfig>
            {
                ["invalid-response"] = CreateStdioServerConfig(scriptPath, timeoutMilliseconds: 10_000)
            }
        };

        string text = McpDoctorReport.Create(CreateSnapshot(
            [new CliConfigFileSource("workspace config", "workspace-config.json", config)],
            temp.Path)).ToDisplayText();

        Assert.Contains("server: invalid-response", text);
        Assert.Contains("connectionStatus: unavailable", text);
        Assert.Contains("invalid JSON-RPC response", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_reports_stdio_initialize_timeout_as_unavailable()
    {
        using TempDirectory temp = TempDirectory.Create();
        string scriptPath = WritePowerShellScript(
            temp.Path,
            """
            $null = [Console]::In.ReadLine()
            Start-Sleep -Seconds 5
            """);
        CliConfigFile config = new()
        {
            McpServers = new Dictionary<string, McpServerConfig>
            {
                ["timeout"] = CreateStdioServerConfig(scriptPath, timeoutMilliseconds: 200)
            }
        };

        string text = McpDoctorReport.Create(CreateSnapshot(
            [new CliConfigFileSource("workspace config", "workspace-config.json", config)],
            temp.Path)).ToDisplayText();

        Assert.Contains("server: timeout", text);
        Assert.Contains("connectionStatus: unavailable", text);
        Assert.Contains("timed out after 200 ms", text, StringComparison.OrdinalIgnoreCase);
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
        string? workspaceRoot = null)
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
            ConfigSources: sources);

        return new CliEnvironmentSnapshot(
            Workspace: workspace,
            Configuration: configuration,
            DotnetSdkVersion: "9.0.308",
            DotnetRuntime: ".NET 9.0.0",
            TargetFramework: "net9.0",
            HasGlobalJson: false);
    }

    private static McpServerConfig CreateStdioServerConfig(string scriptPath, int timeoutMilliseconds)
    {
        return new McpServerConfig
        {
            Enabled = true,
            Transport = "stdio",
            Command = PowerShellExecutable,
            Args = CreatePowerShellScriptArgs(scriptPath),
            TimeoutMilliseconds = timeoutMilliseconds
        };
    }

    private static string[] CreatePowerShellScriptArgs(string scriptPath)
    {
        return
        [
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            scriptPath
        ];
    }

    private static string WritePowerShellScript(string directory, string script)
    {
        string scriptPath = Path.Combine(directory, "mcp-doctor-fixture-" + Guid.NewGuid().ToString("N") + ".ps1");
        File.WriteAllText(scriptPath, script);
        return scriptPath;
    }

    private static string ToPowerShellStringLiteral(string value)
    {
        return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    private static string PowerShellExecutable => OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh";

    private static bool WaitForFile(string path, TimeSpan timeout)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (File.Exists(path))
            {
                return true;
            }

            Thread.Sleep(25);
        }

        return File.Exists(path);
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempDirectory Create()
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "caicli-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
