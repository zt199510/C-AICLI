using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class WorkflowReportsTests
{
    [Fact]
    public void List_report_prints_configured_profiles()
    {
        string text = WorkflowListReport.Create(CreateSnapshot(
            [CreateSource("cpp", "repo-root", "dotnet test")])).ToDisplayText();

        Assert.Contains("C# AI CLI workflows", text);
        Assert.Contains("profile: cpp", text);
        Assert.Contains("workspacePath: repo-root", text);
        Assert.Contains("validationCommand: dotnet test", text);
    }

    [Fact]
    public void Validate_report_suggests_command_without_running_it()
    {
        string text = WorkflowValidateReport.Create(
            CreateSnapshot([CreateSource("cpp", null, "dotnet test")]),
            "cpp").ToDisplayText();

        Assert.Contains("C# AI CLI workflow validation", text);
        Assert.Contains("profile: cpp", text);
        Assert.Contains("workspacePath: workspace-root", text);
        Assert.Contains("workspacePathSource: --workspace", text);
        Assert.Contains("validationCommand: dotnet test", text);
        Assert.Contains("requiresApproval: True", text);
        Assert.Contains("execution: not run", text);
    }

    [Fact]
    public void Validate_report_marks_missing_profile()
    {
        string text = WorkflowValidateReport.Create(CreateSnapshot([]), "missing").ToDisplayText();

        Assert.Contains("profile: missing", text);
        Assert.Contains("status: missing", text);
    }

    private static CliConfigFileSource CreateSource(
        string name,
        string? workspacePath,
        string validationCommand)
    {
        return new CliConfigFileSource(
            "workspace config",
            "workspace-config.json",
            new CliConfigFile
            {
                WorkflowProfiles = new Dictionary<string, WorkflowProfileConfig>
                {
                    [name] = new()
                    {
                        WorkspacePath = workspacePath,
                        ValidationCommand = validationCommand
                    }
                }
            });
    }

    private static CliEnvironmentSnapshot CreateSnapshot(IReadOnlyList<CliConfigFileSource> sources)
    {
        WorkspaceContext workspace = new(
            RootPath: "workspace-root",
            ConfigPath: Path.Combine("workspace-root", ".caicli", "config.json"),
            Status: WorkspaceStatus.Ready);
        EffectiveConfiguration configuration = new(
            WorkspaceRoot: workspace.RootPath,
            UserConfigPath: Path.Combine("user-home", ".caicli", "config.json"),
            WorkspaceConfigPath: workspace.ConfigPath,
            Model: "not configured",
            ModelSource: "default",
            AgentBackend: "direct",
            AgentBackendSource: "default",
            DisabledTools: new HashSet<string>(StringComparer.Ordinal),
            ApiKey: null,
            ApiKeySource: "missing",
            LoadedConfigPaths: [],
            Warnings: [],
            ConfigSources: sources);

        return new CliEnvironmentSnapshot(
            Workspace: workspace,
            Configuration: configuration,
            DotnetSdkVersion: "9.0.308",
            DotnetRuntime: ".NET 9.0.0",
            TargetFramework: "net9.0",
            HasGlobalJson: false);
    }
}
