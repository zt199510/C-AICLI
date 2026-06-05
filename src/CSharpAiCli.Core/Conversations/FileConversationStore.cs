using System.Text.Json;

namespace CSharpAiCli.Core;

public sealed class FileConversationStore : IConversationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string sessionDirectory;

    public FileConversationStore(string sessionDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionDirectory);
        this.sessionDirectory = sessionDirectory;
    }

    public static FileConversationStore Create(CliEnvironmentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        string? userConfigDirectory = Path.GetDirectoryName(snapshot.UserConfigPath);
        string root = string.IsNullOrWhiteSpace(userConfigDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".caicli")
            : userConfigDirectory;

        return new FileConversationStore(Path.Combine(root, "sessions"));
    }

    public ConversationTranscript LoadOrCreate(ConversationSessionName sessionName, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(sessionName);

        string path = GetPath(sessionName);
        if (!File.Exists(path))
        {
            return ConversationTranscript.Create(sessionName.Value, nowUtc);
        }

        string json = File.ReadAllText(path);
        ConversationTranscript? transcript = JsonSerializer.Deserialize<ConversationTranscript>(json, JsonOptions);
        if (transcript is null || transcript.SchemaVersion != ConversationTranscript.CurrentSchemaVersion)
        {
            throw new InvalidOperationException("Conversation transcript is missing or uses an unsupported schema version.");
        }

        return transcript;
    }

    public string Save(ConversationSessionName sessionName, ConversationTranscript transcript)
    {
        ArgumentNullException.ThrowIfNull(sessionName);
        ArgumentNullException.ThrowIfNull(transcript);

        Directory.CreateDirectory(sessionDirectory);
        string path = GetPath(sessionName);
        string json = JsonSerializer.Serialize(transcript, JsonOptions);
        File.WriteAllText(path, json);
        return path;
    }

    private string GetPath(ConversationSessionName sessionName)
    {
        return Path.Combine(sessionDirectory, $"{sessionName.FileSafeName}.transcript.json");
    }
}
