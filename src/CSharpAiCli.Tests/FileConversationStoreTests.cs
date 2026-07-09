using System.Reflection;
using System.Text.Json;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class FileConversationStoreTests
{
    private const int LargeTranscriptContentLength = 16 * 1024 * 1024;

    private static readonly JsonSerializerOptions TestJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    [Fact]
    public void Load_or_create_returns_new_transcript_when_file_is_missing()
    {
        using TempDirectory temp = TempDirectory.Create();
        FileConversationStore store = new(Path.Combine(temp.Path, ".caicli", "sessions"));
        ConversationSessionName sessionName = ConversationSessionName.Parse("smoke");
        DateTimeOffset now = DateTimeOffset.Parse("2024-01-01T00:00:00Z");

        ConversationTranscript transcript = store.LoadOrCreate(sessionName, now);

        Assert.Equal("smoke", transcript.SessionName);
        Assert.Equal(now, transcript.CreatedAtUtc);
        Assert.Empty(transcript.Messages);
    }

    [Fact]
    public void Try_load_returns_false_without_creating_file_when_session_is_missing()
    {
        using TempDirectory temp = TempDirectory.Create();
        string sessionDirectory = Path.Combine(temp.Path, ".caicli", "sessions");
        FileConversationStore store = new(sessionDirectory);
        ConversationSessionName sessionName = ConversationSessionName.Parse("missing");

        bool found = store.TryLoad(sessionName, out ConversationTranscript? transcript);

        Assert.False(found);
        Assert.Null(transcript);
        Assert.False(File.Exists(Path.Combine(sessionDirectory, "missing.transcript.json")));
    }

    [Fact]
    public void Try_load_returns_existing_transcript()
    {
        using TempDirectory temp = TempDirectory.Create();
        FileConversationStore store = new(Path.Combine(temp.Path, ".caicli", "sessions"));
        ConversationSessionName sessionName = ConversationSessionName.Parse("smoke");
        ConversationTranscript transcript = ConversationTranscript.Create(
            sessionName.Value,
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        transcript.AddUserMessage("first", DateTimeOffset.Parse("2024-01-01T00:00:01Z"));
        store.Save(sessionName, transcript);

        bool found = store.TryLoad(sessionName, out ConversationTranscript? restored);

        Assert.True(found);
        Assert.NotNull(restored);
        Assert.Equal("smoke", restored.SessionName);
        Assert.Equal("first", Assert.Single(restored.Messages).Content);
    }

    [Fact]
    public void Save_writes_versioned_json_to_session_file()
    {
        using TempDirectory temp = TempDirectory.Create();
        FileConversationStore store = new(Path.Combine(temp.Path, ".caicli", "sessions"));
        ConversationSessionName sessionName = ConversationSessionName.Parse("smoke");
        ConversationTranscript transcript = ConversationTranscript.Create(
            sessionName.Value,
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        transcript.AddUserMessage("Reply with OK.", DateTimeOffset.Parse("2024-01-01T00:00:01Z"));

        string path = store.Save(sessionName, transcript);

        Assert.Equal(Path.Combine(temp.Path, ".caicli", "sessions", "smoke.transcript.json"), path);
        string json = File.ReadAllText(path);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("smoke", root.GetProperty("sessionName").GetString());
        Assert.Equal("Reply with OK.", root.GetProperty("messages")[0].GetProperty("content").GetString());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("toolCalls").ValueKind);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("errors").ValueKind);
    }

    [Fact]
    public async Task Save_writes_transcript_through_temporary_file()
    {
        using TempDirectory temp = TempDirectory.Create();
        string sessionDirectory = Path.Combine(temp.Path, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        FileConversationStore store = new(sessionDirectory);
        ConversationSessionName sessionName = ConversationSessionName.Parse("smoke");
        string largeContent = new('s', LargeTranscriptContentLength);
        ConversationTranscript transcript = CreateTranscriptWithUserMessage(sessionName.Value, largeContent);

        TemporaryFileObservation<string> observation = await ObserveTemporaryFileDuringOperationAsync(
            sessionDirectory,
            () => store.Save(sessionName, transcript));

        AssertTemporaryFileWasObserved(observation.TemporaryFilePath);
        string path = observation.Result;
        Assert.Equal(Path.Combine(sessionDirectory, "smoke.transcript.json"), path);
        string json = File.ReadAllText(path);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("smoke", root.GetProperty("sessionName").GetString());
        Assert.Equal(largeContent, root.GetProperty("messages")[0].GetProperty("content").GetString());
        AssertNoTemporaryFiles(sessionDirectory);
    }

    [Fact]
    public void Load_or_create_restores_existing_transcript()
    {
        using TempDirectory temp = TempDirectory.Create();
        FileConversationStore store = new(Path.Combine(temp.Path, ".caicli", "sessions"));
        ConversationSessionName sessionName = ConversationSessionName.Parse("smoke");
        ConversationTranscript transcript = ConversationTranscript.Create(
            sessionName.Value,
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        transcript.AddUserMessage("first", DateTimeOffset.Parse("2024-01-01T00:00:01Z"));
        store.Save(sessionName, transcript);

        ConversationTranscript restored = store.LoadOrCreate(
            sessionName,
            DateTimeOffset.Parse("2024-01-01T00:01:00Z"));

        Assert.Equal("smoke", restored.SessionName);
        Assert.Equal(DateTimeOffset.Parse("2024-01-01T00:00:00Z"), restored.CreatedAtUtc);
        Assert.Equal("first", Assert.Single(restored.Messages).Content);
    }

    [Fact]
    public void Save_after_restore_preserves_existing_messages_and_appends_new_turn()
    {
        using TempDirectory temp = TempDirectory.Create();
        FileConversationStore store = new(Path.Combine(temp.Path, ".caicli", "sessions"));
        ConversationSessionName sessionName = ConversationSessionName.Parse("smoke");
        ConversationTranscript transcript = ConversationTranscript.Create(
            sessionName.Value,
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        transcript.AddUserMessage("first", DateTimeOffset.Parse("2024-01-01T00:00:01Z"));
        store.Save(sessionName, transcript);

        ConversationTranscript restored = store.LoadOrCreate(
            sessionName,
            DateTimeOffset.Parse("2024-01-01T00:01:00Z"));
        restored.AddUserMessage("second", DateTimeOffset.Parse("2024-01-01T00:01:01Z"));
        store.Save(sessionName, restored);

        ConversationTranscript loadedAgain = store.LoadOrCreate(
            sessionName,
            DateTimeOffset.Parse("2024-01-01T00:02:00Z"));

        Assert.Equal(["first", "second"], loadedAgain.Messages.Select(message => message.Content).ToArray());
    }

    [Fact]
    public void List_session_names_returns_saved_sessions_in_name_order()
    {
        using TempDirectory temp = TempDirectory.Create();
        FileConversationStore store = new(Path.Combine(temp.Path, ".caicli", "sessions"));
        ConversationSessionName alpha = ConversationSessionName.Parse("alpha");
        ConversationSessionName bravo = ConversationSessionName.Parse("bravo");
        ConversationSessionName charlie = ConversationSessionName.Parse("charlie");
        DateTimeOffset now = DateTimeOffset.Parse("2024-01-01T00:00:00Z");
        store.Save(charlie, ConversationTranscript.Create(charlie.Value, now));
        store.Save(alpha, ConversationTranscript.Create(alpha.Value, now));
        store.Save(bravo, ConversationTranscript.Create(bravo.Value, now));

        IReadOnlyList<string> sessionNames = store.ListSessionNames();

        Assert.Equal(["alpha", "bravo", "charlie"], sessionNames);
    }

    [Fact]
    public void List_session_names_returns_logical_transcript_names()
    {
        using TempDirectory temp = TempDirectory.Create();
        FileConversationStore store = new(Path.Combine(temp.Path, ".caicli", "sessions"));
        ConversationSessionName sessionName = ConversationSessionName.Parse("release notes");
        store.Save(
            sessionName,
            ConversationTranscript.Create(sessionName.Value, DateTimeOffset.Parse("2024-01-01T00:00:00Z")));

        IReadOnlyList<string> sessionNames = store.ListSessionNames();

        Assert.Equal(["release notes"], sessionNames);
    }

    [Fact]
    public void List_session_names_returns_empty_collection_when_session_directory_is_missing()
    {
        using TempDirectory temp = TempDirectory.Create();
        FileConversationStore store = new(Path.Combine(temp.Path, ".caicli", "sessions"));

        IReadOnlyList<string> sessionNames = store.ListSessionNames();

        Assert.Empty(sessionNames);
    }

    [Fact]
    public void Try_get_summary_returns_transcript_metadata_and_counts()
    {
        using TempDirectory temp = TempDirectory.Create();
        FileConversationStore store = new(Path.Combine(temp.Path, ".caicli", "sessions"));
        ConversationSessionName sessionName = ConversationSessionName.Parse("smoke");
        ConversationTranscript transcript = ConversationTranscript.Create(
            sessionName.Value,
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        transcript.AddUserMessage("first", DateTimeOffset.Parse("2024-01-01T00:00:01Z"));
        transcript.AddAssistantMessage(
            new ChatResponse("first reply", "openai", "gpt-test", "response-1"),
            DateTimeOffset.Parse("2024-01-01T00:00:02Z"));
        transcript.AddUserMessage("second", DateTimeOffset.Parse("2024-01-01T00:00:03Z"));
        transcript.AddAssistantMessage(
            new ChatResponse("second reply", "openai", "gpt-test", "response-2"),
            DateTimeOffset.Parse("2024-01-01T00:00:04Z"));
        transcript.AddToolCall(new ConversationToolCall(
            CreatedAtUtc: DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
            CallId: "call-1",
            ToolName: "shell",
            ArgumentsJson: "{}",
            ApprovalStatus: "approved",
            CompletedAtUtc: DateTimeOffset.Parse("2024-01-01T00:00:06Z"),
            Succeeded: true,
            OutputSummary: "ok",
            FailureReason: null,
            ErrorCode: null,
            Retryable: false));
        store.Save(sessionName, transcript);

        bool found = store.TryGetSummary(sessionName, out ConversationTranscriptSummary? summary);

        Assert.True(found);
        Assert.NotNull(summary);
        Assert.Equal("smoke", summary.Name);
        Assert.Equal(DateTimeOffset.Parse("2024-01-01T00:00:00Z"), summary.CreatedAtUtc);
        Assert.Equal(DateTimeOffset.Parse("2024-01-01T00:00:06Z"), summary.UpdatedAtUtc);
        Assert.Equal(2, summary.TurnCount);
        Assert.Equal(1, summary.ToolCallCount);
    }

    [Fact]
    public void List_summaries_returns_logical_names_in_name_order()
    {
        using TempDirectory temp = TempDirectory.Create();
        FileConversationStore store = new(Path.Combine(temp.Path, ".caicli", "sessions"));
        ConversationSessionName zulu = ConversationSessionName.Parse("zulu");
        ConversationSessionName releaseNotes = ConversationSessionName.Parse("release notes");
        ConversationSessionName alpha = ConversationSessionName.Parse("alpha");
        DateTimeOffset now = DateTimeOffset.Parse("2024-01-01T00:00:00Z");
        store.Save(zulu, ConversationTranscript.Create(zulu.Value, now));
        store.Save(releaseNotes, ConversationTranscript.Create(releaseNotes.Value, now));
        store.Save(alpha, ConversationTranscript.Create(alpha.Value, now));

        IReadOnlyList<ConversationTranscriptSummary> summaries = store.ListSummaries();

        Assert.Equal(["alpha", "release notes", "zulu"], summaries.Select(summary => summary.Name).ToArray());
    }

    [Fact]
    public void Try_get_summary_reports_false_when_session_is_missing()
    {
        using TempDirectory temp = TempDirectory.Create();
        FileConversationStore store = new(Path.Combine(temp.Path, ".caicli", "sessions"));

        bool found = store.TryGetSummary(ConversationSessionName.Parse("missing"), out ConversationTranscriptSummary? summary);

        Assert.False(found);
        Assert.Null(summary);
    }

    [Fact]
    public void List_summaries_rejects_transcript_with_unsupported_schema_version()
    {
        using TempDirectory temp = TempDirectory.Create();
        string sessionDirectory = Path.Combine(temp.Path, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        File.WriteAllText(
            Path.Combine(sessionDirectory, "smoke.transcript.json"),
            """
            {
              "schemaVersion": 2,
              "sessionName": "smoke",
              "createdAtUtc": "2024-01-01T00:00:00+00:00",
              "updatedAtUtc": "2024-01-01T00:00:00+00:00",
              "messages": [],
              "toolCalls": [],
              "errors": []
            }
            """);
        FileConversationStore store = new(sessionDirectory);

        Assert.Throws<InvalidOperationException>(() => store.ListSummaries());
    }

    [Fact]
    public void List_summaries_rejects_transcript_with_malformed_json()
    {
        using TempDirectory temp = TempDirectory.Create();
        string sessionDirectory = Path.Combine(temp.Path, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        File.WriteAllText(
            Path.Combine(sessionDirectory, "smoke.transcript.json"),
            """
            {
              "schemaVersion": 1,
            """);
        FileConversationStore store = new(sessionDirectory);

        Assert.Throws<InvalidOperationException>(() => store.ListSummaries());
    }

    [Fact]
    public void Try_get_summary_throws_for_existing_transcript_with_unsupported_schema_version()
    {
        using TempDirectory temp = TempDirectory.Create();
        string sessionDirectory = Path.Combine(temp.Path, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        File.WriteAllText(
            Path.Combine(sessionDirectory, "smoke.transcript.json"),
            """
            {
              "schemaVersion": 2,
              "sessionName": "smoke",
              "createdAtUtc": "2024-01-01T00:00:00+00:00",
              "updatedAtUtc": "2024-01-01T00:00:00+00:00",
              "messages": [],
              "toolCalls": [],
              "errors": []
            }
            """);
        FileConversationStore store = new(sessionDirectory);

        Assert.Throws<InvalidOperationException>(() => store.TryGetSummary(
            ConversationSessionName.Parse("smoke"),
            out _));
    }

    [Fact]
    public void Try_get_summary_throws_for_existing_transcript_with_malformed_json()
    {
        using TempDirectory temp = TempDirectory.Create();
        string sessionDirectory = Path.Combine(temp.Path, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        File.WriteAllText(
            Path.Combine(sessionDirectory, "smoke.transcript.json"),
            """
            {
              "schemaVersion": 1,
            """);
        FileConversationStore store = new(sessionDirectory);

        Assert.Throws<InvalidOperationException>(() => store.TryGetSummary(
            ConversationSessionName.Parse("smoke"),
            out _));
    }

    [Fact]
    public void List_summaries_rejects_transcript_missing_required_fields()
    {
        using TempDirectory temp = TempDirectory.Create();
        string sessionDirectory = Path.Combine(temp.Path, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        File.WriteAllText(
            Path.Combine(sessionDirectory, "smoke.transcript.json"),
            """
            {
              "schemaVersion": 1
            }
            """);
        FileConversationStore store = new(sessionDirectory);

        Assert.Throws<InvalidOperationException>(() => store.ListSummaries());
    }

    [Theory]
    [InlineData("smoke\nstatus: succeeded")]
    [InlineData("smoke\tstatus")]
    public void Load_or_create_rejects_transcript_with_unsupported_session_name_whitespace(string malformedSessionName)
    {
        using TempDirectory temp = TempDirectory.Create();
        string sessionDirectory = Path.Combine(temp.Path, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        File.WriteAllText(
            Path.Combine(sessionDirectory, "smoke.transcript.json"),
            $$"""
            {
              "schemaVersion": 1,
              "sessionName": {{JsonSerializer.Serialize(malformedSessionName)}},
              "createdAtUtc": "2024-01-01T00:00:00+00:00",
              "updatedAtUtc": "2024-01-01T00:00:00+00:00",
              "messages": [],
              "toolCalls": [],
              "errors": []
            }
            """);
        FileConversationStore store = new(sessionDirectory);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => store.LoadOrCreate(
            ConversationSessionName.Parse("smoke"),
            DateTimeOffset.Parse("2024-01-01T00:01:00Z")));
        Assert.Equal("Conversation transcript is missing or uses an unsupported schema version.", exception.Message);
    }

    [Fact]
    public void Load_or_create_rejects_transcript_when_internal_session_name_does_not_match_filename()
    {
        using TempDirectory temp = TempDirectory.Create();
        string sessionDirectory = Path.Combine(temp.Path, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        File.WriteAllText(
            Path.Combine(sessionDirectory, "smoke.transcript.json"),
            """
            {
              "schemaVersion": 1,
              "sessionName": "archive",
              "createdAtUtc": "2024-01-01T00:00:00+00:00",
              "updatedAtUtc": "2024-01-01T00:00:00+00:00",
              "messages": [],
              "toolCalls": [],
              "errors": []
            }
            """);
        FileConversationStore store = new(sessionDirectory);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => store.LoadOrCreate(
            ConversationSessionName.Parse("smoke"),
            DateTimeOffset.Parse("2024-01-01T00:01:00Z")));
        Assert.Equal("Conversation transcript is missing or uses an unsupported schema version.", exception.Message);
    }

    [Theory]
    [InlineData("smoke\nstatus: succeeded")]
    [InlineData("smoke\tstatus")]
    public void List_summaries_rejects_transcript_with_unsupported_session_name_whitespace(string malformedSessionName)
    {
        using TempDirectory temp = TempDirectory.Create();
        string sessionDirectory = Path.Combine(temp.Path, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        File.WriteAllText(
            Path.Combine(sessionDirectory, "smoke.transcript.json"),
            $$"""
            {
              "schemaVersion": 1,
              "sessionName": {{JsonSerializer.Serialize(malformedSessionName)}},
              "createdAtUtc": "2024-01-01T00:00:00+00:00",
              "updatedAtUtc": "2024-01-01T00:00:00+00:00",
              "messages": [],
              "toolCalls": [],
              "errors": []
            }
            """);
        FileConversationStore store = new(sessionDirectory);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => store.ListSummaries());
        Assert.Equal("Conversation transcript is missing or uses an unsupported schema version.", exception.Message);
    }

    [Fact]
    public void List_summaries_rejects_transcript_when_internal_session_name_does_not_match_filename()
    {
        using TempDirectory temp = TempDirectory.Create();
        string sessionDirectory = Path.Combine(temp.Path, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        File.WriteAllText(
            Path.Combine(sessionDirectory, "smoke.transcript.json"),
            """
            {
              "schemaVersion": 1,
              "sessionName": "archive",
              "createdAtUtc": "2024-01-01T00:00:00+00:00",
              "updatedAtUtc": "2024-01-01T00:00:00+00:00",
              "messages": [],
              "toolCalls": [],
              "errors": []
            }
            """);
        FileConversationStore store = new(sessionDirectory);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => store.ListSummaries());
        Assert.Equal("Conversation transcript is missing or uses an unsupported schema version.", exception.Message);
    }

    [Fact]
    public void Save_rejects_transcript_when_session_name_argument_does_not_match_transcript_session_name()
    {
        using TempDirectory temp = TempDirectory.Create();
        FileConversationStore store = new(Path.Combine(temp.Path, ".caicli", "sessions"));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => store.Save(
            ConversationSessionName.Parse("smoke"),
            ConversationTranscript.Create("archive", DateTimeOffset.Parse("2024-01-01T00:00:00Z"))));
        Assert.Equal("Conversation transcript is missing or uses an unsupported schema version.", exception.Message);
    }

    [Theory]
    [InlineData("messages")]
    [InlineData("toolCalls")]
    [InlineData("errors")]
    public void Try_get_summary_throws_for_existing_transcript_with_null_collection(string nullCollectionName)
    {
        using TempDirectory temp = TempDirectory.Create();
        string sessionDirectory = Path.Combine(temp.Path, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        string messagesJson = nullCollectionName == "messages" ? "null" : "[]";
        string toolCallsJson = nullCollectionName == "toolCalls" ? "null" : "[]";
        string errorsJson = nullCollectionName == "errors" ? "null" : "[]";
        File.WriteAllText(
            Path.Combine(sessionDirectory, "smoke.transcript.json"),
            $$"""
            {
              "schemaVersion": 1,
              "sessionName": "smoke",
              "createdAtUtc": "2024-01-01T00:00:00+00:00",
              "updatedAtUtc": "2024-01-01T00:00:00+00:00",
              "messages": {{messagesJson}},
              "toolCalls": {{toolCallsJson}},
              "errors": {{errorsJson}}
            }
            """);
        FileConversationStore store = new(sessionDirectory);

        Assert.Throws<InvalidOperationException>(() => store.TryGetSummary(
            ConversationSessionName.Parse("smoke"),
            out _));
    }

    [Fact]
    public void Try_get_summary_throws_for_existing_transcript_with_null_message()
    {
        using TempDirectory temp = TempDirectory.Create();
        string sessionDirectory = Path.Combine(temp.Path, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        File.WriteAllText(
            Path.Combine(sessionDirectory, "smoke.transcript.json"),
            """
            {
              "schemaVersion": 1,
              "sessionName": "smoke",
              "createdAtUtc": "2024-01-01T00:00:00+00:00",
              "updatedAtUtc": "2024-01-01T00:00:00+00:00",
              "messages": [null],
              "toolCalls": [],
              "errors": []
            }
            """);
        FileConversationStore store = new(sessionDirectory);

        Assert.Throws<InvalidOperationException>(() => store.TryGetSummary(
            ConversationSessionName.Parse("smoke"),
            out _));
    }

    [Theory]
    [InlineData("toolCalls")]
    [InlineData("errors")]
    public void Load_list_and_summary_throw_for_existing_transcript_with_null_tool_call_or_error(
        string nullEntryCollectionName)
    {
        using TempDirectory temp = TempDirectory.Create();
        string sessionDirectory = Path.Combine(temp.Path, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        string toolCallsJson = nullEntryCollectionName == "toolCalls" ? "[null]" : "[]";
        string errorsJson = nullEntryCollectionName == "errors" ? "[null]" : "[]";
        File.WriteAllText(
            Path.Combine(sessionDirectory, "smoke.transcript.json"),
            $$"""
            {
              "schemaVersion": 1,
              "sessionName": "smoke",
              "createdAtUtc": "2024-01-01T00:00:00+00:00",
              "updatedAtUtc": "2024-01-01T00:00:00+00:00",
              "messages": [],
              "toolCalls": {{toolCallsJson}},
              "errors": {{errorsJson}}
            }
            """);
        FileConversationStore store = new(sessionDirectory);
        ConversationSessionName sessionName = ConversationSessionName.Parse("smoke");

        Assert.Throws<InvalidOperationException>(() => store.TryLoad(sessionName, out _));
        Assert.Throws<InvalidOperationException>(() => store.LoadOrCreate(
            sessionName,
            DateTimeOffset.Parse("2024-01-01T00:01:00Z")));
        Assert.Throws<InvalidOperationException>(() => store.ListSessionNames());
        Assert.Throws<InvalidOperationException>(() => store.ListSummaries());
        Assert.Throws<InvalidOperationException>(() => store.TryGetSummary(sessionName, out _));
    }

    [Fact]
    public void Exists_reports_whether_session_file_is_present()
    {
        using TempDirectory temp = TempDirectory.Create();
        FileConversationStore store = new(Path.Combine(temp.Path, ".caicli", "sessions"));
        ConversationSessionName sessionName = ConversationSessionName.Parse("smoke");

        Assert.False(store.Exists(sessionName));

        store.Save(
            sessionName,
            ConversationTranscript.Create(sessionName.Value, DateTimeOffset.Parse("2024-01-01T00:00:00Z")));

        Assert.True(store.Exists(sessionName));
    }

    [Fact]
    public void Delete_removes_existing_session_and_reports_result()
    {
        using TempDirectory temp = TempDirectory.Create();
        FileConversationStore store = new(Path.Combine(temp.Path, ".caicli", "sessions"));
        ConversationSessionName sessionName = ConversationSessionName.Parse("smoke");
        store.Save(
            sessionName,
            ConversationTranscript.Create(sessionName.Value, DateTimeOffset.Parse("2024-01-01T00:00:00Z")));

        Assert.True(store.Delete(sessionName));
        Assert.False(store.Exists(sessionName));
        Assert.False(store.Delete(sessionName));
    }

    [Fact]
    public void Rename_moves_existing_session_when_destination_is_available()
    {
        using TempDirectory temp = TempDirectory.Create();
        FileConversationStore store = new(Path.Combine(temp.Path, ".caicli", "sessions"));
        ConversationSessionName source = ConversationSessionName.Parse("draft");
        ConversationSessionName destination = ConversationSessionName.Parse("final");
        ConversationTranscript transcript = ConversationTranscript.Create(
            source.Value,
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        transcript.AddUserMessage("keep me", DateTimeOffset.Parse("2024-01-01T00:00:01Z"));
        transcript.AddToolCall(new ConversationToolCall(
            CreatedAtUtc: DateTimeOffset.Parse("2024-01-01T00:00:02Z"),
            CallId: "call-1",
            ToolName: "shell",
            ArgumentsJson: "{}",
            ApprovalStatus: "approved",
            CompletedAtUtc: DateTimeOffset.Parse("2024-01-01T00:00:03Z"),
            Succeeded: true,
            OutputSummary: "ok",
            FailureReason: null,
            ErrorCode: null,
            Retryable: false));
        transcript.AddError(
            new ModelError(
                Provider: "openai",
                Operation: "responses",
                StatusCode: 429,
                LocalErrorCode: "rate_limit",
                SafeMessage: "try later",
                Retryable: true),
            DateTimeOffset.Parse("2024-01-01T00:00:04Z"));
        store.Save(source, transcript);

        Assert.True(store.Rename(source, destination));

        Assert.False(store.Exists(source));
        Assert.True(store.Exists(destination));
        ConversationTranscript renamed = store.LoadOrCreate(destination, DateTimeOffset.Parse("2024-01-01T00:01:00Z"));
        Assert.Equal(destination.Value, renamed.SessionName);
        Assert.Equal("keep me", Assert.Single(renamed.Messages).Content);
        ConversationToolCall toolCall = Assert.Single(renamed.ToolCalls);
        Assert.Equal("call-1", toolCall.CallId);
        Assert.Equal("ok", toolCall.OutputSummary);
        ConversationError error = Assert.Single(renamed.Errors);
        Assert.Equal("rate_limit", error.LocalErrorCode);
        Assert.Equal("try later", error.SafeMessage);
    }

    [Fact]
    public async Task Rename_writes_destination_transcript_through_temporary_file()
    {
        using TempDirectory temp = TempDirectory.Create();
        string sessionDirectory = Path.Combine(temp.Path, ".caicli", "sessions");
        FileConversationStore store = new(sessionDirectory);
        ConversationSessionName source = ConversationSessionName.Parse("draft");
        ConversationSessionName destination = ConversationSessionName.Parse("final");
        string largeContent = new('r', LargeTranscriptContentLength);
        ConversationTranscript transcript = CreateTranscriptWithUserMessage(source.Value, largeContent);
        store.Save(source, transcript);

        TemporaryFileObservation<bool> observation = await ObserveTemporaryFileDuringOperationAsync(
            sessionDirectory,
            () => store.Rename(source, destination));

        AssertTemporaryFileWasObserved(observation.TemporaryFilePath);
        Assert.True(observation.Result);
        Assert.False(store.Exists(source));
        Assert.True(store.Exists(destination));
        ConversationTranscript renamed = store.LoadOrCreate(destination, DateTimeOffset.Parse("2024-01-01T00:01:00Z"));
        Assert.Equal(destination.Value, renamed.SessionName);
        Assert.Equal(largeContent, Assert.Single(renamed.Messages).Content);
        AssertNoTemporaryFiles(sessionDirectory);
    }

    [Fact]
    public void Write_json_atomically_returns_false_without_overwriting_existing_destination()
    {
        using TempDirectory temp = TempDirectory.Create();
        string sessionDirectory = Path.Combine(temp.Path, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        string destinationPath = Path.Combine(sessionDirectory, "final.transcript.json");
        string originalContent = "existing transcript";
        string replacementContent = "replacement transcript";
        string originalJson = JsonSerializer.Serialize(
            CreateTranscriptWithUserMessage("final", originalContent),
            TestJsonOptions);
        string replacementJson = JsonSerializer.Serialize(
            CreateTranscriptWithUserMessage("final", replacementContent),
            TestJsonOptions);
        File.WriteAllText(destinationPath, originalJson);

        bool written = InvokeWriteJsonAtomically(destinationPath, replacementJson, overwrite: false);

        Assert.False(written);
        string destinationJson = File.ReadAllText(destinationPath);
        using JsonDocument document = JsonDocument.Parse(destinationJson);
        Assert.Equal(originalContent, document.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
        AssertNoTemporaryFiles(sessionDirectory);
    }

    [Fact]
    public void Rename_reports_false_when_source_is_missing_or_destination_exists()
    {
        using TempDirectory temp = TempDirectory.Create();
        FileConversationStore store = new(Path.Combine(temp.Path, ".caicli", "sessions"));
        ConversationSessionName source = ConversationSessionName.Parse("draft");
        ConversationSessionName destination = ConversationSessionName.Parse("final");
        store.Save(
            destination,
            ConversationTranscript.Create(destination.Value, DateTimeOffset.Parse("2024-01-01T00:00:00Z")));

        Assert.False(store.Rename(source, destination));

        store.Save(
            source,
            ConversationTranscript.Create(source.Value, DateTimeOffset.Parse("2024-01-01T00:00:00Z")));

        Assert.False(store.Rename(source, destination));
        Assert.True(store.Exists(source));
        Assert.True(store.Exists(destination));
    }

    [Fact]
    public void Load_or_create_rejects_transcript_missing_schema_version()
    {
        using TempDirectory temp = TempDirectory.Create();
        string sessionDirectory = Path.Combine(temp.Path, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        File.WriteAllText(
            Path.Combine(sessionDirectory, "smoke.transcript.json"),
            """
            {
              "sessionName": "smoke",
              "createdAtUtc": "2024-01-01T00:00:00+00:00",
              "updatedAtUtc": "2024-01-01T00:00:00+00:00",
              "messages": [],
              "toolCalls": [],
              "errors": []
            }
            """);
        FileConversationStore store = new(sessionDirectory);
        ConversationSessionName sessionName = ConversationSessionName.Parse("smoke");

        Assert.Throws<InvalidOperationException>(() => store.LoadOrCreate(
            sessionName,
            DateTimeOffset.Parse("2024-01-01T00:01:00Z")));
    }

    [Fact]
    public void Load_or_create_rejects_transcript_with_unsupported_schema_version()
    {
        using TempDirectory temp = TempDirectory.Create();
        string sessionDirectory = Path.Combine(temp.Path, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        File.WriteAllText(
            Path.Combine(sessionDirectory, "smoke.transcript.json"),
            """
            {
              "schemaVersion": 2,
              "sessionName": "smoke",
              "createdAtUtc": "2024-01-01T00:00:00+00:00",
              "updatedAtUtc": "2024-01-01T00:00:00+00:00",
              "messages": [],
              "toolCalls": [],
              "errors": []
            }
            """);
        FileConversationStore store = new(sessionDirectory);
        ConversationSessionName sessionName = ConversationSessionName.Parse("smoke");

        Assert.Throws<InvalidOperationException>(() => store.LoadOrCreate(
            sessionName,
            DateTimeOffset.Parse("2024-01-01T00:01:00Z")));
    }

    [Fact]
    public void Save_and_load_reject_session_paths_outside_session_directory()
    {
        using TempDirectory temp = TempDirectory.Create();
        string sessionDirectory = Path.Combine(temp.Path, ".caicli", "sessions");
        FileConversationStore store = new(sessionDirectory);
        ConversationSessionName sessionName = CreateSessionNameBypassingParse("smoke", Path.Combine("..", "outside"));
        ConversationTranscript transcript = ConversationTranscript.Create(
            sessionName.Value,
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));

        Assert.Throws<InvalidOperationException>(() => store.Save(sessionName, transcript));
        Assert.Throws<InvalidOperationException>(() => store.LoadOrCreate(
            sessionName,
            DateTimeOffset.Parse("2024-01-01T00:01:00Z")));
        Assert.Throws<InvalidOperationException>(() => store.Exists(sessionName));
        Assert.Throws<InvalidOperationException>(() => store.Delete(sessionName));
        Assert.Throws<InvalidOperationException>(() => store.Rename(sessionName, ConversationSessionName.Parse("inside")));
        Assert.Throws<InvalidOperationException>(() => store.Rename(ConversationSessionName.Parse("inside"), sessionName));
        Assert.False(File.Exists(Path.Combine(temp.Path, ".caicli", "outside.transcript.json")));
    }

    private static ConversationSessionName CreateSessionNameBypassingParse(string value, string fileSafeName)
    {
        var constructor = typeof(ConversationSessionName).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(string), typeof(string)],
            modifiers: null);

        Assert.NotNull(constructor);
        return (ConversationSessionName)constructor.Invoke([value, fileSafeName]);
    }

    private static ConversationTranscript CreateTranscriptWithUserMessage(string sessionName, string content)
    {
        ConversationTranscript transcript = ConversationTranscript.Create(
            sessionName,
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        transcript.AddUserMessage(content, DateTimeOffset.Parse("2024-01-01T00:00:01Z"));
        return transcript;
    }

    private static bool InvokeWriteJsonAtomically(string path, string json, bool overwrite)
    {
        var method = typeof(FileConversationStore).GetMethod(
            "WriteJsonAtomically",
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(string), typeof(string), typeof(bool)],
            modifiers: null);

        Assert.NotNull(method);
        return (bool)method.Invoke(null, [path, json, overwrite])!;
    }

    private static async Task<TemporaryFileObservation<TResult>> ObserveTemporaryFileDuringOperationAsync<TResult>(
        string sessionDirectory,
        Func<TResult> operation)
    {
        TaskCompletionSource<string?> temporaryFileObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using CancellationTokenRegistration timeoutRegistration = timeout.Token.Register(
            static state => ((TaskCompletionSource<string?>)state!).TrySetResult(null),
            temporaryFileObserved);
        using FileSystemWatcher watcher = new(sessionDirectory, "*.tmp")
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime
        };
        watcher.Created += (_, args) => temporaryFileObserved.TrySetResult(args.FullPath);
        watcher.Renamed += (_, args) =>
        {
            string observedPath = args.OldFullPath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
                ? args.OldFullPath
                : args.FullPath;
            temporaryFileObserved.TrySetResult(observedPath);
        };
        watcher.EnableRaisingEvents = true;

        Task<TResult> operationTask = Task.Run(operation);
        TResult result = await operationTask;
        string? temporaryFilePath = await temporaryFileObserved.Task;
        return new TemporaryFileObservation<TResult>(result, temporaryFilePath);
    }

    private static void AssertTemporaryFileWasObserved(string? temporaryFilePath)
    {
        Assert.True(
            temporaryFilePath is not null,
            "Expected FileConversationStore to write through a temporary .tmp file.");
    }

    private static void AssertNoTemporaryFiles(string sessionDirectory)
    {
        Assert.Empty(Directory.EnumerateFiles(sessionDirectory, "*.tmp", SearchOption.TopDirectoryOnly));
    }

    private sealed record TemporaryFileObservation<TResult>(TResult Result, string? TemporaryFilePath);

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempDirectory Create()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "caicli-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
