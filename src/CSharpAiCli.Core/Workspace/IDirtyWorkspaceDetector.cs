namespace CSharpAiCli.Core;

public interface IDirtyWorkspaceDetector
{
    DirtyWorkspaceStatus Detect(WorkspaceContext workspace);
}
