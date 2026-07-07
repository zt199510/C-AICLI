using System.Text;

namespace CSharpAiCli.Core;

public static class ConversationTranscriptContextFormatter
{
    public static string Format(ConversationTranscript transcript)
    {
        ArgumentNullException.ThrowIfNull(transcript);

        StringBuilder builder = new();
        builder.AppendLine("Conversation transcript context");
        builder.AppendLine($"session: {transcript.SessionName}");

        if (transcript.Messages.Count > 0)
        {
            builder.AppendLine("messages:");
            foreach (ConversationMessage message in transcript.Messages)
            {
                builder.AppendLine($"- {message.Role}: {message.Content}");
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
                    builder.Append($"localErrorCode={error.LocalErrorCode} ");
                }

                builder.AppendLine($"safeMessage={error.SafeMessage}");
            }
        }

        if (transcript.ToolCalls.Count > 0)
        {
            builder.AppendLine("tool calls:");
            foreach (ConversationToolCall toolCall in transcript.ToolCalls)
            {
                string status = toolCall.Succeeded ? "succeeded" : "failed";
                string? summary = toolCall.Succeeded ? toolCall.OutputSummary : toolCall.FailureReason;
                builder.Append($"- {toolCall.ToolName}: {status}");
                if (!string.IsNullOrWhiteSpace(toolCall.ErrorCode))
                {
                    builder.Append($" errorCode={toolCall.ErrorCode}");
                }

                if (!string.IsNullOrWhiteSpace(summary))
                {
                    builder.Append($" summary={summary}");
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
}
