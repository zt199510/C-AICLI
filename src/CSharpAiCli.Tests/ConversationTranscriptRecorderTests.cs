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

    [Fact]
    public void Add_assistant_message_appends_message_and_updates_timestamp()
    {
        ConversationTranscript transcript = ConversationTranscript.Create(
            "smoke",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        ChatResponse response = new(
            Provider: "openai",
            Model: "gpt-4.1",
            ResponseId: "resp_123",
            Text: "OK.");
        DateTimeOffset messageTime = DateTimeOffset.Parse("2024-01-01T00:00:02Z");

        transcript.AddAssistantMessage(response, messageTime);

        ConversationMessage message = Assert.Single(transcript.Messages);
        Assert.Equal("assistant", message.Role);
        Assert.Equal("OK.", message.Content);
        Assert.Equal(messageTime, message.CreatedAtUtc);
        Assert.Equal("openai", message.Provider);
        Assert.Equal("gpt-4.1", message.Model);
        Assert.Equal("resp_123", message.ResponseId);
        Assert.Equal(messageTime, transcript.UpdatedAtUtc);
    }

    [Fact]
    public void Add_error_copies_safe_model_error_fields_and_updates_timestamp()
    {
        ConversationTranscript transcript = ConversationTranscript.Create(
            "smoke",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        ModelError modelError = new(
            Provider: "openai",
            Operation: "responses.create",
            StatusCode: 429,
            LocalErrorCode: "rate_limited",
            SafeMessage: "Request was rate limited. Retry later.",
            Retryable: true);
        DateTimeOffset errorTime = DateTimeOffset.Parse("2024-01-01T00:00:03Z");

        transcript.AddError(modelError, errorTime);

        ConversationError error = Assert.Single(transcript.Errors);
        Assert.Equal(errorTime, error.CreatedAtUtc);
        Assert.Equal("openai", error.Provider);
        Assert.Equal("responses.create", error.Operation);
        Assert.Equal(429, error.StatusCode);
        Assert.Equal("rate_limited", error.LocalErrorCode);
        Assert.Equal("Request was rate limited. Retry later.", error.SafeMessage);
        Assert.True(error.Retryable);
        Assert.DoesNotContain("sk-", error.SafeMessage);
        Assert.Equal(errorTime, transcript.UpdatedAtUtc);
    }

    [Fact]
    public void Record_success_appends_user_and_assistant_messages()
    {
        DateTimeOffset start = DateTimeOffset.Parse("2024-01-01T00:00:00Z");
        DateTimeOffset writeTime = DateTimeOffset.Parse("2024-01-01T00:00:05Z");
        ConversationTranscript transcript = ConversationTranscript.Create("smoke", start);
        ChatModelResult result = ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_123",
            Text: "OK."));

        ConversationTranscriptRecorder.RecordTurn(transcript, "Reply with OK.", result, writeTime);

        Assert.Equal(2, transcript.Messages.Count);
        Assert.Equal("user", transcript.Messages[0].Role);
        Assert.Equal("Reply with OK.", transcript.Messages[0].Content);
        Assert.Equal("assistant", transcript.Messages[1].Role);
        Assert.Equal("OK.", transcript.Messages[1].Content);
        Assert.Equal("openai", transcript.Messages[1].Provider);
        Assert.Equal("gpt-test", transcript.Messages[1].Model);
        Assert.Equal("resp_123", transcript.Messages[1].ResponseId);
        Assert.Empty(transcript.Errors);
        Assert.Equal(writeTime, transcript.UpdatedAtUtc);
    }

    [Fact]
    public void Record_failure_appends_user_message_and_safe_error()
    {
        DateTimeOffset start = DateTimeOffset.Parse("2024-01-01T00:00:00Z");
        DateTimeOffset writeTime = DateTimeOffset.Parse("2024-01-01T00:00:05Z");
        ConversationTranscript transcript = ConversationTranscript.Create("smoke", start);
        ChatModelResult result = ChatModelResult.Failure(new ModelError(
            Provider: "openai",
            Operation: "responses.create",
            StatusCode: null,
            LocalErrorCode: "missing-openai-api-key",
            SafeMessage: "OpenAI API key is missing. Set OPENAI_API_KEY or user config apiKey.",
            Retryable: false));

        ConversationTranscriptRecorder.RecordTurn(transcript, "Reply with OK.", result, writeTime);

        ConversationMessage message = Assert.Single(transcript.Messages);
        Assert.Equal("user", message.Role);
        Assert.Equal("Reply with OK.", message.Content);
        ConversationError error = Assert.Single(transcript.Errors);
        Assert.Equal("openai", error.Provider);
        Assert.Equal("responses.create", error.Operation);
        Assert.Equal("missing-openai-api-key", error.LocalErrorCode);
        Assert.DoesNotContain("sk-", error.SafeMessage, StringComparison.Ordinal);
        Assert.Equal(writeTime, transcript.UpdatedAtUtc);
    }

    [Fact]
    public void Record_malformed_result_throws_without_mutating_transcript()
    {
        DateTimeOffset start = DateTimeOffset.Parse("2024-01-01T00:00:00Z");
        DateTimeOffset writeTime = DateTimeOffset.Parse("2024-01-01T00:00:05Z");
        ConversationTranscript transcript = ConversationTranscript.Create("smoke", start);
        ChatModelResult result = new(null, null);

        Assert.Throws<InvalidOperationException>(
            () => ConversationTranscriptRecorder.RecordTurn(transcript, "Reply with OK.", result, writeTime));

        Assert.Empty(transcript.Messages);
        Assert.Empty(transcript.Errors);
        Assert.Equal(start, transcript.UpdatedAtUtc);
    }
}
