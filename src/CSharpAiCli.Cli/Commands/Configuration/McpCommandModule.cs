using System.CommandLine;
using CSharpAiCli.Core;

namespace CSharpAiCli.Cli;

internal sealed class McpCommandModule : ICliCommandModule
{
    public Command Create(CliCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Command command = new("mcp", "Inspect MCP server configuration.");
        Command listCommand = new("list", "List configured MCP servers.");
        listCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(context.GlobalOptions.Workspace);
            CliEnvironmentSnapshot snapshot = context.WorkspaceSnapshotProvider(workspacePath);
            context.TryWriteCommandLog("mcp list", snapshot);
            context.WriteVerboseDiagnostics(parseResult, "mcp list", snapshot);
            context.Output.WriteLine(McpListReport.Create(snapshot).ToDisplayText());
            return 0;
        });
        command.Subcommands.Add(listCommand);

        Command doctorCommand = new("doctor", "Diagnose configured MCP servers.");
        doctorCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(context.GlobalOptions.Workspace);
            CliEnvironmentSnapshot snapshot = context.WorkspaceSnapshotProvider(workspacePath);
            context.TryWriteCommandLog("mcp doctor", snapshot);
            context.WriteVerboseDiagnostics(parseResult, "mcp doctor", snapshot);
            context.Output.WriteLine(McpDoctorReport.Create(snapshot).ToDisplayText());
            return 0;
        });
        command.Subcommands.Add(doctorCommand);
        return command;
    }
}
