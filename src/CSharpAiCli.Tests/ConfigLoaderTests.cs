using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ConfigLoaderTests
{
    [Fact]
    public void Load_merges_user_workspace_and_environment_with_expected_priority()
    {
        string root = CreateTempDirectory();

        try
        {
            string userProfile = Path.Combine(root, "home");
            string workspaceRoot = Path.Combine(root, "workspace");
            Directory.CreateDirectory(userProfile);
            Directory.CreateDirectory(workspaceRoot);

            WriteConfig(Path.Combine(userProfile, ".caicli", "config.json"), "gpt-user", "sk-user-secret");
            WriteConfig(Path.Combine(workspaceRoot, ".caicli", "config.json"), "gpt-workspace", "sk-workspace-secret");

            WorkspaceContext workspace = WorkspaceContext.Detect(workspaceRoot, root);
            EffectiveConfiguration configuration = ConfigLoader.Load(
                workspace,
                userProfile: userProfile,
                openAiApiKey: "sk-env-secret",
                openAiModel: "gpt-env");

            Assert.Equal("gpt-env", configuration.Model);
            Assert.Equal("OPENAI_MODEL", configuration.ModelSource);
            Assert.True(configuration.HasApiKey);
            Assert.Equal("OPENAI_API_KEY", configuration.ApiKeySource);
            Assert.Equal("sk-env-secret", configuration.ApiKey!.Value);
            Assert.Contains(Path.Combine(userProfile, ".caicli", "config.json"), configuration.LoadedConfigPaths);
            Assert.Contains(Path.Combine(workspaceRoot, ".caicli", "config.json"), configuration.LoadedConfigPaths);
            Assert.DoesNotContain("sk-env-secret", configuration.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("sk-workspace-secret", configuration.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("sk-user-secret", configuration.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_uses_user_api_key_when_environment_is_missing_and_ignores_workspace_api_key()
    {
        string root = CreateTempDirectory();

        try
        {
            string userProfile = Path.Combine(root, "home");
            string workspaceRoot = Path.Combine(root, "workspace");
            Directory.CreateDirectory(userProfile);
            Directory.CreateDirectory(workspaceRoot);

            WriteConfig(Path.Combine(userProfile, ".caicli", "config.json"), "gpt-user", "sk-user-secret");
            WriteConfig(Path.Combine(workspaceRoot, ".caicli", "config.json"), "gpt-workspace", "sk-workspace-secret");

            WorkspaceContext workspace = WorkspaceContext.Detect(workspaceRoot, root);
            EffectiveConfiguration configuration = ConfigLoader.Load(
                workspace,
                userProfile: userProfile,
                openAiApiKey: "");

            Assert.Equal("sk-user-secret", configuration.ApiKey!.Value);
            Assert.Equal("user config", configuration.ApiKeySource);
            Assert.DoesNotContain("sk-workspace-secret", configuration.ToString(), StringComparison.Ordinal);
            Assert.Contains(configuration.Warnings, warning => warning.Contains("ignored workspace config apiKey", StringComparison.Ordinal));
            Assert.DoesNotContain("sk-workspace-secret", string.Join(Environment.NewLine, configuration.Warnings), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_uses_user_model_before_workspace_model_when_environment_model_is_missing()
    {
        string root = CreateTempDirectory();

        try
        {
            string userProfile = Path.Combine(root, "home");
            string workspaceRoot = Path.Combine(root, "workspace");
            Directory.CreateDirectory(userProfile);
            Directory.CreateDirectory(workspaceRoot);

            WriteConfig(Path.Combine(userProfile, ".caicli", "config.json"), "gpt-user", "sk-user-secret");
            WriteConfig(Path.Combine(workspaceRoot, ".caicli", "config.json"), "gpt-workspace", "sk-workspace-secret");

            WorkspaceContext workspace = WorkspaceContext.Detect(workspaceRoot, root);
            EffectiveConfiguration configuration = ConfigLoader.Load(
                workspace,
                userProfile: userProfile,
                openAiApiKey: "");

            Assert.Equal("gpt-user", configuration.Model);
            Assert.Equal("user config", configuration.ModelSource);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_uses_workspace_model_when_environment_and_user_model_are_missing()
    {
        string root = CreateTempDirectory();

        try
        {
            string userProfile = Path.Combine(root, "home");
            string workspaceRoot = Path.Combine(root, "workspace");
            Directory.CreateDirectory(userProfile);
            Directory.CreateDirectory(workspaceRoot);

            WriteConfig(Path.Combine(workspaceRoot, ".caicli", "config.json"), "gpt-workspace", "sk-workspace-secret");

            WorkspaceContext workspace = WorkspaceContext.Detect(workspaceRoot, root);
            EffectiveConfiguration configuration = ConfigLoader.Load(
                workspace,
                userProfile: userProfile,
                openAiApiKey: "");

            Assert.Equal("gpt-workspace", configuration.Model);
            Assert.Equal("workspace config", configuration.ModelSource);
            Assert.False(configuration.HasApiKey);
            Assert.Equal("missing", configuration.ApiKeySource);
            Assert.Contains(configuration.Warnings, warning => warning.Contains("ignored workspace config apiKey", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_uses_user_config_when_workspace_config_is_missing()
    {
        string root = CreateTempDirectory();

        try
        {
            string userProfile = Path.Combine(root, "home");
            string workspaceRoot = Path.Combine(root, "workspace");
            Directory.CreateDirectory(userProfile);
            Directory.CreateDirectory(workspaceRoot);

            WriteConfig(Path.Combine(userProfile, ".caicli", "config.json"), "gpt-user", "sk-user-secret");

            WorkspaceContext workspace = WorkspaceContext.Detect(workspaceRoot, root);
            EffectiveConfiguration configuration = ConfigLoader.Load(
                workspace,
                userProfile: userProfile,
                openAiApiKey: "");

            Assert.Equal("gpt-user", configuration.Model);
            Assert.Equal("user config", configuration.ModelSource);
            Assert.Equal("sk-user-secret", configuration.ApiKey!.Value);
            Assert.Equal("user config", configuration.ApiKeySource);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_ignores_invalid_json_and_records_warning()
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
            File.WriteAllText(workspaceConfigPath, "{ invalid json");

            WorkspaceContext workspace = WorkspaceContext.Detect(workspaceRoot, root);
            EffectiveConfiguration configuration = ConfigLoader.Load(
                workspace,
                userProfile: userProfile,
                openAiApiKey: "");

            Assert.Equal("not configured", configuration.Model);
            Assert.Equal("default", configuration.ModelSource);
            Assert.False(configuration.HasApiKey);
            Assert.Equal("missing", configuration.ApiKeySource);
            Assert.Contains(configuration.Warnings, warning => warning.Contains("ignored invalid config", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_ignores_empty_config_object_and_records_warning()
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
            File.WriteAllText(workspaceConfigPath, "{}");

            WorkspaceContext workspace = WorkspaceContext.Detect(workspaceRoot, root);
            EffectiveConfiguration configuration = ConfigLoader.Load(
                workspace,
                userProfile: userProfile,
                openAiApiKey: "");

            Assert.Equal("not configured", configuration.Model);
            Assert.Equal("default", configuration.ModelSource);
            Assert.False(configuration.HasApiKey);
            Assert.Equal("missing", configuration.ApiKeySource);
            Assert.Contains(configuration.Warnings, warning => warning.Contains("ignored invalid config", StringComparison.Ordinal));
            Assert.DoesNotContain(workspaceConfigPath, configuration.LoadedConfigPaths);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteConfig(string path, string model, string apiKey)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $@"{{
  ""model"": ""{model}"",
  ""apiKey"": ""{apiKey}""
}}");
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "caicli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
