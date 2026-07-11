using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ToolErrorCodeTests
{
    [Fact]
    public void Core_executor_error_codes_expose_stable_canonical_values()
    {
        Assert.Equal("unknown-tool", ToolErrorCode.UnknownTool);
        Assert.Equal("tool-disabled", ToolErrorCode.ToolDisabled);
        Assert.Equal("invalid-tool-arguments", ToolErrorCode.InvalidToolArguments);
        Assert.Equal("tool-returned-null", ToolErrorCode.ToolReturnedNull);
        Assert.Equal("tool-execution-failed", ToolErrorCode.ToolExecutionFailed);
        Assert.Equal("planning-phase-write-denied", ToolErrorCode.PlanningPhaseWriteDenied);
    }

    [Fact]
    public void Built_in_tool_error_codes_expose_stable_canonical_values()
    {
        Assert.Equal("file-not-found", ToolErrorCode.FileNotFound);
        Assert.Equal("file-too-large", ToolErrorCode.FileTooLarge);
        Assert.Equal("binary-file-not-supported", ToolErrorCode.BinaryFileNotSupported);
        Assert.Equal("search-path-not-directory", ToolErrorCode.SearchPathNotDirectory);
        Assert.Equal("approval-denied", ToolErrorCode.ApprovalDenied);
        Assert.Equal("shell-command-failed", ToolErrorCode.ShellCommandFailed);
        Assert.Equal("patch-apply-failed", ToolErrorCode.PatchApplyFailed);
        Assert.Equal("git-not-repository", ToolErrorCode.GitNotRepository);
        Assert.Equal("git-status-failed", ToolErrorCode.GitStatusFailed);
        Assert.Equal("git-diff-failed", ToolErrorCode.GitDiffFailed);
        Assert.Equal("workspace-unavailable", ToolErrorCode.WorkspaceUnavailable);
        Assert.Equal("invalid-workspace-path", ToolErrorCode.InvalidWorkspacePath);
        Assert.Equal("workspace-boundary-denied", ToolErrorCode.WorkspaceBoundaryDenied);
        Assert.Equal("git-timeout", ToolErrorCode.GitTimeout);
        Assert.Equal("git-command-failed", ToolErrorCode.GitCommandFailed);
        Assert.Equal("git-unavailable", ToolErrorCode.GitUnavailable);
        Assert.Equal("shell-cwd-denied", ToolErrorCode.ShellCwdDenied);
        Assert.Equal("shell-policy-denied", ToolErrorCode.ShellPolicyDenied);
        Assert.Equal("dangerous-command-denied", ToolErrorCode.DangerousCommandDenied);
        Assert.Equal("shell-timeout", ToolErrorCode.ShellTimeout);
        Assert.Equal("shell-exit-code", ToolErrorCode.ShellExitCode);
        Assert.Equal("shell-execution-failed", ToolErrorCode.ShellExecutionFailed);
        Assert.Equal("invalid-patch", ToolErrorCode.InvalidPatch);
        Assert.Equal("patch-context-not-found", ToolErrorCode.PatchContextNotFound);
        Assert.Equal("patch-target-changed", ToolErrorCode.PatchTargetChanged);
    }
}
