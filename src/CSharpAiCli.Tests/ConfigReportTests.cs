using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ConfigReportTests
{
    [Fact]
    public void Create_formats_effective_configuration_with_sources()
    {
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            apiKey: null,
            apiKeySource: "missing",
            model: "gpt-workspace",
            modelSource: "workspace config",
            baseUrl: "https://gateway.example.test/v1",
            baseUrlSource: "workspace config");

        string text = ConfigReport.Create(snapshot).ToDisplayText();

        Assert.Contains("C# AI CLI effective configuration", text);
        Assert.Contains("workspace: workspace-root", text);
        Assert.Contains("workspaceStatus: ready", text);
        Assert.Contains("userConfigPath: " + Path.Combine("user-home", ".caicli", "config.json"), text);
        Assert.Contains("workspaceConfigPath: " + Path.Combine("workspace-root", ".caicli", "config.json"), text);
        Assert.Contains("logDirectory: " + Path.Combine("workspace-root", ".caicli", "logs"), text);
        Assert.Contains("model: gpt-workspace", text);
        Assert.Contains("modelSource: workspace config", text);
        Assert.Contains("baseUrl: https://gateway.example.test/v1", text);
        Assert.Contains("baseUrlSource: workspace config", text);
        Assert.Contains("agentBackend: direct", text);
        Assert.Contains("agentBackendSource: default", text);
        Assert.Contains("approvalMode: on-request", text);
        Assert.Contains("approvalModeSource: default", text);
        Assert.Contains("apiKey: missing", text);
        Assert.Contains("apiKeySource: missing", text);
        Assert.Contains("loadedConfigPaths: none", text);
    }

    [Fact]
    public void Create_marks_api_key_present_without_printing_value()
    {
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            apiKey: "sk-test-secret",
            apiKeySource: "OPENAI_API_KEY",
            baseUrl: "https://gateway.example.test/v1",
            baseUrlSource: "OPENAI_BASE_URL");

        string text = ConfigReport.Create(snapshot).ToDisplayText();

        Assert.Contains("baseUrl: https://gateway.example.test/v1", text);
        Assert.Contains("baseUrlSource: OPENAI_BASE_URL", text);
        Assert.Contains("apiKey: present", text);
        Assert.Contains("apiKeySource: OPENAI_API_KEY", text);
        Assert.DoesNotContain("sk-test-secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_prints_disabled_tools()
    {
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            apiKey: null,
            apiKeySource: "missing",
            disabledTools: new HashSet<string>(StringComparer.Ordinal)
            {
                "workspace.run_shell",
                "workspace.apply_patch"
            });

        string text = ConfigReport.Create(snapshot).ToDisplayText();

        Assert.Contains("disabledTools: workspace.apply_patch, workspace.run_shell", text);
    }

    [Fact]
    public void Create_prints_loaded_config_paths_and_warnings()
    {
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            apiKey: null,
            apiKeySource: "missing",
            loadedConfigPaths: ["user-config.json", "workspace-config.json"],
            warnings: ["ignored invalid config: broken-config.json"]);

        string text = ConfigReport.Create(snapshot).ToDisplayText();

        Assert.Contains("loadedConfigPaths: user-config.json; workspace-config.json", text);
        Assert.Contains("configWarning: ignored invalid config: broken-config.json", text);
    }

    [Fact]
    public void Create_prints_instruction_warnings_without_instruction_content()
    {
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            apiKey: null,
            apiKeySource: "missing")
            with
            {
                Instructions = InstructionLoadResult.Empty(["ignored instruction file over 4 bytes: workspace-root/AICLI.md"])
            };

        string text = ConfigReport.Create(snapshot).ToDisplayText();

        Assert.Contains("instructionWarning: ignored instruction file over 4 bytes", text);
    }

    private static CliEnvironmentSnapshot CreateSnapshot(
        string? apiKey,
        string apiKeySource,
        string model = "not configured",
        string modelSource = "default",
        string baseUrl = "https://api.openai.com/v1",
        string baseUrlSource = "default",
        IReadOnlyList<string>? loadedConfigPaths = null,
        IReadOnlyList<string>? warnings = null,
        IReadOnlySet<string>? disabledTools = null)
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
            AgentBackend: "direct",
            AgentBackendSource: "default",
            DisabledTools: disabledTools ?? new HashSet<string>(StringComparer.Ordinal),
            ApiKey: SecretValue.From(apiKey),
            ApiKeySource: apiKeySource,
            LoadedConfigPaths: loadedConfigPaths ?? [],
            Warnings: warnings ?? [],
            ConfigSources: [])
        {
            BaseUrl = baseUrl,
            BaseUrlSource = baseUrlSource
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
