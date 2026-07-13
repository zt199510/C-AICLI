using System.Text.Json;

namespace CSharpAiCli.Core;

public sealed class AgentTaskContextCollector
{
    private const int MaxGitSummaryCharacters = 4096;

    private readonly IWorkspaceGuard workspaceGuard;
    private readonly GitStatusTool gitStatusTool;
    private readonly GitDiffTool gitDiffTool;
    private readonly WorkflowReferenceResolver referenceResolver;

    public AgentTaskContextCollector()
        : this(new WorkspaceGuard())
    {
    }

    internal AgentTaskContextCollector(
        IWorkspaceGuard workspaceGuard,
        GitStatusTool? gitStatusTool = null,
        GitDiffTool? gitDiffTool = null,
        WorkflowReferenceResolver? referenceResolver = null)
    {
        ArgumentNullException.ThrowIfNull(workspaceGuard);
        this.workspaceGuard = workspaceGuard;
        this.gitStatusTool = gitStatusTool ?? new GitStatusTool(workspaceGuard);
        this.gitDiffTool = gitDiffTool ?? new GitDiffTool(workspaceGuard);
        this.referenceResolver = referenceResolver ?? new WorkflowReferenceResolver(workspaceGuard);
    }

    public AgentTaskContext Collect(
        WorkspaceContext workspace,
        InstructionLoadResult instructions,
        string? currentDirectory = null,
        string? sessionName = null,
        bool hasTranscriptContext = false,
        string? prompt = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(instructions);

        (string resolvedCurrentDirectory, string? currentDirectoryErrorCode) =
            ResolveCurrentDirectory(workspace, currentDirectory);
        WorkflowReferenceResolution references = referenceResolver.Resolve(workspace, prompt);
        AgentGitContextSummary git = references.HasErrors
            ? CreateSkippedGitSummary(references)
            : CollectGitSummary(workspace);

        return new AgentTaskContext(
            CurrentDirectory: resolvedCurrentDirectory,
            WorkspaceRoot: workspace.RootPath,
            WorkspaceStatus: workspace.Status.ToString(),
            CurrentDirectoryErrorCode: currentDirectoryErrorCode,
            Instructions: instructions.Instructions,
            InstructionSources: instructions.Sources.ToArray(),
            InstructionWarnings: instructions.Warnings.ToArray(),
            SessionName: sessionName,
            HasTranscriptContext: hasTranscriptContext,
            Git: git,
            References: references);
    }

    private (string CurrentDirectory, string? ErrorCode) ResolveCurrentDirectory(
        WorkspaceContext workspace,
        string? currentDirectory)
    {
        string requestedPath = string.IsNullOrWhiteSpace(currentDirectory)
            ? "."
            : currentDirectory;
        WorkspaceGuardResult guardResult = workspaceGuard.ResolvePath(workspace, requestedPath);
        if (guardResult.IsAllowed && !string.IsNullOrWhiteSpace(guardResult.FullPath))
        {
            return (guardResult.FullPath!, null);
        }

        return (requestedPath, guardResult.ErrorCode ?? ToolErrorCode.WorkspaceBoundaryDenied);
    }

    private AgentGitContextSummary CollectGitSummary(WorkspaceContext workspace)
    {
        ToolExecutionContext statusContext = new(
            "context_git_status",
            workspace,
            "{}",
            ToolExecutionPhase.Planning);
        ToolExecutionResult status = gitStatusTool.Execute(statusContext);

        ToolExecutionContext diffContext = new(
            "context_git_diff",
            workspace,
            """{"stat":true}""",
            ToolExecutionPhase.Planning);
        ToolExecutionResult diff = gitDiffTool.Execute(diffContext);

        BoundedText statusSummary = Bound(status.Summary, MaxGitSummaryCharacters);
        BoundedText diffSummary = Bound(diff.Summary, MaxGitSummaryCharacters);

        return new AgentGitContextSummary(
            StatusSummary: statusSummary.Text,
            StatusSucceeded: status.Succeeded,
            StatusErrorCode: status.ErrorCode,
            IsDirty: status.Succeeded && !IsCleanStatus(status.Summary),
            StatusSummaryTruncated: statusSummary.Truncated,
            DiffSummary: diffSummary.Text,
            DiffSucceeded: diff.Succeeded,
            DiffErrorCode: diff.ErrorCode,
            DiffOutputTruncated: IsDiffOutputTruncated(diff),
            DiffSummaryTruncated: diffSummary.Truncated);
    }

    private static AgentGitContextSummary CreateSkippedGitSummary(WorkflowReferenceResolution references)
    {
        string errorCode = references.FirstErrorCode ?? WorkflowReferenceErrorCode.ResolutionFailed;
        return new AgentGitContextSummary(
            StatusSummary: "Git status was not collected because workflow reference resolution failed.",
            StatusSucceeded: false,
            StatusErrorCode: errorCode,
            IsDirty: false,
            StatusSummaryTruncated: false,
            DiffSummary: "Git diff summary was not collected because workflow reference resolution failed.",
            DiffSucceeded: false,
            DiffErrorCode: errorCode,
            DiffOutputTruncated: false,
            DiffSummaryTruncated: false);
    }

    private static bool IsCleanStatus(string summary)
    {
        return string.Equals(summary.Trim(), "working tree clean", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDiffOutputTruncated(ToolExecutionResult diff)
    {
        if (diff.StructuredPayload is null ||
            !diff.StructuredPayload.TryGetValue("truncated", out JsonElement truncatedElement) ||
            truncatedElement.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            return diff.Summary.Contains(GitDiffTool.TruncationWarning, StringComparison.Ordinal);
        }

        return truncatedElement.GetBoolean();
    }

    private static BoundedText Bound(string? value, int maxCharacters)
    {
        string text = value ?? string.Empty;
        if (text.Length <= maxCharacters)
        {
            return new BoundedText(text, false);
        }

        return new BoundedText(text[..maxCharacters], true);
    }

    private readonly record struct BoundedText(string Text, bool Truncated);
}
