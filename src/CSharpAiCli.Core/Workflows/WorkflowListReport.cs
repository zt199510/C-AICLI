namespace CSharpAiCli.Core;

public sealed record WorkflowListReport(IReadOnlyList<string> Lines)
{
    public static WorkflowListReport Create(CliEnvironmentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        WorkflowConfiguration configuration = WorkflowProfileLoader.Load(snapshot.Configuration);
        List<string> lines =
        [
            $"{ProductInfo.DisplayName} workflows"
        ];

        if (configuration.Profiles.Count == 0)
        {
            lines.Add("profiles: none");
            return new WorkflowListReport(lines);
        }

        foreach (WorkflowProfile profile in configuration.Profiles)
        {
            lines.Add($"profile: {profile.Name}");
            lines.Add($"  source: {profile.Source}");
            lines.Add($"  workspacePath: {profile.WorkspacePath ?? "(from --workspace)"}");
            lines.Add($"  validationCommand: {profile.ValidationCommand ?? "(not configured)"}");
        }

        return new WorkflowListReport(lines);
    }

    public string ToDisplayText() => string.Join(Environment.NewLine, Lines);
}
