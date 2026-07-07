using System.Reflection;
using System.Text.Json;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class FileConversationStoreTests
{
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
    public void List_session_names_returns_empty_collection_when_session_directory_is_missing()
    {
        using TempDirectory temp = TempDirectory.Create();
        FileConversationStore store = new(Path.Combine(temp.Path, ".caicli", "sessions"));

        IReadOnlyList<string> sessionNames = store.ListSessionNames();

        Assert.Empty(sessionNames);
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
        store.Save(source, transcript);

        Assert.True(store.Rename(source, destination));

        Assert.False(store.Exists(source));
        Assert.True(store.Exists(destination));
        ConversationTranscript renamed = store.LoadOrCreate(destination, DateTimeOffset.Parse("2024-01-01T00:01:00Z"));
        Assert.Equal("keep me", Assert.Single(renamed.Messages).Content);
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
