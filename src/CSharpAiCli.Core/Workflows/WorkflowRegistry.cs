namespace CSharpAiCli.Core;

public sealed class WorkflowRegistry
{
    private readonly WorkflowConfiguration configuration;

    public WorkflowRegistry(WorkflowConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        this.configuration = configuration;
    }

    public IReadOnlyList<WorkflowProfile> ListProfiles()
    {
        return configuration.Profiles;
    }

    public bool TrySuggestValidation(
        string profileName,
        WorkspaceContext workspace,
        out WorkflowValidationSuggestion? suggestion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);
        ArgumentNullException.ThrowIfNull(workspace);

        WorkflowProfile? profile = configuration.Profiles.FirstOrDefault(
            candidate => string.Equals(candidate.Name, profileName, StringComparison.Ordinal));
        if (profile is null)
        {
            suggestion = null;
            return false;
        }

        string workspacePath = string.IsNullOrWhiteSpace(profile.WorkspacePath)
            ? workspace.RootPath
            : profile.WorkspacePath;
        string workspacePathSource = string.IsNullOrWhiteSpace(profile.WorkspacePath)
            ? "--workspace"
            : "profile";

        suggestion = new WorkflowValidationSuggestion(
            ProfileName: profile.Name,
            WorkspacePath: workspacePath,
            WorkspacePathSource: workspacePathSource,
            ValidationCommand: profile.ValidationCommand,
            Source: profile.Source,
            RequiresApproval: !string.IsNullOrWhiteSpace(profile.ValidationCommand));
        return true;
    }
}
