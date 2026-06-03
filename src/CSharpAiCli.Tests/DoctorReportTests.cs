using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class DoctorReportTests
{
    [Fact]
    public void Create_formats_runtime_workspace_config_and_key_status()
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

        string text = DoctorReport.Create(snapshot).ToDisplayText();

        Assert.Contains("C# AI CLI doctor", text);
        Assert.Contains("command: caicli", text);
        Assert.Contains("target framework: net9.0", text);
        Assert.Contains("dotnet SDK: 9.0.308", text);
        Assert.Contains("dotnet runtime: .NET 9.0.0", text);
        Assert.Contains("sdk lock: not locked", text);
        Assert.Contains("workspace: workspace-root", text);
        Assert.Contains("user config: " + Path.Combine("user-home", ".caicli", "config.json"), text);
        Assert.Contains("workspace config: " + Path.Combine("workspace-root", ".caicli", "config.json"), text);
        Assert.Contains("api key: missing", text);
    }

    [Fact]
    public void Create_never_prints_api_key_value()
    {
        CliEnvironmentSnapshot snapshot = CliEnvironmentSnapshot.Create(
            currentDirectory: "workspace-root",
            userProfile: "user-home",
            dotnetSdkVersion: "9.0.308",
            dotnetRuntime: ".NET 9.0.0",
            openAiApiKey: "sk-test-secret",
            hasGlobalJson: true);

        string text = DoctorReport.Create(snapshot).ToDisplayText();

        Assert.Contains("api key: present", text);
        Assert.Contains("sdk lock: global.json found", text);
        Assert.DoesNotContain("sk-test-secret", text, StringComparison.Ordinal);
    }
}
