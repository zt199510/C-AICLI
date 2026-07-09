using System.Security.Cryptography;
using System.Text;

namespace CSharpAiCli.Core;

public sealed class SingleFilePatchApplier : IPatchApplier
{
    private readonly IWorkspaceGuard workspaceGuard;
    private readonly IDirtyWorkspaceDetector dirtyWorkspaceDetector;

    public SingleFilePatchApplier(
        IWorkspaceGuard workspaceGuard,
        IDirtyWorkspaceDetector dirtyWorkspaceDetector)
    {
        ArgumentNullException.ThrowIfNull(workspaceGuard);
        ArgumentNullException.ThrowIfNull(dirtyWorkspaceDetector);

        this.workspaceGuard = workspaceGuard;
        this.dirtyWorkspaceDetector = dirtyWorkspaceDetector;
    }

    public PatchPreview Preview(WorkspaceContext workspace, PatchOperation operation)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(operation);

        WorkspaceGuardResult guardResult = workspaceGuard.ResolvePath(workspace, operation.Path);
        if (!guardResult.IsAllowed || guardResult.FullPath is null)
        {
            throw new ToolSecurityException(
                guardResult.ErrorCode ?? ToolErrorCode.WorkspaceBoundaryDenied,
                guardResult.SafeMessage);
        }

        if (!File.Exists(guardResult.FullPath))
        {
            throw new ToolExecutionException(
                ToolErrorCode.FileNotFound,
                "Patch target file was not found.");
        }

        if (string.IsNullOrEmpty(operation.Find))
        {
            throw new ToolExecutionException(
                ToolErrorCode.InvalidPatch,
                "Patch find text must not be empty.");
        }

        string originalContent = File.ReadAllText(guardResult.FullPath);
        int replacements = CountOccurrences(originalContent, operation.Find);
        if (replacements == 0)
        {
            throw new ToolExecutionException(
                ToolErrorCode.PatchContextNotFound,
                "Patch context was not found in the target file.");
        }

        string relativePath = Path.GetRelativePath(workspace.RootPath, guardResult.FullPath);
        string diff = CreateSimpleDiff(relativePath, operation.Find, operation.Replace);
        DirtyWorkspaceStatus dirtyStatus = dirtyWorkspaceDetector.Detect(workspace);

        return new PatchPreview(
            Operation: operation,
            FullPath: guardResult.FullPath,
            OriginalContentHash: ComputeHash(originalContent),
            Replacements: replacements,
            Summary: $"Replace {replacements} occurrence(s) in {relativePath}. Dirty workspace: {dirtyStatus.Summary}.",
            Diff: diff,
            DirtyWorkspace: dirtyStatus);
    }

    public PatchApplyResult Apply(WorkspaceContext workspace, PatchPreview preview)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(preview);

        if (!File.Exists(preview.FullPath))
        {
            return PatchApplyResult.Failure(
                ToolErrorCode.FileNotFound,
                "Patch target file was not found.",
                diff: preview.Diff);
        }

        WorkspaceGuardResult guardResult = workspaceGuard.ResolvePath(workspace, preview.FullPath);
        if (!guardResult.IsAllowed)
        {
            return PatchApplyResult.Failure(
                guardResult.ErrorCode ?? ToolErrorCode.WorkspaceBoundaryDenied,
                guardResult.SafeMessage,
                diff: preview.Diff);
        }

        string currentContent = File.ReadAllText(preview.FullPath);
        if (!string.Equals(ComputeHash(currentContent), preview.OriginalContentHash, StringComparison.Ordinal))
        {
            return PatchApplyResult.Failure(
                ToolErrorCode.PatchTargetChanged,
                "Patch target changed after preview; refusing to apply.",
                diff: preview.Diff);
        }

        if (!currentContent.Contains(preview.Operation.Find, StringComparison.Ordinal))
        {
            return PatchApplyResult.Failure(
                ToolErrorCode.PatchContextNotFound,
                "Patch context was not found in the target file.",
                diff: preview.Diff);
        }

        string updatedContent = currentContent.Replace(
            preview.Operation.Find,
            preview.Operation.Replace,
            StringComparison.Ordinal);
        File.WriteAllText(preview.FullPath, updatedContent);

        return PatchApplyResult.Success(
            $"Applied patch: {preview.Summary}",
            approvalStatus: "approved",
            diff: preview.Diff);
    }

    private static int CountOccurrences(string content, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = content.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string ComputeHash(string content)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string CreateSimpleDiff(string path, string find, string replace)
    {
        return string.Join(
            Environment.NewLine,
            [
                $"--- a/{path}",
                $"+++ b/{path}",
                "@@",
                "-" + find,
                "+" + replace
            ]);
    }
}
