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

    public IReadOnlyList<string> ListSessionNames()
    {
        if (!Directory.Exists(sessionDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(sessionDirectory, "*.transcript.json", SearchOption.TopDirectoryOnly)
            .Select(path => LoadTranscript(path).SessionName)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<ConversationTranscriptSummary> ListSummaries()
    {
        if (!Directory.Exists(sessionDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(sessionDirectory, "*.transcript.json", SearchOption.TopDirectoryOnly)
            .Select(path => ConversationTranscriptSummary.FromTranscript(LoadTranscript(path)))
            .OrderBy(summary => summary.Name, StringComparer.Ordinal)
            .ToArray();
    }

    public bool Exists(ConversationSessionName sessionName)
    {
        ArgumentNullException.ThrowIfNull(sessionName);

        return File.Exists(GetPath(sessionName));
    }

    public bool TryGetSummary(ConversationSessionName sessionName, out ConversationTranscriptSummary? summary)
    {
        ArgumentNullException.ThrowIfNull(sessionName);

        string path = GetPath(sessionName);
        if (!File.Exists(path))
        {
            summary = null;
            return false;
        }

        summary = ConversationTranscriptSummary.FromTranscript(LoadTranscript(path));
        return true;
    }

    public ConversationTranscript LoadOrCreate(ConversationSessionName sessionName, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(sessionName);

        string path = GetPath(sessionName);
        if (!File.Exists(path))
        {
            return ConversationTranscript.Create(sessionName.Value, nowUtc);
        }

        return LoadTranscript(path);
    }

    public bool Rename(ConversationSessionName sourceSessionName, ConversationSessionName destinationSessionName)
    {
        ArgumentNullException.ThrowIfNull(sourceSessionName);
        ArgumentNullException.ThrowIfNull(destinationSessionName);

        string sourcePath = GetPath(sourceSessionName);
        string destinationPath = GetPath(destinationSessionName);
        if (!File.Exists(sourcePath) || File.Exists(destinationPath))
        {
            return false;
        }

        ConversationTranscript sourceTranscript = LoadTranscript(sourcePath);
        ConversationTranscript destinationTranscript = new()
        {
            SchemaVersion = sourceTranscript.SchemaVersion,
            SessionName = destinationSessionName.Value,
            CreatedAtUtc = sourceTranscript.CreatedAtUtc,
            UpdatedAtUtc = sourceTranscript.UpdatedAtUtc,
            Messages = sourceTranscript.Messages,
            ToolCalls = sourceTranscript.ToolCalls,
            Errors = sourceTranscript.Errors,
        };

        string json = JsonSerializer.Serialize(destinationTranscript, JsonOptions);
        File.WriteAllText(destinationPath, json);
        File.Delete(sourcePath);
        return true;
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

    public bool Delete(ConversationSessionName sessionName)
    {
        ArgumentNullException.ThrowIfNull(sessionName);

        string path = GetPath(sessionName);
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    private static ConversationTranscript LoadTranscript(string path)
    {
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
