using System.CommandLine;
using CSharpAiCli.Core;

namespace CSharpAiCli.Cli;

internal sealed class CliRootComposer
{
    public CliRootComposer(CliGlobalOptions globalOptions)
    {
        ArgumentNullException.ThrowIfNull(globalOptions);
        RootCommand = new RootCommand($"{ProductInfo.CommandName} - {ProductInfo.Description}");
        globalOptions.AttachTo(RootCommand);
    }

    public RootCommand RootCommand { get; }

    public void Add(ICliCommandModule module, CliCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(context);
        Add(module.Create(context));
    }

    public void Add(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);
        RootCommand.Subcommands.Add(command);
    }

    public RootCommand Build() => RootCommand;
}
