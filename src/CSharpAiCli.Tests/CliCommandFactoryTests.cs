using System.Diagnostics;
using System.CommandLine;
using System.Text.Json.Nodes;
using CSharpAiCli.Cli;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class CliCommandFactoryTests
{
    [Fact]
    public void Doctor_command_writes_doctor_report()
    {
        using StringWriter output = new();

        int exitCode = CliCommandFactory
            .Create(output, CreateSnapshot)
            .Parse(["doctor"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Contains("C# AI CLI doctor", output.ToString());
        Assert.Contains("api key: missing", output.ToString());
    }

    [Fact]
    public void Status_command_writes_status_report_for_non_git_workspace()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();

        int exitCode = CliCommandFactory
            .Create(output, workspacePath => CreateSnapshot(workspacePath))
            .Parse(["status", "--workspace", temp.Path])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("C# AI CLI status", text);
        Assert.Contains($"workspace: {temp.Path}", text);
        Assert.Contains("workspaceStatus: ready", text);
        Assert.Contains("gitStatus: not a git repository", text);
        Assert.Contains("configurationStatus: incomplete", text);
        Assert.Contains("model: not configured (default)", text);
        Assert.Contains("baseUrl: https://api.openai.com/v1 (default)", text);
        Assert.Contains("apiKey: missing (missing)", text);
        Assert.Contains("approvalMode: on-request (default)", text);
    }

    [Fact]
    public void Diff_command_writes_current_diff_for_git_workspace()
    {
        using TempDirectory temp = TempDirectory.Create();
        string filePath = InitializeGitRepository(temp.Path);
        File.AppendAllText(filePath, "changed\n");
        using StringWriter output = new();
        List<string> loggedCommands = [];

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(workspacePath),
                (commandName, _) => loggedCommands.Add(commandName))
            .Parse(["diff", "--workspace", temp.Path])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Equal(["diff"], loggedCommands);
        Assert.Contains("diff --git", text, StringComparison.Ordinal);
        Assert.Contains("+changed", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Diff_command_stat_writes_git_diff_stat()
    {
        using TempDirectory temp = TempDirectory.Create();
        string filePath = InitializeGitRepository(temp.Path);
        File.AppendAllText(filePath, "changed\n");
        using StringWriter output = new();

        int exitCode = CliCommandFactory
            .Create(output, workspacePath => CreateSnapshot(workspacePath))
            .Parse(["diff", "--stat", "--workspace", temp.Path])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("tracked.txt", text, StringComparison.Ordinal);
        Assert.Contains("1 file changed", text, StringComparison.Ordinal);
        Assert.DoesNotContain("diff --git", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Config_get_command_writes_config_report()
    {
        using StringWriter output = new();

        int exitCode = CliCommandFactory
            .Create(output, CreateSnapshot)
            .Parse(["config", "get"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Contains("C# AI CLI effective configuration", output.ToString());
        Assert.Contains("model: not configured", output.ToString());
        Assert.Contains("baseUrl: https://api.openai.com/v1", output.ToString());
    }

    [Fact]
    public void Config_list_command_writes_non_secret_config_report()
    {
        using StringWriter output = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: "sk-test-secret",
            apiKeySource: "OPENAI_API_KEY",
            model: "gpt-workspace",
            baseUrl: "https://gateway.example.test/v1",
            baseUrlSource: "workspace config",
            disabledTools: new HashSet<string>(StringComparer.Ordinal)
            {
                "workspace.run_shell"
            });

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["config", "list"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Contains("C# AI CLI effective configuration", output.ToString());
        Assert.Contains("model: gpt-workspace", output.ToString());
        Assert.Contains("modelSource: workspace config", output.ToString());
        Assert.Contains("baseUrl: https://gateway.example.test/v1", output.ToString());
        Assert.Contains("baseUrlSource: workspace config", output.ToString());
        Assert.Contains("agentBackend: direct", output.ToString());
        Assert.Contains("agentBackendSource: default", output.ToString());
        Assert.Contains("disabledTools: workspace.run_shell", output.ToString());
        Assert.Contains("apiKey: present", output.ToString());
        Assert.Contains("apiKeySource: OPENAI_API_KEY", output.ToString());
        Assert.DoesNotContain("sk-test-secret", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Config_set_model_creates_user_config_json()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: userConfigPath);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["config", "set", "model", "gpt-test"])
            .Invoke();

        Assert.Equal(0, exitCode);
        JsonObject json = ReadJsonObject(userConfigPath);
        Assert.Equal("gpt-test", json["model"]?.GetValue<string>());
        Assert.Contains("status: updated", output.ToString());
        Assert.Contains("key: model", output.ToString());
        Assert.Contains("scope: user", output.ToString());
        Assert.Contains($"path: {userConfigPath}", output.ToString());
    }

    [Fact]
    public void Config_set_preserves_existing_user_config_fields()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(userConfigPath)!);
        File.WriteAllText(userConfigPath, """
        {
          "baseUrl": "https://gateway.example.test/v1",
          "customSetting": 42,
          "disabledTools": [
            "workspace.run_shell"
          ]
        }
        """);
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: userConfigPath);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["config", "set", "model", "gpt-test"])
            .Invoke();

        Assert.Equal(0, exitCode);
        JsonObject json = ReadJsonObject(userConfigPath);
        Assert.Equal("gpt-test", json["model"]?.GetValue<string>());
        Assert.Equal("https://gateway.example.test/v1", json["baseUrl"]?.GetValue<string>());
        Assert.Equal(42, json["customSetting"]?.GetValue<int>());
        Assert.Equal("workspace.run_shell", json["disabledTools"]?[0]?.GetValue<string>());
    }

    [Fact]
    public void Config_set_base_url_validates_and_writes_normalized_value()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: userConfigPath);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["config", "set", "baseUrl", "  https://gateway.example.test/v1  "])
            .Invoke();

        Assert.Equal(0, exitCode);
        JsonObject json = ReadJsonObject(userConfigPath);
        Assert.Equal("https://gateway.example.test/v1", json["baseUrl"]?.GetValue<string>());
        Assert.Contains("status: updated", output.ToString());
        Assert.Contains("key: baseUrl", output.ToString());
        Assert.Contains("scope: user", output.ToString());
        Assert.Contains($"path: {userConfigPath}", output.ToString());
    }

    [Fact]
    public void Config_set_base_url_rejects_invalid_value_without_writing()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: userConfigPath);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["config", "set", "baseUrl", "file:///tmp/gateway"])
            .Invoke();

        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(userConfigPath));
        Assert.Contains("status: failed", output.ToString());
        Assert.Contains("errorCode: invalid-base-url", output.ToString());
    }

    [Fact]
    public void Config_set_base_url_rejects_secret_like_invalid_value_without_printing_value()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        string secretLikeInvalidBaseUrl = "https://gateway.example.test/v1?api-key=sk-command-secret";
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: userConfigPath);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["config", "set", "baseUrl", secretLikeInvalidBaseUrl])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(userConfigPath));
        Assert.Contains("status: failed", text);
        Assert.Contains("errorCode: invalid-base-url", text);
        Assert.DoesNotContain(secretLikeInvalidBaseUrl, text, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-command-secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Config_set_base_url_rejects_newline_secret_like_value_without_printing_value()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        string secretLikeInvalidBaseUrl = "https://gateway.example.test/v1\napiKey: sk-command-secret";
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: userConfigPath);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["config", "set", "baseUrl", secretLikeInvalidBaseUrl])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(userConfigPath));
        Assert.Contains("status: failed", text);
        Assert.Contains("errorCode: invalid-base-url", text);
        Assert.DoesNotContain(secretLikeInvalidBaseUrl, text, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-command-secret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("apiKey:", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Config_set_api_key_does_not_print_secret_value()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: userConfigPath);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["config", "set", "ApiKey", "sk-test-secret"])
            .Invoke();

        Assert.Equal(0, exitCode);
        JsonObject json = ReadJsonObject(userConfigPath);
        Assert.Equal("sk-test-secret", json["apiKey"]?.GetValue<string>());
        Assert.DoesNotContain("sk-test-secret", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Config_set_api_key_removes_case_insensitive_duplicate_key_without_printing_secrets()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(userConfigPath)!);
        File.WriteAllText(userConfigPath, """
        {
          "ApiKey": "old-secret",
          "model": "gpt-existing"
        }
        """);
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: userConfigPath);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["config", "set", "apiKey", "new-secret"])
            .Invoke();

        Assert.Equal(0, exitCode);
        JsonObject json = ReadJsonObject(userConfigPath);
        Assert.Equal("new-secret", json["apiKey"]?.GetValue<string>());
        Assert.False(json.ContainsKey("ApiKey"));
        Assert.Equal("gpt-existing", json["model"]?.GetValue<string>());
        Assert.DoesNotContain("old-secret", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("new-secret", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Config_set_agent_backend_writes_normalized_framework_alias()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: userConfigPath);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["config", "set", "agentBackend", "maf"])
            .Invoke();

        Assert.Equal(0, exitCode);
        JsonObject json = ReadJsonObject(userConfigPath);
        Assert.Equal("framework", json["agentBackend"]?.GetValue<string>());
    }

    [Fact]
    public void Config_set_agent_backend_rejects_invalid_value_without_writing_or_echoing_value()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: userConfigPath);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["config", "set", "agentBackend", "nope"])
            .Invoke();

        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(userConfigPath));
        Assert.Contains("status: failed", output.ToString());
        Assert.Contains("errorCode: invalid-agent-backend", output.ToString());
        Assert.DoesNotContain("nope", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Config_set_unknown_key_returns_nonzero_without_creating_file()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: userConfigPath);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["config", "set", "temperature", "0.2"])
            .Invoke();

        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(userConfigPath));
        Assert.Contains("status: failed", output.ToString());
        Assert.Contains("errorCode: unknown-config-key", output.ToString());
    }

    [Fact]
    public void Config_set_unknown_secret_like_key_returns_nonzero_without_printing_key()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        string secretLikeKey = "apiKey: sk-command-secret";
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: userConfigPath);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["config", "set", secretLikeKey, "0.2"])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(userConfigPath));
        Assert.Contains("status: failed", text);
        Assert.Contains("errorCode: unknown-config-key", text);
        Assert.Contains("Supported scalar keys are: model, baseUrl, agentBackend, apiKey.", text);
        Assert.DoesNotContain(secretLikeKey, text, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-command-secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Config_set_invalid_existing_json_returns_nonzero_without_overwriting()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(userConfigPath)!);
        File.WriteAllText(userConfigPath, "{not json");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: userConfigPath);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["config", "set", "model", "gpt-test"])
            .Invoke();

        Assert.Equal(1, exitCode);
        Assert.Equal("{not json", File.ReadAllText(userConfigPath));
        Assert.Contains("status: failed", output.ToString());
        Assert.Contains("errorCode: invalid-config-file", output.ToString());
    }

    [Fact]
    public void Config_set_json_array_returns_invalid_config_file_without_overwriting()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(userConfigPath)!);
        File.WriteAllText(userConfigPath, "[]");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: userConfigPath);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["config", "set", "model", "gpt-test"])
            .Invoke();

        Assert.Equal(1, exitCode);
        Assert.Equal("[]", File.ReadAllText(userConfigPath));
        Assert.Contains("status: failed", output.ToString());
        Assert.Contains("errorCode: invalid-config-file", output.ToString());
    }

    [Fact]
    public void Config_set_parent_path_conflict_returns_write_failure_without_throwing()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string userConfigDirectory = Path.Combine(temp.Path, ".caicli");
        string userConfigPath = Path.Combine(userConfigDirectory, "config.json");
        File.WriteAllText(userConfigDirectory, "not a directory");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: userConfigPath);

        int exitCode = -1;
        Exception? exception = Record.Exception(() =>
        {
            exitCode = CliCommandFactory
                .Create(output, _ => snapshot)
                .Parse(["config", "set", "model", "gpt-test"])
                .Invoke();
        });

        Assert.Null(exception);
        Assert.Equal(1, exitCode);
        Assert.Equal("not a directory", File.ReadAllText(userConfigDirectory));
        Assert.Contains("status: failed", output.ToString());
        Assert.Contains("errorCode: config-write-failed", output.ToString());
    }

    [Fact]
    public void Config_unset_api_key_removes_case_insensitive_matches_without_printing_secrets()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(userConfigPath)!);
        File.WriteAllText(userConfigPath, """
        {
          "apiKey": "old-secret",
          "ApiKey": "older-secret",
          "model": "gpt-existing"
        }
        """);
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: userConfigPath);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["config", "unset", "APIKEY"])
            .Invoke();

        Assert.Equal(0, exitCode);
        JsonObject json = ReadJsonObject(userConfigPath);
        Assert.False(json.ContainsKey("apiKey"));
        Assert.False(json.ContainsKey("ApiKey"));
        Assert.Equal("gpt-existing", json["model"]?.GetValue<string>());
        Assert.Contains("status: updated", output.ToString());
        Assert.Contains("key: apiKey", output.ToString());
        Assert.Contains("scope: user", output.ToString());
        Assert.Contains($"path: {userConfigPath}", output.ToString());
        Assert.DoesNotContain("old-secret", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("older-secret", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Config_unset_base_url_removes_user_config_value_without_printing_secret_like_value()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        string secretLikeBaseUrl = "https://gateway.example.test/v1?api-key=sk-existing-secret";
        Directory.CreateDirectory(Path.GetDirectoryName(userConfigPath)!);
        File.WriteAllText(userConfigPath, $$"""
        {
          "baseUrl": "{{secretLikeBaseUrl}}",
          "model": "gpt-existing"
        }
        """);
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: userConfigPath);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["config", "unset", "BaseURL"])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        JsonObject json = ReadJsonObject(userConfigPath);
        Assert.False(json.ContainsKey("baseUrl"));
        Assert.Equal("gpt-existing", json["model"]?.GetValue<string>());
        Assert.Contains("status: updated", text);
        Assert.Contains("key: baseUrl", text);
        Assert.Contains("scope: user", text);
        Assert.Contains($"path: {userConfigPath}", text);
        Assert.DoesNotContain(secretLikeBaseUrl, text, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-existing-secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Config_unset_missing_config_file_succeeds_unchanged_without_creating_file()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: userConfigPath);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["config", "unset", "model"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.False(File.Exists(userConfigPath));
        Assert.Contains("status: unchanged", output.ToString());
        Assert.Contains("key: model", output.ToString());
        Assert.Contains("scope: user", output.ToString());
    }

    [Fact]
    public void Config_unset_unknown_key_returns_nonzero_without_creating_file()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: userConfigPath);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["config", "unset", "temperature"])
            .Invoke();

        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(userConfigPath));
        Assert.Contains("status: failed", output.ToString());
        Assert.Contains("errorCode: unknown-config-key", output.ToString());
    }

    [Fact]
    public void Config_unset_unknown_secret_like_key_returns_nonzero_without_printing_key()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        string secretLikeKey = "apiKey: sk-command-secret";
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: userConfigPath);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["config", "unset", secretLikeKey])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(userConfigPath));
        Assert.Contains("status: failed", text);
        Assert.Contains("errorCode: unknown-config-key", text);
        Assert.Contains("Supported scalar keys are: model, baseUrl, agentBackend, apiKey.", text);
        Assert.DoesNotContain(secretLikeKey, text, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-command-secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Version_command_writes_version_metadata()
    {
        using StringWriter output = new();

        int exitCode = CliCommandFactory
            .Create(output, CreateSnapshot)
            .Parse(["version"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Contains("caicli ", output.ToString());
        Assert.Contains("target framework: net9.0", output.ToString());
        Assert.Contains("release runtime: win-x64", output.ToString());
    }

    [Fact]
    public void Mcp_list_command_writes_mcp_server_report()
    {
        using StringWriter output = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            configSources:
            [
                new CliConfigFileSource(
                    "workspace config",
                    "workspace-config.json",
                    new CliConfigFile
                    {
                        McpServers = new Dictionary<string, McpServerConfig>
                        {
                            ["disabled"] = new()
                            {
                                Enabled = false,
                                Transport = "stdio",
                                Command = "mcp-disabled"
                            }
                        }
                    })
            ]);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["mcp", "list"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Contains("C# AI CLI MCP servers", output.ToString());
        Assert.Contains("server: disabled", output.ToString());
        Assert.Contains("status: inactive", output.ToString());
    }

    [Fact]
    public void Mcp_doctor_command_writes_mcp_diagnostic_report()
    {
        using StringWriter output = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            configSources:
            [
                new CliConfigFileSource(
                    "workspace config",
                    "workspace-config.json",
                    new CliConfigFile
                    {
                        McpServers = new Dictionary<string, McpServerConfig>
                        {
                            ["disabled"] = new()
                            {
                                Enabled = false,
                                Transport = "stdio",
                                Command = "mcp-disabled"
                            }
                        }
                    })
            ]);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["mcp", "doctor"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Contains("C# AI CLI MCP doctor", output.ToString());
        Assert.Contains("server: disabled", output.ToString());
        Assert.Contains("connectionStatus: inactive", output.ToString());
    }

    [Fact]
    public void Workflow_list_command_writes_workflow_profiles()
    {
        using StringWriter output = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            configSources: [CreateWorkflowSource()]);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["workflow", "list"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Contains("C# AI CLI workflows", output.ToString());
        Assert.Contains("profile: cpp", output.ToString());
    }

    [Fact]
    public void Workflow_validate_command_suggests_validation_command_without_running()
    {
        using StringWriter output = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: "cli-root",
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            configSources: [CreateWorkflowSource()]);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["workflow", "validate", "cpp"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Contains("profile: cpp", output.ToString());
        Assert.Contains("validationCommand: dotnet test", output.ToString());
        Assert.Contains("execution: not run", output.ToString());
    }

    [Fact]
    public void Tools_list_prints_enabled_tools_and_disabled_tools()
    {
        using StringWriter output = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            disabledTools: new HashSet<string>(StringComparer.Ordinal)
            {
                "workspace.run_shell"
            });

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["tools", "list"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Contains("workspace.read_text", output.ToString());
        Assert.DoesNotContain("workspace.run_shell: Run", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("disabledTools: workspace.run_shell", output.ToString());
    }

    [Fact]
    public void Tools_call_returns_unknown_tool_when_tool_is_disabled()
    {
        using StringWriter output = new();
        using TempDirectory temp = TempDirectory.Create();
        string argumentsPath = Path.Combine(temp.Path, "arguments.json");
        File.WriteAllText(argumentsPath, """{"command":"dotnet --version"}""");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            disabledTools: new HashSet<string>(StringComparer.Ordinal)
            {
                "workspace.run_shell"
            });

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["tools", "call", "workspace.run_shell", "--arguments-file", argumentsPath])
            .Invoke();

        Assert.Equal(1, exitCode);
        Assert.Contains("errorCode: unknown-tool", output.ToString());
    }

    [Fact]
    public void Tools_call_returns_unknown_tool_when_mcp_tool_is_disabled()
    {
        using StringWriter output = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            configSources:
            [
                new CliConfigFileSource(
                    "workspace config",
                    "workspace-config.json",
                    new CliConfigFile
                    {
                        McpServers = new Dictionary<string, McpServerConfig>
                        {
                            ["active"] = new()
                            {
                                Enabled = true,
                                Transport = "stdio",
                                Command = "mcp-active"
                            }
                        }
                    })
            ],
            disabledTools: new HashSet<string>(StringComparer.Ordinal)
            {
                "mcp.active.call"
            });

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["tools", "call", "mcp.active.call", """{"tool":"echo"}"""])
            .Invoke();

        Assert.Equal(1, exitCode);
        Assert.Contains("errorCode: unknown-tool", output.ToString());
    }

    [Fact]
    public void Tools_call_refuses_patch_without_approval()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();

        int exitCode = InvokeToolsCallPatch(temp, output, []);

        Assert.Equal(1, exitCode);
        Assert.Contains("errorCode: approval-denied", output.ToString());
        Assert.Contains("approvalStatus: approval-required", output.ToString());
        Assert.Equal("before", File.ReadAllText(Path.Combine(temp.Path, "note.txt")));
    }

    [Theory]
    [InlineData("on-request", "Approval is required, but this CLI cannot request interactive approval.")]
    [InlineData("on-failure", "Approval after failure is not available because sandbox retry escalation is not implemented.")]
    public void Tools_call_interactive_approval_modes_report_approval_required_for_write_tool(
        string approvalMode,
        string expectedSummary)
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();

        int exitCode = InvokeToolsCallPatch(temp, output, ["--approval", approvalMode]);

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("errorCode: approval-denied", text);
        Assert.Contains("approvalStatus: approval-required", text);
        Assert.Contains(expectedSummary, text);
        Assert.Equal("before", File.ReadAllText(Path.Combine(temp.Path, "note.txt")));
    }

    [Fact]
    public void Tools_call_approval_always_applies_patch()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();

        int exitCode = InvokeToolsCallPatch(temp, output, ["--approval", "always"]);

        Assert.Equal(0, exitCode);
        Assert.Contains("approvalStatus: approved", output.ToString());
        Assert.Equal("after", File.ReadAllText(Path.Combine(temp.Path, "note.txt")));
    }

    [Fact]
    public void Tools_call_approval_never_denies_patch()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();

        int exitCode = InvokeToolsCallPatch(temp, output, ["--approval", "never"]);

        Assert.Equal(1, exitCode);
        Assert.Contains("errorCode: approval-denied", output.ToString());
        Assert.Contains("approvalStatus: denied", output.ToString());
        Assert.Equal("before", File.ReadAllText(Path.Combine(temp.Path, "note.txt")));
    }

    [Fact]
    public void Tools_call_without_approval_refuses_shell_without_executing_command()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string argumentsPath = Path.Combine(temp.Path, "arguments.json");
        string markerPath = Path.Combine(temp.Path, "shell-marker.txt");
        File.WriteAllText(argumentsPath, """{"command":"dotnet --version > shell-marker.txt"}""");

        int exitCode = CliCommandFactory
            .Create(output, workspacePath => CreateSnapshot(workspacePath))
            .Parse(["tools", "call", "--workspace", temp.Path, "workspace.run_shell", "--arguments-file", argumentsPath])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("errorCode: approval-denied", text);
        Assert.Contains("approvalStatus: approval-required", text);
        Assert.False(File.Exists(markerPath));
    }

    [Fact]
    public void Tools_call_approval_always_runs_safe_shell_command()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string argumentsPath = Path.Combine(temp.Path, "arguments.json");
        File.WriteAllText(argumentsPath, """{"command":"dotnet --version","timeoutMilliseconds":10000}""");

        int exitCode = CliCommandFactory
            .Create(output, workspacePath => CreateSnapshot(workspacePath))
            .Parse([
                "tools",
                "call",
                "--workspace",
                temp.Path,
                "--approval",
                "always",
                "workspace.run_shell",
                "--arguments-file",
                argumentsPath
            ])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("status: succeeded", text);
        Assert.Contains("approvalStatus: approved", text);
        Assert.Contains("stdout:", text);
    }

    [Fact]
    public void Tools_call_approval_always_reports_dangerous_shell_denial()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string argumentsPath = Path.Combine(temp.Path, "arguments.json");
        File.WriteAllText(argumentsPath, """{"command":"rm -rf ."}""");

        int exitCode = CliCommandFactory
            .Create(output, workspacePath => CreateSnapshot(workspacePath))
            .Parse([
                "tools",
                "call",
                "--workspace",
                temp.Path,
                "--approval",
                "always",
                "workspace.run_shell",
                "--arguments-file",
                argumentsPath
            ])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("errorCode: approval-denied", text);
        Assert.Contains("approvalStatus: dangerous-shell-denied", text);
        Assert.DoesNotContain("approvalStatus: approved", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Tools_call_approve_still_applies_patch()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();

        int exitCode = InvokeToolsCallPatch(temp, output, ["--approve"]);

        Assert.Equal(0, exitCode);
        Assert.Contains("approvalStatus: approved", output.ToString());
        Assert.Equal("after", File.ReadAllText(Path.Combine(temp.Path, "note.txt")));
    }

    [Fact]
    public void Tools_call_approval_option_takes_priority_over_approve()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();

        int exitCode = InvokeToolsCallPatch(temp, output, ["--approval", "never", "--approve"]);

        Assert.Equal(1, exitCode);
        Assert.Contains("errorCode: approval-denied", output.ToString());
        Assert.Contains("approvalStatus: denied", output.ToString());
        Assert.Equal("before", File.ReadAllText(Path.Combine(temp.Path, "note.txt")));
    }

    [Fact]
    public void Tools_call_invalid_approval_rejects_before_invoking_tool()
    {
        using TempDirectory temp = TempDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "note.txt"), "before");
        string argumentsPath = Path.Combine(temp.Path, "arguments.json");
        File.WriteAllText(argumentsPath, """{"path":"note.txt","find":"before","replace":"after"}""");
        using StringWriter output = new();
        List<string> loggedCommands = [];
        RootCommand command = CliCommandFactory.Create(
            output,
            workspacePath => CreateSnapshot(workspacePath),
            (commandName, _) => loggedCommands.Add(commandName));

        int exitCode = CliCommandFactory.Invoke(
            command,
            ["tools", "call", "--workspace", temp.Path, "--approval", "maybe", "workspace.apply_patch", "--arguments-file", argumentsPath],
            output);

        string text = output.ToString();
        Assert.Equal(2, exitCode);
        Assert.Empty(loggedCommands);
        Assert.Contains("Invalid value for --approval. Allowed values are never, on-request, on-failure, and always.", text);
        Assert.Equal("before", File.ReadAllText(Path.Combine(temp.Path, "note.txt")));
    }

    [Fact]
    public void Run_create_smoke_note_applies_patch_when_approved()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();

        int exitCode = CliCommandFactory
            .Create(output, workspacePath => CreateSnapshot(workspacePath))
            .Parse(["run", "--workspace", temp.Path, "--approve", "create smoke note"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Contains("status: succeeded", output.ToString());
        Assert.Contains("status: completed", File.ReadAllText(Path.Combine(temp.Path, "caicli-smoke.txt")));
    }

    [Fact]
    public void Run_create_smoke_note_without_approval_returns_failure_without_creating_smoke_note()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();

        int exitCode = CliCommandFactory
            .Create(output, workspacePath => CreateSnapshot(workspacePath))
            .Parse(["run", "--workspace", temp.Path, "create smoke note"])
            .Invoke();

        string smokeNotePath = Path.Combine(temp.Path, "caicli-smoke.txt");
        Assert.Equal(1, exitCode);
        Assert.Contains("errorCode: approval-denied", output.ToString());
        Assert.False(File.Exists(smokeNotePath));
    }

    [Fact]
    public void Run_approve_dangerous_shell_reports_dangerous_approval_status()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();

        int exitCode = CliCommandFactory
            .Create(output, workspacePath => CreateSnapshot(workspacePath))
            .Parse(["run", "--workspace", temp.Path, "--approve", "shell rm -rf ."])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("status: failed", text);
        Assert.Contains("approvalStatus: dangerous-shell-denied", text);
        Assert.DoesNotContain("approvalStatus: approved", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_create_smoke_note_respects_patch_tool_disable()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: temp.Path,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            disabledTools: new HashSet<string>(StringComparer.Ordinal)
            {
                "workspace.apply_patch"
            });

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["run", "--approve", "create smoke note"])
            .Invoke();

        Assert.Equal(1, exitCode);
        Assert.Contains("errorCode: unknown-tool", output.ToString());
        Assert.False(File.Exists(Path.Combine(temp.Path, "caicli-smoke.txt")));
    }

    [Fact]
    public void Run_read_note_text_output_returns_old_shape()
    {
        using TempDirectory temp = TempDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "note.txt"), "hello run");
        using StringWriter output = new();

        int exitCode = CliCommandFactory
            .Create(output, workspacePath => CreateSnapshot(workspacePath))
            .Parse(["run", "--workspace", temp.Path, "read note.txt"])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("status: succeeded", text);
        Assert.Contains("approvalStatus: not-required", text);
        Assert.Contains("summary:", text);
        Assert.Contains("hello run", text);
        Assert.DoesNotContain("event:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("exec.result", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Exec_uses_injected_agent_runner_and_renders_success_text_output()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        FakeAgentRunner agentRunner = new(AgentRunResult.Success(
            "agent completed task",
            [],
            [
                new AgentRunEvent(
                    Type: "final.response",
                    Sequence: 0,
                    Timestamp: DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                    Summary: "agent completed task",
                    Payload: new Dictionary<string, string>
                    {
                        ["kind"] = "final"
                    })
            ]));
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: temp.Path,
            apiKey: "sk-test-secret",
            apiKeySource: "OPENAI_API_KEY",
            model: "gpt-test")
            with
            {
                Instructions = InstructionLoadResult.Loaded("Prefer concise answers.", Path.Combine(temp.Path, "AICLI.md"))
            };

        int exitCode = CliCommandFactory
            .Create(
                output,
                _ => snapshot,
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => new FakeConversationStore(),
                () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                (_, _, _) => agentRunner)
            .Parse([
                "exec",
                "--workspace",
                temp.Path,
                "--max-turns",
                "3",
                "--max-tool-calls",
                "5",
                "--timeout-seconds",
                "7",
                "summarize workspace"
            ])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Equal("summarize workspace", agentRunner.LastRequest?.Prompt);
        Assert.Same(snapshot.Workspace, agentRunner.LastRequest?.Workspace);
        Assert.Equal("Prefer concise answers.", agentRunner.LastRequest?.Instructions);
        Assert.Equal(3, agentRunner.LastRequest?.Limits?.MaxTurns);
        Assert.Equal(5, agentRunner.LastRequest?.Limits?.MaxToolCalls);
        Assert.Equal(TimeSpan.FromSeconds(7), agentRunner.LastRequest?.Limits?.OverallTimeout);
        Assert.Equal(TimeSpan.FromSeconds(7), agentRunner.LastRequest?.Limits?.ModelCallTimeout);
        Assert.Contains("event: final.response", text);
        Assert.Contains("result: success", text);
        Assert.Contains("agent completed task", text);
        Assert.Contains("payload.kind=final", text);
        Assert.DoesNotContain("sk-test-secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Exec_command_passes_cwd_to_snapshot_provider_and_merged_instructions_to_agent_request()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string cwd = Path.Combine("src", "app");
        string? receivedWorkspace = null;
        string? receivedCwd = null;
        FakeAgentRunner agentRunner = new(AgentRunResult.Success("agent completed task", []));
        RootCommand command = CliCommandFactory.Create(
            output,
            (workspacePath, instructionTargetPath) =>
            {
                receivedWorkspace = workspacePath;
                receivedCwd = instructionTargetPath;
                string workspaceRoot = workspacePath ?? temp.Path;
                return CreateSnapshot(
                    workspacePath,
                    apiKey: "sk-test-secret",
                    apiKeySource: "OPENAI_API_KEY",
                    model: "gpt-test")
                    with
                    {
                        Instructions = InstructionLoadResult.Loaded(
                            "Root rules\n\nApp rules",
                            [
                                new InstructionSource(Path.Combine(workspaceRoot, "AICLI.md"), 0),
                                new InstructionSource(Path.Combine(workspaceRoot, "src", "app", "AICLI.md"), 1)
                            ])
                    };
            },
            (_, _) => { },
            _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => new FakeConversationStore(),
            () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
            (_, _, _) => agentRunner);

        int exitCode = CliCommandFactory.Invoke(
            command,
            ["exec", "--workspace", temp.Path, "--cwd", cwd, "summarize workspace"],
            output);

        Assert.Equal(0, exitCode);
        Assert.Equal(temp.Path, receivedWorkspace);
        Assert.Equal(cwd, receivedCwd);
        Assert.Equal(temp.Path, agentRunner.LastRequest?.Workspace.RootPath);
        Assert.Equal("Root rules\n\nApp rules", agentRunner.LastRequest?.Instructions);
        Assert.DoesNotContain("Root rules", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("sk-test-secret", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Exec_command_without_cwd_keeps_legacy_snapshot_provider_compatibility()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string? receivedWorkspace = null;
        FakeAgentRunner agentRunner = new(AgentRunResult.Success("agent completed task", []));

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath =>
                {
                    receivedWorkspace = workspacePath;
                    return CreateSnapshot(
                        workspacePath,
                        apiKey: "sk-test-secret",
                        apiKeySource: "OPENAI_API_KEY",
                        model: "gpt-test");
                },
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => new FakeConversationStore(),
                () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                (_, _, _) => agentRunner)
            .Parse(["exec", "--workspace", temp.Path, "summarize workspace"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal(temp.Path, receivedWorkspace);
        Assert.Null(agentRunner.LastRequest?.Instructions);
    }

    [Fact]
    public void Exec_uses_injected_agent_runner_and_renders_success_json_output()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        FakeAgentRunner agentRunner = new(AgentRunResult.Success(
            "json agent summary",
            [],
            [
                new AgentRunEvent(
                    Type: "model.turn",
                    Sequence: 0,
                    Timestamp: DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                    Summary: "model planned",
                    Payload: new Dictionary<string, string>
                    {
                        ["toolCallCount"] = "0"
                    })
            ]));

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(
                    workspacePath,
                    apiKey: "sk-test-secret",
                    apiKeySource: "OPENAI_API_KEY",
                    model: "gpt-test"),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => new FakeConversationStore(),
                () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                (_, _, _) => agentRunner)
            .Parse(["exec", "--json", "--workspace", temp.Path, "summarize workspace"])
            .Invoke();

        string[] lines = output.ToString()
            .TrimEnd()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(0, exitCode);
        Assert.Equal(2, lines.Length);
        foreach (string line in lines)
        {
            JsonNode? node = JsonNode.Parse(line);
            Assert.NotNull(node);
        }

        JsonObject modelTurn = Assert.IsType<JsonObject>(JsonNode.Parse(lines[0]));
        JsonObject result = Assert.IsType<JsonObject>(JsonNode.Parse(lines[^1]));
        Assert.Equal("model.turn", modelTurn["type"]?.GetValue<string>());
        Assert.Equal("0", modelTurn["payload"]?["toolCallCount"]?.GetValue<string>());
        Assert.Equal("exec.result", result["type"]?.GetValue<string>());
        Assert.Equal("json agent summary", result["summary"]?.GetValue<string>());
        Assert.Equal("success", result["payload"]?["status"]?.GetValue<string>());
        Assert.Equal(0, result["payload"]?["exitCode"]?.GetValue<int>());
        Assert.DoesNotContain("sk-test-secret", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Exec_json_output_includes_approval_status_for_tool_event_and_result()
    {
        using TempDirectory temp = TempDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "note.txt"), "before");
        using StringWriter output = new();
        ExecutorToolCallAgentRunner agentRunner = new(
            "workspace.apply_patch",
            """{"path":"note.txt","find":"before","replace":"after"}""");

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(workspacePath),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => new FakeConversationStore(),
                () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                (_, _, executor) =>
                {
                    agentRunner.Executor = executor;
                    return agentRunner;
                })
            .Parse(["exec", "--json", "--workspace", temp.Path, "--approval", "always", "update note"])
            .Invoke();

        string[] lines = output.ToString()
            .TrimEnd()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(0, exitCode);
        Assert.Equal(2, lines.Length);
        JsonObject toolEvent = Assert.IsType<JsonObject>(JsonNode.Parse(lines[0]));
        JsonObject result = Assert.IsType<JsonObject>(JsonNode.Parse(lines[^1]));
        Assert.Equal("tool.completed", toolEvent["type"]?.GetValue<string>());
        Assert.Equal("approved", toolEvent["approvalStatus"]?.GetValue<string>());
        Assert.Equal("exec.result", result["type"]?.GetValue<string>());
        Assert.Equal("approved", result["approvalStatus"]?.GetValue<string>());
        Assert.Equal("success", result["payload"]?["status"]?.GetValue<string>());
        Assert.Equal("after", File.ReadAllText(Path.Combine(temp.Path, "note.txt")));
    }

    [Fact]
    public void Exec_approval_always_allows_injected_runner_tool_call()
    {
        using TempDirectory temp = TempDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "note.txt"), "before");
        using StringWriter output = new();
        ExecutorToolCallAgentRunner agentRunner = new(
            "workspace.apply_patch",
            """{"path":"note.txt","find":"before","replace":"after"}""");

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(workspacePath),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => new FakeConversationStore(),
                () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                (_, _, executor) =>
                {
                    agentRunner.Executor = executor;
                    return agentRunner;
                })
            .Parse(["exec", "--workspace", temp.Path, "--approval", "always", "update note"])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("approvalStatus=approved", text);
        Assert.Contains("result: success", text);
        Assert.Equal("after", File.ReadAllText(Path.Combine(temp.Path, "note.txt")));
    }

    [Fact]
    public void Exec_approval_never_takes_priority_over_approve()
    {
        using TempDirectory temp = TempDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "note.txt"), "before");
        using StringWriter output = new();
        ExecutorToolCallAgentRunner agentRunner = new(
            "workspace.apply_patch",
            """{"path":"note.txt","find":"before","replace":"after"}""");

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(workspacePath),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => new FakeConversationStore(),
                () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                (_, _, executor) =>
                {
                    agentRunner.Executor = executor;
                    return agentRunner;
                })
            .Parse(["exec", "--workspace", temp.Path, "--approval", "never", "--approve", "update note"])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("errorCode=approval-denied", text);
        Assert.Contains("approvalStatus=denied", text);
        Assert.Contains("result: failure", text);
        Assert.Equal("before", File.ReadAllText(Path.Combine(temp.Path, "note.txt")));
    }

    [Fact]
    public void Exec_invalid_approval_rejects_before_logging_or_running_agent()
    {
        using TempDirectory temp = TempDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "note.txt"), "before");
        using StringWriter output = new();
        List<string> loggedCommands = [];
        FakeAgentRunner agentRunner = new(AgentRunResult.Success("should not run", [], []));
        RootCommand command = CliCommandFactory.Create(
            output,
            workspacePath => CreateSnapshot(workspacePath),
            (commandName, _) => loggedCommands.Add(commandName),
            _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => new FakeConversationStore(),
            () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
            (_, _, _) => agentRunner);

        int exitCode = CliCommandFactory.Invoke(
            command,
            ["exec", "--workspace", temp.Path, "--approval", "maybe", "update note"],
            output);

        string text = output.ToString();
        Assert.Equal(2, exitCode);
        Assert.Empty(loggedCommands);
        Assert.Null(agentRunner.LastRequest);
        Assert.Contains("Invalid value for --approval. Allowed values are never, on-request, on-failure, and always.", text);
        Assert.Equal("before", File.ReadAllText(Path.Combine(temp.Path, "note.txt")));
    }

    [Fact]
    public void Exec_approve_legacy_still_allows_injected_runner_tool_call()
    {
        using TempDirectory temp = TempDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "note.txt"), "before");
        using StringWriter output = new();
        ExecutorToolCallAgentRunner agentRunner = new(
            "workspace.apply_patch",
            """{"path":"note.txt","find":"before","replace":"after"}""");

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(workspacePath),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => new FakeConversationStore(),
                () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                (_, _, executor) =>
                {
                    agentRunner.Executor = executor;
                    return agentRunner;
                })
            .Parse(["exec", "--workspace", temp.Path, "--approve", "update note"])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("approvalStatus=approved", text);
        Assert.Contains("result: success", text);
        Assert.Equal("after", File.ReadAllText(Path.Combine(temp.Path, "note.txt")));
    }

    [Fact]
    public void Exec_uses_configured_approval_mode_without_cli_override()
    {
        using TempDirectory temp = TempDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "note.txt"), "before");
        using StringWriter output = new();
        ExecutorToolCallAgentRunner agentRunner = new(
            "workspace.apply_patch",
            """{"path":"note.txt","find":"before","replace":"after"}""");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(temp.Path)
            with
            {
                Configuration = CreateSnapshot(temp.Path).Configuration with
                {
                    ApprovalMode = ApprovalMode.Always,
                    ApprovalModeSource = "workspace config"
                }
            };

        int exitCode = CliCommandFactory
            .Create(
                output,
                _ => snapshot,
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => new FakeConversationStore(),
                () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                (_, _, executor) =>
                {
                    agentRunner.Executor = executor;
                    return agentRunner;
                })
            .Parse(["exec", "--workspace", temp.Path, "update note"])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("approvalStatus=approved", text);
        Assert.Equal("after", File.ReadAllText(Path.Combine(temp.Path, "note.txt")));
    }

    [Fact]
    public void Exec_session_loads_transcript_passes_it_to_runner_and_saves()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        FakeAgentRunner agentRunner = new(AgentRunResult.Success("agent completed task", [], []));
        ConversationTranscript existing = ConversationTranscript.Create(
            "smoke",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        FakeConversationStore store = new()
        {
            Transcript = existing
        };

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(
                    workspacePath,
                    apiKey: "sk-test-secret",
                    apiKeySource: "OPENAI_API_KEY",
                    model: "gpt-test"),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => store,
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
                (_, _, _) => agentRunner)
            .Parse(["exec", "--workspace", temp.Path, "--session", "smoke", "summarize workspace"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal("smoke", store.LoadedSessionName?.Value);
        Assert.Equal("smoke", store.SavedSessionName?.Value);
        Assert.Equal("smoke", agentRunner.LastRequest?.SessionName);
        Assert.Same(existing, agentRunner.LastTranscript);
        Assert.Same(existing, store.SavedTranscript);
    }

    [Fact]
    public void Exec_resume_requires_existing_transcript_passes_it_to_runner_and_saves()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        FakeAgentRunner agentRunner = new(AgentRunResult.Success("agent completed task", [], []));
        ConversationTranscript existing = ConversationTranscript.Create(
            "smoke",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        FakeConversationStore store = new()
        {
            Transcript = existing
        };

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(
                    workspacePath,
                    apiKey: "sk-test-secret",
                    apiKeySource: "OPENAI_API_KEY",
                    model: "gpt-test"),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => store,
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
                (_, _, _) => agentRunner)
            .Parse(["exec", "--workspace", temp.Path, "--resume", "smoke", "summarize workspace"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal("smoke", store.TryLoadedSessionName?.Value);
        Assert.Null(store.LoadedSessionName);
        Assert.Equal("smoke", store.SavedSessionName?.Value);
        Assert.Equal("smoke", agentRunner.LastRequest?.SessionName);
        Assert.Same(existing, agentRunner.LastRequest?.TranscriptContext);
        Assert.Same(existing, agentRunner.LastTranscript);
        Assert.Same(existing, store.SavedTranscript);
    }

    [Fact]
    public void Exec_resume_missing_returns_session_not_found_without_running_agent()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        FakeAgentRunner agentRunner = new(AgentRunResult.Success("agent completed task", [], []));
        FakeConversationStore store = new()
        {
            TryLoadResult = false
        };

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(
                    workspacePath,
                    apiKey: "sk-test-secret",
                    apiKeySource: "OPENAI_API_KEY",
                    model: "gpt-test"),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => store,
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
                (_, _, _) => agentRunner)
            .Parse(["exec", "--workspace", temp.Path, "--resume", "missing", "summarize workspace"])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("status: failed", text);
        Assert.Contains("errorCode: session-not-found", text);
        Assert.Contains("summary:", text);
        Assert.Contains("Session transcript was not found.", text);
        Assert.Equal("missing", store.TryLoadedSessionName?.Value);
        Assert.Null(store.ExistsSessionName);
        Assert.Null(store.LoadedSessionName);
        Assert.Null(store.SavedSessionName);
        Assert.Null(agentRunner.LastRequest);
    }

    [Fact]
    public void Exec_json_resume_missing_renders_json_failure_without_running_agent()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        FakeAgentRunner agentRunner = new(AgentRunResult.Success("agent completed task", [], []));
        FakeConversationStore store = new()
        {
            TryLoadResult = false
        };

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(
                    workspacePath,
                    apiKey: "sk-test-secret",
                    apiKeySource: "OPENAI_API_KEY",
                    model: "gpt-test"),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => store,
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
                (_, _, _) => agentRunner)
            .Parse(["exec", "--json", "--workspace", temp.Path, "--resume", "missing", "summarize workspace"])
            .Invoke();

        string[] lines = output.ToString().TrimEnd().Split(Environment.NewLine);
        JsonObject result = Assert.IsType<JsonObject>(JsonNode.Parse(Assert.Single(lines)));
        Assert.Equal(1, exitCode);
        Assert.Equal("exec.result", result["type"]?.GetValue<string>());
        Assert.Equal("session-not-found", result["errorCode"]?.GetValue<string>());
        Assert.Equal("Session transcript was not found.", result["summary"]?.GetValue<string>());
        JsonObject payload = Assert.IsType<JsonObject>(result["payload"]);
        Assert.Equal("failure", payload["status"]?.GetValue<string>());
        Assert.Equal(1, payload["exitCode"]?.GetValue<int>());
        Assert.Equal("missing", store.TryLoadedSessionName?.Value);
        Assert.Null(agentRunner.LastRequest);
    }

    [Fact]
    public void Exec_json_resume_invalid_session_name_renders_json_failure_without_running_agent_or_store()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        bool storeFactoryInvoked = false;
        bool runnerFactoryInvoked = false;

        RootCommand command = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(
                    workspacePath,
                    apiKey: "sk-test-secret",
                    apiKeySource: "OPENAI_API_KEY",
                    model: "gpt-test"),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ =>
                {
                    storeFactoryInvoked = true;
                    return new FakeConversationStore();
                },
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
                (_, _, _) =>
                {
                    runnerFactoryInvoked = true;
                    return new FakeAgentRunner(AgentRunResult.Success("agent completed task", [], []));
                });
        int exitCode = CliCommandFactory.Invoke(
            command,
            ["exec", "--json", "--workspace", temp.Path, "--resume", "../secret", "summarize workspace"],
            output);

        string[] lines = output.ToString().TrimEnd().Split(Environment.NewLine);
        JsonObject result = Assert.IsType<JsonObject>(JsonNode.Parse(Assert.Single(lines)));
        Assert.Equal(1, exitCode);
        Assert.Equal("exec.result", result["type"]?.GetValue<string>());
        Assert.Equal("invalid-session-name", result["errorCode"]?.GetValue<string>());
        Assert.Equal("Session name contains invalid path characters.", result["summary"]?.GetValue<string>());
        JsonObject payload = Assert.IsType<JsonObject>(result["payload"]);
        Assert.Equal("failure", payload["status"]?.GetValue<string>());
        Assert.Equal(1, payload["exitCode"]?.GetValue<int>());
        Assert.False(storeFactoryInvoked);
        Assert.False(runnerFactoryInvoked);
    }

    [Fact]
    public void Exec_json_resume_empty_session_name_renders_json_failure_without_running_agent_or_store()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        bool storeFactoryInvoked = false;
        bool runnerFactoryInvoked = false;

        RootCommand command = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(
                    workspacePath,
                    apiKey: "sk-test-secret",
                    apiKeySource: "OPENAI_API_KEY",
                    model: "gpt-test"),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ =>
                {
                    storeFactoryInvoked = true;
                    return new FakeConversationStore();
                },
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
                (_, _, _) =>
                {
                    runnerFactoryInvoked = true;
                    return new FakeAgentRunner(AgentRunResult.Success("agent completed task", [], []));
                });
        int exitCode = CliCommandFactory.Invoke(
            command,
            ["exec", "--json", "--workspace", temp.Path, "--resume", "", "summarize workspace"],
            output);

        JsonObject result = AssertSingleExecJsonResult(output);
        Assert.Equal(1, exitCode);
        Assert.Equal("invalid-session-name", result["errorCode"]?.GetValue<string>());
        Assert.Equal("Session name must not be empty.", result["summary"]?.GetValue<string>());
        JsonObject payload = Assert.IsType<JsonObject>(result["payload"]);
        Assert.Equal("failure", payload["status"]?.GetValue<string>());
        Assert.Equal(1, payload["exitCode"]?.GetValue<int>());
        Assert.False(storeFactoryInvoked);
        Assert.False(runnerFactoryInvoked);
    }

    [Fact]
    public void Exec_output_json_session_invalid_session_name_renders_json_failure_without_running_agent_or_store()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        bool storeFactoryInvoked = false;
        bool runnerFactoryInvoked = false;

        RootCommand command = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(
                    workspacePath,
                    apiKey: "sk-test-secret",
                    apiKeySource: "OPENAI_API_KEY",
                    model: "gpt-test"),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ =>
                {
                    storeFactoryInvoked = true;
                    return new FakeConversationStore();
                },
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
                (_, _, _) =>
                {
                    runnerFactoryInvoked = true;
                    return new FakeAgentRunner(AgentRunResult.Success("agent completed task", [], []));
                });
        int exitCode = CliCommandFactory.Invoke(
            command,
            ["exec", "--workspace", temp.Path, "--output", "json", "--session", "../secret", "summarize workspace"],
            output);

        string[] lines = output.ToString().TrimEnd().Split(Environment.NewLine);
        JsonObject result = Assert.IsType<JsonObject>(JsonNode.Parse(Assert.Single(lines)));
        Assert.Equal(1, exitCode);
        Assert.Equal("exec.result", result["type"]?.GetValue<string>());
        Assert.Equal("invalid-session-name", result["errorCode"]?.GetValue<string>());
        Assert.Equal("Session name contains invalid path characters.", result["summary"]?.GetValue<string>());
        JsonObject payload = Assert.IsType<JsonObject>(result["payload"]);
        Assert.Equal("failure", payload["status"]?.GetValue<string>());
        Assert.Equal(1, payload["exitCode"]?.GetValue<int>());
        Assert.False(storeFactoryInvoked);
        Assert.False(runnerFactoryInvoked);
    }

    [Fact]
    public void Exec_output_json_session_whitespace_session_name_renders_json_failure_without_running_agent_or_store()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        bool storeFactoryInvoked = false;
        bool runnerFactoryInvoked = false;

        RootCommand command = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(
                    workspacePath,
                    apiKey: "sk-test-secret",
                    apiKeySource: "OPENAI_API_KEY",
                    model: "gpt-test"),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ =>
                {
                    storeFactoryInvoked = true;
                    return new FakeConversationStore();
                },
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
                (_, _, _) =>
                {
                    runnerFactoryInvoked = true;
                    return new FakeAgentRunner(AgentRunResult.Success("agent completed task", [], []));
                });
        int exitCode = CliCommandFactory.Invoke(
            command,
            ["exec", "--workspace", temp.Path, "--output", "json", "--session", " ", "summarize workspace"],
            output);

        JsonObject result = AssertSingleExecJsonResult(output);
        Assert.Equal(1, exitCode);
        Assert.Equal("invalid-session-name", result["errorCode"]?.GetValue<string>());
        Assert.Equal("Session name must not be empty.", result["summary"]?.GetValue<string>());
        JsonObject payload = Assert.IsType<JsonObject>(result["payload"]);
        Assert.Equal("failure", payload["status"]?.GetValue<string>());
        Assert.Equal(1, payload["exitCode"]?.GetValue<int>());
        Assert.False(storeFactoryInvoked);
        Assert.False(runnerFactoryInvoked);
    }

    [Fact]
    public void Exec_session_and_resume_conflict_returns_failure_without_running_agent()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        FakeAgentRunner agentRunner = new(AgentRunResult.Success("agent completed task", [], []));
        FakeConversationStore store = new();

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(
                    workspacePath,
                    apiKey: "sk-test-secret",
                    apiKeySource: "OPENAI_API_KEY",
                    model: "gpt-test"),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => store,
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
                (_, _, _) => agentRunner)
            .Parse(["exec", "--workspace", temp.Path, "--session", "smoke", "--resume", "smoke", "summarize workspace"])
            .Invoke();

        Assert.Equal(1, exitCode);
        Assert.Contains("session-option-conflict", output.ToString());
        Assert.Null(store.ExistsSessionName);
        Assert.Null(store.LoadedSessionName);
        Assert.Null(agentRunner.LastRequest);
    }

    [Fact]
    public void Exec_output_json_session_and_resume_conflict_renders_json_failure_without_running_agent()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        FakeAgentRunner agentRunner = new(AgentRunResult.Success("agent completed task", [], []));
        FakeConversationStore store = new();

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(
                    workspacePath,
                    apiKey: "sk-test-secret",
                    apiKeySource: "OPENAI_API_KEY",
                    model: "gpt-test"),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => store,
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
                (_, _, _) => agentRunner)
            .Parse(["exec", "--workspace", temp.Path, "--output", "json", "--session", "smoke", "--resume", "smoke", "summarize workspace"])
            .Invoke();

        string[] lines = output.ToString().TrimEnd().Split(Environment.NewLine);
        JsonObject result = Assert.IsType<JsonObject>(JsonNode.Parse(Assert.Single(lines)));
        Assert.Equal(1, exitCode);
        Assert.Equal("exec.result", result["type"]?.GetValue<string>());
        Assert.Equal("session-option-conflict", result["errorCode"]?.GetValue<string>());
        Assert.Equal("Use either --session or --resume, not both.", result["summary"]?.GetValue<string>());
        JsonObject payload = Assert.IsType<JsonObject>(result["payload"]);
        Assert.Equal("failure", payload["status"]?.GetValue<string>());
        Assert.Equal(1, payload["exitCode"]?.GetValue<int>());
        Assert.Null(store.TryLoadedSessionName);
        Assert.Null(agentRunner.LastRequest);
    }

    [Fact]
    public void Exec_output_json_empty_session_and_resume_conflict_renders_json_failure_without_running_agent_or_store()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        bool storeFactoryInvoked = false;
        bool runnerFactoryInvoked = false;

        RootCommand command = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(
                    workspacePath,
                    apiKey: "sk-test-secret",
                    apiKeySource: "OPENAI_API_KEY",
                    model: "gpt-test"),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ =>
                {
                    storeFactoryInvoked = true;
                    return new FakeConversationStore();
                },
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
                (_, _, _) =>
                {
                    runnerFactoryInvoked = true;
                    return new FakeAgentRunner(AgentRunResult.Success("agent completed task", [], []));
                });
        int exitCode = CliCommandFactory.Invoke(
            command,
            ["exec", "--workspace", temp.Path, "--output", "json", "--session", "", "--resume", "smoke", "summarize workspace"],
            output);

        JsonObject result = AssertSingleExecJsonResult(output);
        Assert.Equal(1, exitCode);
        Assert.Equal("session-option-conflict", result["errorCode"]?.GetValue<string>());
        Assert.Equal("Use either --session or --resume, not both.", result["summary"]?.GetValue<string>());
        JsonObject payload = Assert.IsType<JsonObject>(result["payload"]);
        Assert.Equal("failure", payload["status"]?.GetValue<string>());
        Assert.Equal(1, payload["exitCode"]?.GetValue<int>());
        Assert.False(storeFactoryInvoked);
        Assert.False(runnerFactoryInvoked);
    }

    [Fact]
    public void Exec_json_resume_malformed_file_transcript_renders_json_failure_without_running_agent()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        bool runnerFactoryInvoked = false;
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        string sessionDirectory = Path.Combine(temp.Path, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        File.WriteAllText(Path.Combine(sessionDirectory, "smoke.transcript.json"), """{"schemaVersion":""");

        RootCommand command = CliCommandFactory.Create(
            output,
            workspacePath => CreateSnapshot(
                workspacePath,
                apiKey: "sk-test-secret",
                apiKeySource: "OPENAI_API_KEY",
                model: "gpt-test",
                userConfigPath: userConfigPath),
            (_, _) => { },
            _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
            writer => new TerminalChatStreamingRenderer(writer),
            snapshot => FileConversationStore.Create(snapshot),
            () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
            (_, _, _) =>
            {
                runnerFactoryInvoked = true;
                return new FakeAgentRunner(AgentRunResult.Success("agent completed task", [], []));
            });

        int exitCode = CliCommandFactory.Invoke(
            command,
            ["exec", "--json", "--workspace", temp.Path, "--resume", "smoke", "summarize workspace"],
            output);

        JsonObject result = AssertSingleExecJsonResult(output);
        Assert.Equal(1, exitCode);
        Assert.Equal("session-transcript-invalid", result["errorCode"]?.GetValue<string>());
        Assert.Equal("Conversation transcript is missing or uses an unsupported schema version.", result["summary"]?.GetValue<string>());
        JsonObject payload = Assert.IsType<JsonObject>(result["payload"]);
        Assert.Equal("failure", payload["status"]?.GetValue<string>());
        Assert.Equal(1, payload["exitCode"]?.GetValue<int>());
        Assert.False(runnerFactoryInvoked);
    }

    [Fact]
    public void Exec_json_session_malformed_file_transcript_renders_json_failure_without_running_agent()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        bool runnerFactoryInvoked = false;
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        string sessionDirectory = Path.Combine(temp.Path, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        File.WriteAllText(Path.Combine(sessionDirectory, "smoke.transcript.json"), """{"schemaVersion":""");

        RootCommand command = CliCommandFactory.Create(
            output,
            workspacePath => CreateSnapshot(
                workspacePath,
                apiKey: "sk-test-secret",
                apiKeySource: "OPENAI_API_KEY",
                model: "gpt-test",
                userConfigPath: userConfigPath),
            (_, _) => { },
            _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
            writer => new TerminalChatStreamingRenderer(writer),
            snapshot => FileConversationStore.Create(snapshot),
            () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
            (_, _, _) =>
            {
                runnerFactoryInvoked = true;
                return new FakeAgentRunner(AgentRunResult.Success("agent completed task", [], []));
            });

        int exitCode = CliCommandFactory.Invoke(
            command,
            ["exec", "--json", "--workspace", temp.Path, "--session", "smoke", "summarize workspace"],
            output);

        JsonObject result = AssertSingleExecJsonResult(output);
        Assert.Equal(1, exitCode);
        Assert.Equal("session-transcript-invalid", result["errorCode"]?.GetValue<string>());
        Assert.Equal("Conversation transcript is missing or uses an unsupported schema version.", result["summary"]?.GetValue<string>());
        JsonObject payload = Assert.IsType<JsonObject>(result["payload"]);
        Assert.Equal("failure", payload["status"]?.GetValue<string>());
        Assert.Equal(1, payload["exitCode"]?.GetValue<int>());
        Assert.False(runnerFactoryInvoked);
    }

    [Fact]
    public void Exec_json_session_save_exception_renders_json_failure_without_leaking_exception_detail()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        FakeAgentRunner agentRunner = new(AgentRunResult.Success("agent completed task", [], []));
        FakeConversationStore store = new()
        {
            SaveException = new IOException("cannot write C:\\secret\\smoke.transcript.json")
        };

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(
                    workspacePath,
                    apiKey: "sk-test-secret",
                    apiKeySource: "OPENAI_API_KEY",
                    model: "gpt-test"),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => store,
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
                (_, _, _) => agentRunner)
            .Parse(["exec", "--json", "--workspace", temp.Path, "--session", "smoke", "summarize workspace"])
            .Invoke();

        JsonObject result = AssertSingleExecJsonResult(output);
        Assert.Equal(1, exitCode);
        Assert.Equal("session-store-error", result["errorCode"]?.GetValue<string>());
        Assert.Equal("Conversation session store operation failed.", result["summary"]?.GetValue<string>());
        JsonObject payload = Assert.IsType<JsonObject>(result["payload"]);
        Assert.Equal("failure", payload["status"]?.GetValue<string>());
        Assert.Equal(1, payload["exitCode"]?.GetValue<int>());
        Assert.Equal("smoke", store.SavedSessionName?.Value);
        Assert.NotNull(agentRunner.LastRequest);
        Assert.DoesNotContain("C:\\secret", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Exec_session_persists_tool_call_recorded_by_runner_into_passed_transcript()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        FakeConversationStore store = new();
        TranscriptRecordingAgentRunner agentRunner = new(
            callId: "call_tool_1",
            toolName: "workspace.search",
            argumentsJson: """{"query":"workspace"}""",
            result: ToolExecutionResult.Success("3 matching files", "approved"));

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(
                    workspacePath,
                    apiKey: "sk-test-secret",
                    apiKeySource: "OPENAI_API_KEY",
                    model: "gpt-test"),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => store,
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
                (_, _, _) => agentRunner)
            .Parse(["exec", "--workspace", temp.Path, "--session", "smoke", "summarize workspace"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal("smoke", store.LoadedSessionName?.Value);
        Assert.Equal("smoke", store.SavedSessionName?.Value);
        Assert.NotNull(store.SavedTranscript);
        Assert.Same(agentRunner.LastTranscript, store.SavedTranscript);
        ConversationToolCall toolCall = Assert.Single(store.SavedTranscript.ToolCalls);
        Assert.Equal("call_tool_1", toolCall.CallId);
        Assert.Equal("workspace.search", toolCall.ToolName);
        Assert.Equal("""{"query":"workspace"}""", toolCall.ArgumentsJson);
        Assert.Equal("approved", toolCall.ApprovalStatus);
        Assert.True(toolCall.Succeeded);
        Assert.Equal("3 matching files", toolCall.OutputSummary);
        Assert.Null(toolCall.FailureReason);
        Assert.Null(toolCall.ErrorCode);
        Assert.False(toolCall.Retryable);
    }

    [Fact]
    public void Exec_without_session_does_not_create_transcript()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        FakeAgentRunner agentRunner = new(AgentRunResult.Success("agent completed task", [], []));
        FakeConversationStore store = new();

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(
                    workspacePath,
                    apiKey: "sk-test-secret",
                    apiKeySource: "OPENAI_API_KEY",
                    model: "gpt-test"),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => store,
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
                (_, _, _) => agentRunner)
            .Parse(["exec", "--workspace", temp.Path, "summarize workspace"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Null(store.LoadedSessionName);
        Assert.Null(store.SavedSessionName);
        Assert.Null(store.SavedTranscript);
        Assert.Null(agentRunner.LastTranscript);
    }

    [Fact]
    public void Exec_output_rejects_unknown_value_before_running_task()
    {
        using TempDirectory temp = TempDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "note.txt"), "hello exec");
        using StringWriter output = new();
        List<string> loggedCommands = [];
        RootCommand command = CliCommandFactory.Create(
            output,
            CreateSnapshot,
            (commandName, _) => loggedCommands.Add(commandName));

        int exitCode = CliCommandFactory.Invoke(command, ["exec", "--workspace", temp.Path, "--output", "banana", "read note.txt"], output);

        Assert.Equal(2, exitCode);
        Assert.Empty(loggedCommands);
        Assert.DoesNotContain("hello exec", output.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--max-turns", "0")]
    [InlineData("--max-tool-calls", "0")]
    [InlineData("--timeout-seconds", "0")]
    public void Exec_limit_options_reject_non_positive_values_before_running_task(string option, string value)
    {
        using TempDirectory temp = TempDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "note.txt"), "hello exec");
        using StringWriter output = new();
        List<string> loggedCommands = [];
        RootCommand command = CliCommandFactory.Create(
            output,
            CreateSnapshot,
            (commandName, _) => loggedCommands.Add(commandName));

        int exitCode = CliCommandFactory.Invoke(
            command,
            ["exec", "--workspace", temp.Path, option, value, "read note.txt"],
            output);

        Assert.Equal(2, exitCode);
        Assert.Empty(loggedCommands);
        Assert.DoesNotContain("hello exec", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Exec_agent_failure_maps_to_nonzero_exit_code_and_error_code()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        FakeAgentRunner agentRunner = new(AgentRunResult.Failure(
            new AgentError(
                "agent-test-failure",
                "Agent test failure.",
                Retryable: false),
            [],
            [
                new AgentRunEvent(
                    Type: "agent.error",
                    Sequence: 0,
                    Timestamp: DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                    Message: "Agent test failure.",
                    ErrorCode: "agent-test-failure")
            ]));
        RootCommand command = CliCommandFactory.Create(
            output,
            workspacePath => CreateSnapshot(
                workspacePath,
                apiKey: "sk-test-secret",
                apiKeySource: "OPENAI_API_KEY",
                model: "gpt-test"),
            (_, _) => { },
            _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => new FakeConversationStore(),
            () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
            (_, _, _) => agentRunner);

        int exitCode = CliCommandFactory.Invoke(command, ["exec", "--workspace", temp.Path, "paint the moon"], output);

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("event: agent.error", text);
        Assert.Contains("result: failure exitCode=1", text);
        Assert.Contains("errorCode=agent-test-failure", text);
        Assert.DoesNotContain("sk-test-secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Exec_runner_not_supported_exception_surfaces_from_injected_runner()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        RootCommand command = CliCommandFactory.Create(
            output,
            workspacePath => CreateSnapshot(
                workspacePath,
                apiKey: "sk-test-secret",
                apiKeySource: "OPENAI_API_KEY",
                model: "gpt-test"),
            (_, _) => { },
            _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => new FakeConversationStore(),
            () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
            (_, _, _) => new ThrowingAgentRunner(new NotSupportedException("runner detail")));

        NotSupportedException exception = Assert.Throws<NotSupportedException>(
            () => CliCommandFactory.Invoke(command, ["exec", "--workspace", temp.Path, "paint the moon"], output));

        Assert.Contains("runner detail", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("agent-backend-unavailable", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("sk-test-secret", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Exec_default_with_missing_model_reports_missing_model()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        RootCommand command = CliCommandFactory.Create(
            output,
            workspacePath => CreateSnapshot(
                workspacePath,
                apiKey: "sk-test-secret",
                apiKeySource: "OPENAI_API_KEY",
                model: "not configured"));

        int exitCode = CliCommandFactory.Invoke(command, ["exec", "--workspace", temp.Path, "summarize workspace"], output);

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("event: agent.error", text);
        Assert.Contains("result: failure exitCode=1", text);
        Assert.Contains("errorCode=missing-model", text);
        Assert.DoesNotContain("sk-test-secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Exec_default_with_configured_model_and_missing_api_key_reports_missing_openai_api_key()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        RootCommand command = CliCommandFactory.Create(
            output,
            workspacePath => CreateSnapshot(
                workspacePath,
                apiKey: null,
                apiKeySource: "missing",
                model: "gpt-test"));

        int exitCode = CliCommandFactory.Invoke(command, ["exec", "--workspace", temp.Path, "summarize workspace"], output);

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("event: agent.error", text);
        Assert.Contains("result: failure exitCode=1", text);
        Assert.Contains("errorCode=missing-openai-api-key", text);
    }

    [Fact]
    public void Exec_default_with_unsupported_api_key_source_reports_unsupported_api_key_source_without_secret()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        RootCommand command = CliCommandFactory.Create(
            output,
            workspacePath => CreateSnapshot(
                workspacePath,
                apiKey: "sk-workspace-secret",
                apiKeySource: "workspace config",
                model: "gpt-test"));

        int exitCode = CliCommandFactory.Invoke(command, ["exec", "--workspace", temp.Path, "summarize workspace"], output);

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("event: agent.error", text);
        Assert.Contains("result: failure exitCode=1", text);
        Assert.Contains("errorCode=unsupported-api-key-source", text);
        Assert.DoesNotContain("sk-workspace-secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Exec_default_direct_backend_reports_unavailable_until_gateway_is_enabled()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        RootCommand command = CliCommandFactory.Create(
            output,
            workspacePath => CreateSnapshot(
                workspacePath,
                apiKey: "sk-test-secret",
                apiKeySource: "OPENAI_API_KEY",
                model: "gpt-test"));

        int exitCode = CliCommandFactory.Invoke(command, ["exec", "--workspace", temp.Path, "summarize workspace"], output);

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("event: agent.error", text);
        Assert.Contains("result: failure exitCode=1", text);
        Assert.Contains("errorCode=agent-backend-unavailable", text);
        Assert.DoesNotContain("sk-test-secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Exec_command_writes_command_log_through_delegate()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        List<string> loggedCommands = [];
        FakeAgentRunner agentRunner = new(AgentRunResult.Success("logged", []));
        RootCommand command = CliCommandFactory.Create(
            output,
            CreateSnapshot,
            (commandName, _) => loggedCommands.Add(commandName),
            _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => new FakeConversationStore(),
            () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
            (_, _, _) => agentRunner);

        int exitCode = CliCommandFactory.Invoke(command, ["exec", "--workspace", temp.Path, "read note.txt"], output);

        Assert.Equal(0, exitCode);
        Assert.Equal(["exec"], loggedCommands);
    }

    [Fact]
    public void Run_unsupported_task_returns_task_failure_exit_code()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        RootCommand command = CliCommandFactory.Create(output, workspacePath => CreateSnapshot(workspacePath));

        int exitCode = CliCommandFactory.Invoke(command, ["run", "--workspace", temp.Path, "paint the moon"], output);

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("status: failed", text);
        Assert.Contains("errorCode: unsupported-run-task", text);
    }

    [Fact]
    public void Session_export_defaults_to_json_transcript_file()
    {
        using TempDirectory temp = TempDirectory.Create();
        string userHome = Path.Combine(temp.Path, "user-home");
        string workspace = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(workspace);
        string sessionDirectory = Path.Combine(userHome, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        File.WriteAllText(Path.Combine(sessionDirectory, "smoke.transcript.json"), """
        {
          "schemaVersion": 1,
          "sessionName": "smoke",
          "createdAtUtc": "2024-01-01T00:00:00+00:00",
          "updatedAtUtc": "2024-01-01T00:00:00+00:00",
          "messages": [],
          "toolCalls": [],
          "errors": []
        }
        """);
        using StringWriter exportOutput = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: workspace,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: Path.Combine(userHome, ".caicli", "config.json"));

        int exportExitCode = CliCommandFactory
            .Create(exportOutput, _ => snapshot)
            .Parse(["session", "export", "smoke"])
            .Invoke();

        Assert.Equal(0, exportExitCode);
        Assert.Contains("\"sessionName\": \"smoke\"", exportOutput.ToString());
    }

    [Fact]
    public void Session_export_format_json_prints_json_transcript_file()
    {
        using TempDirectory temp = TempDirectory.Create();
        string userHome = Path.Combine(temp.Path, "user-home");
        string workspace = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(workspace);
        string sessionDirectory = Path.Combine(userHome, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        File.WriteAllText(Path.Combine(sessionDirectory, "smoke.transcript.json"), """
        {
          "schemaVersion": 1,
          "sessionName": "smoke",
          "createdAtUtc": "2024-01-01T00:00:00+00:00",
          "updatedAtUtc": "2024-01-01T00:00:00+00:00",
          "messages": [],
          "toolCalls": [],
          "errors": []
        }
        """);
        using StringWriter exportOutput = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: workspace,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: Path.Combine(userHome, ".caicli", "config.json"));

        int exportExitCode = CliCommandFactory
            .Create(exportOutput, _ => snapshot)
            .Parse(["session", "export", "--format", "json", "smoke"])
            .Invoke();

        Assert.Equal(0, exportExitCode);
        Assert.Contains("\"sessionName\": \"smoke\"", exportOutput.ToString());
    }

    [Fact]
    public void Session_export_format_json_does_not_create_conversation_store()
    {
        using TempDirectory temp = TempDirectory.Create();
        string userHome = Path.Combine(temp.Path, "user-home");
        string workspace = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(workspace);
        string sessionDirectory = Path.Combine(userHome, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        File.WriteAllText(Path.Combine(sessionDirectory, "smoke.transcript.json"), """
        {
          "schemaVersion": 1,
          "sessionName": "smoke",
          "createdAtUtc": "2024-01-01T00:00:00+00:00",
          "updatedAtUtc": "2024-01-01T00:00:00+00:00",
          "messages": [],
          "toolCalls": [],
          "errors": []
        }
        """);
        using StringWriter exportOutput = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: workspace,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: Path.Combine(userHome, ".caicli", "config.json"));
        int storeFactoryCalls = 0;

        int exportExitCode = CliCommandFactory
            .Create(
                exportOutput,
                _ => snapshot,
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ =>
                {
                    storeFactoryCalls++;
                    return new FakeConversationStore();
                },
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"))
            .Parse(["session", "export", "--format", "json", "smoke"])
            .Invoke();

        Assert.Equal(0, exportExitCode);
        Assert.Equal(0, storeFactoryCalls);
        Assert.Contains("\"sessionName\": \"smoke\"", exportOutput.ToString());
    }

    [Fact]
    public void Session_export_format_markdown_with_file_store_prints_safe_readable_transcript_from_disk()
    {
        using TempDirectory temp = TempDirectory.Create();
        string userHome = Path.Combine(temp.Path, "user-home");
        string workspace = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(workspace);
        string sessionDirectory = Path.Combine(userHome, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        File.WriteAllText(Path.Combine(sessionDirectory, "smoke.transcript.json"), """
        {
          "schemaVersion": 1,
          "sessionName": "smoke",
          "createdAtUtc": "2024-01-01T00:00:00+00:00",
          "updatedAtUtc": "2024-01-01T00:00:04+00:00",
          "messages": [
            {
              "role": "user",
              "createdAtUtc": "2024-01-01T00:00:01+00:00",
              "content": "hello from disk",
              "provider": null,
              "model": null,
              "responseId": null
            }
          ],
          "toolCalls": [
            {
              "createdAtUtc": "2024-01-01T00:00:02+00:00",
              "callId": "call_test",
              "toolName": "workspace.read_text",
              "argumentsJson": "{\"apiKey\":\"sk-tool-secret\",\"path\":\"note.txt\"}",
              "approvalStatus": "approved",
              "completedAtUtc": "2024-01-01T00:00:03+00:00",
              "succeeded": true,
              "outputSummary": "read note from disk",
              "failureReason": null,
              "errorCode": null,
              "retryable": false
            }
          ],
          "errors": []
        }
        """);
        using StringWriter exportOutput = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: workspace,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: Path.Combine(userHome, ".caicli", "config.json"));

        int exportExitCode = CliCommandFactory
            .Create(exportOutput, _ => snapshot)
            .Parse(["session", "export", "--format", "markdown", "smoke"])
            .Invoke();

        string text = exportOutput.ToString();
        Assert.Equal(0, exportExitCode);
        Assert.Contains("# Session: smoke", text);
        Assert.Contains("hello from disk", text);
        Assert.Contains("workspace.read_text succeeded", text);
        Assert.Contains("read note from disk", text);
        Assert.DoesNotContain("ArgumentsJson", text, StringComparison.Ordinal);
        Assert.DoesNotContain("argumentsJson", text, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-tool-secret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("apiKey", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Session_export_format_json_with_malformed_file_transcript_returns_safe_failure()
    {
        using TempDirectory temp = TempDirectory.Create();
        string userHome = Path.Combine(temp.Path, "user-home");
        string workspace = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(workspace);
        string sessionDirectory = Path.Combine(userHome, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        File.WriteAllText(Path.Combine(sessionDirectory, "smoke.transcript.json"), """{"schemaVersion":""");
        using StringWriter exportOutput = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: workspace,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: Path.Combine(userHome, ".caicli", "config.json"));

        int exportExitCode = CliCommandFactory.Invoke(
            CliCommandFactory.Create(exportOutput, _ => snapshot),
            ["session", "export", "--format", "json", "smoke"],
            exportOutput);

        string text = exportOutput.ToString();
        Assert.Equal(1, exportExitCode);
        Assert.Contains("status: failed", text);
        Assert.Contains("errorCode: session-transcript-invalid", text);
        Assert.Contains("Conversation transcript is missing or uses an unsupported schema version.", text);
        Assert.DoesNotContain("JsonReaderException", text, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Text.Json", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Session_export_format_markdown_with_malformed_file_transcript_returns_safe_failure()
    {
        using TempDirectory temp = TempDirectory.Create();
        string userHome = Path.Combine(temp.Path, "user-home");
        string workspace = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(workspace);
        string sessionDirectory = Path.Combine(userHome, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        File.WriteAllText(Path.Combine(sessionDirectory, "smoke.transcript.json"), """{"schemaVersion":""");
        using StringWriter exportOutput = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: workspace,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: Path.Combine(userHome, ".caicli", "config.json"));

        int exportExitCode = CliCommandFactory.Invoke(
            CliCommandFactory.Create(exportOutput, _ => snapshot),
            ["session", "export", "--format", "markdown", "smoke"],
            exportOutput);

        string text = exportOutput.ToString();
        Assert.Equal(1, exportExitCode);
        Assert.Contains("status: failed", text);
        Assert.Contains("errorCode: session-transcript-invalid", text);
        Assert.Contains("Conversation transcript is missing or uses an unsupported schema version.", text);
        Assert.DoesNotContain("JsonReaderException", text, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Text.Json", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("toolCalls")]
    [InlineData("errors")]
    public void Session_export_format_markdown_with_null_tool_call_or_error_returns_safe_failure(
        string nullEntryCollectionName)
    {
        using TempDirectory temp = TempDirectory.Create();
        string userHome = Path.Combine(temp.Path, "user-home");
        string workspace = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(workspace);
        string sessionDirectory = Path.Combine(userHome, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        string toolCallsJson = nullEntryCollectionName == "toolCalls" ? "[null]" : "[]";
        string errorsJson = nullEntryCollectionName == "errors" ? "[null]" : "[]";
        File.WriteAllText(Path.Combine(sessionDirectory, "smoke.transcript.json"), $$"""
        {
          "schemaVersion": 1,
          "sessionName": "smoke",
          "createdAtUtc": "2024-01-01T00:00:00+00:00",
          "updatedAtUtc": "2024-01-01T00:00:00+00:00",
          "messages": [],
          "toolCalls": {{toolCallsJson}},
          "errors": {{errorsJson}}
        }
        """);
        using StringWriter exportOutput = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: workspace,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: Path.Combine(userHome, ".caicli", "config.json"));

        int exportExitCode = CliCommandFactory.Invoke(
            CliCommandFactory.Create(exportOutput, _ => snapshot),
            ["session", "export", "--format", "markdown", "smoke"],
            exportOutput);

        string text = exportOutput.ToString();
        Assert.Equal(1, exportExitCode);
        Assert.Contains("status: failed", text);
        Assert.Contains("errorCode: session-transcript-invalid", text);
        Assert.Contains("summary:", text);
        Assert.Contains("Conversation transcript is missing or uses an unsupported schema version.", text);
        Assert.DoesNotContain("NullReferenceException", text, StringComparison.Ordinal);
        Assert.DoesNotContain("System.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Session_export_format_markdown_prints_safe_readable_transcript()
    {
        using StringWriter output = new();
        ConversationTranscript transcript = ConversationTranscript.Create(
            "smoke",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        transcript.AddUserMessage("hello assistant", DateTimeOffset.Parse("2024-01-01T00:00:01Z"));
        transcript.AddAssistantMessage(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "hello user"), DateTimeOffset.Parse("2024-01-01T00:00:02Z"));
        transcript.AddError(new ModelError(
            Provider: "openai",
            Operation: "responses.create",
            StatusCode: null,
            LocalErrorCode: "missing-openai-api-key",
            SafeMessage: "safe message without secret",
            Retryable: false), DateTimeOffset.Parse("2024-01-01T00:00:03Z"));
        transcript.AddToolCall(ConversationToolCall.FromExecution(
            "call_test",
            "workspace.read_text",
            """{"apiKey":"sk-tool-secret","path":"note.txt"}""",
            ToolExecutionResult.Success("read safe summary"),
            DateTimeOffset.Parse("2024-01-01T00:00:04Z")));
        FakeConversationStore store = new()
        {
            Transcript = transcript
        };

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(workspacePath),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => store,
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"))
            .Parse(["session", "export", "--format", "markdown", "smoke"])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Equal("smoke", store.TryLoadedSessionName?.Value);
        Assert.Contains("# Session: smoke", text);
        Assert.Contains("- Created: 2024-01-01T00:00:00.0000000+00:00", text);
        Assert.Contains("- Updated: 2024-01-01T00:00:04.0000000+00:00", text);
        Assert.Contains("- Turns: 1", text);
        Assert.Contains("- Tool calls: 1", text);
        Assert.Contains("### User - 2024-01-01T00:00:01.0000000+00:00", text);
        Assert.Contains("hello assistant", text);
        Assert.Contains("### Assistant - 2024-01-01T00:00:02.0000000+00:00", text);
        Assert.Contains("hello user", text);
        Assert.Contains("## Errors", text);
        Assert.Contains("- 2024-01-01T00:00:03.0000000+00:00 missing-openai-api-key", text);
        Assert.Contains("safe message without secret", text);
        Assert.Contains("## Tool Calls", text);
        Assert.Contains("- 2024-01-01T00:00:04.0000000+00:00 workspace.read_text succeeded", text);
        Assert.Contains("read safe summary", text);
        Assert.DoesNotContain("sk-", text, StringComparison.Ordinal);
        Assert.DoesNotContain("apiKey", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ArgumentsJson", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Session_export_format_markdown_accepts_case_insensitive_value()
    {
        using StringWriter output = new();
        ConversationTranscript transcript = ConversationTranscript.Create(
            "smoke",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        transcript.AddUserMessage("hello assistant", DateTimeOffset.Parse("2024-01-01T00:00:01Z"));
        FakeConversationStore store = new()
        {
            Transcript = transcript
        };

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(workspacePath),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => store,
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"))
            .Parse(["session", "export", "--format", "Markdown", "smoke"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal("smoke", store.TryLoadedSessionName?.Value);
        Assert.Contains("# Session: smoke", output.ToString());
    }

    [Fact]
    public void Session_export_format_rejects_invalid_value()
    {
        using StringWriter output = new();
        RootCommand command = CliCommandFactory.Create(output, workspacePath => CreateSnapshot(workspacePath));

        int exitCode = CliCommandFactory.Invoke(command, ["session", "export", "--format", "xml", "smoke"], output);

        Assert.Equal(2, exitCode);
        Assert.Contains("Invalid value for --format. Allowed values are json and markdown.", output.ToString());
    }

    [Fact]
    public void Session_clear_deletes_transcript_file()
    {
        using TempDirectory temp = TempDirectory.Create();
        string userHome = Path.Combine(temp.Path, "user-home");
        string workspace = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(workspace);
        string sessionDirectory = Path.Combine(userHome, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        File.WriteAllText(Path.Combine(sessionDirectory, "smoke.transcript.json"), """
        {
          "schemaVersion": 1,
          "sessionName": "smoke",
          "createdAtUtc": "2024-01-01T00:00:00+00:00",
          "updatedAtUtc": "2024-01-01T00:00:00+00:00",
          "messages": [],
          "toolCalls": [],
          "errors": []
        }
        """);
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: workspace,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: Path.Combine(userHome, ".caicli", "config.json"));

        using StringWriter clearOutput = new();
        int clearExitCode = CliCommandFactory
            .Create(clearOutput, _ => snapshot)
            .Parse(["session", "clear", "smoke"])
            .Invoke();

        Assert.Equal(0, clearExitCode);
        Assert.Contains("status: cleared", clearOutput.ToString());
        Assert.False(File.Exists(Path.Combine(sessionDirectory, "smoke.transcript.json")));
    }

    [Fact]
    public void Session_list_writes_session_summaries()
    {
        using StringWriter output = new();
        FakeConversationStore store = new()
        {
            Summaries =
            [
                new ConversationTranscriptSummary(
                    "beta",
                    DateTimeOffset.Parse("2024-01-02T00:00:00Z"),
                    DateTimeOffset.Parse("2024-01-02T00:03:00Z"),
                    TurnCount: 4,
                    ToolCallCount: 3),
                new ConversationTranscriptSummary(
                    "alpha",
                    DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                    DateTimeOffset.Parse("2024-01-01T00:01:00Z"),
                    TurnCount: 2,
                    ToolCallCount: 1)
            ]
        };

        RootCommand command = CliCommandFactory.Create(
            output,
            CreateSnapshot,
            (_, _) => { },
            _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp_test", "ok"))),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => store,
            () => DateTimeOffset.Parse("2024-01-03T00:00:00Z"));

        int exitCode = CliCommandFactory.Invoke(command, ["session", "list"], output);

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Equal(
            [
                "C# AI CLI sessions",
                "- alpha created=2024-01-01T00:00:00.0000000+00:00 updated=2024-01-01T00:01:00.0000000+00:00 turns=2 toolCalls=1",
                "- beta created=2024-01-02T00:00:00.0000000+00:00 updated=2024-01-02T00:03:00.0000000+00:00 turns=4 toolCalls=3"
            ],
            text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void Session_list_with_malformed_file_transcript_returns_safe_failure()
    {
        using TempDirectory temp = TempDirectory.Create();
        string userHome = Path.Combine(temp.Path, "user-home");
        string workspace = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(workspace);
        string sessionDirectory = Path.Combine(userHome, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        File.WriteAllText(Path.Combine(sessionDirectory, "smoke.transcript.json"), """{"schemaVersion":""");
        using StringWriter output = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: workspace,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: Path.Combine(userHome, ".caicli", "config.json"));

        int exitCode = CliCommandFactory.Invoke(
            CliCommandFactory.Create(output, _ => snapshot),
            ["session", "list"],
            output);

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("status: failed", text);
        Assert.Contains("errorCode: session-transcript-invalid", text);
        Assert.Contains("Conversation transcript is missing or uses an unsupported schema version.", text);
        Assert.DoesNotContain("JsonReaderException", text, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Text.Json", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Session_list_writes_empty_status_for_empty_store()
    {
        using StringWriter output = new();
        FakeConversationStore store = new();

        RootCommand command = CliCommandFactory.Create(
            output,
            CreateSnapshot,
            (_, _) => { },
            _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp_test", "ok"))),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => store,
            () => DateTimeOffset.Parse("2024-01-03T00:00:00Z"));

        int exitCode = CliCommandFactory.Invoke(command, ["session", "list"], output);

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("C# AI CLI sessions", text);
        Assert.Contains("status: empty", text);
    }

    [Fact]
    public void Session_list_writes_command_log_through_delegate()
    {
        using StringWriter output = new();
        List<string> loggedCommands = [];
        FakeConversationStore store = new();

        RootCommand command = CliCommandFactory.Create(
            output,
            CreateSnapshot,
            (commandName, _) => loggedCommands.Add(commandName),
            _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp_test", "ok"))),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => store,
            () => DateTimeOffset.Parse("2024-01-03T00:00:00Z"));

        int exitCode = CliCommandFactory.Invoke(command, ["session", "list"], output);

        Assert.Equal(0, exitCode);
        Assert.Equal(["session list"], loggedCommands);
    }

    [Fact]
    public void Session_show_writes_session_summary()
    {
        using StringWriter output = new();
        FakeConversationStore store = new()
        {
            Summaries =
            [
                new ConversationTranscriptSummary(
                    "smoke",
                    DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                    DateTimeOffset.Parse("2024-01-01T00:05:00Z"),
                    TurnCount: 2,
                    ToolCallCount: 1)
            ]
        };

        RootCommand command = CliCommandFactory.Create(
            output,
            CreateSnapshot,
            (_, _) => { },
            _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp_test", "ok"))),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => store,
            () => DateTimeOffset.Parse("2024-01-03T00:00:00Z"));

        int exitCode = CliCommandFactory.Invoke(command, ["session", "show", "smoke"], output);

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Equal(
            [
                "C# AI CLI session",
                "name: smoke",
                "createdAtUtc: 2024-01-01T00:00:00.0000000+00:00",
                "updatedAtUtc: 2024-01-01T00:05:00.0000000+00:00",
                "turnCount: 2",
                "toolCallCount: 1"
            ],
            text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void Session_show_missing_session_returns_session_not_found()
    {
        using StringWriter output = new();
        FakeConversationStore store = new();

        RootCommand command = CliCommandFactory.Create(
            output,
            CreateSnapshot,
            (_, _) => { },
            _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp_test", "ok"))),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => store,
            () => DateTimeOffset.Parse("2024-01-03T00:00:00Z"));

        int exitCode = CliCommandFactory.Invoke(command, ["session", "show", "missing"], output);

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Equal(
            [
                "status: failed",
                "errorCode: session-not-found",
                "summary:",
                "Session transcript was not found."
            ],
            text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void Session_show_writes_command_log_through_delegate()
    {
        using StringWriter output = new();
        List<string> loggedCommands = [];
        FakeConversationStore store = new()
        {
            Summaries =
            [
                new ConversationTranscriptSummary(
                    "smoke",
                    DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                    DateTimeOffset.Parse("2024-01-01T00:05:00Z"),
                    TurnCount: 2,
                    ToolCallCount: 1)
            ]
        };

        RootCommand command = CliCommandFactory.Create(
            output,
            CreateSnapshot,
            (commandName, _) => loggedCommands.Add(commandName),
            _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp_test", "ok"))),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => store,
            () => DateTimeOffset.Parse("2024-01-03T00:00:00Z"));

        int exitCode = CliCommandFactory.Invoke(command, ["session", "show", "smoke"], output);

        Assert.Equal(0, exitCode);
        Assert.Equal(["session show"], loggedCommands);
    }

    [Fact]
    public void Session_commands_wrap_expected_store_failures_without_leaking_details()
    {
        static RootCommand CreateCommand(StringWriter output, FakeConversationStore store)
        {
            return CliCommandFactory.Create(
                output,
                CreateSnapshot,
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp_test", "ok"))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => store,
                () => DateTimeOffset.Parse("2024-01-03T00:00:00Z"));
        }

        foreach ((string[] args, FakeConversationStore store) in new[]
        {
            (new[] { "session", "list" }, new FakeConversationStore { ListSummariesException = new IOException("cannot read C:\\secret\\smoke.transcript.json") }),
            (new[] { "session", "show", "smoke" }, new FakeConversationStore { TryGetSummaryException = new IOException("cannot read C:\\secret\\smoke.transcript.json") }),
            (new[] { "session", "export", "--format", "markdown", "smoke" }, new FakeConversationStore { TryLoadException = new IOException("cannot read C:\\secret\\smoke.transcript.json") }),
            (new[] { "session", "clear", "smoke" }, new FakeConversationStore { DeleteException = new IOException("cannot delete C:\\secret\\smoke.transcript.json") }),
            (new[] { "session", "delete", "smoke" }, new FakeConversationStore { DeleteException = new IOException("cannot delete C:\\secret\\smoke.transcript.json") }),
            (new[] { "session", "rename", "smoke", "archive" }, new FakeConversationStore { RenameException = new IOException("cannot move C:\\secret\\smoke.transcript.json") }),
        })
        {
            using StringWriter output = new();
            int exitCode = CliCommandFactory.Invoke(CreateCommand(output, store), args, output);

            string text = output.ToString();
            Assert.Equal(1, exitCode);
            Assert.Contains("status: failed", text);
            Assert.Contains("errorCode: session-store-error", text);
            Assert.Contains("Conversation session store operation failed.", text);
            Assert.DoesNotContain("C:\\secret", text, StringComparison.Ordinal);
            Assert.DoesNotContain("System.", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Session_show_does_not_report_store_factory_argument_exception_as_invalid_session_name()
    {
        using StringWriter output = new();
        RootCommand command = CliCommandFactory.Create(
            output,
            CreateSnapshot,
            (_, _) => { },
            _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp_test", "ok"))),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => throw new ArgumentException("factory failed"),
            () => DateTimeOffset.Parse("2024-01-03T00:00:00Z"));

        int exitCode = CliCommandFactory.Invoke(command, ["session", "show", "smoke"], output);

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.DoesNotContain("errorCode: invalid-session-name", text, StringComparison.Ordinal);
        Assert.Contains("factory failed", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Session_rename_calls_store_and_writes_ordered_success_output()
    {
        using StringWriter output = new();
        FakeConversationStore store = new()
        {
            RenameResult = true
        };

        RootCommand command = CliCommandFactory.Create(
            output,
            CreateSnapshot,
            (_, _) => { },
            _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp_test", "ok"))),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => store,
            () => DateTimeOffset.Parse("2024-01-03T00:00:00Z"));

        int exitCode = CliCommandFactory.Invoke(command, ["session", "rename", "smoke", "archive"], output);

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Equal("smoke", store.RenamedSourceSessionName?.Value);
        Assert.Equal("archive", store.RenamedDestinationSessionName?.Value);
        Assert.Equal(
            [
                "status: renamed",
                "from: smoke",
                "to: archive"
            ],
            text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void Session_rename_with_file_store_moves_transcript_file_and_updates_session_name()
    {
        using TempDirectory temp = TempDirectory.Create();
        string userHome = Path.Combine(temp.Path, "user-home");
        string workspace = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(workspace);
        string sessionDirectory = Path.Combine(userHome, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        string sourcePath = Path.Combine(sessionDirectory, "smoke.transcript.json");
        string destinationPath = Path.Combine(sessionDirectory, "archive.transcript.json");
        File.WriteAllText(sourcePath, """
        {
          "schemaVersion": 1,
          "sessionName": "smoke",
          "createdAtUtc": "2024-01-01T00:00:00+00:00",
          "updatedAtUtc": "2024-01-01T00:00:01+00:00",
          "messages": [
            {
              "role": "user",
              "createdAtUtc": "2024-01-01T00:00:01+00:00",
              "content": "keep me",
              "provider": null,
              "model": null,
              "responseId": null
            }
          ],
          "toolCalls": [],
          "errors": []
        }
        """);
        using StringWriter output = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: workspace,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: Path.Combine(userHome, ".caicli", "config.json"));

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["session", "rename", "smoke", "archive"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.False(File.Exists(sourcePath));
        Assert.True(File.Exists(destinationPath));
        JsonObject json = ReadJsonObject(destinationPath);
        Assert.Equal("archive", json["sessionName"]?.GetValue<string>());
        Assert.Equal("keep me", json["messages"]?[0]?["content"]?.GetValue<string>());
        Assert.Contains("status: renamed", output.ToString());
    }

    [Fact]
    public void Session_rename_with_file_store_preserves_files_when_destination_exists()
    {
        using TempDirectory temp = TempDirectory.Create();
        string userHome = Path.Combine(temp.Path, "user-home");
        string workspace = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(workspace);
        string sessionDirectory = Path.Combine(userHome, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        string sourcePath = Path.Combine(sessionDirectory, "smoke.transcript.json");
        string destinationPath = Path.Combine(sessionDirectory, "archive.transcript.json");
        File.WriteAllText(sourcePath, """
        {
          "schemaVersion": 1,
          "sessionName": "smoke",
          "createdAtUtc": "2024-01-01T00:00:00+00:00",
          "updatedAtUtc": "2024-01-01T00:00:00+00:00",
          "messages": [],
          "toolCalls": [],
          "errors": []
        }
        """);
        File.WriteAllText(destinationPath, """
        {
          "schemaVersion": 1,
          "sessionName": "archive",
          "createdAtUtc": "2024-01-02T00:00:00+00:00",
          "updatedAtUtc": "2024-01-02T00:00:00+00:00",
          "messages": [],
          "toolCalls": [],
          "errors": []
        }
        """);
        string originalSourceJson = File.ReadAllText(sourcePath);
        string originalDestinationJson = File.ReadAllText(destinationPath);
        using StringWriter output = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: workspace,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: Path.Combine(userHome, ".caicli", "config.json"));

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["session", "rename", "smoke", "archive"])
            .Invoke();

        Assert.Equal(1, exitCode);
        Assert.Equal(originalSourceJson, File.ReadAllText(sourcePath));
        Assert.Equal(originalDestinationJson, File.ReadAllText(destinationPath));
        Assert.Contains("status: failed", output.ToString());
        Assert.Contains("errorCode: session-rename-failed", output.ToString());
    }

    [Fact]
    public void Session_rename_failure_returns_generic_safe_failure_output()
    {
        using StringWriter output = new();
        FakeConversationStore store = new()
        {
            RenameResult = false
        };

        RootCommand command = CliCommandFactory.Create(
            output,
            CreateSnapshot,
            (_, _) => { },
            _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp_test", "ok"))),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => store,
            () => DateTimeOffset.Parse("2024-01-03T00:00:00Z"));

        int exitCode = CliCommandFactory.Invoke(command, ["session", "rename", "missing", "archive"], output);

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Equal("missing", store.RenamedSourceSessionName?.Value);
        Assert.Equal("archive", store.RenamedDestinationSessionName?.Value);
        Assert.Equal(
            [
                "status: failed",
                "errorCode: session-rename-failed",
                "summary:",
                "Session could not be renamed because the source is missing or the destination already exists."
            ],
            text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void Session_rename_writes_command_log_through_delegate()
    {
        using StringWriter output = new();
        List<string> loggedCommands = [];
        FakeConversationStore store = new()
        {
            RenameResult = true
        };

        RootCommand command = CliCommandFactory.Create(
            output,
            CreateSnapshot,
            (commandName, _) => loggedCommands.Add(commandName),
            _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp_test", "ok"))),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => store,
            () => DateTimeOffset.Parse("2024-01-03T00:00:00Z"));

        int exitCode = CliCommandFactory.Invoke(command, ["session", "rename", "smoke", "archive"], output);

        Assert.Equal(0, exitCode);
        Assert.Equal(["session rename"], loggedCommands);
    }

    [Fact]
    public void Session_delete_calls_store_and_writes_ordered_success_output()
    {
        using StringWriter output = new();
        FakeConversationStore store = new()
        {
            DeleteResult = true
        };

        RootCommand command = CliCommandFactory.Create(
            output,
            CreateSnapshot,
            (_, _) => { },
            _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp_test", "ok"))),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => store,
            () => DateTimeOffset.Parse("2024-01-03T00:00:00Z"));

        int exitCode = CliCommandFactory.Invoke(command, ["session", "delete", "smoke"], output);

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Equal("smoke", store.DeletedSessionName?.Value);
        Assert.Equal(
            [
                "status: deleted",
                "session: smoke"
            ],
            text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void Session_delete_with_file_store_removes_transcript_file()
    {
        using TempDirectory temp = TempDirectory.Create();
        string userHome = Path.Combine(temp.Path, "user-home");
        string workspace = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(workspace);
        string sessionDirectory = Path.Combine(userHome, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        string sessionPath = Path.Combine(sessionDirectory, "smoke.transcript.json");
        File.WriteAllText(sessionPath, """
        {
          "schemaVersion": 1,
          "sessionName": "smoke",
          "createdAtUtc": "2024-01-01T00:00:00+00:00",
          "updatedAtUtc": "2024-01-01T00:00:00+00:00",
          "messages": [],
          "toolCalls": [],
          "errors": []
        }
        """);
        using StringWriter output = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: workspace,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: Path.Combine(userHome, ".caicli", "config.json"));

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["session", "delete", "smoke"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.False(File.Exists(sessionPath));
        Assert.Contains("status: deleted", output.ToString());
    }

    [Fact]
    public void Session_delete_missing_session_returns_session_not_found()
    {
        using StringWriter output = new();
        FakeConversationStore store = new()
        {
            DeleteResult = false
        };

        RootCommand command = CliCommandFactory.Create(
            output,
            CreateSnapshot,
            (_, _) => { },
            _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp_test", "ok"))),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => store,
            () => DateTimeOffset.Parse("2024-01-03T00:00:00Z"));

        int exitCode = CliCommandFactory.Invoke(command, ["session", "delete", "missing"], output);

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Equal("missing", store.DeletedSessionName?.Value);
        Assert.Equal(
            [
                "status: not-found",
                "errorCode: session-not-found"
            ],
            text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void Session_clear_calls_store_delete_and_preserves_success_output()
    {
        using StringWriter output = new();
        FakeConversationStore store = new()
        {
            DeleteResult = true
        };

        RootCommand command = CliCommandFactory.Create(
            output,
            CreateSnapshot,
            (_, _) => { },
            _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp_test", "ok"))),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => store,
            () => DateTimeOffset.Parse("2024-01-03T00:00:00Z"));

        int exitCode = CliCommandFactory.Invoke(command, ["session", "clear", "smoke"], output);

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Equal("smoke", store.DeletedSessionName?.Value);
        Assert.Equal(
            [
                "status: cleared",
                "session: smoke"
            ],
            text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void Session_clear_missing_session_preserves_not_found_exit_zero_output()
    {
        using StringWriter output = new();
        FakeConversationStore store = new()
        {
            DeleteResult = false
        };

        RootCommand command = CliCommandFactory.Create(
            output,
            CreateSnapshot,
            (_, _) => { },
            _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp_test", "ok"))),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => store,
            () => DateTimeOffset.Parse("2024-01-03T00:00:00Z"));

        int exitCode = CliCommandFactory.Invoke(command, ["session", "clear", "missing"], output);

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Equal("missing", store.DeletedSessionName?.Value);
        Assert.Equal(
            [
                "status: not-found"
            ],
            text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void Session_delete_writes_command_log_through_delegate()
    {
        using StringWriter output = new();
        List<string> loggedCommands = [];
        FakeConversationStore store = new()
        {
            DeleteResult = true
        };

        RootCommand command = CliCommandFactory.Create(
            output,
            CreateSnapshot,
            (commandName, _) => loggedCommands.Add(commandName),
            _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp_test", "ok"))),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => store,
            () => DateTimeOffset.Parse("2024-01-03T00:00:00Z"));

        int exitCode = CliCommandFactory.Invoke(command, ["session", "delete", "smoke"], output);

        Assert.Equal(0, exitCode);
        Assert.Equal(["session delete"], loggedCommands);
    }

    [Fact]
    public void Session_export_invalid_session_name_returns_safe_failure()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: temp.Path,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: Path.Combine(temp.Path, ".caicli", "config.json"));
        RootCommand command = CliCommandFactory.Create(output, _ => snapshot);

        int exitCode = CliCommandFactory.Invoke(command, ["session", "export", "../secret"], output);

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("status: failed", text);
        Assert.Contains("errorCode: invalid-session-name", text);
        Assert.Contains("Session name contains invalid path characters.", text);
        Assert.DoesNotContain("Unhandled exception", text, StringComparison.Ordinal);
        Assert.DoesNotContain("System.", text, StringComparison.Ordinal);
        Assert.DoesNotContain(temp.Path, text, StringComparison.OrdinalIgnoreCase);
    }

    public static TheoryData<string[]> InvalidSessionNameCommandCases => new()
    {
        new[] { "session", "show", "../secret" },
        new[] { "session", "export", "../secret" },
        new[] { "session", "export", "--format", "markdown", "../secret" },
        new[] { "session", "clear", "../secret" },
        new[] { "session", "delete", "../secret" },
        new[] { "session", "rename", "../secret", "archive" },
        new[] { "session", "rename", "smoke", "../secret" },
    };

    [Theory]
    [MemberData(nameof(InvalidSessionNameCommandCases))]
    public void Session_commands_reject_invalid_session_names_without_creating_files(string[] args)
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string userHome = Path.Combine(temp.Path, "user-home");
        string workspace = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(workspace);
        string sessionDirectory = Path.Combine(userHome, ".caicli", "sessions");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: workspace,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: Path.Combine(userHome, ".caicli", "config.json"));

        RootCommand command = CliCommandFactory.Create(output, _ => snapshot);

        int exitCode = CliCommandFactory.Invoke(command, args, output);

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("status: failed", text);
        Assert.Contains("errorCode: invalid-session-name", text);
        Assert.Contains("summary:", text);
        Assert.Contains("Session name contains invalid path characters.", text);
        Assert.DoesNotContain("Unhandled exception", text, StringComparison.Ordinal);
        Assert.DoesNotContain("System.", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Directory.GetCurrentDirectory(), text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(temp.Path, text, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(sessionDirectory));
    }

    [Fact]
    public void Workspace_option_is_passed_to_doctor_command()
    {
        using StringWriter output = new();
        string? receivedWorkspace = null;

        int exitCode = CliCommandFactory
            .Create(output, workspacePath =>
            {
                receivedWorkspace = workspacePath;
                return CreateSnapshot(workspacePath);
            })
            .Parse(["doctor", "--workspace", "custom-root"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal("custom-root", receivedWorkspace);
        Assert.Contains("workspace: custom-root", output.ToString());
    }

    [Fact]
    public void Workspace_option_is_passed_to_config_get_command()
    {
        using StringWriter output = new();
        string? receivedWorkspace = null;

        int exitCode = CliCommandFactory
            .Create(output, workspacePath =>
            {
                receivedWorkspace = workspacePath;
                return CreateSnapshot(workspacePath);
            })
            .Parse(["config", "get", "--workspace", "custom-root"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal("custom-root", receivedWorkspace);
        Assert.Contains("workspace: custom-root", output.ToString());
    }

    [Fact]
    public void Workspace_option_is_passed_to_config_list_command()
    {
        using StringWriter output = new();
        string? receivedWorkspace = null;

        int exitCode = CliCommandFactory
            .Create(output, workspacePath =>
            {
                receivedWorkspace = workspacePath;
                return CreateSnapshot(workspacePath);
            })
            .Parse(["config", "list", "--workspace", "custom-root"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal("custom-root", receivedWorkspace);
        Assert.Contains("workspace: custom-root", output.ToString());
    }

    [Fact]
    public void Doctor_command_writes_command_log_through_delegate()
    {
        using StringWriter output = new();
        List<string> loggedCommands = [];

        int exitCode = CliCommandFactory
            .Create(
                output,
                CreateSnapshot,
                (commandName, _) => loggedCommands.Add(commandName))
            .Parse(["doctor"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal(["doctor"], loggedCommands);
    }

    [Fact]
    public void Status_command_writes_command_log_through_delegate()
    {
        using StringWriter output = new();
        List<string> loggedCommands = [];

        int exitCode = CliCommandFactory
            .Create(
                output,
                CreateSnapshot,
                (commandName, _) => loggedCommands.Add(commandName))
            .Parse(["status"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal(["status"], loggedCommands);
    }

    [Fact]
    public void Config_get_command_writes_command_log_through_delegate()
    {
        using StringWriter output = new();
        List<string> loggedCommands = [];

        int exitCode = CliCommandFactory
            .Create(
                output,
                CreateSnapshot,
                (commandName, _) => loggedCommands.Add(commandName))
            .Parse(["config", "get"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal(["config get"], loggedCommands);
    }

    [Fact]
    public void Config_list_command_writes_command_log_through_delegate()
    {
        using StringWriter output = new();
        List<string> loggedCommands = [];

        int exitCode = CliCommandFactory
            .Create(
                output,
                CreateSnapshot,
                (commandName, _) => loggedCommands.Add(commandName))
            .Parse(["config", "list"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal(["config list"], loggedCommands);
    }

    [Fact]
    public void Config_set_command_writes_command_log_through_delegate()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        List<string> loggedCommands = [];
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: userConfigPath);

        int exitCode = CliCommandFactory
            .Create(
                output,
                _ => snapshot,
                (commandName, _) => loggedCommands.Add(commandName))
            .Parse(["config", "set", "model", "gpt-test"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal(["config set"], loggedCommands);
    }

    [Fact]
    public void Config_unset_command_writes_command_log_through_delegate()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        List<string> loggedCommands = [];
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: userConfigPath);

        int exitCode = CliCommandFactory
            .Create(
                output,
                _ => snapshot,
                (commandName, _) => loggedCommands.Add(commandName))
            .Parse(["config", "unset", "model"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal(["config unset"], loggedCommands);
    }

    [Fact]
    public void Command_logger_failure_does_not_block_doctor_report()
    {
        using StringWriter output = new();

        int exitCode = CliCommandFactory
            .Create(
                output,
                CreateSnapshot,
                (_, _) => throw new IOException("log directory unavailable"))
            .Parse(["doctor"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Contains("C# AI CLI doctor", output.ToString());
    }

    [Fact]
    public void Chat_command_streams_prompt_to_model_client_and_logs_command()
    {
        using StringWriter output = new();
        string? receivedWorkspace = null;
        List<string> loggedCommands = [];
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "fake model output")));

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath =>
                {
                    receivedWorkspace = workspacePath;
                    return CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test");
                },
                (commandName, _) => loggedCommands.Add(commandName),
                _ => chatClient,
                writer => new TerminalChatStreamingRenderer(writer))
            .Parse(["chat", "--workspace", "custom-root", "hello model"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal("custom-root", receivedWorkspace);
        Assert.Equal(["chat"], loggedCommands);
        Assert.Equal("hello model", chatClient.LastStreamingPrompt);
        Assert.Null(chatClient.LastRequest?.Instructions);
        Assert.Null(chatClient.LastNonStreamingPrompt);
        Assert.Contains("status: streaming", output.ToString());
        Assert.Contains("fake model output", output.ToString());
        Assert.Contains("status: completed", output.ToString());
    }

    [Fact]
    public void Chat_command_without_cwd_keeps_legacy_snapshot_provider_compatibility()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string? receivedWorkspace = null;
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "fake model output")));

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath =>
                {
                    receivedWorkspace = workspacePath;
                    return CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test");
                },
                (_, _) => { },
                _ => chatClient,
                writer => new TerminalChatStreamingRenderer(writer))
            .Parse(["chat", "--workspace", temp.Path, "hello model"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal(temp.Path, receivedWorkspace);
        Assert.Equal("hello model", chatClient.LastRequest?.Prompt);
        Assert.Null(chatClient.LastRequest?.Instructions);
    }

    [Fact]
    public void Chat_command_passes_workspace_instructions_to_model_request()
    {
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "fake model output")));
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: "sk-test",
            apiKeySource: "OPENAI_API_KEY",
            model: "gpt-test")
            with
            {
                Instructions = InstructionLoadResult.Loaded("Be concise.", Path.Combine("workspace-root", "AICLI.md"))
            };

        int exitCode = CliCommandFactory
            .Create(
                output,
                _ => snapshot,
                (_, _) => { },
                _ => chatClient,
                writer => new TerminalChatStreamingRenderer(writer))
            .Parse(["chat", "hello model"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal("hello model", chatClient.LastRequest?.Prompt);
        Assert.Equal("Be concise.", chatClient.LastRequest?.Instructions);
    }

    [Fact]
    public void Chat_command_passes_cwd_to_snapshot_provider_and_merged_instructions_to_model_request()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string cwd = Path.Combine("src", "app");
        string? receivedWorkspace = null;
        string? receivedCwd = null;
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "fake model output")));
        RootCommand command = CliCommandFactory.Create(
            output,
            (workspacePath, instructionTargetPath) =>
            {
                receivedWorkspace = workspacePath;
                receivedCwd = instructionTargetPath;
                string workspaceRoot = workspacePath ?? temp.Path;
                return CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test")
                    with
                    {
                        Instructions = InstructionLoadResult.Loaded(
                            "Root rules\n\nApp rules",
                            [
                                new InstructionSource(Path.Combine(workspaceRoot, "AICLI.md"), 0),
                                new InstructionSource(Path.Combine(workspaceRoot, "src", "app", "AICLI.md"), 1)
                            ])
                    };
            },
            (_, _) => { },
            _ => chatClient,
            writer => new TerminalChatStreamingRenderer(writer),
            _ => new FakeConversationStore(),
            () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
            (_, _, _) => new FakeAgentRunner(AgentRunResult.Success("agent completed task", [])));

        int exitCode = CliCommandFactory.Invoke(command, ["chat", "--workspace", temp.Path, "--cwd", cwd, "hello model"], output);

        Assert.Equal(0, exitCode);
        Assert.Equal(temp.Path, receivedWorkspace);
        Assert.Equal(cwd, receivedCwd);
        Assert.Equal("hello model", chatClient.LastRequest?.Prompt);
        Assert.Equal("Root rules\n\nApp rules", chatClient.LastRequest?.Instructions);
        Assert.DoesNotContain("Root rules", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Chat_command_with_real_snapshot_loads_merged_cwd_instructions_without_printing_them()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string workspaceRoot = Path.Combine(temp.Path, "workspace");
        string homeDirectory = Path.Combine(temp.Path, "home");
        string appDirectory = Path.Combine(workspaceRoot, "src", "app");
        Directory.CreateDirectory(homeDirectory);
        Directory.CreateDirectory(appDirectory);

        string rootInstruction = "root-secret instructions.";
        string appInstruction = "app-secret instructions.";
        File.WriteAllText(Path.Combine(workspaceRoot, "AICLI.md"), rootInstruction);
        File.WriteAllText(Path.Combine(appDirectory, "AGENTS.md"), appInstruction);

        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "fake model output")));
        RootCommand command = CliCommandFactory.Create(
            output,
            (workspacePath, instructionTargetPath) => CliEnvironmentSnapshot.Create(
                workspacePath: workspacePath,
                currentDirectory: temp.Path,
                userProfile: homeDirectory,
                dotnetSdkVersion: "9.0.308",
                dotnetRuntime: ".NET 9.0.0",
                openAiApiKey: "sk-test-secret",
                openAiModel: "gpt-test",
                hasGlobalJson: false,
                instructionTargetPath: instructionTargetPath),
            (_, _) => { },
            _ => chatClient,
            writer => new TerminalChatStreamingRenderer(writer),
            _ => new FakeConversationStore(),
            () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
            (_, _, _) => new FakeAgentRunner(AgentRunResult.Success("agent completed task", [])));

        int exitCode = CliCommandFactory.Invoke(
            command,
            ["chat", "--workspace", workspaceRoot, "--cwd", Path.Combine("src", "app"), "hello model"],
            output);

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Equal("hello model", chatClient.LastRequest?.Prompt);
        Assert.Equal(
            string.Join(
                $"{Environment.NewLine}{Environment.NewLine}",
                rootInstruction,
                appInstruction),
            chatClient.LastRequest?.Instructions);
        Assert.Contains("fake model output", text);
        Assert.DoesNotContain(rootInstruction, text, StringComparison.Ordinal);
        Assert.DoesNotContain(appInstruction, text, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-test-secret", text, StringComparison.Ordinal);
    }

    private static CliEnvironmentSnapshot CreateSnapshot(string? workspacePath)
    {
        return CreateSnapshot(
            workspacePath,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured");
    }

    [Fact]
    public void Chat_command_returns_nonzero_for_streaming_model_error()
    {
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Failure(new ModelError(
            Provider: "openai",
            Operation: "responses.create",
            StatusCode: null,
            LocalErrorCode: "missing-openai-api-key",
            SafeMessage: "OpenAI API key is missing. Set OPENAI_API_KEY or user config apiKey.",
            Retryable: false)));

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(workspacePath, apiKey: null, apiKeySource: "missing", model: "gpt-test"),
                (_, _) => { },
                _ => chatClient,
                writer => new TerminalChatStreamingRenderer(writer))
            .Parse(["chat", "hello model"])
            .Invoke();

        Assert.Equal(1, exitCode);
        Assert.Contains("status: failed", output.ToString());
        Assert.Contains("localErrorCode: missing-openai-api-key", output.ToString());
    }

    [Fact]
    public void Chat_session_loads_transcript_records_success_and_saves()
    {
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "fake model output")));
        FakeConversationStore store = new();

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
                (_, _) => { },
                _ => chatClient,
                writer => new TerminalChatStreamingRenderer(writer),
                _ => store,
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"))
            .Parse(["chat", "--session", "smoke", "hello model"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal("smoke", chatClient.LastRequest?.SessionName);
        Assert.Null(chatClient.LastRequest?.Instructions);
        Assert.Equal("smoke", store.LoadedSessionName?.Value);
        Assert.Equal("smoke", store.SavedSessionName?.Value);
        Assert.NotNull(store.SavedTranscript);
        Assert.Equal(2, store.SavedTranscript.Messages.Count);
        Assert.Equal("hello model", store.SavedTranscript.Messages[0].Content);
        Assert.Equal("fake model output", store.SavedTranscript.Messages[1].Content);
    }

    [Fact]
    public void Chat_resume_requires_existing_transcript_records_success_and_saves()
    {
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "fake model output")));
        ConversationTranscript existing = ConversationTranscript.Create(
            "smoke",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        FakeConversationStore store = new()
        {
            Transcript = existing
        };

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
                (_, _) => { },
                _ => chatClient,
                writer => new TerminalChatStreamingRenderer(writer),
                _ => store,
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"))
            .Parse(["chat", "--resume", "smoke", "hello model"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal("smoke", chatClient.LastRequest?.SessionName);
        Assert.Same(existing, chatClient.LastRequest?.TranscriptContext);
        Assert.Equal("smoke", store.TryLoadedSessionName?.Value);
        Assert.Null(store.LoadedSessionName);
        Assert.Equal("smoke", store.SavedSessionName?.Value);
        Assert.Same(existing, store.SavedTranscript);
        ConversationTranscript savedTranscript = Assert.IsType<ConversationTranscript>(store.SavedTranscript);
        Assert.Equal(2, savedTranscript.Messages.Count);
        Assert.Equal("hello model", savedTranscript.Messages[0].Content);
        Assert.Equal("fake model output", savedTranscript.Messages[1].Content);
    }

    [Fact]
    public void Chat_resume_missing_returns_session_not_found_without_calling_model()
    {
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
        Text: "fake model output")));
        FakeConversationStore store = new()
        {
            TryLoadResult = false
        };

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
                (_, _) => { },
                _ => chatClient,
                writer => new TerminalChatStreamingRenderer(writer),
                _ => store,
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"))
            .Parse(["chat", "--resume", "missing", "hello model"])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("status: failed", text);
        Assert.Contains("errorCode: session-not-found", text);
        Assert.Contains("summary:", text);
        Assert.Contains("Session transcript was not found.", text);
        Assert.Equal("missing", store.TryLoadedSessionName?.Value);
        Assert.Null(store.ExistsSessionName);
        Assert.Null(store.LoadedSessionName);
        Assert.Null(store.SavedSessionName);
        Assert.Null(chatClient.LastRequest);
    }

    [Fact]
    public void Chat_resume_malformed_file_transcript_returns_safe_failure_without_calling_model()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "fake model output")));
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        string sessionDirectory = Path.Combine(temp.Path, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        File.WriteAllText(Path.Combine(sessionDirectory, "smoke.transcript.json"), """{"schemaVersion":""");
        RootCommand command = CliCommandFactory.Create(
            output,
            workspacePath => CreateSnapshot(
                workspacePath,
                apiKey: "sk-test",
                apiKeySource: "OPENAI_API_KEY",
                model: "gpt-test",
                userConfigPath: userConfigPath),
            (_, _) => { },
            _ => chatClient,
            writer => new TerminalChatStreamingRenderer(writer),
            snapshot => FileConversationStore.Create(snapshot),
            () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"));

        int exitCode = CliCommandFactory.Invoke(command, ["chat", "--resume", "smoke", "hello model"], output);

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("status: failed", text);
        Assert.Contains("errorCode: session-transcript-invalid", text);
        Assert.Contains("Conversation transcript is missing or uses an unsupported schema version.", text);
        Assert.DoesNotContain("System.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonReaderException", text, StringComparison.Ordinal);
        Assert.Null(chatClient.LastRequest);
    }

    [Fact]
    public void Chat_session_load_or_create_exception_returns_safe_failure_without_calling_model()
    {
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "fake model output")));
        FakeConversationStore store = new()
        {
            LoadOrCreateException = new IOException("cannot read C:\\secret\\smoke.transcript.json")
        };
        RootCommand command = CliCommandFactory.Create(
            output,
            workspacePath => CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
            (_, _) => { },
            _ => chatClient,
            writer => new TerminalChatStreamingRenderer(writer),
            _ => store,
            () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"));

        int exitCode = CliCommandFactory.Invoke(command, ["chat", "--session", "smoke", "hello model"], output);

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("status: failed", text);
        Assert.Contains("errorCode: session-store-error", text);
        Assert.Contains("Conversation session store operation failed.", text);
        Assert.DoesNotContain("C:\\secret", text, StringComparison.Ordinal);
        Assert.Equal("smoke", store.LoadedSessionName?.Value);
        Assert.Null(store.SavedSessionName);
        Assert.Null(chatClient.LastRequest);
    }

    [Fact]
    public void Chat_resume_empty_session_name_rejects_without_calling_model_or_store()
    {
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "fake model output")));
        FakeConversationStore store = new();
        bool storeFactoryInvoked = false;
        RootCommand command = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
                (_, _) => { },
                _ => chatClient,
                writer => new TerminalChatStreamingRenderer(writer),
                _ =>
                {
                    storeFactoryInvoked = true;
                    return store;
                },
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"));

        int exitCode = CliCommandFactory.Invoke(command, ["chat", "--resume", "", "hello model"], output);

        Assert.Equal(1, exitCode);
        Assert.Contains("Session name must not be empty.", output.ToString());
        Assert.False(storeFactoryInvoked);
        Assert.Null(store.ExistsSessionName);
        Assert.Null(store.TryLoadedSessionName);
        Assert.Null(store.LoadedSessionName);
        Assert.Null(store.SavedSessionName);
        Assert.Null(chatClient.LastRequest);
    }

    [Fact]
    public void Chat_session_whitespace_session_name_rejects_without_calling_model_or_store()
    {
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "fake model output")));
        FakeConversationStore store = new();
        bool storeFactoryInvoked = false;
        RootCommand command = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
                (_, _) => { },
                _ => chatClient,
                writer => new TerminalChatStreamingRenderer(writer),
                _ =>
                {
                    storeFactoryInvoked = true;
                    return store;
                },
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"));

        int exitCode = CliCommandFactory.Invoke(command, ["chat", "--session", " ", "hello model"], output);

        Assert.Equal(1, exitCode);
        Assert.Contains("Session name must not be empty.", output.ToString());
        Assert.False(storeFactoryInvoked);
        Assert.Null(store.ExistsSessionName);
        Assert.Null(store.TryLoadedSessionName);
        Assert.Null(store.LoadedSessionName);
        Assert.Null(store.SavedSessionName);
        Assert.Null(chatClient.LastRequest);
    }

    [Fact]
    public void Chat_empty_session_and_resume_conflict_returns_failure_without_calling_model_or_store()
    {
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "fake model output")));
        FakeConversationStore store = new();
        bool storeFactoryInvoked = false;
        RootCommand command = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
                (_, _) => { },
                _ => chatClient,
                writer => new TerminalChatStreamingRenderer(writer),
                _ =>
                {
                    storeFactoryInvoked = true;
                    return store;
                },
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"));

        int exitCode = CliCommandFactory.Invoke(command, ["chat", "--session", "", "--resume", "smoke", "hello model"], output);

        Assert.Equal(1, exitCode);
        Assert.Contains("session-option-conflict", output.ToString());
        Assert.False(storeFactoryInvoked);
        Assert.Null(store.ExistsSessionName);
        Assert.Null(store.TryLoadedSessionName);
        Assert.Null(store.LoadedSessionName);
        Assert.Null(store.SavedSessionName);
        Assert.Null(chatClient.LastRequest);
    }

    [Fact]
    public void Chat_session_and_resume_conflict_returns_failure_without_calling_model()
    {
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "fake model output")));
        FakeConversationStore store = new();

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
                (_, _) => { },
                _ => chatClient,
                writer => new TerminalChatStreamingRenderer(writer),
                _ => store,
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"))
            .Parse(["chat", "--session", "smoke", "--resume", "smoke", "hello model"])
            .Invoke();

        Assert.Equal(1, exitCode);
        Assert.Contains("session-option-conflict", output.ToString());
        Assert.Null(store.ExistsSessionName);
        Assert.Null(store.LoadedSessionName);
        Assert.Null(chatClient.LastRequest);
    }

    [Fact]
    public void Chat_session_appends_to_existing_transcript()
    {
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_second",
            Text: "second response")));
        ConversationTranscript existing = ConversationTranscript.Create(
            "smoke",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        existing.AddUserMessage("first prompt", DateTimeOffset.Parse("2024-01-01T00:00:01Z"));
        existing.AddAssistantMessage(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_first",
            Text: "first response"), DateTimeOffset.Parse("2024-01-01T00:00:02Z"));
        FakeConversationStore store = new()
        {
            Transcript = existing
        };

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
                (_, _) => { },
                _ => chatClient,
                writer => new TerminalChatStreamingRenderer(writer),
                _ => store,
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"))
            .Parse(["chat", "--session", "smoke", "second prompt"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal("second prompt", chatClient.LastStreamingPrompt);
        Assert.NotNull(store.SavedTranscript);
        Assert.Equal(
            ["first prompt", "first response", "second prompt", "second response"],
            store.SavedTranscript.Messages.Select(message => message.Content).ToArray());
    }

    [Fact]
    public void Chat_session_records_failure_and_returns_nonzero()
    {
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Failure(new ModelError(
            Provider: "openai",
            Operation: "responses.create",
            StatusCode: null,
            LocalErrorCode: "missing-openai-api-key",
            SafeMessage: "OpenAI API key is missing. Set OPENAI_API_KEY or user config apiKey.",
            Retryable: false)));
        FakeConversationStore store = new();

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(workspacePath, apiKey: null, apiKeySource: "missing", model: "gpt-test"),
                (_, _) => { },
                _ => chatClient,
                writer => new TerminalChatStreamingRenderer(writer),
                _ => store,
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"))
            .Parse(["chat", "--session", "smoke", "hello model"])
            .Invoke();

        Assert.Equal(1, exitCode);
        Assert.NotNull(store.SavedTranscript);
        Assert.Equal("hello model", Assert.Single(store.SavedTranscript.Messages).Content);
        Assert.Equal("missing-openai-api-key", Assert.Single(store.SavedTranscript.Errors).LocalErrorCode);
    }

    [Fact]
    public void Chat_session_save_exception_returns_safe_failure_without_leaking_exception_detail()
    {
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "fake model output")));
        FakeConversationStore store = new()
        {
            SaveException = new IOException("cannot write C:\\secret\\smoke.transcript.json")
        };
        RootCommand command = CliCommandFactory.Create(
            output,
            workspacePath => CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
            (_, _) => { },
            _ => chatClient,
            writer => new TerminalChatStreamingRenderer(writer),
            _ => store,
            () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"));

        int exitCode = CliCommandFactory.Invoke(command, ["chat", "--session", "smoke", "hello model"], output);

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("status: failed", text);
        Assert.Contains("errorCode: session-store-error", text);
        Assert.Contains("Conversation session store operation failed.", text);
        Assert.DoesNotContain("C:\\secret", text, StringComparison.Ordinal);
        Assert.Equal("smoke", chatClient.LastRequest?.SessionName);
        Assert.Equal("smoke", store.SavedSessionName?.Value);
    }

    [Fact]
    public void Chat_without_session_does_not_create_transcript()
    {
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "fake model output")));
        FakeConversationStore store = new();

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
                (_, _) => { },
                _ => chatClient,
                writer => new TerminalChatStreamingRenderer(writer),
                _ => store,
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"))
            .Parse(["chat", "hello model"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Null(store.LoadedSessionName);
        Assert.Null(store.SavedSessionName);
        Assert.Null(store.SavedTranscript);
    }

    private static CliEnvironmentSnapshot CreateSnapshot(
        string? workspacePath,
        string? apiKey,
        string apiKeySource,
        string model,
        IReadOnlyList<CliConfigFileSource>? configSources = null,
        IReadOnlySet<string>? disabledTools = null,
        string? userConfigPath = null,
        string baseUrl = "https://api.openai.com/v1",
        string baseUrlSource = "default")
    {
        string workspaceRoot = string.IsNullOrWhiteSpace(workspacePath) ? "workspace-root" : workspacePath;

        WorkspaceContext workspace = new(
            RootPath: workspaceRoot,
            ConfigPath: Path.Combine(workspaceRoot, ".caicli", "config.json"),
            Status: WorkspaceStatus.Ready);

        EffectiveConfiguration configuration = new(
            WorkspaceRoot: workspaceRoot,
            UserConfigPath: userConfigPath ?? Path.Combine("user-home", ".caicli", "config.json"),
            WorkspaceConfigPath: Path.Combine(workspaceRoot, ".caicli", "config.json"),
            Model: model,
            ModelSource: model == "not configured" ? "default" : "workspace config",
            AgentBackend: "direct",
            AgentBackendSource: "default",
            DisabledTools: disabledTools ?? new HashSet<string>(StringComparer.Ordinal),
            ApiKey: SecretValue.From(apiKey),
            ApiKeySource: apiKeySource,
            LoadedConfigPaths: [],
            Warnings: [],
            ConfigSources: configSources ?? [])
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

    private static JsonObject ReadJsonObject(string path)
    {
        JsonNode? node = JsonNode.Parse(File.ReadAllText(path));
        return Assert.IsType<JsonObject>(node);
    }

    private static JsonObject AssertSingleExecJsonResult(StringWriter output)
    {
        string[] lines = output.ToString().TrimEnd().Split(Environment.NewLine);
        JsonObject result = Assert.IsType<JsonObject>(JsonNode.Parse(Assert.Single(lines)));
        Assert.Equal("exec.result", result["type"]?.GetValue<string>());
        return result;
    }

    private static int InvokeToolsCallPatch(TempDirectory temp, StringWriter output, string[] approvalArgs)
    {
        File.WriteAllText(Path.Combine(temp.Path, "note.txt"), "before");
        string argumentsPath = Path.Combine(temp.Path, "arguments.json");
        File.WriteAllText(argumentsPath, """{"path":"note.txt","find":"before","replace":"after"}""");

        List<string> args =
        [
            "tools",
            "call",
            "--workspace",
            temp.Path
        ];
        args.AddRange(approvalArgs);
        args.Add("workspace.apply_patch");
        args.Add("--arguments-file");
        args.Add(argumentsPath);

        return CliCommandFactory
            .Create(output, workspacePath => CreateSnapshot(workspacePath))
            .Parse([.. args])
            .Invoke();
    }

    private static string InitializeGitRepository(string root)
    {
        RunGit(root, "init");
        RunGit(root, "config", "user.email", "test@example.invalid");
        RunGit(root, "config", "user.name", "Test User");
        string filePath = Path.Combine(root, "tracked.txt");
        File.WriteAllText(filePath, "original\n");
        RunGit(root, "add", "tracked.txt");
        RunGit(root, "-c", "commit.gpgSign=false", "commit", "--no-gpg-sign", "--no-verify", "-m", "initial");
        return filePath;
    }

    private static void RunGit(string workingDirectory, params string[] arguments)
    {
        ProcessStartInfo startInfo = new("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new()
        {
            StartInfo = startInfo
        };

        process.Start();
        string commandText = string.Join(" ", arguments);
        Assert.True(process.WaitForExit(10_000), "git command timed out: " + commandText);
        string stderr = process.StandardError.ReadToEnd();
        Assert.True(process.ExitCode == 0, $"git {commandText} failed: {stderr}");
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempDirectory Create()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "caicli-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                ClearReadOnlyAttributes(Path);
                Directory.Delete(Path, recursive: true);
            }
        }

        private static void ClearReadOnlyAttributes(string path)
        {
            foreach (string filePath in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(filePath, FileAttributes.Normal);
            }

            foreach (string directoryPath in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(directoryPath, FileAttributes.Normal);
            }
        }
    }

    private static CliConfigFileSource CreateWorkflowSource()
    {
        return new CliConfigFileSource(
            "workspace config",
            "workspace-config.json",
            new CliConfigFile
            {
                WorkflowProfiles = new Dictionary<string, WorkflowProfileConfig>
                {
                    ["cpp"] = new()
                    {
                        WorkspacePath = null,
                        ValidationCommand = "dotnet test"
                    }
                }
            });
    }

    private sealed class FakeChatModelClient(ChatModelResult result) : IChatModelClient
    {
        public string? LastNonStreamingPrompt { get; private set; }
        public string? LastStreamingPrompt { get; private set; }
        public ChatRequest? LastRequest { get; private set; }

        public ChatModelResult Send(ChatRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            LastNonStreamingPrompt = request.Prompt;
            return result;
        }

        public ChatModelResult SendStreaming(
            ChatRequest request,
            IChatStreamingRenderer renderer,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            LastStreamingPrompt = request.Prompt;

            if (result.Response is not null)
            {
                renderer.Start(CreateSnapshot(
                    workspacePath: null,
                    apiKey: "sk-test",
                    apiKeySource: "OPENAI_API_KEY",
                    model: result.Response.Model), result.Response.Provider, result.Response.Model);
                renderer.WriteDelta(result.Response.Text);
                renderer.Complete(result.Response);
                return result;
            }

            renderer.Fail(CreateSnapshot(
                workspacePath: null,
                apiKey: null,
                apiKeySource: "missing",
                model: "gpt-test"), result.Error!);
            return result;
        }
    }

    private sealed class FakeAgentRunner(AgentRunResult result) : IAgentRunner
    {
        public AgentRunRequest? LastRequest { get; private set; }
        public ConversationTranscript? LastTranscript { get; private set; }

        public AgentRunResult Run(
            AgentRunRequest request,
            ConversationTranscript? transcript = null,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            LastTranscript = transcript;
            return result;
        }
    }

    private sealed class TranscriptRecordingAgentRunner(
        string callId,
        string toolName,
        string argumentsJson,
        ToolExecutionResult result) : IAgentRunner
    {
        public ConversationTranscript? LastTranscript { get; private set; }

        public AgentRunResult Run(
            AgentRunRequest request,
            ConversationTranscript? transcript = null,
            CancellationToken cancellationToken = default)
        {
            LastTranscript = transcript;
            ConversationToolCall toolCall = ConversationToolCall.FromExecution(
                callId,
                toolName,
                argumentsJson,
                result,
                DateTimeOffset.Parse("2024-01-01T00:00:06Z"));
            transcript?.AddToolCall(toolCall);
            return AgentRunResult.Success("agent completed task", [toolCall], []);
        }
    }

    private sealed class ExecutorToolCallAgentRunner(
        string toolName,
        string argumentsJson) : IAgentRunner
    {
        public IToolExecutor? Executor { get; set; }

        public AgentRunRequest? LastRequest { get; private set; }

        public AgentRunResult Run(
            AgentRunRequest request,
            ConversationTranscript? transcript = null,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            IToolExecutor executor = Executor ?? throw new InvalidOperationException("Executor was not injected.");
            ToolExecutionResult result = executor.Execute(
                toolName,
                new ToolExecutionContext("call_tool_1", request.Workspace, argumentsJson),
                cancellationToken);

            AgentRunEvent toolEvent = new(
                Type: result.Succeeded ? "tool.completed" : "tool.failed",
                Sequence: 0,
                Timestamp: DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                Summary: result.Summary,
                Payload: new Dictionary<string, string>
                {
                    ["toolName"] = toolName
                },
                ErrorCode: result.ErrorCode,
                ApprovalStatus: result.ApprovalStatus);

            ConversationToolCall toolCall = ConversationToolCall.FromExecution(
                "call_tool_1",
                toolName,
                argumentsJson,
                result,
                DateTimeOffset.Parse("2024-01-01T00:00:00Z"));

            return result.Succeeded
                ? AgentRunResult.Success(result.Summary, [toolCall], [toolEvent])
                : AgentRunResult.Failure(
                    new AgentError(
                        result.ErrorCode ?? "tool-call-failed",
                        result.Summary,
                        result.Retryable),
                    [toolCall],
                    [toolEvent]);
        }
    }

    private sealed class ThrowingAgentRunner(Exception exception) : IAgentRunner
    {
        public AgentRunResult Run(
            AgentRunRequest request,
            ConversationTranscript? transcript = null,
            CancellationToken cancellationToken = default)
        {
            throw exception;
        }
    }

    private sealed class FakeConversationStore : IConversationStore
    {
        public ConversationSessionName? ExistsSessionName { get; private set; }
        public ConversationSessionName? TryLoadedSessionName { get; private set; }
        public ConversationSessionName? LoadedSessionName { get; private set; }
        public ConversationSessionName? SavedSessionName { get; private set; }
        public ConversationTranscript? SavedTranscript { get; private set; }
        public ConversationSessionName? RenamedSourceSessionName { get; private set; }
        public ConversationSessionName? RenamedDestinationSessionName { get; private set; }
        public ConversationSessionName? DeletedSessionName { get; private set; }
        public bool ExistsResult { get; init; } = true;
        public bool TryLoadResult { get; init; } = true;
        public bool RenameResult { get; init; }
        public bool DeleteResult { get; init; }
        public Exception? LoadOrCreateException { get; init; }
        public Exception? SaveException { get; init; }
        public Exception? TryLoadException { get; init; }
        public Exception? ListSummariesException { get; init; }
        public Exception? TryGetSummaryException { get; init; }
        public Exception? RenameException { get; init; }
        public Exception? DeleteException { get; init; }
        public ConversationTranscript Transcript { get; init; } = ConversationTranscript.Create(
            "smoke",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        public IReadOnlyList<ConversationTranscriptSummary> Summaries { get; init; } = [];

        public bool Exists(ConversationSessionName sessionName)
        {
            ExistsSessionName = sessionName;
            return ExistsResult;
        }

        public bool TryLoad(ConversationSessionName sessionName, out ConversationTranscript? transcript)
        {
            TryLoadedSessionName = sessionName;
            if (TryLoadException is not null)
            {
                throw TryLoadException;
            }

            transcript = TryLoadResult ? Transcript : null;
            return TryLoadResult;
        }

        public ConversationTranscript LoadOrCreate(ConversationSessionName sessionName, DateTimeOffset nowUtc)
        {
            LoadedSessionName = sessionName;
            if (LoadOrCreateException is not null)
            {
                throw LoadOrCreateException;
            }

            return Transcript;
        }

        public string Save(ConversationSessionName sessionName, ConversationTranscript transcript)
        {
            SavedSessionName = sessionName;
            SavedTranscript = transcript;
            if (SaveException is not null)
            {
                throw SaveException;
            }

            return Path.Combine("user-home", ".caicli", "sessions", $"{sessionName.FileSafeName}.transcript.json");
        }

        public IReadOnlyList<ConversationTranscriptSummary> ListSummaries()
        {
            if (ListSummariesException is not null)
            {
                throw ListSummariesException;
            }

            return Summaries;
        }

        public bool TryGetSummary(ConversationSessionName sessionName, out ConversationTranscriptSummary? summary)
        {
            if (TryGetSummaryException is not null)
            {
                throw TryGetSummaryException;
            }

            summary = Summaries.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, sessionName.Value, StringComparison.Ordinal));
            return summary is not null;
        }

        public bool Rename(ConversationSessionName sourceSessionName, ConversationSessionName destinationSessionName)
        {
            RenamedSourceSessionName = sourceSessionName;
            RenamedDestinationSessionName = destinationSessionName;
            if (RenameException is not null)
            {
                throw RenameException;
            }

            return RenameResult;
        }

        public bool Delete(ConversationSessionName sessionName)
        {
            DeletedSessionName = sessionName;
            if (DeleteException is not null)
            {
                throw DeleteException;
            }

            return DeleteResult;
        }
    }
}
