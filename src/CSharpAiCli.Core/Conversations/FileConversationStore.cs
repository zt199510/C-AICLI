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
        using JsonDocument document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("schemaVersion", out JsonElement schemaVersionElement) ||
            schemaVersionElement.ValueKind != JsonValueKind.Number ||
            !schemaVersionElement.TryGetInt32(out int schemaVersion) ||
            schemaVersion != ConversationTranscript.CurrentSchemaVersion)
        {
            throw new InvalidOperationException("Conversation transcript is missing or uses an unsupported schema version.");
        }

        ConversationTranscript? transcript = JsonSerializer.Deserialize<ConversationTranscript>(json, JsonOptions);
        if (transcript is null)
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
        string fullSessionDirectory = Path.GetFullPath(sessionDirectory);
        string path = Path.GetFullPath(Path.Combine(fullSessionDirectory, $"{sessionName.FileSafeName}.transcript.json"));
        string rootedSessionDirectory = fullSessionDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? fullSessionDirectory
            : fullSessionDirectory + Path.DirectorySeparatorChar;

        StringComparison pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!path.StartsWith(rootedSessionDirectory, pathComparison))
        {
            throw new InvalidOperationException("Conversation transcript path must remain inside the session directory.");
        }

        return path;
    }
}
