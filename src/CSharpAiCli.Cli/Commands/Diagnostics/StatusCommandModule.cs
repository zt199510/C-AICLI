using System.CommandLine;
using CSharpAiCli.Core;

namespace CSharpAiCli.Cli;

internal sealed class StatusCommandModule : ICliCommandModule
{
    public Command Create(CliCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Command command = new("status", "Summarize workspace, git, configuration, and approval status.");
        command.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(context.GlobalOptions.Workspace);
            CliEnvironmentSnapshot snapshot = context.WorkspaceSnapshotProvider(workspacePath);
            context.TryWriteCommandLog("status", snapshot);
            context.WriteVerboseDiagnostics(parseResult, "status", snapshot);
            GitStatusTool gitStatusTool = new(new WorkspaceGuard());
            ToolExecutionResult gitStatus = gitStatusTool.Execute(new ToolExecutionContext(
                "cli_status",
                snapshot.Workspace,
                "{}"));
            context.Output.WriteLine(StatusReport.Create(snapshot, gitStatus).ToDisplayText());
            return 0;
        });
        return command;
    }
}
