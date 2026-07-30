using System.CommandLine;
using CSharpAiCli.Core;

namespace CSharpAiCli.Cli;

internal sealed class WorkflowCommandModule : ICliCommandModule
{
    public Command Create(CliCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Command command = new("workflow", "Inspect project workflow profiles.");
        Command listCommand = new("list", "List configured workflow profiles.");
        listCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(context.GlobalOptions.Workspace);
            CliEnvironmentSnapshot snapshot = context.WorkspaceSnapshotProvider(workspacePath);
            context.TryWriteCommandLog("workflow list", snapshot);
            context.WriteVerboseDiagnostics(parseResult, "workflow list", snapshot);
            context.Output.WriteLine(WorkflowListReport.Create(snapshot).ToDisplayText());
            return 0;
        });

        Command validateCommand = new("validate", "Suggest the validation command for a workflow profile.");
        Argument<string> profileArgument = new("profile")
        {
            Description = "The configured workflow profile name.",
        };
        validateCommand.Arguments.Add(profileArgument);
        validateCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(context.GlobalOptions.Workspace);
            string profile = parseResult.GetValue(profileArgument) ?? string.Empty;
            CliEnvironmentSnapshot snapshot = context.WorkspaceSnapshotProvider(workspacePath);
            context.TryWriteCommandLog("workflow validate", snapshot);
            context.WriteVerboseDiagnostics(parseResult, "workflow validate", snapshot);
            context.Output.WriteLine(WorkflowValidateReport.Create(snapshot, profile).ToDisplayText());
            return 0;
        });

        command.Subcommands.Add(listCommand);
        command.Subcommands.Add(validateCommand);
        return command;
    }
}
