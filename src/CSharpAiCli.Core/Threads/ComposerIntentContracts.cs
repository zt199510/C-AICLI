using System.Text;

namespace CSharpAiCli.Core;

public static class ComposerIntentLimits
{
    public const int MaxPromptBytes = 64 * 1024;
    public const int MaxContextSelections = 32;
    public const int MaxCatalogSelections = 16;
    public const long MaxSingleFileBytes = 10L * 1024 * 1024;
    public const long MaxTotalFileBytes = 32L * 1024 * 1024;
    public const int MaxFolderFiles = 500;
    public const long MaxFolderBytes = 64L * 1024 * 1024;
    public const int MaxSearchResults = 100;
    public const int MaxScannedEntries = 5_000;
    public const int MaxSearchQueryBytes = 256;
    public const int MaxRelativePathBytes = 4_096;
    public const int MaxMutationIdBytes = 128;
    public const int MaxMutationReceipts = 64;
    public const int MaxExecutionInputBytes = 256 * 1024;
}

public static class ComposerQueueLifecycle
{
    public const string Empty = "empty";
    public const string Pending = "pending";
    public const string Claimed = "claimed";

    public static bool IsKnown(string? value) => value is Empty or Pending or Claimed;
}

public static class ComposerDelivery
{
    public const string Ready = "ready";
    public const string NextTurn = "next-turn";

    public static bool IsKnown(string? value) => value is Ready or NextTurn;
}

public static class ComposerContextKind
{
    public const string File = "file";
    public const string Folder = "folder";

    public static bool IsKnown(string? value) => value is File or Folder;
}

public static class ComposerCatalogKind
{
    public const string Skill = "skill";
    public const string Expert = "expert";
    public const string Automation = "automation";

    public static bool IsKnown(string? value) => value is Skill or Expert or Automation;
}

public static class ComposerIntentErrorCode
{
    public const string Invalid = "composer-intent-invalid";
    public const string NotFound = "composer-intent-not-found";
    public const string AlreadyPending = "composer-intent-already-pending";
    public const string RevisionConflict = "composer-queue-revision-conflict";
    public const string MutationConflict = "composer-mutation-conflict";
    public const string Corrupt = "composer-queue-corrupt";
    public const string Unavailable = "composer-queue-unavailable";
    public const string WriteFailed = "composer-queue-write-failed";
}

public sealed record ComposerContextReferenceRecord
{
    public string SelectionId { get; init; } = string.Empty;
    public string RelativePath { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public long ByteCount { get; init; }
    public int FileCount { get; init; }
    public string ObservedIdentity { get; init; } = string.Empty;
}

public sealed record ComposerCatalogReferenceRecord
{
    public string Kind { get; init; } = string.Empty;
    public string Id { get; init; } = string.Empty;
    public string CatalogRevision { get; init; } = string.Empty;
}

public sealed record PendingComposerIntentRecord
{
    public string IntentId { get; init; } = string.Empty;
    public string WorkspaceId { get; init; } = string.Empty;
    public string WorkspaceRootIdentity { get; init; } = string.Empty;
    public string ThreadId { get; init; } = string.Empty;
    public long ThreadRevision { get; init; }
    public string Delivery { get; init; } = ComposerDelivery.Ready;
    public string Prompt { get; init; } = string.Empty;
    public IReadOnlyList<ComposerContextReferenceRecord> Context { get; init; } = [];
    public IReadOnlyList<ComposerCatalogReferenceRecord> Catalog { get; init; } = [];
    public ThreadSourcePointerRecord? SourcePointer { get; init; }
    public string EffectiveModel { get; init; } = string.Empty;
    public string ModelSource { get; init; } = string.Empty;
    public string ApprovalMode { get; init; } = string.Empty;
    public string ApprovalModeSource { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed record ComposerIntentClaimRecord
{
    public string ClaimId { get; init; } = string.Empty;
    public string IntentId { get; init; } = string.Empty;
    public string TurnId { get; init; } = string.Empty;
    public string CanonicalInputSha256 { get; init; } = string.Empty;
    public string StartMutationId { get; init; } = string.Empty;
    public long SourceQueueRevision { get; init; }
    public DateTimeOffset ClaimedAtUtc { get; init; }
}

public sealed record ComposerMutationReceiptRecord
{
    public string MutationId { get; init; } = string.Empty;
    public string PayloadSha256 { get; init; } = string.Empty;
    public string Operation { get; init; } = string.Empty;
    public long ResultRevision { get; init; }
    public string? TurnId { get; init; }
    public string? CanonicalInputSha256 { get; init; }
}

public sealed record ComposerQueueRecord
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string WorkspaceId { get; init; } = string.Empty;
    public string WorkspaceRootIdentity { get; init; } = string.Empty;
    public string ThreadId { get; init; } = string.Empty;
    public long Revision { get; init; }
    public string Lifecycle { get; init; } = ComposerQueueLifecycle.Empty;
    public PendingComposerIntentRecord? PendingIntent { get; init; }
    public ComposerIntentClaimRecord? Claim { get; init; }
    public IReadOnlyList<ComposerMutationReceiptRecord> MutationReceipts { get; init; } = [];
}

public sealed class ComposerIntentContractException : Exception
{
    public ComposerIntentContractException(string errorCode, string message) : base(message) => ErrorCode = errorCode;
    public string ErrorCode { get; }
}

public static class ComposerIntentContractValidator
{
    public static void ValidateQueue(ComposerQueueRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.SchemaVersion != ComposerQueueRecord.CurrentSchemaVersion ||
            string.IsNullOrWhiteSpace(record.WorkspaceId) || string.IsNullOrWhiteSpace(record.WorkspaceRootIdentity) ||
            !ThreadIdentity.IsThreadId(record.ThreadId) || record.Revision < 0 || !ComposerQueueLifecycle.IsKnown(record.Lifecycle) ||
            record.MutationReceipts.Count > ComposerIntentLimits.MaxMutationReceipts)
        {
            throw Corrupt("Composer queue envelope is invalid.");
        }

        if (record.PendingIntent is not null)
        {
            ValidateIntent(record.PendingIntent);
            if (record.PendingIntent.WorkspaceId != record.WorkspaceId ||
                record.PendingIntent.WorkspaceRootIdentity != record.WorkspaceRootIdentity ||
                record.PendingIntent.ThreadId != record.ThreadId)
            {
                throw Corrupt("Composer queue binding is invalid.");
            }
        }


        if ((record.Lifecycle == ComposerQueueLifecycle.Empty) != (record.PendingIntent is null) ||
            (record.Lifecycle == ComposerQueueLifecycle.Claimed) != (record.Claim is not null) ||
            (record.Lifecycle == ComposerQueueLifecycle.Pending && record.Claim is not null))
        {
            throw Corrupt("Composer queue lifecycle is inconsistent.");
        }

        if (record.Claim is not null)
        {
            ValidateClaim(record.Claim);
            if (record.PendingIntent is null || record.Claim.IntentId != record.PendingIntent.IntentId ||
                record.Claim.SourceQueueRevision < 0 || record.Claim.SourceQueueRevision >= record.Revision)
            {
                throw Corrupt("Composer claim binding is invalid.");
            }
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (ComposerMutationReceiptRecord receipt in record.MutationReceipts)
        {
            if (!IsSafeMutationId(receipt.MutationId) || !ids.Add(receipt.MutationId) ||
                !IsSha256(receipt.PayloadSha256) || receipt.Operation is not ("enqueue" or "clear" or "claim" or "finalize") ||
                receipt.ResultRevision < 0 || receipt.ResultRevision > record.Revision ||
                ((receipt.TurnId is null) != (receipt.CanonicalInputSha256 is null)) ||
                (receipt.TurnId is not null && (!ThreadIdentity.IsTurnId(receipt.TurnId) || !IsSha256(receipt.CanonicalInputSha256))))
            {
                throw Corrupt("Composer mutation receipt is invalid.");
            }
        }
    }


    public static void ValidateClaim(ComposerIntentClaimRecord claim)
    {
        ArgumentNullException.ThrowIfNull(claim);
        if (!IsOpaqueId(claim.ClaimId, "claim_") || !IsOpaqueId(claim.IntentId, "intent_") ||
            !ThreadIdentity.IsTurnId(claim.TurnId) || !IsSha256(claim.CanonicalInputSha256) ||
            !IsSafeMutationId(claim.StartMutationId) || claim.SourceQueueRevision < 0 ||
            claim.ClaimedAtUtc == default || claim.ClaimedAtUtc.Offset != TimeSpan.Zero)
        {
            throw Invalid("Composer intent claim is invalid.");
        }
    }

    public static void ValidateIntent(PendingComposerIntentRecord intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (!IsOpaqueId(intent.IntentId, "intent_") || string.IsNullOrWhiteSpace(intent.WorkspaceId) ||
            string.IsNullOrWhiteSpace(intent.WorkspaceRootIdentity) || !ThreadIdentity.IsThreadId(intent.ThreadId) ||
            intent.ThreadRevision < 0 || !ComposerDelivery.IsKnown(intent.Delivery) ||
            string.IsNullOrWhiteSpace(intent.Prompt) || !string.Equals(intent.Prompt, intent.Prompt.Trim(), StringComparison.Ordinal) ||
            Utf8(intent.Prompt) > ComposerIntentLimits.MaxPromptBytes ||
            intent.Context.Count > ComposerIntentLimits.MaxContextSelections ||
            intent.Catalog.Count > ComposerIntentLimits.MaxCatalogSelections ||
            string.IsNullOrWhiteSpace(intent.EffectiveModel) || Utf8(intent.EffectiveModel) > 256 ||
            Utf8(intent.ModelSource) > 256 || Utf8(intent.ApprovalMode) > 64 || Utf8(intent.ApprovalModeSource) > 256 ||
            intent.CreatedAtUtc == default || intent.CreatedAtUtc.Offset != TimeSpan.Zero)
        {
            throw Invalid("Pending composer intent is invalid.");
        }

        long totalFiles = 0;
        var selections = new HashSet<string>(StringComparer.Ordinal);
        foreach (ComposerContextReferenceRecord context in intent.Context)
        {
            if (!IsOpaqueId(context.SelectionId, "ctx_") || !selections.Add(context.SelectionId) ||
                !ComposerContextKind.IsKnown(context.Kind) || !IsSafeRelativePath(context.RelativePath) ||
                !IsSha256(context.ObservedIdentity) || context.ByteCount < 0 || context.FileCount < 1 ||
                (context.Kind == ComposerContextKind.File && (context.FileCount != 1 || context.ByteCount > ComposerIntentLimits.MaxSingleFileBytes)) ||
                (context.Kind == ComposerContextKind.Folder && (context.FileCount > ComposerIntentLimits.MaxFolderFiles || context.ByteCount > ComposerIntentLimits.MaxFolderBytes)))
            {
                throw Invalid("Controlled context reference is invalid.");
            }
            if (context.Kind == ComposerContextKind.File) totalFiles += context.ByteCount;
        }
        if (intent.SourcePointer is ThreadSourcePointerRecord pointer &&
            (!ThreadSourceKind.IsKnown(pointer.Kind) || !ThreadSourceAvailability.IsKnown(pointer.Availability) ||
             string.IsNullOrWhiteSpace(pointer.SourceId) || Utf8(pointer.SourceId) > ThreadPersistenceLimits.MaxPointerValueBytes))
            throw Invalid("Composer source pointer is invalid.");
        if (totalFiles > ComposerIntentLimits.MaxTotalFileBytes) throw Invalid("File context byte limit was exceeded.");

        var catalog = new HashSet<string>(StringComparer.Ordinal);
        foreach (ComposerCatalogReferenceRecord item in intent.Catalog)
        {
            if (!ComposerCatalogKind.IsKnown(item.Kind) || string.IsNullOrWhiteSpace(item.Id) || Utf8(item.Id) > 256 ||
                !IsSha256(item.CatalogRevision) || !catalog.Add($"{item.Kind}\0{item.Id}"))
            {
                throw Invalid("Catalog reference is invalid.");
            }
        }
    }

    public static bool IsSafeMutationId(string? value) => !string.IsNullOrWhiteSpace(value) &&
        Utf8(value) <= ComposerIntentLimits.MaxMutationIdBytes &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.');

    public static bool IsSafeRelativePath(string? value) => !string.IsNullOrWhiteSpace(value) &&
        Utf8(value) <= ComposerIntentLimits.MaxRelativePathBytes && !Path.IsPathRooted(value) &&
        value != ".." && !value.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
        !value.StartsWith("../", StringComparison.Ordinal);

    public static string ComputeCanonicalInputSha256(PendingComposerIntentRecord intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ValidateIntent(intent);
        byte[] canonical = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(intent, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(canonical)).ToLowerInvariant();
    }

    public static string CreateDeterministicTurnId(string intentId, string canonicalInputSha256)
    {
        if (!IsOpaqueId(intentId, "intent_") || !IsSha256(canonicalInputSha256))
            throw Invalid("Deterministic turn binding is invalid.");
        byte[] digest = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(intentId + "\0" + canonicalInputSha256));
        return "turn_" + Convert.ToHexString(digest).ToLowerInvariant()[..24];
    }

    private static bool IsOpaqueId(string? value, string prefix) => value is { Length: <= 96 } &&
        value.StartsWith(prefix, StringComparison.Ordinal) && value[prefix.Length..].All(char.IsAsciiLetterOrDigit);
    private static bool IsSha256(string? value) => value is { Length: 64 } && value.AsSpan().IndexOfAnyExcept("0123456789abcdef") < 0;
    private static int Utf8(string? value) => Encoding.UTF8.GetByteCount(value ?? string.Empty);
    private static ComposerIntentContractException Invalid(string message) => new(ComposerIntentErrorCode.Invalid, message);
    private static ComposerIntentContractException Corrupt(string message) => new(ComposerIntentErrorCode.Corrupt, message);
}
