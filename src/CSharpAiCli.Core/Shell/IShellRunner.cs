namespace CSharpAiCli.Core;

public interface IShellRunner
{
    ShellCommandResult Run(
        WorkspaceContext workspace,
        ShellCommandRequest request,
        CancellationToken cancellationToken = default);
}
