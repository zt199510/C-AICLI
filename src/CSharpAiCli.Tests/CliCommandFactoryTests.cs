using System.CommandLine;
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

    private static CliEnvironmentSnapshot CreateSnapshot(string? workspacePath)
    {
        string workspaceRoot = string.IsNullOrWhiteSpace(workspacePath) ? "workspace-root" : workspacePath;

        WorkspaceContext workspace = new(
            RootPath: workspaceRoot,
            ConfigPath: Path.Combine(workspaceRoot, ".caicli", "config.json"),
            Status: WorkspaceStatus.Ready);

        EffectiveConfiguration configuration = new(
            WorkspaceRoot: workspaceRoot,
            UserConfigPath: Path.Combine("user-home", ".caicli", "config.json"),
            WorkspaceConfigPath: Path.Combine(workspaceRoot, ".caicli", "config.json"),
            Model: "not configured",
            ModelSource: "default",
            ApiKey: null,
            ApiKeySource: "missing",
            LoadedConfigPaths: [],
            Warnings: []);

        return new CliEnvironmentSnapshot(
            Workspace: workspace,
            Configuration: configuration,
            DotnetSdkVersion: "9.0.308",
            DotnetRuntime: ".NET 9.0.0",
            TargetFramework: "net9.0",
            HasGlobalJson: false);
    }
}
