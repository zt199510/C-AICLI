using System.CommandLine;
using CSharpAiCli.Core;

namespace CSharpAiCli.Cli;

public static class CliCommandFactory
{
    public static RootCommand Create(TextWriter output)
    {
        return Create(output, () => CliEnvironmentSnapshot.Create());
    }

    public static RootCommand Create(TextWriter output, Func<CliEnvironmentSnapshot> snapshotProvider)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(snapshotProvider);

        RootCommand rootCommand = new($"{ProductInfo.CommandName} - {ProductInfo.Description}");

        Command doctorCommand = new("doctor", "Inspect runtime, workspace, and configuration readiness.");
        doctorCommand.SetAction(_ =>
        {
            CliEnvironmentSnapshot snapshot = snapshotProvider();
            output.WriteLine(DoctorReport.Create(snapshot).ToDisplayText());
            return 0;
        });

        Command configCommand = new("config", "Inspect CLI configuration.");
        Command configGetCommand = new("get", "Print the effective configuration summary.");
        configGetCommand.SetAction(_ =>
        {
            CliEnvironmentSnapshot snapshot = snapshotProvider();
            output.WriteLine(ConfigReport.Create(snapshot).ToDisplayText());
            return 0;
        });

        configCommand.Subcommands.Add(configGetCommand);
        rootCommand.Subcommands.Add(doctorCommand);
        rootCommand.Subcommands.Add(configCommand);

        return rootCommand;
    }
}
