namespace CSharpAiCli.Core;

public interface IWorkspaceGuard
{
    WorkspaceGuardResult ResolvePath(WorkspaceContext workspace, string requestedPath);
}
