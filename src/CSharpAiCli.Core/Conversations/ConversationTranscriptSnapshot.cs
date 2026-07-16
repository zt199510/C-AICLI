namespace CSharpAiCli.Core;

public static class ConversationTranscriptReadErrorCode
{
    public const string NotFound = "session-import-not-found";
    public const string Corrupt = "session-import-corrupt";
    public const string SchemaUnsupported = "session-import-schema-unsupported";
    public const string LimitExceeded = ThreadErrorCode.SessionImportLimitExceeded;
    public const string ReparsePoint = "session-import-reparse-point";
    public const string SourceChanged = ThreadErrorCode.SessionImportSourceChanged;
    public const string Unavailable = "session-import-unavailable";
}

public sealed record ConversationTranscriptSnapshot(
    ConversationTranscript Transcript,
    string SourceIdentity,
    string Fingerprint,
    long ByteCount,
    int RecordCount,
    DateTimeOffset LastWriteAtUtc);

public sealed class ConversationTranscriptReadException : Exception
{
    public ConversationTranscriptReadException(string errorCode, string safeMessage, Exception? innerException = null)
        : base(safeMessage, innerException)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}
