using System.CommandLine;
using CSharpAiCli.Core;

namespace CSharpAiCli.Cli;

internal sealed class ConfigCommandModule : ICliCommandModule
{
    public Command Create(CliCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Command command = new("config", "Inspect CLI configuration.");
        Command getCommand = new("get", "Print the effective configuration summary.");
        getCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(context.GlobalOptions.Workspace);
            CliEnvironmentSnapshot snapshot = context.WorkspaceSnapshotProvider(workspacePath);
            context.TryWriteCommandLog("config get", snapshot);
            context.WriteVerboseDiagnostics(parseResult, "config get", snapshot);
            context.Output.WriteLine(ConfigReport.Create(snapshot).ToDisplayText());
            return 0;
        });

        Command listCommand = new("list", "List non-secret configuration values and sources.");
        listCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(context.GlobalOptions.Workspace);
            CliEnvironmentSnapshot snapshot = context.WorkspaceSnapshotProvider(workspacePath);
            context.TryWriteCommandLog("config list", snapshot);
            context.WriteVerboseDiagnostics(parseResult, "config list", snapshot);
            context.Output.WriteLine(ConfigReport.Create(snapshot).ToDisplayText());
            return 0;
        });

        Command setCommand = new("set", "Set a scalar user configuration value.");
        Argument<string> setKeyArgument = new("key")
        {
            Description = "The scalar config key to set.",
        };
        Argument<string> setValueArgument = new("value")
        {
            Description = "The scalar config value to write.",
        };
        setCommand.Arguments.Add(setKeyArgument);
        setCommand.Arguments.Add(setValueArgument);
        setCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(context.GlobalOptions.Workspace);
            string key = parseResult.GetValue(setKeyArgument) ?? string.Empty;
            string value = parseResult.GetValue(setValueArgument) ?? string.Empty;
            CliEnvironmentSnapshot snapshot = context.WorkspaceSnapshotProvider(workspacePath);
            context.TryWriteCommandLog("config set", snapshot);
            context.WriteVerboseDiagnostics(parseResult, "config set", snapshot);

            ConfigFileEditResult result = ConfigFileEditor.SetUserScalar(
                snapshot.UserConfigPath,
                key,
                value);
            context.WriteConfigEditResult(result);
            return result.Succeeded ? 0 : 1;
        });

        Command unsetCommand = new("unset", "Unset a scalar user configuration value.");
        Argument<string> unsetKeyArgument = new("key")
        {
            Description = "The scalar config key to unset.",
        };
        unsetCommand.Arguments.Add(unsetKeyArgument);
        unsetCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(context.GlobalOptions.Workspace);
            string key = parseResult.GetValue(unsetKeyArgument) ?? string.Empty;
            CliEnvironmentSnapshot snapshot = context.WorkspaceSnapshotProvider(workspacePath);
            context.TryWriteCommandLog("config unset", snapshot);
            context.WriteVerboseDiagnostics(parseResult, "config unset", snapshot);

            ConfigFileEditResult result = ConfigFileEditor.UnsetUserScalar(
                snapshot.UserConfigPath,
                key);
            context.WriteConfigEditResult(result);
            return result.Succeeded ? 0 : 1;
        });

        command.Subcommands.Add(getCommand);
        command.Subcommands.Add(listCommand);
        command.Subcommands.Add(setCommand);
        command.Subcommands.Add(unsetCommand);
        return command;
    }
}
