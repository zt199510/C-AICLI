using System.CommandLine;
using System.Text.Json;
using CSharpAiCli.Core;

namespace CSharpAiCli.Cli;

internal sealed class ChatCommandModule : ICliCommandModule
{
    private const string InvalidTranscriptSummary =
        "Conversation transcript is missing or uses an unsupported schema version.";

    public Command Create(CliCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Command command = new("chat", "Send one prompt to the configured model.");
        Argument<string> promptArgument = new("prompt")
        {
            Description = "The user message to send to the model.",
        };
        Option<string> sessionOption = new("--session")
        {
            Description = "Create or append to a named chat transcript.",
        };
        Option<string> resumeOption = new("--resume")
        {
            Description = "Resume an existing named chat session.",
        };
        Option<string> cwdOption = new("--cwd")
        {
            Description = "Use a working context path for hierarchical instruction discovery.",
        };
        command.Arguments.Add(promptArgument);
        command.Options.Add(sessionOption);
        command.Options.Add(resumeOption);
        command.Options.Add(cwdOption);
        command.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(context.GlobalOptions.Workspace);
            string? cwdPath = parseResult.GetValue(cwdOption);
            string prompt = parseResult.GetValue(promptArgument) ?? string.Empty;
            string? session = parseResult.GetValue(sessionOption);
            string? resume = parseResult.GetValue(resumeOption);
            bool sessionSupplied = IsExplicit(parseResult, sessionOption);
            bool resumeSupplied = IsExplicit(parseResult, resumeOption);
            CliEnvironmentSnapshot snapshot = context.Dependencies.SnapshotProvider(workspacePath, cwdPath);
            context.TryWriteCommandLog("chat", snapshot);
            context.WriteVerboseDiagnostics(parseResult, "chat", snapshot);

            if (sessionSupplied && resumeSupplied)
            {
                context.WriteSafeFailure("session-option-conflict", "Use either --session or --resume, not both.");
                return 1;
            }

            string? effectiveSession = resumeSupplied ? resume : session;
            ConversationSessionName? sessionName = null;
            ConversationTranscript? transcript = null;
            ConversationTranscript? transcriptContext = null;
            IConversationStore? conversationStore = null;
            DateTimeOffset nowUtc = default;
            if (sessionSupplied || resumeSupplied)
            {
                if (!context.TryParseSessionName(effectiveSession ?? string.Empty, out sessionName))
                {
                    return 1;
                }

                try
                {
                    conversationStore = context.Dependencies.ConversationStoreFactory(snapshot);
                    nowUtc = context.Dependencies.UtcNowProvider();
                    if (resumeSupplied)
                    {
                        if (!conversationStore.TryLoad(sessionName, out transcript) || transcript is null)
                        {
                            context.WriteSafeFailure("session-not-found", "Session transcript was not found.");
                            return 1;
                        }

                        transcriptContext = transcript;
                    }
                    else
                    {
                        transcript = conversationStore.LoadOrCreate(sessionName, nowUtc);
                    }
                }
                catch (Exception exception) when (IsStoreException(exception))
                {
                    return WriteStoreFailure(context, exception);
                }
            }

            IChatModelClient modelClient = context.Dependencies.ChatModelClientFactory(snapshot);
            IChatStreamingRenderer renderer = context.Dependencies.StreamingRendererFactory(context.Output);
            ChatRequest request = new(
                prompt,
                effectiveSession,
                snapshot.Instructions.Instructions,
                transcriptContext);
            ChatModelResult result = modelClient.SendStreaming(request, renderer);
            if (sessionName is not null && transcript is not null && conversationStore is not null)
            {
                ConversationTranscriptRecorder.RecordTurn(transcript, prompt, result, nowUtc);
                try
                {
                    conversationStore.Save(sessionName, transcript);
                }
                catch (Exception exception) when (IsStoreException(exception))
                {
                    return WriteStoreFailure(context, exception);
                }
            }

            return result.IsSuccess ? 0 : 1;
        });
        return command;
    }

    private static bool IsExplicit<T>(ParseResult parseResult, Option<T> option)
    {
        return parseResult.GetResult(option) is { Implicit: false };
    }

    private static int WriteStoreFailure(CliCommandContext context, Exception exception)
    {
        bool invalidTranscript = exception is JsonException ||
            exception is InvalidOperationException invalidOperationException &&
            string.Equals(invalidOperationException.Message, InvalidTranscriptSummary, StringComparison.Ordinal);
        context.WriteSafeFailure(
            invalidTranscript ? "session-transcript-invalid" : "session-store-error",
            invalidTranscript ? InvalidTranscriptSummary : "Conversation session store operation failed.");
        return 1;
    }

    private static bool IsStoreException(Exception exception)
    {
        return exception is InvalidOperationException or IOException or UnauthorizedAccessException or JsonException or NotSupportedException;
    }
}
