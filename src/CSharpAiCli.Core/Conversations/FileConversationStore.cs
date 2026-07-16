using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CSharpAiCli.Core;

public sealed class FileConversationStore : IConversationStore
{
    private const string InvalidTranscriptMessage = "Conversation transcript is missing or uses an unsupported schema version.";
    private const string TranscriptFileSuffix = ".transcript.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private static readonly JsonSerializerOptions StrictJsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 64
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

    public IReadOnlyList<ConversationTranscriptSummary> ListSummaries() => ListSummaries(null);

    public IReadOnlyList<ConversationTranscriptSummary> ListSummaries(int? limit)
    {
        if (limit is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        if (!Directory.Exists(sessionDirectory))
        {
            return [];
        }

        IEnumerable<string> paths = Directory
            .EnumerateFiles(sessionDirectory, "*.transcript.json", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal);
        if (limit is > 0)
        {
            paths = paths.Take(limit.Value);
        }

        return paths
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

    public ConversationTranscriptSnapshot ReadBounded(
        ConversationSessionName sessionName,
        int maxBytes = ThreadPersistenceLimits.MaxSessionImportBytes,
        int maxRecords = ThreadPersistenceLimits.MaxSessionImportRecords)
    {
        ArgumentNullException.ThrowIfNull(sessionName);
        if (maxBytes <= 0 || maxRecords <= 0)
        {
            throw new ArgumentOutOfRangeException(maxBytes <= 0 ? nameof(maxBytes) : nameof(maxRecords));
        }

        string path = GetPath(sessionName);
        try
        {
            EnsureNoReparseInExistingChain(path);
            if (!File.Exists(path))
            {
                throw new ConversationTranscriptReadException(
                    ConversationTranscriptReadErrorCode.NotFound,
                    "Conversation transcript was not found.");
            }

            FileInfo before = new(path);
            long length = before.Length;
            DateTimeOffset lastWriteAtUtc = before.LastWriteTimeUtc;
            if (length <= 0 || length > maxBytes || length > int.MaxValue)
            {
                throw new ConversationTranscriptReadException(
                    ConversationTranscriptReadErrorCode.LimitExceeded,
                    "Conversation transcript exceeds its import byte limit.");
            }

            byte[] bytes = new byte[checked((int)length)];
            using (FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan))
            {
                stream.ReadExactly(bytes);
                if (stream.Position != stream.Length || stream.Length != length)
                {
                    throw new ConversationTranscriptReadException(
                        ConversationTranscriptReadErrorCode.SourceChanged,
                        "Conversation transcript changed while it was being read.");
                }
            }

            string json = new UTF8Encoding(false, true).GetString(bytes);
            int schema = ReadSchemaVersionStrict(json);
            if (schema != ConversationTranscript.CurrentSchemaVersion)
            {
                throw new ConversationTranscriptReadException(
                    ConversationTranscriptReadErrorCode.SchemaUnsupported,
                    "Conversation transcript uses an unsupported schema.");
            }

            ConversationTranscript transcript = JsonSerializer.Deserialize<ConversationTranscript>(json, StrictJsonOptions)
                ?? throw new ConversationTranscriptReadException(
                    ConversationTranscriptReadErrorCode.Corrupt,
                    "Conversation transcript is corrupt.");
            ConversationSessionName transcriptName = ValidateTranscriptShape(transcript);
            ValidateTranscriptPathBinding(path, transcriptName);
            int recordCount = checked(transcript.Messages.Count + transcript.ToolCalls.Count +
                transcript.Errors.Count + transcript.AgentRuns.Count);
            if (recordCount > maxRecords)
            {
                throw new ConversationTranscriptReadException(
                    ConversationTranscriptReadErrorCode.LimitExceeded,
                    "Conversation transcript exceeds its import record limit.");
            }

            FileInfo after = new(path);
            if (after.Length != length || after.LastWriteTimeUtc != lastWriteAtUtc)
            {
                throw new ConversationTranscriptReadException(
                    ConversationTranscriptReadErrorCode.SourceChanged,
                    "Conversation transcript changed while it was being read.");
            }

            EnsureNoReparseInExistingChain(path);
            string contentHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            string fingerprintInput = $"session\0{sessionName.Value}\0{contentHash}";
            string fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintInput))).ToLowerInvariant();
            return new ConversationTranscriptSnapshot(
                transcript,
                sessionName.Value,
                fingerprint,
                length,
                recordCount,
                lastWriteAtUtc);
        }
        catch (ConversationTranscriptReadException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new ConversationTranscriptReadException(
                ConversationTranscriptReadErrorCode.Corrupt,
                "Conversation transcript is corrupt.",
                exception);
        }
        catch (DecoderFallbackException exception)
        {
            throw new ConversationTranscriptReadException(
                ConversationTranscriptReadErrorCode.Corrupt,
                "Conversation transcript is not valid UTF-8.",
                exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new ConversationTranscriptReadException(
                ConversationTranscriptReadErrorCode.Corrupt,
                "Conversation transcript is invalid.",
                exception);
        }
        catch (Exception exception) when (IsConversationStoreIoException(exception) || exception is PathTooLongException)
        {
            throw new ConversationTranscriptReadException(
                ConversationTranscriptReadErrorCode.Unavailable,
                "Conversation transcript could not be read.",
                exception);
        }
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
            AgentRuns = sourceTranscript.AgentRuns,
        };

        string json = JsonSerializer.Serialize(destinationTranscript, JsonOptions);
        if (!WriteJsonAtomically(destinationPath, json, overwrite: false))
        {
            return false;
        }

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
        WriteJsonAtomically(path, json, overwrite: true);
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

    private static int ReadSchemaVersionStrict(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
        return document.RootElement.TryGetProperty("schemaVersion", out JsonElement schemaVersionElement) &&
            schemaVersionElement.ValueKind == JsonValueKind.Number &&
            schemaVersionElement.TryGetInt32(out int schemaVersion)
                ? schemaVersion
                : throw new ConversationTranscriptReadException(
                    ConversationTranscriptReadErrorCode.Corrupt,
                    "Conversation transcript schema is missing.");
    }

    private static void EnsureNoReparseInExistingChain(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string? root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ConversationTranscriptReadException(
                ConversationTranscriptReadErrorCode.ReparsePoint,
                "Conversation transcript path is unsafe.");
        }

        string current = root;
        foreach (string segment in Path.GetRelativePath(root, fullPath).Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!Directory.Exists(current) && !File.Exists(current))
            {
                continue;
            }

            if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
            {
                throw new ConversationTranscriptReadException(
                    ConversationTranscriptReadErrorCode.ReparsePoint,
                    "Conversation transcript path contains a reparse point.");
            }
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
            transcript.AgentRuns is null ||
            transcript.Messages.Any(message => message is null) ||
            transcript.ToolCalls.Any(toolCall => toolCall is null) ||
            transcript.Errors.Any(error => error is null) ||
            transcript.AgentRuns.Any(run => run is null))
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

    private static bool WriteJsonAtomically(string path, string json, bool overwrite)
    {
        string? directory = Path.GetDirectoryName(path);
        string fileName = Path.GetFileName(path);
        string temporaryPath = string.IsNullOrWhiteSpace(directory)
            ? $"{fileName}.{Guid.NewGuid():N}.tmp"
            : Path.Combine(directory, $"{fileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(temporaryPath, json);
            try
            {
                File.Move(temporaryPath, path, overwrite);
                return true;
            }
            catch (IOException) when (!overwrite && File.Exists(path))
            {
                return false;
            }
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
