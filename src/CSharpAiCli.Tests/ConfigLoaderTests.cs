using System.Text.Json;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ConfigLoaderTests
{
    [Fact]
    public void Load_uses_default_openai_base_url_when_base_url_is_missing()
    {
        string root = CreateTempDirectory();

        try
        {
            string userProfile = Path.Combine(root, "home");
            string workspaceRoot = Path.Combine(root, "workspace");
            Directory.CreateDirectory(userProfile);
            Directory.CreateDirectory(workspaceRoot);

            WorkspaceContext workspace = WorkspaceContext.Detect(workspaceRoot, root);
            EffectiveConfiguration configuration = ConfigLoader.Load(
                workspace,
                userProfile: userProfile,
                openAiApiKey: "",
                openAiBaseUrl: "");

            Assert.Equal("https://api.openai.com/v1", configuration.BaseUrl);
            Assert.Equal("default", configuration.BaseUrlSource);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CliConfigFile_deserializes_base_url_from_camel_case_json()
    {
        CliConfigFile? config = JsonSerializer.Deserialize<CliConfigFile>(
            """{ "baseUrl": "https://openai.example.test/v1" }""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(config);
        Assert.Equal("https://openai.example.test/v1", config.BaseUrl);
    }

    [Theory]
    [InlineData(" https://gateway.example.test/v1 ", "https://gateway.example.test/v1")]
    [InlineData("http://localhost:8080/v1", "http://localhost:8080/v1")]
    public void TryNormalizeBaseUrl_accepts_absolute_http_and_https_urls(string value, string expected)
    {
        Assert.True(ConfigLoader.TryNormalizeBaseUrl(value, out string? baseUrl));
        Assert.Equal(expected, baseUrl);
    }

    [Theory]
    [InlineData("api.openai.com/v1")]
    [InlineData("/v1")]
    [InlineData("ftp://api.openai.com/v1")]
    [InlineData("   ")]
    public void TryNormalizeBaseUrl_rejects_non_absolute_http_urls(string value)
    {
        Assert.False(ConfigLoader.TryNormalizeBaseUrl(value, out string? baseUrl));
        Assert.Null(baseUrl);
    }

    [Theory]
    [InlineData("https://user:pass@example.test/v1")]
    [InlineData("https://example.test/v1?token=abc")]
    [InlineData("https://example.test/v1#frag")]
    public void TryNormalizeBaseUrl_rejects_urls_with_non_display_safe_parts(string value)
    {
        Assert.False(ConfigLoader.TryNormalizeBaseUrl(value, out string? baseUrl));
        Assert.Null(baseUrl);
    }

    [Theory]
    [InlineData("https://example.test/v1 apiKey: sk-secret")]
    [InlineData("https://example.test/v1\tapiKey: sk-secret")]
    [InlineData("https://example.test/v1\napiKey: sk-secret")]
    [InlineData("https://example.test/v1\r\napiKey: sk-secret")]
    public void TryNormalizeBaseUrl_rejects_urls_with_internal_whitespace_or_control_characters(string value)
    {
        Assert.False(ConfigLoader.TryNormalizeBaseUrl(value, out string? baseUrl));
        Assert.Null(baseUrl);
    }

    [Fact]
    public void Load_loads_base_url_only_workspace_config_and_uses_it_as_effective_base_url()
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
            File.WriteAllText(workspaceConfigPath, """
            {
              "baseUrl": "https://gateway.example.test/v1"
            }
            """);

            WorkspaceContext workspace = WorkspaceContext.Detect(workspaceRoot, root);
            EffectiveConfiguration configuration = ConfigLoader.Load(
                workspace,
                userProfile: userProfile,
                openAiApiKey: "",
                openAiBaseUrl: "");

            Assert.Equal("https://gateway.example.test/v1", configuration.BaseUrl);
            Assert.Equal("workspace config", configuration.BaseUrlSource);
            Assert.Contains(workspaceConfigPath, configuration.LoadedConfigPaths);
            CliConfigFileSource source = Assert.Single(configuration.ConfigSources);
            Assert.Equal("workspace config", source.SourceName);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_uses_base_url_priority_environment_user_workspace_default()
    {
        string root = CreateTempDirectory();

        try
        {
            string userProfile = Path.Combine(root, "home");
            string workspaceRoot = Path.Combine(root, "workspace");
            Directory.CreateDirectory(userProfile);
            Directory.CreateDirectory(workspaceRoot);

            string userConfigPath = Path.Combine(userProfile, ".caicli", "config.json");
            string workspaceConfigPath = Path.Combine(workspaceRoot, ".caicli", "config.json");
            WriteConfig(userConfigPath, model: "", apiKey: "", baseUrl: "https://user.example.test/v1");
            WriteConfig(workspaceConfigPath, model: "", apiKey: "", baseUrl: "https://workspace.example.test/v1");

            WorkspaceContext workspace = WorkspaceContext.Detect(workspaceRoot, root);
            EffectiveConfiguration envConfiguration = ConfigLoader.Load(
                workspace,
                userProfile: userProfile,
                openAiApiKey: "",
                openAiBaseUrl: " https://env.example.test/v1 ");
            EffectiveConfiguration userConfiguration = ConfigLoader.Load(
                workspace,
                userProfile: userProfile,
                openAiApiKey: "",
                openAiBaseUrl: "");
            File.Delete(userConfigPath);
            EffectiveConfiguration workspaceConfiguration = ConfigLoader.Load(
                workspace,
                userProfile: userProfile,
                openAiApiKey: "",
                openAiBaseUrl: "");
            File.Delete(workspaceConfigPath);
            EffectiveConfiguration defaultConfiguration = ConfigLoader.Load(
                workspace,
                userProfile: userProfile,
                openAiApiKey: "",
                openAiBaseUrl: "");

            Assert.Equal("https://env.example.test/v1", envConfiguration.BaseUrl);
            Assert.Equal("OPENAI_BASE_URL", envConfiguration.BaseUrlSource);
            Assert.Equal("https://user.example.test/v1", userConfiguration.BaseUrl);
            Assert.Equal("user config", userConfiguration.BaseUrlSource);
            Assert.Equal("https://workspace.example.test/v1", workspaceConfiguration.BaseUrl);
            Assert.Equal("workspace config", workspaceConfiguration.BaseUrlSource);
            Assert.Equal(ConfigLoader.DefaultOpenAiBaseUrl, defaultConfiguration.BaseUrl);
            Assert.Equal("default", defaultConfiguration.BaseUrlSource);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_warns_for_invalid_environment_base_url_and_falls_back_to_user_config()
    {
        string root = CreateTempDirectory();

        try
        {
            string userProfile = Path.Combine(root, "home");
            string workspaceRoot = Path.Combine(root, "workspace");
            Directory.CreateDirectory(userProfile);
            Directory.CreateDirectory(workspaceRoot);

            WriteConfig(
                Path.Combine(userProfile, ".caicli", "config.json"),
                model: "",
                apiKey: "",
                baseUrl: "https://user.example.test/v1");

            WorkspaceContext workspace = WorkspaceContext.Detect(workspaceRoot, root);
            EffectiveConfiguration configuration = ConfigLoader.Load(
                workspace,
                userProfile: userProfile,
                openAiApiKey: "",
                openAiBaseUrl: "ftp://env.example.test/v1");

            Assert.Equal("https://user.example.test/v1", configuration.BaseUrl);
            Assert.Equal("user config", configuration.BaseUrlSource);
            Assert.Contains(configuration.Warnings, warning =>
                warning.Contains("ignored invalid baseUrl", StringComparison.Ordinal)
                && warning.Contains("OPENAI_BASE_URL", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_does_not_write_api_key_like_values_from_invalid_base_url_to_warnings()
    {
        string root = CreateTempDirectory();

        try
        {
            string userProfile = Path.Combine(root, "home");
            string workspaceRoot = Path.Combine(root, "workspace");
            Directory.CreateDirectory(userProfile);
            Directory.CreateDirectory(workspaceRoot);

            WorkspaceContext workspace = WorkspaceContext.Detect(workspaceRoot, root);
            EffectiveConfiguration configuration = ConfigLoader.Load(
                workspace,
                userProfile: userProfile,
                openAiApiKey: "",
                openAiBaseUrl: "https://env.example.test/v1?api-key=sk-env-secret");

            Assert.Equal(ConfigLoader.DefaultOpenAiBaseUrl, configuration.BaseUrl);
            Assert.Equal("default", configuration.BaseUrlSource);
            Assert.Contains(configuration.Warnings, warning =>
                warning.Contains("ignored invalid baseUrl", StringComparison.Ordinal)
                && warning.Contains("OPENAI_BASE_URL", StringComparison.Ordinal));
            Assert.DoesNotContain("sk-env-secret", string.Join(Environment.NewLine, configuration.Warnings), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_rejects_environment_base_url_with_newline_without_leaking_to_warning_or_report()
    {
        string root = CreateTempDirectory();

        try
        {
            string userProfile = Path.Combine(root, "home");
            string workspaceRoot = Path.Combine(root, "workspace");
            Directory.CreateDirectory(userProfile);
            Directory.CreateDirectory(workspaceRoot);

            string secretLikeBaseUrl = "https://env.example.test/v1\napiKey: sk-env-secret";
            WorkspaceContext workspace = WorkspaceContext.Detect(workspaceRoot, root);
            EffectiveConfiguration configuration = ConfigLoader.Load(
                workspace,
                userProfile: userProfile,
                openAiApiKey: "",
                openAiBaseUrl: secretLikeBaseUrl);
            string report = ConfigReport.Create(CreateSnapshot(workspace, configuration)).ToDisplayText();

            Assert.Equal(ConfigLoader.DefaultOpenAiBaseUrl, configuration.BaseUrl);
            Assert.Equal("default", configuration.BaseUrlSource);
            Assert.Contains(configuration.Warnings, warning =>
                warning.Contains("ignored invalid baseUrl", StringComparison.Ordinal)
                && warning.Contains("OPENAI_BASE_URL", StringComparison.Ordinal));
            Assert.DoesNotContain("sk-env-secret", string.Join(Environment.NewLine, configuration.Warnings), StringComparison.Ordinal);
            Assert.DoesNotContain(secretLikeBaseUrl, report, StringComparison.Ordinal);
            Assert.DoesNotContain("sk-env-secret", report, StringComparison.Ordinal);
            Assert.DoesNotContain("apiKey: sk-env-secret", report, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_warns_for_invalid_user_base_url_and_falls_back_to_workspace_config()
    {
        string root = CreateTempDirectory();

        try
        {
            string userProfile = Path.Combine(root, "home");
            string workspaceRoot = Path.Combine(root, "workspace");
            Directory.CreateDirectory(userProfile);
            Directory.CreateDirectory(workspaceRoot);

            string userConfigPath = Path.Combine(userProfile, ".caicli", "config.json");
            WriteConfig(userConfigPath, model: "", apiKey: "", baseUrl: "api.example.test/v1");
            WriteConfig(
                Path.Combine(workspaceRoot, ".caicli", "config.json"),
                model: "",
                apiKey: "",
                baseUrl: "https://workspace.example.test/v1");

            WorkspaceContext workspace = WorkspaceContext.Detect(workspaceRoot, root);
            EffectiveConfiguration configuration = ConfigLoader.Load(
                workspace,
                userProfile: userProfile,
                openAiApiKey: "",
                openAiBaseUrl: "");

            Assert.Equal("https://workspace.example.test/v1", configuration.BaseUrl);
            Assert.Equal("workspace config", configuration.BaseUrlSource);
            Assert.Contains(userConfigPath, configuration.LoadedConfigPaths);
            Assert.Contains(configuration.Warnings, warning =>
                warning.Contains("ignored invalid baseUrl", StringComparison.Ordinal)
                && warning.Contains("user config", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_rejects_workspace_base_url_with_newline_without_leaking_to_warning_or_report()
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
            File.WriteAllText(workspaceConfigPath, """
            {
              "baseUrl": "https://workspace.example.test/v1\napiKey: sk-workspace-secret"
            }
            """);

            WorkspaceContext workspace = WorkspaceContext.Detect(workspaceRoot, root);
            EffectiveConfiguration configuration = ConfigLoader.Load(
                workspace,
                userProfile: userProfile,
                openAiApiKey: "",
                openAiBaseUrl: "");
            string report = ConfigReport.Create(CreateSnapshot(workspace, configuration)).ToDisplayText();

            Assert.Equal(ConfigLoader.DefaultOpenAiBaseUrl, configuration.BaseUrl);
            Assert.Equal("default", configuration.BaseUrlSource);
            Assert.Contains(workspaceConfigPath, configuration.LoadedConfigPaths);
            Assert.Contains(configuration.Warnings, warning =>
                warning.Contains("ignored invalid baseUrl", StringComparison.Ordinal)
                && warning.Contains("workspace config", StringComparison.Ordinal));
            Assert.DoesNotContain("sk-workspace-secret", string.Join(Environment.NewLine, configuration.Warnings), StringComparison.Ordinal);
            Assert.DoesNotContain("sk-workspace-secret", report, StringComparison.Ordinal);
            Assert.DoesNotContain("apiKey: sk-workspace-secret", report, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_warns_for_invalid_workspace_base_url_and_falls_back_to_default()
    {
        string root = CreateTempDirectory();

        try
        {
            string userProfile = Path.Combine(root, "home");
            string workspaceRoot = Path.Combine(root, "workspace");
            Directory.CreateDirectory(userProfile);
            Directory.CreateDirectory(workspaceRoot);

            string workspaceConfigPath = Path.Combine(workspaceRoot, ".caicli", "config.json");
            WriteConfig(workspaceConfigPath, model: "", apiKey: "", baseUrl: "https://workspace.example.test/v1?token=abc");

            WorkspaceContext workspace = WorkspaceContext.Detect(workspaceRoot, root);
            EffectiveConfiguration configuration = ConfigLoader.Load(
                workspace,
                userProfile: userProfile,
                openAiApiKey: "",
                openAiBaseUrl: "");

            Assert.Equal(ConfigLoader.DefaultOpenAiBaseUrl, configuration.BaseUrl);
            Assert.Equal("default", configuration.BaseUrlSource);
            Assert.Contains(workspaceConfigPath, configuration.LoadedConfigPaths);
            Assert.Contains(configuration.Warnings, warning =>
                warning.Contains("ignored invalid baseUrl", StringComparison.Ordinal)
                && warning.Contains("workspace config", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

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
    public void Load_uses_agent_backend_priority_environment_user_workspace_default()
    {
        string root = CreateTempDirectory();

        try
        {
            string userProfile = Path.Combine(root, "home");
            string workspaceRoot = Path.Combine(root, "workspace");
            Directory.CreateDirectory(userProfile);
            Directory.CreateDirectory(workspaceRoot);
            WriteConfig(
                Path.Combine(userProfile, ".caicli", "config.json"),
                model: "gpt-user",
                apiKey: "sk-user-secret",
                agentBackend: "framework");
            WriteConfig(
                Path.Combine(workspaceRoot, ".caicli", "config.json"),
                model: "gpt-workspace",
                apiKey: "",
                agentBackend: "direct");

            WorkspaceContext workspace = WorkspaceContext.Detect(workspaceRoot, root);
            EffectiveConfiguration envConfiguration = ConfigLoader.Load(
                workspace,
                userProfile: userProfile,
                openAiApiKey: "",
                agentBackend: "maf");
            EffectiveConfiguration userConfiguration = ConfigLoader.Load(
                workspace,
                userProfile: userProfile,
                openAiApiKey: "");

            Assert.Equal("framework", envConfiguration.AgentBackend);
            Assert.Equal("CAICLI_AGENT_BACKEND", envConfiguration.AgentBackendSource);
            Assert.Equal("framework", userConfiguration.AgentBackend);
            Assert.Equal("user config", userConfiguration.AgentBackendSource);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_uses_workspace_agent_backend_when_user_and_environment_are_missing()
    {
        string root = CreateTempDirectory();

        try
        {
            string userProfile = Path.Combine(root, "home");
            string workspaceRoot = Path.Combine(root, "workspace");
            Directory.CreateDirectory(userProfile);
            Directory.CreateDirectory(workspaceRoot);
            WriteConfig(
                Path.Combine(workspaceRoot, ".caicli", "config.json"),
                model: "gpt-workspace",
                apiKey: "",
                agentBackend: "framework");

            WorkspaceContext workspace = WorkspaceContext.Detect(workspaceRoot, root);
            EffectiveConfiguration configuration = ConfigLoader.Load(
                workspace,
                userProfile: userProfile,
                openAiApiKey: "");

            Assert.Equal("framework", configuration.AgentBackend);
            Assert.Equal("workspace config", configuration.AgentBackendSource);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_ignores_invalid_agent_backend_and_defaults_to_direct()
    {
        string root = CreateTempDirectory();

        try
        {
            string userProfile = Path.Combine(root, "home");
            string workspaceRoot = Path.Combine(root, "workspace");
            Directory.CreateDirectory(userProfile);
            Directory.CreateDirectory(workspaceRoot);
            WriteConfig(
                Path.Combine(workspaceRoot, ".caicli", "config.json"),
                model: "",
                apiKey: "",
                agentBackend: "unknown");

            WorkspaceContext workspace = WorkspaceContext.Detect(workspaceRoot, root);
            EffectiveConfiguration configuration = ConfigLoader.Load(
                workspace,
                userProfile: userProfile,
                openAiApiKey: "");

            Assert.Equal("direct", configuration.AgentBackend);
            Assert.Equal("default", configuration.AgentBackendSource);
            Assert.Contains(configuration.Warnings, warning => warning.Contains("ignored invalid agent backend", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_does_not_write_api_key_like_values_from_invalid_agent_backend_to_warnings()
    {
        string root = CreateTempDirectory();

        try
        {
            string userProfile = Path.Combine(root, "home");
            string workspaceRoot = Path.Combine(root, "workspace");
            Directory.CreateDirectory(userProfile);
            Directory.CreateDirectory(workspaceRoot);

            WorkspaceContext workspace = WorkspaceContext.Detect(workspaceRoot, root);
            EffectiveConfiguration configuration = ConfigLoader.Load(
                workspace,
                userProfile: userProfile,
                openAiApiKey: "",
                agentBackend: "sk-env-secret");

            Assert.Equal("direct", configuration.AgentBackend);
            Assert.Equal("default", configuration.AgentBackendSource);
            Assert.Contains(configuration.Warnings, warning =>
                warning.Contains("ignored invalid agent backend", StringComparison.Ordinal)
                && warning.Contains("CAICLI_AGENT_BACKEND", StringComparison.Ordinal));
            Assert.DoesNotContain("sk-env-secret", string.Join(Environment.NewLine, configuration.Warnings), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_preserves_mcp_server_sources_from_config_files()
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
            File.WriteAllText(workspaceConfigPath, """
            {
              "mcpServers": {
                "disabled": {
                  "enabled": false,
                  "transport": "stdio",
                  "command": "mcp-disabled"
                }
              }
            }
            """);

            WorkspaceContext workspace = WorkspaceContext.Detect(workspaceRoot, root);
            EffectiveConfiguration configuration = ConfigLoader.Load(
                workspace,
                userProfile: userProfile,
                openAiApiKey: "");

            CliConfigFileSource source = Assert.Single(configuration.ConfigSources);
            Assert.Equal("workspace config", source.SourceName);
            Assert.NotNull(source.Config.McpServers);
            McpServerConfig server = source.Config.McpServers["disabled"];
            Assert.False(server.Enabled);
            Assert.Equal("stdio", server.Transport);
            Assert.Equal("mcp-disabled", server.Command);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_preserves_workflow_profile_sources_from_config_files()
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
            File.WriteAllText(workspaceConfigPath, """
            {
              "workflowProfiles": {
                "cpp": {
                  "workspacePath": "cpp-root",
                  "validationCommand": "ctest"
                }
              }
            }
            """);

            WorkspaceContext workspace = WorkspaceContext.Detect(workspaceRoot, root);
            EffectiveConfiguration configuration = ConfigLoader.Load(
                workspace,
                userProfile: userProfile,
                openAiApiKey: "");

            CliConfigFileSource source = Assert.Single(configuration.ConfigSources);
            Assert.NotNull(source.Config.WorkflowProfiles);
            WorkflowProfileConfig profile = source.Config.WorkflowProfiles["cpp"];
            Assert.Equal("cpp-root", profile.WorkspacePath);
            Assert.Equal("ctest", profile.ValidationCommand);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_merges_disabled_tools_from_user_and_workspace_config()
    {
        string root = CreateTempDirectory();

        try
        {
            string userProfile = Path.Combine(root, "home");
            string workspaceRoot = Path.Combine(root, "workspace");
            Directory.CreateDirectory(userProfile);
            Directory.CreateDirectory(workspaceRoot);

            string userConfigPath = Path.Combine(userProfile, ".caicli", "config.json");
            Directory.CreateDirectory(Path.GetDirectoryName(userConfigPath)!);
            File.WriteAllText(userConfigPath, """
            {
              "disabledTools": [ "workspace.run_shell", "git.diff" ]
            }
            """);
            string workspaceConfigPath = Path.Combine(workspaceRoot, ".caicli", "config.json");
            Directory.CreateDirectory(Path.GetDirectoryName(workspaceConfigPath)!);
            File.WriteAllText(workspaceConfigPath, """
            {
              "disabledTools": [ "workspace.apply_patch", "workspace.run_shell" ]
            }
            """);

            WorkspaceContext workspace = WorkspaceContext.Detect(workspaceRoot, root);
            EffectiveConfiguration configuration = ConfigLoader.Load(
                workspace,
                userProfile: userProfile,
                openAiApiKey: "");

            Assert.Contains("workspace.run_shell", configuration.DisabledTools);
            Assert.Contains("workspace.apply_patch", configuration.DisabledTools);
            Assert.Contains("git.diff", configuration.DisabledTools);
            Assert.Equal(3, configuration.DisabledTools.Count);
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

    private static void WriteConfig(string path, string model, string apiKey, string? agentBackend = null, string? baseUrl = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string backendLine = agentBackend is null
            ? string.Empty
            : $",{Environment.NewLine}  \"agentBackend\": \"{agentBackend}\"";
        string baseUrlLine = baseUrl is null
            ? string.Empty
            : $",{Environment.NewLine}  \"baseUrl\": \"{baseUrl}\"";
        File.WriteAllText(path, $@"{{
  ""model"": ""{model}"",
  ""apiKey"": ""{apiKey}""{backendLine}{baseUrlLine}
}}");
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "caicli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static CliEnvironmentSnapshot CreateSnapshot(WorkspaceContext workspace, EffectiveConfiguration configuration)
    {
        return new CliEnvironmentSnapshot(
            Workspace: workspace,
            Configuration: configuration,
            DotnetSdkVersion: "9.0.308",
            DotnetRuntime: ".NET 9.0.0",
            TargetFramework: "net9.0",
            HasGlobalJson: false);
    }
}
