namespace CSharpAiCli.Core;

public sealed record WorkflowValidateReport(IReadOnlyList<string> Lines)
{
    public static WorkflowValidateReport Create(CliEnvironmentSnapshot snapshot, string profileName)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        WorkflowRegistry registry = new(WorkflowProfileLoader.Load(snapshot.Configuration));
        List<string> lines =
        [
            $"{ProductInfo.DisplayName} workflow validation"
        ];

        if (!registry.TrySuggestValidation(profileName, snapshot.Workspace, out WorkflowValidationSuggestion? suggestion) ||
            suggestion is null)
        {
            lines.Add($"profile: {profileName}");
            lines.Add("status: missing");
            return new WorkflowValidateReport(lines);
        }

        lines.Add($"profile: {suggestion.ProfileName}");
        lines.Add($"status: configured");
        lines.Add($"workspacePath: {suggestion.WorkspacePath}");
        lines.Add($"workspacePathSource: {suggestion.WorkspacePathSource}");
        lines.Add($"validationCommand: {suggestion.ValidationCommand ?? "(not configured)"}");
        lines.Add($"requiresApproval: {suggestion.RequiresApproval}");
        lines.Add("execution: not run");
        return new WorkflowValidateReport(lines);
    }

    public string ToDisplayText() => string.Join(Environment.NewLine, Lines);
}
