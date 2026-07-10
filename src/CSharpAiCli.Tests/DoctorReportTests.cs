using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class DoctorReportTests
{
    [Fact]
    public void Create_formats_runtime_workspace_config_and_key_status()
    {
        CliEnvironmentSnapshot snapshot = CreateSnapshot(apiKey: null, apiKeySource: "missing");

        string text = DoctorReport.Create(snapshot).ToDisplayText();

        Assert.Contains("C# AI CLI doctor", text);
        Assert.Contains("command: caicli", text);
        Assert.Contains("target framework: net9.0", text);
        Assert.Contains("dotnet SDK: 9.0.308", text);
        Assert.Contains("dotnet runtime: .NET 9.0.0", text);
        Assert.Contains("sdk lock: not locked", text);
        Assert.Contains("workspace: workspace-root", text);
        Assert.Contains("workspace status: ready", text);
        Assert.Contains("user config: " + Path.Combine("user-home", ".caicli", "config.json"), text);
        Assert.Contains("workspace config: " + Path.Combine("workspace-root", ".caicli", "config.json"), text);
        Assert.Contains("log directory: " + Path.Combine("workspace-root", ".caicli", "logs"), text);
        Assert.Contains("model: not configured (default)", text);
        Assert.Contains("base URL: https://api.openai.com/v1 (default)", text);
        Assert.Contains("api key: missing (missing)", text);
        Assert.Contains("agent backend: direct (default)", text);
        Assert.Contains("approval mode: on-request (default)", text);
        Assert.Contains("agent backend status: available", text);
    }

    [Fact]
    public void Create_includes_default_shell_patch_and_mcp_policy_diagnostics()
    {
        CliEnvironmentSnapshot snapshot = CreateSnapshot(apiKey: null, apiKeySource: "missing");

        string text = DoctorReport.Create(snapshot).ToDisplayText();

        Assert.Contains("shell policy allowed commands configured: false (default)", text);
        Assert.Contains("shell policy allowed commands: []", text);
        Assert.Contains("shell policy denied commands: []", text);
        Assert.Contains("shell policy max timeout milliseconds: none (default)", text);
        Assert.Contains("shell policy dangerous command detector: enabled", text);
        Assert.Contains("patch policy tool status: enabled", text);
        Assert.Contains("patch policy approval: required for write operations", text);
        Assert.Contains("patch policy dry-run preview: enabled", text);
        Assert.Contains("patch policy dirty workspace reporting: enabled", text);
        Assert.Contains("mcp execution policy startup risk check: enabled for stdio commands", text);
        Assert.Contains("mcp execution policy servers: 0 configured, 0 enabled", text);
        Assert.Contains("mcp execution policy stdio servers: 0 configured, 0 enabled", text);
    }

    [Fact]
    public void Create_prints_configured_shell_policy_with_json_escaped_command_lists()
    {
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            apiKey: null,
            apiKeySource: "missing",
            shellPolicy: new ShellPolicyConfiguration(
                AllowedCommands: ["git status", "line-one\nshell policy denied commands: injected"],
                AllowedCommandsConfigured: true,
                AllowedCommandsSource: "workspace config",
                DeniedCommands: ["rm -rf", "powershell,encoded"],
                MaxTimeoutMilliseconds: 1234,
                MaxTimeoutMillisecondsSource: "user config"));

        string text = DoctorReport.Create(snapshot).ToDisplayText();

        Assert.Contains("shell policy allowed commands configured: true (workspace config)", text);
        Assert.Contains("""shell policy allowed commands: ["git status","line-one\nshell policy denied commands: injected"]""", text);
        Assert.Contains("""shell policy denied commands: ["rm -rf","powershell,encoded"]""", text);
        Assert.Contains("shell policy max timeout milliseconds: 1234 (user config)", text);
        Assert.DoesNotContain($"{Environment.NewLine}shell policy denied commands: injected", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_marks_patch_policy_tool_status_disabled_when_apply_patch_tool_is_disabled()
    {
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            apiKey: null,
            apiKeySource: "missing",
            disabledTools: new HashSet<string>(["workspace.apply_patch"], StringComparer.Ordinal));

        string text = DoctorReport.Create(snapshot).ToDisplayText();

        Assert.Contains("patch policy tool status: disabled", text);
    }

    [Fact]
    public void Create_prints_mcp_execution_policy_server_counts()
    {
        CliConfigFile config = new()
        {
            McpServers = new Dictionary<string, McpServerConfig>
            {
                ["stdio-active"] = new() { Enabled = true, Transport = "stdio", Command = "active-command" },
                ["stdio-disabled"] = new() { Enabled = false, Transport = "stdio", Command = "disabled-command" },
                ["http-active"] = new() { Enabled = true, Transport = "http", Url = "https://mcp.example.test" }
            }
        };
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            apiKey: null,
            apiKeySource: "missing",
            configSources: [new CliConfigFileSource("workspace config", "workspace-config.json", config)]);

        string text = DoctorReport.Create(snapshot).ToDisplayText();

        Assert.Contains("mcp execution policy servers: 3 configured, 2 enabled", text);
        Assert.Contains("mcp execution policy stdio servers: 2 configured, 1 enabled", text);
    }

    [Fact]
    public void Create_separates_configured_stdio_counts_from_trusted_execution_eligible_servers()
    {
        CliConfigFile userConfig = new()
        {
            McpServers = new Dictionary<string, McpServerConfig>
            {
                ["user-stdio"] = new() { Enabled = true, Transport = "stdio", Command = "user-command" }
            }
        };
        CliConfigFile workspaceConfig = new()
        {
            McpServers = new Dictionary<string, McpServerConfig>
            {
                ["workspace-stdio"] = new() { Enabled = true, Transport = "stdio", Command = "workspace-command" }
            }
        };
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            apiKey: null,
            apiKeySource: "missing",
            configSources:
            [
                new CliConfigFileSource("user config", "user-config.json", userConfig),
                new CliConfigFileSource("workspace config", "workspace-config.json", workspaceConfig)
            ]);

        string text = DoctorReport.Create(snapshot).ToDisplayText();

        Assert.Contains("mcp execution policy servers: 2 configured, 2 enabled", text);
        Assert.Contains("mcp execution policy stdio servers: 2 configured, 2 enabled", text);
        Assert.Contains("mcp execution policy trusted stdio servers: 1 user-config enabled", text);
        Assert.Contains("mcp execution policy registry source: user config only", text);
        Assert.DoesNotContain("mcp execution policy trusted stdio servers: 2", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_explains_framework_backend_unavailable()
    {
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            apiKey: null,
            apiKeySource: "missing",
            agentBackend: "framework",
            agentBackendSource: "workspace config");

        string text = DoctorReport.Create(snapshot).ToDisplayText();

        Assert.Contains("agent backend: framework (workspace config)", text);
        Assert.Contains("agent backend status: unavailable: Microsoft Agent Framework adapter is an experimental stub", text);
    }

    [Fact]
    public void Create_never_prints_api_key_value()
    {
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            apiKey: "sk-test-secret",
            apiKeySource: "OPENAI_API_KEY",
            model: "gpt-test",
            modelSource: "OPENAI_MODEL",
            baseUrl: "https://gateway.example.test/v1",
            baseUrlSource: "OPENAI_BASE_URL",
            hasGlobalJson: true);

        string text = DoctorReport.Create(snapshot).ToDisplayText();

        Assert.Contains("model: gpt-test (OPENAI_MODEL)", text);
        Assert.Contains("base URL: https://gateway.example.test/v1 (OPENAI_BASE_URL)", text);
        Assert.Contains("api key: present (OPENAI_API_KEY)", text);
        Assert.Contains("sdk lock: global.json found", text);
        Assert.DoesNotContain("sk-test-secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_prints_config_warnings()
    {
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            apiKey: null,
            apiKeySource: "missing",
            warnings: ["ignored invalid config: " + Path.Combine("workspace-root", ".caicli", "config.json")]);

        string text = DoctorReport.Create(snapshot).ToDisplayText();

        Assert.Contains("config warning: ignored invalid config", text);
    }

    [Fact]
    public void Create_prints_instruction_warnings()
    {
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            apiKey: null,
            apiKeySource: "missing")
            with
            {
                Instructions = InstructionLoadResult.Empty(["ignored instruction file over 4 bytes: workspace-root/AICLI.md"])
            };

        string text = DoctorReport.Create(snapshot).ToDisplayText();

        Assert.Contains("instruction warning: ignored instruction file over 4 bytes", text);
    }

    [Fact]
    public void Create_prints_no_instruction_sources_when_none_are_loaded()
    {
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            apiKey: null,
            apiKeySource: "missing");

        string text = DoctorReport.Create(snapshot).ToDisplayText();

        Assert.Contains("instruction sources: none", text);
    }

    [Fact]
    public void Create_prints_instruction_source_paths_in_order_without_instruction_content()
    {
        string rootInstructionPath = Path.Combine("workspace-root", "AGENTS.md");
        string nestedInstructionPath = Path.Combine("workspace-root", "src", "AGENTS.md");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            apiKey: null,
            apiKeySource: "missing")
            with
            {
                Instructions = InstructionLoadResult.Loaded(
                    "Root guidance.\n\nNested secret instruction: sk-instruction-secret",
                    [
                        new InstructionSource(rootInstructionPath, 12),
                        new InstructionSource(nestedInstructionPath, 99)
                    ])
            };

        string text = DoctorReport.Create(snapshot).ToDisplayText();

        Assert.Contains($"instruction source: 0: {rootInstructionPath}", text);
        Assert.Contains($"instruction source: 1: {nestedInstructionPath}", text);
        Assert.DoesNotContain("instruction sources: none", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Root guidance.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-instruction-secret", text, StringComparison.Ordinal);
    }

    private static CliEnvironmentSnapshot CreateSnapshot(
        string? apiKey,
        string apiKeySource,
        bool hasGlobalJson = false,
        IReadOnlyList<string>? warnings = null,
        string model = "not configured",
        string modelSource = "default",
        string baseUrl = "https://api.openai.com/v1",
        string baseUrlSource = "default",
        string agentBackend = "direct",
        string agentBackendSource = "default",
        IReadOnlySet<string>? disabledTools = null,
        ShellPolicyConfiguration? shellPolicy = null,
        IReadOnlyList<CliConfigFileSource>? configSources = null)
    {
        WorkspaceContext workspace = new(
            RootPath: "workspace-root",
            ConfigPath: Path.Combine("workspace-root", ".caicli", "config.json"),
            Status: WorkspaceStatus.Ready);

        EffectiveConfiguration configuration = new(
            WorkspaceRoot: "workspace-root",
            UserConfigPath: Path.Combine("user-home", ".caicli", "config.json"),
            WorkspaceConfigPath: Path.Combine("workspace-root", ".caicli", "config.json"),
            Model: model,
            ModelSource: modelSource,
            AgentBackend: agentBackend,
            AgentBackendSource: agentBackendSource,
            DisabledTools: disabledTools ?? new HashSet<string>(StringComparer.Ordinal),
            ApiKey: SecretValue.From(apiKey),
            ApiKeySource: apiKeySource,
            LoadedConfigPaths: [],
            Warnings: warnings ?? [],
            ConfigSources: configSources ?? [])
        {
            BaseUrl = baseUrl,
            BaseUrlSource = baseUrlSource,
            ShellPolicy = shellPolicy ?? ShellPolicyConfiguration.Default
        };

        return new CliEnvironmentSnapshot(
            Workspace: workspace,
            Configuration: configuration,
            DotnetSdkVersion: "9.0.308",
            DotnetRuntime: ".NET 9.0.0",
            TargetFramework: "net9.0",
            HasGlobalJson: hasGlobalJson);
    }
}
