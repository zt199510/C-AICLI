using System.Text.Json;

namespace CSharpAiCli.Core;

public sealed class GitDiffTool : ITool
{
    private readonly IWorkspaceGuard workspaceGuard;
    private readonly GitCommandRunner gitCommandRunner = new();

    public GitDiffTool(IWorkspaceGuard workspaceGuard)
    {
        ArgumentNullException.ThrowIfNull(workspaceGuard);
        this.workspaceGuard = workspaceGuard;
    }

    public ToolDefinition Definition { get; } = new(
        "git.diff",
        "Show git diff for the current workspace.",
        """{"type":"object","properties":{"stat":{"type":"boolean"}}}""",
        ToolRiskLevel.Read);

    public ToolExecutionResult Execute(
        ToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryReadArguments(context.ArgumentsJson, out bool stat, out ToolExecutionResult? failure))
        {
            return failure;
        }

        WorkspaceGuardResult guardResult = workspaceGuard.ResolvePath(context.Workspace, ".");
        if (!guardResult.IsAllowed || guardResult.FullPath is null)
        {
            return guardResult.ToFailure();
        }

        string gitArguments = stat ? "diff --stat --" : "diff --";
        GitCommandResult result = gitCommandRunner.Run(guardResult.FullPath, gitArguments);
        if (!result.Succeeded)
        {
            return ToolExecutionResult.Failure(
                NormalizeGitError(result),
                string.IsNullOrWhiteSpace(result.Stderr) ? result.Summary : result.Stderr.Trim());
        }

        string output = string.IsNullOrWhiteSpace(result.Stdout)
            ? "no diff"
            : result.Stdout.Trim();
        return ToolExecutionResult.Success(output);
    }

    private static bool TryReadArguments(
        string? argumentsJson,
        out bool stat,
        out ToolExecutionResult failure)
    {
        stat = false;
        failure = null!;

        try
        {
            using JsonDocument document = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                failure = ToolExecutionResult.Failure(
                    "invalid-tool-arguments",
                    "Tool arguments must be a JSON object.");
                return false;
            }

            if (root.TryGetProperty("stat", out JsonElement statElement))
            {
                if (statElement.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
                {
                    failure = ToolExecutionResult.Failure(
                        "invalid-tool-arguments",
                        "stat must be a boolean.");
                    return false;
                }

                stat = statElement.GetBoolean();
            }

            return true;
        }
        catch (JsonException)
        {
            failure = ToolExecutionResult.Failure(
                "invalid-tool-arguments",
                "Tool arguments must be valid JSON.");
            return false;
        }
    }

    private static string NormalizeGitError(GitCommandResult result)
    {
        return result.Stderr.Contains("not a git repository", StringComparison.OrdinalIgnoreCase)
            ? "git-not-repository"
            : result.ErrorCode ?? "git-diff-failed";
    }
}
