namespace CSharpAiCli.Core;

public interface IInstructionLoader
{
    InstructionLoadResult Load(WorkspaceContext workspace);

    InstructionLoadResult Load(WorkspaceContext workspace, string? targetPath);
}
