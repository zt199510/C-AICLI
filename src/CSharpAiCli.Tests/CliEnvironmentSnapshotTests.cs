using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class CliEnvironmentSnapshotTests
{
    [Fact]
    public void Create_builds_paths_and_status_without_reading_config_files()
    {
        CliEnvironmentSnapshot snapshot = CliEnvironmentSnapshot.Create(
            currentDirectory: "workspace-root",
            userProfile: "user-home",
            dotnetSdkVersion: "9.0.308",
            dotnetRuntime: ".NET 9.0.0",
            openAiApiKey: "",
            hasGlobalJson: false);

        Assert.Equal("workspace-root", snapshot.CurrentDirectory);
        Assert.Equal(Path.Combine("user-home", ".caicli", "config.json"), snapshot.UserConfigPath);
        Assert.Equal(Path.Combine("workspace-root", ".caicli", "config.json"), snapshot.WorkspaceConfigPath);
        Assert.Equal("9.0.308", snapshot.DotnetSdkVersion);
        Assert.Equal(".NET 9.0.0", snapshot.DotnetRuntime);
        Assert.Equal("net9.0", snapshot.TargetFramework);
        Assert.False(snapshot.HasGlobalJson);
        Assert.False(snapshot.HasOpenAiApiKey);
    }

    [Fact]
    public void Create_marks_api_key_present_without_storing_the_value()
    {
        CliEnvironmentSnapshot snapshot = CliEnvironmentSnapshot.Create(
            currentDirectory: "workspace-root",
            userProfile: "user-home",
            dotnetSdkVersion: "9.0.308",
            dotnetRuntime: ".NET 9.0.0",
            openAiApiKey: "sk-test-secret",
            hasGlobalJson: true);

        Assert.True(snapshot.HasOpenAiApiKey);
        Assert.True(snapshot.HasGlobalJson);
        Assert.DoesNotContain("sk-test-secret", snapshot.ToString(), StringComparison.Ordinal);
    }
}
