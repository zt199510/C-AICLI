namespace CSharpAiCli.Core;

public sealed class GitStatusTool : ITool
{
    private readonly IWorkspaceGuard workspaceGuard;
    private readonly GitCommandRunner gitCommandRunner = new();

    public GitStatusTool(IWorkspaceGuard workspaceGuard)
    {
        ArgumentNullException.ThrowIfNull(workspaceGuard);
        this.workspaceGuard = workspaceGuard;
    }

    public ToolDefinition Definition { get; } = new(
        "git.status",
        "Summarize git status for the current workspace.",
        """{"type":"object"}""");

    public ToolExecutionResult Execute(
        ToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        WorkspaceGuardResult guardResult = workspaceGuard.ResolvePath(context.Workspace, ".");
        if (!guardResult.IsAllowed || guardResult.FullPath is null)
        {
            return guardResult.ToFailure();
        }

        GitCommandResult result = gitCommandRunner.Run(guardResult.FullPath, "status --short");
        if (!result.Succeeded)
        {
            return ToolExecutionResult.Failure(
                NormalizeGitError(result),
                string.IsNullOrWhiteSpace(result.Stderr) ? result.Summary : result.Stderr.Trim());
        }

        string output = string.IsNullOrWhiteSpace(result.Stdout)
            ? "working tree clean"
            : result.Stdout.Trim();
        return ToolExecutionResult.Success(output);
    }

    private static string NormalizeGitError(GitCommandResult result)
    {
        return result.Stderr.Contains("not a git repository", StringComparison.OrdinalIgnoreCase)
            ? "git-not-repository"
            : result.ErrorCode ?? "git-status-failed";
    }
}
