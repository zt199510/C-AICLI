using System.CommandLine;
using CSharpAiCli.Core;

namespace CSharpAiCli.Cli;

internal sealed class DiffCommandModule : ICliCommandModule
{
    public Command Create(CliCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Command command = new("diff", "Show current git diff for the workspace.");
        Option<bool> statOption = new("--stat")
        {
            Description = "Show git diff stat instead of the full patch.",
        };
        command.Options.Add(statOption);
        command.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(context.GlobalOptions.Workspace);
            bool stat = parseResult.GetValue(statOption);
            CliEnvironmentSnapshot snapshot = context.WorkspaceSnapshotProvider(workspacePath);
            context.TryWriteCommandLog("diff", snapshot);
            context.WriteVerboseDiagnostics(parseResult, "diff", snapshot);
            GitDiffTool gitDiffTool = new(new WorkspaceGuard());
            string argumentsJson = stat ? """{"stat":true}""" : "{}";
            ToolExecutionResult result = gitDiffTool.Execute(new ToolExecutionContext(
                "cli_diff",
                snapshot.Workspace,
                argumentsJson));

            if (result.Succeeded)
            {
                context.Output.WriteLine(result.Summary);
            }
            else
            {
                context.WriteToolResult(result);
            }

            return result.Succeeded ? 0 : 1;
        });
        return command;
    }
}
