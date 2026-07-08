using System.Text.Json;

namespace CSharpAiCli.Core;

public sealed class FileConversationStore : IConversationStore
{
    private const string InvalidTranscriptMessage = "Conversation transcript is missing or uses an unsupported schema version.";
    private const string TranscriptFileSuffix = ".transcript.json";

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

    public bool TryLoad(ConversationSessionName sessionName, out ConversationTranscript? transcript)
    {
        ArgumentNullException.ThrowIfNull(sessionName);

        string path = GetPath(sessionName);
        if (!File.Exists(path))
        {
            transcript = null;
            return false;
        }

        transcript = LoadTranscript(path);
        return true;
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
        WriteJsonAtomically(destinationPath, json);
        File.Delete(sourcePath);
        return true;
    }

    public string Save(ConversationSessionName sessionName, ConversationTranscript transcript)
    {
        ArgumentNullException.ThrowIfNull(sessionName);
        ArgumentNullException.ThrowIfNull(transcript);

        ValidateTranscriptSessionMatch(sessionName, transcript);
        Directory.CreateDirectory(sessionDirectory);
        string path = GetPath(sessionName);
        string json = JsonSerializer.Serialize(transcript, JsonOptions);
        WriteJsonAtomically(path, json);
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
        try
        {
            string json = File.ReadAllText(path);
            using JsonDocument document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("schemaVersion", out JsonElement schemaVersionElement) ||
                schemaVersionElement.ValueKind != JsonValueKind.Number ||
                !schemaVersionElement.TryGetInt32(out int schemaVersion) ||
                schemaVersion != ConversationTranscript.CurrentSchemaVersion)
            {
                throw new InvalidOperationException(InvalidTranscriptMessage);
            }

            ConversationTranscript? transcript = JsonSerializer.Deserialize<ConversationTranscript>(json, JsonOptions);
            if (transcript is null)
            {
                throw new InvalidOperationException(InvalidTranscriptMessage);
            }

            ConversationSessionName transcriptSessionName = ValidateTranscriptShape(transcript);
            ValidateTranscriptPathBinding(path, transcriptSessionName);

            return transcript;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                InvalidTranscriptMessage,
                exception);
        }
    }

    private static ConversationSessionName ValidateTranscriptShape(ConversationTranscript transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript.SessionName) ||
            transcript.CreatedAtUtc == default ||
            transcript.UpdatedAtUtc == default ||
            transcript.Messages is null ||
            transcript.ToolCalls is null ||
            transcript.Errors is null ||
            transcript.Messages.Any(message => message is null) ||
            transcript.ToolCalls.Any(toolCall => toolCall is null) ||
            transcript.Errors.Any(error => error is null))
        {
            throw new InvalidOperationException(InvalidTranscriptMessage);
        }

        try
        {
            return ConversationSessionName.Parse(transcript.SessionName);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(InvalidTranscriptMessage, exception);
        }
    }

    private static void ValidateTranscriptPathBinding(string path, ConversationSessionName transcriptSessionName)
    {
        string fileName = Path.GetFileName(path);
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!fileName.EndsWith(TranscriptFileSuffix, comparison))
        {
            throw new InvalidOperationException(InvalidTranscriptMessage);
        }

        string fileSafeName = fileName[..^TranscriptFileSuffix.Length];
        if (!string.Equals(fileSafeName, transcriptSessionName.FileSafeName, comparison))
        {
            throw new InvalidOperationException(InvalidTranscriptMessage);
        }
    }

    private static void ValidateTranscriptSessionMatch(
        ConversationSessionName sessionName,
        ConversationTranscript transcript)
    {
        ConversationSessionName transcriptSessionName = ValidateTranscriptShape(transcript);
        if (!string.Equals(sessionName.FileSafeName, transcriptSessionName.FileSafeName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(InvalidTranscriptMessage);
        }
    }

    private static void WriteJsonAtomically(string path, string json)
    {
        string? directory = Path.GetDirectoryName(path);
        string fileName = Path.GetFileName(path);
        string temporaryPath = string.IsNullOrWhiteSpace(directory)
            ? $"{fileName}.{Guid.NewGuid():N}.tmp"
            : Path.Combine(directory, $"{fileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (IsConversationStoreIoException(exception))
        {
        }
    }

    private static bool IsConversationStoreIoException(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException;
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
