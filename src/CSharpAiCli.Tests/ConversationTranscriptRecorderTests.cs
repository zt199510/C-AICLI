using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ConversationTranscriptRecorderTests
{
    [Fact]
    public void New_transcript_starts_with_schema_version_and_empty_tool_calls()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2024-01-01T00:00:00Z");

        ConversationTranscript transcript = ConversationTranscript.Create("smoke", now);

        Assert.Equal(1, transcript.SchemaVersion);
        Assert.Equal("smoke", transcript.SessionName);
        Assert.Equal(now, transcript.CreatedAtUtc);
        Assert.Equal(now, transcript.UpdatedAtUtc);
        Assert.Empty(transcript.Messages);
        Assert.Empty(transcript.ToolCalls);
        Assert.Empty(transcript.Errors);
    }

    [Fact]
    public void Add_user_message_appends_message_and_updates_timestamp()
    {
        ConversationTranscript transcript = ConversationTranscript.Create(
            "smoke",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        DateTimeOffset messageTime = DateTimeOffset.Parse("2024-01-01T00:00:01Z");

        transcript.AddUserMessage("Reply with OK.", messageTime);

        ConversationMessage message = Assert.Single(transcript.Messages);
        Assert.Equal("user", message.Role);
        Assert.Equal("Reply with OK.", message.Content);
        Assert.Equal(messageTime, message.CreatedAtUtc);
        Assert.Null(message.Provider);
        Assert.Null(message.Model);
        Assert.Null(message.ResponseId);
        Assert.Equal(messageTime, transcript.UpdatedAtUtc);
    }
}
