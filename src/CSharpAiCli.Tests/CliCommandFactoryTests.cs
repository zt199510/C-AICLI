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
    public void Tools_call_refuses_patch_without_approval()
    {
        using TempDirectory temp = TempDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "note.txt"), "before");
        string argumentsPath = Path.Combine(temp.Path, "arguments.json");
        File.WriteAllText(argumentsPath, """{"path":"note.txt","find":"before","replace":"after"}""");
        using StringWriter output = new();

        int exitCode = CliCommandFactory
            .Create(output, workspacePath => CreateSnapshot(workspacePath))
            .Parse(["tools", "call", "--workspace", temp.Path, "workspace.apply_patch", "--arguments-file", argumentsPath])
            .Invoke();

        Assert.Equal(1, exitCode);
        Assert.Contains("errorCode: approval-denied", output.ToString());
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
    }

    [Fact]
    public void Session_export_and_clear_manage_transcript_file()
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
                Directory.Delete(Path, recursive: true);
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

    private sealed class FakeConversationStore : IConversationStore
    {
        public ConversationSessionName? LoadedSessionName { get; private set; }
        public ConversationSessionName? SavedSessionName { get; private set; }
        public ConversationTranscript? SavedTranscript { get; private set; }
        public ConversationTranscript Transcript { get; init; } = ConversationTranscript.Create(
            "smoke",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));

        public ConversationTranscript LoadOrCreate(ConversationSessionName sessionName, DateTimeOffset nowUtc)
        {
            LoadedSessionName = sessionName;
            return Transcript;
        }

        public string Save(ConversationSessionName sessionName, ConversationTranscript transcript)
        {
            SavedSessionName = sessionName;
            SavedTranscript = transcript;
            return Path.Combine("user-home", ".caicli", "sessions", $"{sessionName.FileSafeName}.transcript.json");
        }
    }
}
