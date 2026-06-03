using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class CliEnvironmentSnapshotTests
{
    [Fact]
    public void Create_builds_workspace_paths_config_paths_and_runtime_status()
    {
        string root = CreateTempDirectory();

        try
        {
            string userProfile = Path.Combine(root, "home");
            string workspaceRoot = Path.Combine(root, "workspace");
            Directory.CreateDirectory(userProfile);
            Directory.CreateDirectory(workspaceRoot);

            CliEnvironmentSnapshot snapshot = CliEnvironmentSnapshot.Create(
                workspacePath: workspaceRoot,
                currentDirectory: root,
                userProfile: userProfile,
                dotnetSdkVersion: "9.0.308",
                dotnetRuntime: ".NET 9.0.0",
                openAiApiKey: "",
                hasGlobalJson: false);

            Assert.Equal(Path.GetFullPath(workspaceRoot), snapshot.CurrentDirectory);
            Assert.Equal(Path.Combine(userProfile, ".caicli", "config.json"), snapshot.UserConfigPath);
            Assert.Equal(Path.Combine(Path.GetFullPath(workspaceRoot), ".caicli", "config.json"), snapshot.WorkspaceConfigPath);
            Assert.Equal(WorkspaceStatus.Ready, snapshot.WorkspaceStatus);
            Assert.False(snapshot.HasOpenAiApiKey);
            Assert.Equal("9.0.308", snapshot.DotnetSdkVersion);
            Assert.Equal(".NET 9.0.0", snapshot.DotnetRuntime);
            Assert.Equal("net9.0", snapshot.TargetFramework);
            Assert.False(snapshot.HasGlobalJson);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Create_loads_effective_configuration_and_redacts_api_key_in_ToString()
    {
        string root = CreateTempDirectory();

        try
        {
            string userProfile = Path.Combine(root, "home");
            string workspaceRoot = Path.Combine(root, "workspace");
            Directory.CreateDirectory(userProfile);
            Directory.CreateDirectory(workspaceRoot);

            string workspaceConfigPath = Path.Combine(workspaceRoot, ".caicli", "config.json");
            Directory.CreateDirectory(Path.GetDirectoryName(workspaceConfigPath)!);
            File.WriteAllText(workspaceConfigPath, @"{
  ""model"": ""gpt-workspace"",
  ""apiKey"": ""sk-workspace-secret""
}");

            CliEnvironmentSnapshot snapshot = CliEnvironmentSnapshot.Create(
                workspacePath: workspaceRoot,
                currentDirectory: root,
                userProfile: userProfile,
                dotnetSdkVersion: "9.0.308",
                dotnetRuntime: ".NET 9.0.0",
                openAiApiKey: "sk-env-secret",
                hasGlobalJson: true);

            Assert.Equal("gpt-workspace", snapshot.Configuration.Model);
            Assert.Equal("workspace config", snapshot.Configuration.ModelSource);
            Assert.True(snapshot.HasOpenAiApiKey);
            Assert.Equal("OPENAI_API_KEY", snapshot.Configuration.ApiKeySource);
            Assert.Equal("sk-env-secret", snapshot.Configuration.ApiKey!.Value);
            Assert.True(snapshot.HasGlobalJson);
            Assert.DoesNotContain("sk-env-secret", snapshot.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("sk-workspace-secret", snapshot.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Create_reports_missing_workspace_and_skips_workspace_config_loading()
    {
        string root = CreateTempDirectory();

        try
        {
            string userProfile = Path.Combine(root, "home");
            string missingWorkspace = Path.Combine(root, "missing-workspace");
            Directory.CreateDirectory(userProfile);

            CliEnvironmentSnapshot snapshot = CliEnvironmentSnapshot.Create(
                workspacePath: missingWorkspace,
                currentDirectory: root,
                userProfile: userProfile,
                dotnetSdkVersion: "9.0.308",
                dotnetRuntime: ".NET 9.0.0",
                openAiApiKey: "",
                hasGlobalJson: false);

            Assert.Equal(Path.GetFullPath(missingWorkspace), snapshot.CurrentDirectory);
            Assert.Equal(WorkspaceStatus.Missing, snapshot.WorkspaceStatus);
            Assert.Equal("not configured", snapshot.Configuration.Model);
            Assert.Empty(snapshot.Configuration.LoadedConfigPaths);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Create_detects_global_json_from_effective_workspace_root()
    {
        string root = CreateTempDirectory();

        try
        {
            string userProfile = Path.Combine(root, "home");
            string workspaceRoot = Path.Combine(root, "workspace");
            Directory.CreateDirectory(userProfile);
            Directory.CreateDirectory(workspaceRoot);
            File.WriteAllText(Path.Combine(root, "global.json"), "{}");

            CliEnvironmentSnapshot currentDirectoryOnlySnapshot = CliEnvironmentSnapshot.Create(
                workspacePath: workspaceRoot,
                currentDirectory: root,
                userProfile: userProfile,
                dotnetSdkVersion: "9.0.308",
                dotnetRuntime: ".NET 9.0.0",
                openAiApiKey: "");

            Assert.False(currentDirectoryOnlySnapshot.HasGlobalJson);

            File.WriteAllText(Path.Combine(workspaceRoot, "global.json"), "{}");

            CliEnvironmentSnapshot workspaceSnapshot = CliEnvironmentSnapshot.Create(
                workspacePath: workspaceRoot,
                currentDirectory: root,
                userProfile: userProfile,
                dotnetSdkVersion: "9.0.308",
                dotnetRuntime: ".NET 9.0.0",
                openAiApiKey: "");

            Assert.True(workspaceSnapshot.HasGlobalJson);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "caicli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
