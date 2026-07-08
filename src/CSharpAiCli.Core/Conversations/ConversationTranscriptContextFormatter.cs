using System.Text;

namespace CSharpAiCli.Core;

public static class ConversationTranscriptContextFormatter
{
    public static string Format(ConversationTranscript transcript)
    {
        ArgumentNullException.ThrowIfNull(transcript);

        StringBuilder builder = new();
        builder.AppendLine("Conversation transcript context");
        builder.AppendLine($"session: {NormalizeSingleLine(transcript.SessionName)}");

        if (transcript.Messages.Count > 0)
        {
            builder.AppendLine("messages:");
            foreach (ConversationMessage message in transcript.Messages)
            {
                builder.AppendLine($"- {NormalizeSingleLine(message.Role)}: {NormalizeSingleLine(message.Content)}");
            }
        }

        if (transcript.Errors.Count > 0)
        {
            builder.AppendLine("errors:");
            foreach (ConversationError error in transcript.Errors)
            {
                builder.Append("- ");
                if (!string.IsNullOrWhiteSpace(error.LocalErrorCode))
                {
                    builder.Append($"localErrorCode={NormalizeSingleLine(error.LocalErrorCode)} ");
                }

                builder.AppendLine($"safeMessage={NormalizeSingleLine(error.SafeMessage)}");
            }
        }

        if (transcript.ToolCalls.Count > 0)
        {
            builder.AppendLine("tool calls:");
            foreach (ConversationToolCall toolCall in transcript.ToolCalls)
            {
                string status = toolCall.Succeeded ? "succeeded" : "failed";
                string? summary = toolCall.Succeeded ? toolCall.OutputSummary : toolCall.FailureReason;
                builder.Append($"- {NormalizeSingleLine(toolCall.ToolName)}: {status}");
                if (!string.IsNullOrWhiteSpace(toolCall.ErrorCode))
                {
                    builder.Append($" errorCode={NormalizeSingleLine(toolCall.ErrorCode)}");
                }

                if (!string.IsNullOrWhiteSpace(summary))
                {
                    builder.Append($" summary={NormalizeSingleLine(summary)}");
                }

                builder.AppendLine();
            }
        }

        return builder.ToString().TrimEnd();
    }

    public static string FormatWithCurrentPrompt(ConversationTranscript transcript, string currentPrompt)
    {
        ArgumentNullException.ThrowIfNull(transcript);

        return string.Concat(
            Format(transcript),
            Environment.NewLine,
            Environment.NewLine,
            "current prompt:",
            Environment.NewLine,
            currentPrompt);
    }

    private static string NormalizeSingleLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
