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
