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
            modelSource: "workspace config");

        string text = ConfigReport.Create(snapshot).ToDisplayText();

        Assert.Contains("C# AI CLI effective configuration", text);
        Assert.Contains("workspace: workspace-root", text);
        Assert.Contains("workspaceStatus: ready", text);
        Assert.Contains("userConfigPath: " + Path.Combine("user-home", ".caicli", "config.json"), text);
        Assert.Contains("workspaceConfigPath: " + Path.Combine("workspace-root", ".caicli", "config.json"), text);
        Assert.Contains("logDirectory: " + Path.Combine("workspace-root", ".caicli", "logs"), text);
        Assert.Contains("model: gpt-workspace", text);
        Assert.Contains("modelSource: workspace config", text);
        Assert.Contains("apiKey: missing", text);
        Assert.Contains("apiKeySource: missing", text);
        Assert.Contains("loadedConfigPaths: none", text);
    }

    [Fact]
    public void Create_marks_api_key_present_without_printing_value()
    {
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            apiKey: "sk-test-secret",
            apiKeySource: "OPENAI_API_KEY");

        string text = ConfigReport.Create(snapshot).ToDisplayText();

        Assert.Contains("apiKey: present", text);
        Assert.Contains("apiKeySource: OPENAI_API_KEY", text);
        Assert.DoesNotContain("sk-test-secret", text, StringComparison.Ordinal);
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

    private static CliEnvironmentSnapshot CreateSnapshot(
        string? apiKey,
        string apiKeySource,
        string model = "not configured",
        string modelSource = "default",
        IReadOnlyList<string>? loadedConfigPaths = null,
        IReadOnlyList<string>? warnings = null)
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
            ApiKey: SecretValue.From(apiKey),
            ApiKeySource: apiKeySource,
            LoadedConfigPaths: loadedConfigPaths ?? [],
            Warnings: warnings ?? []);

        return new CliEnvironmentSnapshot(
            Workspace: workspace,
            Configuration: configuration,
            DotnetSdkVersion: "9.0.308",
            DotnetRuntime: ".NET 9.0.0",
            TargetFramework: "net9.0",
            HasGlobalJson: false);
    }
}
