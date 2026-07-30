using System.CommandLine;
using CSharpAiCli.Core;

namespace CSharpAiCli.Cli;

internal sealed class VersionCommandModule : ICliCommandModule
{
    public Command Create(CliCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Command command = new("version", "Print product version metadata.");
        command.SetAction(parseResult =>
        {
            if (parseResult.GetValue(context.GlobalOptions.Verbose))
            {
                string? workspacePath = parseResult.GetValue(context.GlobalOptions.Workspace);
                CliEnvironmentSnapshot snapshot = context.WorkspaceSnapshotProvider(workspacePath);
                context.WriteVerboseDiagnostics(parseResult, "version", snapshot);
            }

            context.Output.WriteLine($"{ProductInfo.CommandName} {ProductInfo.Version}");
            context.Output.WriteLine($"target framework: {ProductInfo.TargetFramework}");
            context.Output.WriteLine($"release runtime: {ProductInfo.ReleaseRuntime}");
            return 0;
        });
        return command;
    }
}
