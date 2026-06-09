using System.CommandLine;
using CSharpAiCli.Cli;

return CliCommandFactory.Invoke(
    CliCommandFactory.Create(Console.Out),
    args,
    Console.Error);
