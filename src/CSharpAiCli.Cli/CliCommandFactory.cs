using System.CommandLine;
using CSharpAiCli.Core;

namespace CSharpAiCli.Cli;

public static class CliCommandFactory
{
    public static RootCommand Create(TextWriter output)
    {
        return Create(output, workspacePath => CliEnvironmentSnapshot.Create(workspacePath: workspacePath));
    }

    public static RootCommand Create(TextWriter output, Func<string?, CliEnvironmentSnapshot> snapshotProvider)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(snapshotProvider);

        RootCommand rootCommand = new($"{ProductInfo.CommandName} - {ProductInfo.Description}");
        Option<string> workspaceOption = new("--workspace")
        {
            Description = "Use a workspace directory instead of the current directory.",
            Recursive = true,
        };
        rootCommand.Options.Add(workspaceOption);

        Command doctorCommand = new("doctor", "Inspect runtime, workspace, and configuration readiness.");
        doctorCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath);
            output.WriteLine(DoctorReport.Create(snapshot).ToDisplayText());
            return 0;
        });

        Command configCommand = new("config", "Inspect CLI configuration.");
        Command configGetCommand = new("get", "Print the effective configuration summary.");
        configGetCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath);
            output.WriteLine(ConfigReport.Create(snapshot).ToDisplayText());
            return 0;
        });

        configCommand.Subcommands.Add(configGetCommand);
        rootCommand.Subcommands.Add(doctorCommand);
        rootCommand.Subcommands.Add(configCommand);

        return rootCommand;
    }
}
