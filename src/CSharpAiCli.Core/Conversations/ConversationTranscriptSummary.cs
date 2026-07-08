namespace CSharpAiCli.Core;

public sealed record ConversationTranscriptSummary(
    string Name,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    int TurnCount,
    int ToolCallCount)
{
    public static ConversationTranscriptSummary FromTranscript(ConversationTranscript transcript)
    {
        ArgumentNullException.ThrowIfNull(transcript);

        return new ConversationTranscriptSummary(
            Name: transcript.SessionName,
            CreatedAtUtc: transcript.CreatedAtUtc,
            UpdatedAtUtc: transcript.UpdatedAtUtc,
            TurnCount: transcript.Messages.Count(message => string.Equals(message.Role, "user", StringComparison.Ordinal)),
            ToolCallCount: transcript.ToolCalls.Count);
    }
}
