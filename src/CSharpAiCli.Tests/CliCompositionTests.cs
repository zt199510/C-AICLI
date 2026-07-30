using CSharpAiCli.Cli;

namespace CSharpAiCli.Tests;

public sealed class CliCompositionTests
{
    [Fact]
    public void Root_composer_preserves_top_level_command_order_and_recursive_options()
    {
        using StringWriter output = new();
        System.CommandLine.RootCommand root = CliCommandFactory.Create(output);

        Assert.Equal(
            [
                "version",
                "doctor",
                "status",
                "models",
                "diff",
                "changes",
                "jobs",
                "ci",
                "daemon",
                "api",
                "queue",
                "automation",
                "pipeline",
                "review",
                "config",
                "mcp",
                "workflow",
                "packs",
                "artifacts",
                "skills",
                "tools",
                "logs",
                "exec",
                "run",
                "session",
                "chat",
            ],
            root.Subcommands.Select(command => command.Name));
        Assert.Equal(
            ["--help", "--version", "--workspace", "--verbose", "--trace"],
            root.Options.Select(option => option.Name));
        Assert.All(
            root.Options.Where(option =>
                option.Name is "--workspace" or "--verbose" or "--trace"),
            option => Assert.True(option.Recursive));
    }
}
