using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ConfigReportTests
{
    [Fact]
    public void Create_formats_minimal_effective_configuration()
    {
        CliEnvironmentSnapshot snapshot = new(
            CurrentDirectory: "workspace-root",
            UserConfigPath: Path.Combine("user-home", ".caicli", "config.json"),
            WorkspaceConfigPath: Path.Combine("workspace-root", ".caicli", "config.json"),
            DotnetSdkVersion: "9.0.308",
            DotnetRuntime: ".NET 9.0.0",
            TargetFramework: "net9.0",
            HasGlobalJson: false,
            HasOpenAiApiKey: false);

        string text = ConfigReport.Create(snapshot).ToDisplayText();

        Assert.Contains("C# AI CLI effective configuration", text);
        Assert.Contains("workspace: workspace-root", text);
        Assert.Contains("userConfigPath: " + Path.Combine("user-home", ".caicli", "config.json"), text);
        Assert.Contains("workspaceConfigPath: " + Path.Combine("workspace-root", ".caicli", "config.json"), text);
        Assert.Contains("model: not configured", text);
        Assert.Contains("apiKey: missing", text);
    }

    [Fact]
    public void Create_marks_api_key_present_without_printing_value()
    {
        CliEnvironmentSnapshot snapshot = CliEnvironmentSnapshot.Create(
            currentDirectory: "workspace-root",
            userProfile: "user-home",
            dotnetSdkVersion: "9.0.308",
            dotnetRuntime: ".NET 9.0.0",
            openAiApiKey: "sk-test-secret",
            hasGlobalJson: false);

        string text = ConfigReport.Create(snapshot).ToDisplayText();

        Assert.Contains("apiKey: present", text);
        Assert.DoesNotContain("sk-test-secret", text, StringComparison.Ordinal);
    }
}
