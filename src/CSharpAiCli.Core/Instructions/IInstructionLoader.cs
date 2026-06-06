namespace CSharpAiCli.Core;

public interface IInstructionLoader
{
    InstructionLoadResult Load(WorkspaceContext workspace);
}
