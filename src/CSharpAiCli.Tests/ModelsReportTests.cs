using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ModelsReportTests
{
    [Fact]
    public void Create_formats_current_model_sources_and_static_configuration_examples_without_secret()
    {
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            apiKey: "sk-test-secret",
            apiKeySource: "OPENAI_API_KEY",
            model: "gpt-test",
            modelSource: "OPENAI_MODEL",
            baseUrl: "https://gateway.example.test/v1",
            baseUrlSource: "OPENAI_BASE_URL");

        string text = ModelsReport.Create(snapshot).ToDisplayText();

        Assert.Contains("C# AI CLI models", text);
        Assert.Contains("source: local configuration only", text);
        Assert.Contains("modelListApi: not called", text);
        Assert.Contains("currentModel: gpt-test", text);
        Assert.Contains("currentModelSource: OPENAI_MODEL", text);
        Assert.Contains("baseUrl: https://gateway.example.test/v1", text);
        Assert.Contains("baseUrlSource: OPENAI_BASE_URL", text);
        Assert.Contains("apiKey: present", text);
        Assert.Contains("apiKeySource: OPENAI_API_KEY", text);
        Assert.Contains("recommendedModels:", text);
        Assert.Contains("  - gpt-4.1-mini", text);
        Assert.Contains("  - gpt-4.1", text);
        Assert.Contains("  - gpt-4o-mini", text);
        Assert.Contains("examples:", text);
        Assert.Contains("  OPENAI_MODEL=gpt-4.1-mini", text);
        Assert.Contains("  caicli config set model gpt-4.1-mini", text);
        Assert.Contains("  caicli config set baseUrl https://api.openai.com/v1", text);
        Assert.Contains("  caicli config set baseUrl https://gateway.example.test/v1", text);
        Assert.DoesNotContain("sk-test-secret", text, StringComparison.Ordinal);
    }

    private static CliEnvironmentSnapshot CreateSnapshot(
        string? apiKey,
        string apiKeySource,
        string model,
        string modelSource,
        string baseUrl,
        string baseUrlSource)
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
            DisabledTools: new HashSet<string>(StringComparer.Ordinal),
            ApiKey: SecretValue.From(apiKey),
            ApiKeySource: apiKeySource,
            LoadedConfigPaths: [],
            Warnings: [],
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
