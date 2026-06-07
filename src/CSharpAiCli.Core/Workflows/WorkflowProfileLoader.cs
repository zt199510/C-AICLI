namespace CSharpAiCli.Core;

public static class WorkflowProfileLoader
{
    public static WorkflowConfiguration Load(EffectiveConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        Dictionary<string, WorkflowProfile> profiles = new(StringComparer.Ordinal);
        foreach (CliConfigFileSource source in configuration.ConfigSources)
        {
            if (source.Config.WorkflowProfiles is null)
            {
                continue;
            }

            foreach ((string name, WorkflowProfileConfig profileConfig) in source.Config.WorkflowProfiles)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                profiles[name] = new WorkflowProfile(
                    Name: name,
                    WorkspacePath: string.IsNullOrWhiteSpace(profileConfig.WorkspacePath) ? null : profileConfig.WorkspacePath,
                    ValidationCommand: string.IsNullOrWhiteSpace(profileConfig.ValidationCommand) ? null : profileConfig.ValidationCommand,
                    Description: profileConfig.Description ?? string.Empty,
                    Source: source.SourceName);
            }
        }

        return profiles.Count == 0
            ? WorkflowConfiguration.Empty
            : new WorkflowConfiguration(profiles.Values.OrderBy(profile => profile.Name, StringComparer.Ordinal).ToArray());
    }
}
