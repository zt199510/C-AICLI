using System.CommandLine;
using CSharpAiCli.Cli;

return CliCommandFactory
    .Create(Console.Out)
    .Parse(args)
    .Invoke();
