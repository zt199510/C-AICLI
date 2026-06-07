using CSharpAiCli.Core;

namespace CSharpAiCli.ProjectPacks.GerberTiff;

public sealed class GerberTiffWorkflowPack
{
    public const string ProfileName = "gerber-tiff";

    public GerberTiffStatusReport CreateStatusReport(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        string plansDirectory = Path.Combine(workspaceRoot, "docs_md", "plans");
        List<string> planFiles = Directory.Exists(plansDirectory)
            ? Directory.EnumerateFiles(plansDirectory, "*.md").Select(Path.GetFileName).Where(name => name is not null).Cast<string>().OrderBy(name => name, StringComparer.Ordinal).ToList()
            : [];
        string status = Directory.Exists(plansDirectory)
            ? "plans-found"
            : "plans-missing";

        return new GerberTiffStatusReport(
            WorkspaceRoot: workspaceRoot,
            PlansDirectory: plansDirectory,
            Status: status,
            PlanFiles: planFiles);
    }

    public WorkflowValidationSuggestion CreateValidationSuggestion(
        WorkflowRegistry registry,
        WorkspaceContext workspace)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(workspace);

        if (registry.TrySuggestValidation(ProfileName, workspace, out WorkflowValidationSuggestion? suggestion) &&
            suggestion is not null)
        {
            return suggestion;
        }

        return new WorkflowValidationSuggestion(
            ProfileName: ProfileName,
            WorkspacePath: workspace.RootPath,
            WorkspacePathSource: "--workspace",
            ValidationCommand: null,
            Source: "missing",
            RequiresApproval: false);
    }
}
