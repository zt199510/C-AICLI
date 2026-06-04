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
        Assert.Contains("api key: missing", text);
    }

    [Fact]
    public void Create_never_prints_api_key_value()
    {
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            apiKey: "sk-test-secret",
            apiKeySource: "OPENAI_API_KEY",
            hasGlobalJson: true);

        string text = DoctorReport.Create(snapshot).ToDisplayText();

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

    private static CliEnvironmentSnapshot CreateSnapshot(
        string? apiKey,
        string apiKeySource,
        bool hasGlobalJson = false,
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
            Model: "not configured",
            ModelSource: "default",
            ApiKey: SecretValue.From(apiKey),
            ApiKeySource: apiKeySource,
            LoadedConfigPaths: [],
            Warnings: warnings ?? []);

        return new CliEnvironmentSnapshot(
            Workspace: workspace,
            Configuration: configuration,
            DotnetSdkVersion: "9.0.308",
            DotnetRuntime: ".NET 9.0.0",
            TargetFramework: "net9.0",
            HasGlobalJson: hasGlobalJson);
    }
}
