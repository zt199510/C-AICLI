using System.CommandLine;

namespace CSharpAiCli.Cli;

internal interface ICliCommandModule
{
    Command Create(CliCommandContext context);
}
