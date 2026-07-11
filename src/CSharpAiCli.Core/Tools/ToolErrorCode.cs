namespace CSharpAiCli.Core;

public static class ToolErrorCode
{
    public const string UnknownTool = "unknown-tool";
    public const string ToolDisabled = "tool-disabled";
    public const string InvalidToolArguments = "invalid-tool-arguments";
    public const string ToolReturnedNull = "tool-returned-null";
    public const string ToolExecutionFailed = "tool-execution-failed";
    public const string PlanningPhaseWriteDenied = "planning-phase-write-denied";
    public const string FileNotFound = "file-not-found";
    public const string FileTooLarge = "file-too-large";
    public const string BinaryFileNotSupported = "binary-file-not-supported";
    public const string SearchPathNotDirectory = "search-path-not-directory";
    public const string ApprovalDenied = "approval-denied";
    public const string ShellCommandFailed = "shell-command-failed";
    public const string PatchApplyFailed = "patch-apply-failed";
    public const string GitNotRepository = "git-not-repository";
    public const string GitStatusFailed = "git-status-failed";
    public const string GitDiffFailed = "git-diff-failed";
    public const string WorkspaceUnavailable = "workspace-unavailable";
    public const string InvalidWorkspacePath = "invalid-workspace-path";
    public const string WorkspaceBoundaryDenied = "workspace-boundary-denied";
    public const string GitTimeout = "git-timeout";
    public const string GitCommandFailed = "git-command-failed";
    public const string GitUnavailable = "git-unavailable";
    public const string ShellCwdDenied = "shell-cwd-denied";
    public const string ShellPolicyDenied = "shell-policy-denied";
    public const string DangerousCommandDenied = "dangerous-command-denied";
    public const string ShellTimeout = "shell-timeout";
    public const string ShellExitCode = "shell-exit-code";
    public const string ShellExecutionFailed = "shell-execution-failed";
    public const string InvalidPatch = "invalid-patch";
    public const string PatchContextNotFound = "patch-context-not-found";
    public const string PatchTargetChanged = "patch-target-changed";
}
