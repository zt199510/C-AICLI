using System.CommandLine;
using CSharpAiCli.Core;

namespace CSharpAiCli.Cli;

public static class CliCommandFactory
{
    public static RootCommand Create(TextWriter output)
    {
        return Create(
            output,
            workspacePath => CliEnvironmentSnapshot.Create(workspacePath: workspacePath),
            (commandName, snapshot) => CommandLogger.Append(commandName, snapshot),
            snapshot => OpenAiResponsesModelClient.Create(snapshot));
    }

    public static RootCommand Create(TextWriter output, Func<string?, CliEnvironmentSnapshot> snapshotProvider)
    {
        return Create(
            output,
            snapshotProvider,
            (_, _) => { },
            snapshot => OpenAiResponsesModelClient.Create(snapshot));
    }

    public static RootCommand Create(
        TextWriter output,
        Func<string?, CliEnvironmentSnapshot> snapshotProvider,
        Action<string, CliEnvironmentSnapshot> commandLogger)
    {
        return Create(
            output,
            snapshotProvider,
            commandLogger,
            snapshot => OpenAiResponsesModelClient.Create(snapshot));
    }

    public static RootCommand Create(
        TextWriter output,
        Func<string?, CliEnvironmentSnapshot> snapshotProvider,
        Action<string, CliEnvironmentSnapshot> commandLogger,
        Func<CliEnvironmentSnapshot, IChatModelClient> chatModelClientFactory)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(snapshotProvider);
        ArgumentNullException.ThrowIfNull(commandLogger);
        ArgumentNullException.ThrowIfNull(chatModelClientFactory);

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
            TryWriteCommandLog(commandLogger, "doctor", snapshot);
            output.WriteLine(DoctorReport.Create(snapshot).ToDisplayText());
            return 0;
        });

        Command configCommand = new("config", "Inspect CLI configuration.");
        Command configGetCommand = new("get", "Print the effective configuration summary.");
        configGetCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "config get", snapshot);
            output.WriteLine(ConfigReport.Create(snapshot).ToDisplayText());
            return 0;
        });

        configCommand.Subcommands.Add(configGetCommand);

        Command chatCommand = new("chat", "Send one prompt to the configured model.");
        Argument<string> promptArgument = new("prompt")
        {
            Description = "The user message to send to the model.",
        };
        chatCommand.Arguments.Add(promptArgument);
        chatCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            string prompt = parseResult.GetValue(promptArgument) ?? string.Empty;
            CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "chat", snapshot);

            IChatModelClient chatModelClient = chatModelClientFactory(snapshot);
            ChatModelResult result = chatModelClient.Send(new ChatRequest(prompt));
            output.WriteLine(ChatModelReport.Create(snapshot, result).ToDisplayText());
            return result.IsSuccess ? 0 : 1;
        });

        rootCommand.Subcommands.Add(doctorCommand);
        rootCommand.Subcommands.Add(configCommand);
        rootCommand.Subcommands.Add(chatCommand);

        return rootCommand;
    }

    private static void TryWriteCommandLog(
        Action<string, CliEnvironmentSnapshot> commandLogger,
        string commandName,
        CliEnvironmentSnapshot snapshot)
    {
        try
        {
            commandLogger(commandName, snapshot);
        }
        catch
        {
        }
    }
}
