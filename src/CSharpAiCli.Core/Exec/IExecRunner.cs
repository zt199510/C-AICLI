namespace CSharpAiCli.Core;

public interface IExecRunner
{
    ExecResult Run(
        ExecRequest request,
        WorkspaceContext workspace,
        IToolExecutor toolExecutor,
        CancellationToken cancellationToken = default);
}
