using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class LogPathResolverTests
{
    [Fact]
    public void ResolveLogDirectory_uses_workspace_logs_when_workspace_is_ready()
    {
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspaceRoot: Path.Combine("root", "workspace"),
            workspaceStatus: WorkspaceStatus.Ready,
            userProfile: Path.Combine("root", "home"));

        string logDirectory = LogPathResolver.ResolveLogDirectory(snapshot);

        Assert.Equal(Path.Combine("root", "workspace", ".caicli", "logs"), logDirectory);
    }

    [Fact]
    public void ResolveLogDirectory_uses_user_logs_when_workspace_is_not_usable()
    {
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspaceRoot: Path.Combine("root", "missing"),
            workspaceStatus: WorkspaceStatus.Missing,
            userProfile: Path.Combine("root", "home"));

        string logDirectory = LogPathResolver.ResolveLogDirectory(snapshot);

        Assert.Equal(Path.Combine("root", "home", ".caicli", "logs"), logDirectory);
    }

    private static CliEnvironmentSnapshot CreateSnapshot(
        string workspaceRoot,
        WorkspaceStatus workspaceStatus,
        string userProfile)
    {
        WorkspaceContext workspace = new(
            RootPath: workspaceRoot,
            ConfigPath: Path.Combine(workspaceRoot, ".caicli", "config.json"),
            Status: workspaceStatus);

        EffectiveConfiguration configuration = new(
            WorkspaceRoot: workspaceRoot,
            UserConfigPath: Path.Combine(userProfile, ".caicli", "config.json"),
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
