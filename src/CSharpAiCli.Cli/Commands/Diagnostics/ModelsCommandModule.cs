using System.CommandLine;
using CSharpAiCli.Core;

namespace CSharpAiCli.Cli;

internal sealed class ModelsCommandModule : ICliCommandModule
{
    public Command Create(CliCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Command command = new("models", "Show current model configuration and static model examples.");
        command.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(context.GlobalOptions.Workspace);
            CliEnvironmentSnapshot snapshot = context.WorkspaceSnapshotProvider(workspacePath);
            context.TryWriteCommandLog("models", snapshot);
            context.WriteVerboseDiagnostics(parseResult, "models", snapshot);
            context.Output.WriteLine(ModelsReport.Create(snapshot).ToDisplayText());
            return 0;
        });
        return command;
    }
}
