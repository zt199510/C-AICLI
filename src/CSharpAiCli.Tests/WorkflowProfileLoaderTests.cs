using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class WorkflowProfileLoaderTests
{
    [Fact]
    public void Load_reads_profiles_and_workspace_overrides_user_profile()
    {
        CliConfigFile userConfig = new()
        {
            WorkflowProfiles = new Dictionary<string, WorkflowProfileConfig>
            {
                ["cpp"] = new()
                {
                    WorkspacePath = "user-workspace",
                    ValidationCommand = "dotnet test",
                    Description = "user profile"
                }
            }
        };
        CliConfigFile workspaceConfig = new()
        {
            WorkflowProfiles = new Dictionary<string, WorkflowProfileConfig>
            {
                ["cpp"] = new()
                {
                    WorkspacePath = "workspace-profile",
                    ValidationCommand = "ctest",
                    Description = "workspace profile"
                }
            }
        };

        WorkflowConfiguration configuration = WorkflowProfileLoader.Load(CreateConfiguration(
            [
                new CliConfigFileSource("user config", "user-config.json", userConfig),
                new CliConfigFileSource("workspace config", "workspace-config.json", workspaceConfig)
            ]));

        WorkflowProfile profile = Assert.Single(configuration.Profiles);
        Assert.Equal("cpp", profile.Name);
        Assert.Equal("workspace-profile", profile.WorkspacePath);
        Assert.Equal("ctest", profile.ValidationCommand);
        Assert.Equal("workspace config", profile.Source);
    }

    [Fact]
    public void Registry_suggests_profile_path_or_workspace_path()
    {
        WorkflowConfiguration configuration = new(
            [
                new WorkflowProfile(
                    Name: "profile-path",
                    WorkspacePath: "configured-root",
                    ValidationCommand: "dotnet test",
                    Description: "",
                    Source: "workspace config"),
                new WorkflowProfile(
                    Name: "workspace-path",
                    WorkspacePath: null,
                    ValidationCommand: "dotnet build",
                    Description: "",
                    Source: "workspace config")
            ]);
        WorkflowRegistry registry = new(configuration);
        WorkspaceContext workspace = new("cli-root", Path.Combine("cli-root", ".caicli", "config.json"), WorkspaceStatus.Ready);

        Assert.True(registry.TrySuggestValidation("profile-path", workspace, out WorkflowValidationSuggestion? profileSuggestion));
        Assert.Equal("configured-root", profileSuggestion?.WorkspacePath);
        Assert.Equal("profile", profileSuggestion?.WorkspacePathSource);
        Assert.True(registry.TrySuggestValidation("workspace-path", workspace, out WorkflowValidationSuggestion? workspaceSuggestion));
        Assert.Equal("cli-root", workspaceSuggestion?.WorkspacePath);
        Assert.Equal("--workspace", workspaceSuggestion?.WorkspacePathSource);
        Assert.True(workspaceSuggestion?.RequiresApproval);
    }

    private static EffectiveConfiguration CreateConfiguration(IReadOnlyList<CliConfigFileSource> sources)
    {
        return new EffectiveConfiguration(
            WorkspaceRoot: "workspace-root",
            UserConfigPath: "user-config.json",
            WorkspaceConfigPath: "workspace-config.json",
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
    }
}
