namespace CSharpAiCli.Core;

public interface IPatchApplier
{
    PatchPreview Preview(WorkspaceContext workspace, PatchOperation operation);

    PatchApplyResult Apply(WorkspaceContext workspace, PatchPreview preview);
}
