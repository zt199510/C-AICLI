using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using CSharpAiCli.Core;

namespace CSharpAiCli.Cli;

internal sealed class SessionCommandModule : ICliCommandModule
{
    private const string InvalidTranscriptSummary =
        "Conversation transcript is missing or uses an unsupported schema version.";

    private const string StoreErrorSummary =
        "Conversation session store operation failed.";

    public Command Create(CliCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Command command = new("session", "Manage local conversation transcripts.");
        Command listCommand = new("list", "List local session summaries.");
        Command showCommand = new("show", "Show one local session summary.");
        Command exportCommand = new("export", "Print one session transcript.");
        Command clearCommand = new("clear", "Delete one session transcript.");
        Command deleteCommand = new("delete", "Delete one local session transcript.");
        Command renameCommand = new("rename", "Rename one local session transcript.");
        Argument<string> nameArgument = new("name") { Description = "The session name." };
        Option<string> exportFormatOption = new("--format")
        {
            Description = "Select export format: json or markdown.",
            DefaultValueFactory = _ => "json",
        };
        exportFormatOption.Validators.Add(result =>
        {
            string format = result.GetValueOrDefault<string>() ?? "json";
            if (!string.Equals(format, "json", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(format, "markdown", StringComparison.OrdinalIgnoreCase))
            {
                result.AddError("Invalid value for --format. Allowed values are json and markdown.");
            }
        });
        Argument<string> renameSourceArgument = new("old") { Description = "The current session name." };
        Argument<string> renameDestinationArgument = new("new") { Description = "The new session name." };
        showCommand.Arguments.Add(nameArgument);
        exportCommand.Arguments.Add(nameArgument);
        exportCommand.Options.Add(exportFormatOption);
        clearCommand.Arguments.Add(nameArgument);
        deleteCommand.Arguments.Add(nameArgument);
        renameCommand.Arguments.Add(renameSourceArgument);
        renameCommand.Arguments.Add(renameDestinationArgument);

        listCommand.SetAction(parseResult =>
        {
            CliEnvironmentSnapshot snapshot = GetSnapshot(context, parseResult, "session list");
            try
            {
                WriteList(context.Output, context.Dependencies.ConversationStoreFactory(snapshot).ListSummaries());
                return 0;
            }
            catch (Exception exception) when (IsStoreException(exception))
            {
                return WriteStoreFailure(context, exception);
            }
        });
        showCommand.SetAction(parseResult =>
        {
            string name = parseResult.GetValue(nameArgument) ?? string.Empty;
            CliEnvironmentSnapshot snapshot = GetSnapshot(context, parseResult, "session show");
            if (!context.TryParseSessionName(name, out ConversationSessionName sessionName))
            {
                return 1;
            }

            try
            {
                return Show(context.Output, context.Dependencies.ConversationStoreFactory(snapshot), sessionName);
            }
            catch (Exception exception) when (IsStoreException(exception))
            {
                return WriteStoreFailure(context, exception);
            }
        });
        exportCommand.SetAction(parseResult =>
        {
            string name = parseResult.GetValue(nameArgument) ?? string.Empty;
            string format = parseResult.GetValue(exportFormatOption) ?? "json";
            string? workspacePath = parseResult.GetValue(context.GlobalOptions.Workspace);
            CliEnvironmentSnapshot snapshot = context.WorkspaceSnapshotProvider(workspacePath);
            context.TryWriteCommandLog("session export", snapshot);
            context.WriteVerboseDiagnostics(
                parseResult,
                "session export",
                snapshot,
                humanReadableOutput: string.Equals(format, "markdown", StringComparison.OrdinalIgnoreCase));
            if (!context.TryParseSessionName(name, out ConversationSessionName sessionName))
            {
                return 1;
            }

            try
            {
                return string.Equals(format, "markdown", StringComparison.OrdinalIgnoreCase)
                    ? ExportMarkdown(context.Output, context.Dependencies.ConversationStoreFactory(snapshot), sessionName)
                    : ExportJson(context.Output, snapshot, sessionName);
            }
            catch (Exception exception) when (IsStoreException(exception))
            {
                return WriteStoreFailure(context, exception);
            }
        });
        clearCommand.SetAction(parseResult =>
        {
            string name = parseResult.GetValue(nameArgument) ?? string.Empty;
            CliEnvironmentSnapshot snapshot = GetSnapshot(context, parseResult, "session clear");
            if (!context.TryParseSessionName(name, out ConversationSessionName sessionName))
            {
                return 1;
            }

            try
            {
                return Delete(
                    context.Output,
                    context.Dependencies.ConversationStoreFactory(snapshot),
                    sessionName,
                    "cleared",
                    missingExitCode: 0,
                    writeMissingErrorCode: false);
            }
            catch (Exception exception) when (IsStoreException(exception))
            {
                return WriteStoreFailure(context, exception);
            }
        });
        deleteCommand.SetAction(parseResult =>
        {
            string name = parseResult.GetValue(nameArgument) ?? string.Empty;
            CliEnvironmentSnapshot snapshot = GetSnapshot(context, parseResult, "session delete");
            if (!context.TryParseSessionName(name, out ConversationSessionName sessionName))
            {
                return 1;
            }

            try
            {
                return Delete(
                    context.Output,
                    context.Dependencies.ConversationStoreFactory(snapshot),
                    sessionName,
                    "deleted",
                    missingExitCode: 1,
                    writeMissingErrorCode: true);
            }
            catch (Exception exception) when (IsStoreException(exception))
            {
                return WriteStoreFailure(context, exception);
            }
        });
        renameCommand.SetAction(parseResult =>
        {
            string source = parseResult.GetValue(renameSourceArgument) ?? string.Empty;
            string destination = parseResult.GetValue(renameDestinationArgument) ?? string.Empty;
            CliEnvironmentSnapshot snapshot = GetSnapshot(context, parseResult, "session rename");
            if (!context.TryParseSessionName(source, out ConversationSessionName sourceName) ||
                !context.TryParseSessionName(destination, out ConversationSessionName destinationName))
            {
                return 1;
            }

            try
            {
                return Rename(
                    context.Output,
                    context.Dependencies.ConversationStoreFactory(snapshot),
                    sourceName,
                    destinationName);
            }
            catch (Exception exception) when (IsStoreException(exception))
            {
                return WriteStoreFailure(context, exception);
            }
        });

        command.Subcommands.Add(listCommand);
        command.Subcommands.Add(showCommand);
        command.Subcommands.Add(exportCommand);
        command.Subcommands.Add(clearCommand);
        command.Subcommands.Add(deleteCommand);
        command.Subcommands.Add(renameCommand);
        return command;
    }

    private static CliEnvironmentSnapshot GetSnapshot(
        CliCommandContext context,
        ParseResult parseResult,
        string commandName)
    {
        string? workspacePath = parseResult.GetValue(context.GlobalOptions.Workspace);
        CliEnvironmentSnapshot snapshot = context.WorkspaceSnapshotProvider(workspacePath);
        context.TryWriteCommandLog(commandName, snapshot);
        context.WriteVerboseDiagnostics(parseResult, commandName, snapshot);
        return snapshot;
    }

    private static void WriteList(TextWriter output, IReadOnlyList<ConversationTranscriptSummary> summaries)
    {
        output.WriteLine("C# AI CLI sessions");
        if (summaries.Count == 0)
        {
            output.WriteLine("status: empty");
            return;
        }

        foreach (ConversationTranscriptSummary summary in summaries.OrderBy(item => item.Name, StringComparer.Ordinal))
        {
            output.WriteLine(
                $"- {summary.Name} created={summary.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)} updated={summary.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture)} turns={summary.TurnCount} toolCalls={summary.ToolCallCount}");
        }
    }

    private static int Show(TextWriter output, IConversationStore store, ConversationSessionName sessionName)
    {
        if (!store.TryGetSummary(sessionName, out ConversationTranscriptSummary? summary) || summary is null)
        {
            WriteNotFound(output);
            return 1;
        }

        output.WriteLine("C# AI CLI session");
        output.WriteLine($"name: {summary.Name}");
        output.WriteLine($"createdAtUtc: {summary.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)}");
        output.WriteLine($"updatedAtUtc: {summary.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture)}");
        output.WriteLine($"turnCount: {summary.TurnCount}");
        output.WriteLine($"toolCallCount: {summary.ToolCallCount}");
        return 0;
    }

    private static int ExportJson(
        TextWriter output,
        CliEnvironmentSnapshot snapshot,
        ConversationSessionName sessionName)
    {
        FileConversationStore store = FileConversationStore.Create(snapshot);
        if (!store.TryLoad(sessionName, out ConversationTranscript? transcript) || transcript is null)
        {
            WriteNotFound(output);
            return 1;
        }

        output.Write(File.ReadAllText(ResolveSessionPath(snapshot, sessionName)));
        return 0;
    }

    private static int ExportMarkdown(TextWriter output, IConversationStore store, ConversationSessionName sessionName)
    {
        if (!store.TryLoad(sessionName, out ConversationTranscript? transcript) || transcript is null)
        {
            WriteNotFound(output);
            return 1;
        }

        output.WriteLine(ConversationTranscriptMarkdownFormatter.Format(transcript));
        return 0;
    }

    private static int Delete(
        TextWriter output,
        IConversationStore store,
        ConversationSessionName sessionName,
        string successStatus,
        int missingExitCode,
        bool writeMissingErrorCode)
    {
        if (!store.Delete(sessionName))
        {
            output.WriteLine("status: not-found");
            if (writeMissingErrorCode)
            {
                output.WriteLine("errorCode: session-not-found");
            }

            return missingExitCode;
        }

        output.WriteLine($"status: {successStatus}");
        output.WriteLine($"session: {sessionName.Value}");
        return 0;
    }

    private static int Rename(
        TextWriter output,
        IConversationStore store,
        ConversationSessionName source,
        ConversationSessionName destination)
    {
        if (store.Rename(source, destination))
        {
            output.WriteLine("status: renamed");
            output.WriteLine($"from: {source.Value}");
            output.WriteLine($"to: {destination.Value}");
            return 0;
        }

        output.WriteLine("status: failed");
        output.WriteLine("errorCode: session-rename-failed");
        output.WriteLine("summary:");
        output.WriteLine("Session could not be renamed because the source is missing or the destination already exists.");
        return 1;
    }

    private static int WriteStoreFailure(CliCommandContext context, Exception exception)
    {
        bool invalidTranscript = IsInvalidTranscriptException(exception);
        context.WriteSafeFailure(
            invalidTranscript ? "session-transcript-invalid" : "session-store-error",
            invalidTranscript ? InvalidTranscriptSummary : StoreErrorSummary);
        return 1;
    }

    private static bool IsInvalidTranscriptException(Exception exception)
    {
        return exception is JsonException ||
            exception is InvalidOperationException invalidOperationException &&
            string.Equals(invalidOperationException.Message, InvalidTranscriptSummary, StringComparison.Ordinal);
    }

    private static bool IsStoreException(Exception exception)
    {
        return exception is InvalidOperationException or IOException or UnauthorizedAccessException or JsonException or NotSupportedException;
    }

    private static void WriteNotFound(TextWriter output)
    {
        output.WriteLine("status: failed");
        output.WriteLine("errorCode: session-not-found");
        output.WriteLine("summary:");
        output.WriteLine("Session transcript was not found.");
    }

    private static string ResolveSessionPath(
        CliEnvironmentSnapshot snapshot,
        ConversationSessionName sessionName)
    {
        string? userConfigDirectory = Path.GetDirectoryName(snapshot.UserConfigPath);
        string root = string.IsNullOrWhiteSpace(userConfigDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".caicli")
            : userConfigDirectory;
        string sessionDirectory = Path.GetFullPath(Path.Combine(root, "sessions"));
        string path = Path.GetFullPath(Path.Combine(sessionDirectory, $"{sessionName.FileSafeName}.transcript.json"));
        string rootedSessionDirectory = sessionDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? sessionDirectory
            : sessionDirectory + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!path.StartsWith(rootedSessionDirectory, comparison))
        {
            throw new InvalidOperationException("Conversation transcript path must remain inside the session directory.");
        }

        return path;
    }
}
