using System.CommandLine;

namespace CSharpAiCli.Cli;

internal sealed class CliGlobalOptions
{
    public CliGlobalOptions()
    {
        Workspace = new Option<string>("--workspace")
        {
            Description = "Use a workspace directory instead of the current directory.",
            Recursive = true,
        };
        Verbose = new Option<bool>("--verbose")
        {
            Description = "Show detailed human-readable diagnostics.",
            Recursive = true,
        };
        Trace = new Option<bool>("--trace")
        {
            Description = "Write trace-level local diagnostics.",
            Recursive = true,
        };
    }

    public Option<string> Workspace { get; }

    public Option<bool> Verbose { get; }

    public Option<bool> Trace { get; }

    public void AttachTo(RootCommand rootCommand)
    {
        ArgumentNullException.ThrowIfNull(rootCommand);
        rootCommand.Options.Add(Workspace);
        rootCommand.Options.Add(Verbose);
        rootCommand.Options.Add(Trace);
    }
}
