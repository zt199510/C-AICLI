using System.Text.Json;

namespace CSharpAiCli.Core;

public sealed class GitStatusTool : ITool
{
    private const string StatusCommand = "git status --short";

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
        """{"type":"object"}""",
        ToolRiskLevel.Read);

    public ToolExecutionResult Execute(
        ToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        WorkspaceGuardResult guardResult = workspaceGuard.ResolvePath(context.Workspace, ".");
        if (!guardResult.IsAllowed || guardResult.FullPath is null)
        {
            return ToolExecutionResult.Failure(
                guardResult.ErrorCode ?? ToolErrorCode.WorkspaceBoundaryDenied,
                guardResult.SafeMessage,
                structuredPayload: ToolStructuredPayload.Create(
                    ("command", StatusCommand),
                    ("errorCode", guardResult.ErrorCode ?? ToolErrorCode.WorkspaceBoundaryDenied)));
        }

        GitCommandResult result = gitCommandRunner.Run(
            guardResult.FullPath,
            "status --short",
            cancellationToken);
        if (!result.Succeeded)
        {
            string errorCode = NormalizeGitError(result);
            return ToolExecutionResult.Failure(
                errorCode,
                string.IsNullOrWhiteSpace(result.Stderr) ? result.Summary : result.Stderr.Trim(),
                structuredPayload: CreateFailurePayload(result, errorCode));
        }

        string output = string.IsNullOrWhiteSpace(result.Stdout)
            ? "working tree clean"
            : result.Stdout.Trim();
        return ToolExecutionResult.Success(
            output,
            structuredPayload: ToolStructuredPayload.Create(
                ("command", StatusCommand),
                ("exitCode", result.ExitCode),
                ("statusTextLength", output.Length)));
    }

    private static string NormalizeGitError(GitCommandResult result)
    {
        return result.Stderr.Contains("not a git repository", StringComparison.OrdinalIgnoreCase)
            ? ToolErrorCode.GitNotRepository
            : result.ErrorCode ?? ToolErrorCode.GitStatusFailed;
    }

    private static IReadOnlyDictionary<string, JsonElement> CreateFailurePayload(
        GitCommandResult result,
        string errorCode)
    {
        return ToolStructuredPayload.Create(
            ("command", StatusCommand),
            ("exitCode", result.ExitCode),
            ("errorCode", errorCode));
    }
}
