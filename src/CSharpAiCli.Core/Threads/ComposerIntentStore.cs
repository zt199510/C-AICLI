using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CSharpAiCli.Core;

public sealed record ComposerIntentStoreDiagnostic(string ErrorCode, string SafeMessage);

public sealed record ComposerIntentStoreResult(
    bool Succeeded,
    ComposerQueueRecord? Queue,
    ComposerIntentStoreDiagnostic? Diagnostic,
    bool Idempotent = false)
{
    public static ComposerIntentStoreResult Success(ComposerQueueRecord queue, bool idempotent = false) =>
        new(true, queue, null, idempotent);
    public static ComposerIntentStoreResult Failure(string code, string message) =>
        new(false, null, new ComposerIntentStoreDiagnostic(code, message));
}

public sealed class ComposerIntentStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 64
    };

    private readonly string root;

    public ComposerIntentStore(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        this.root = Path.GetFullPath(root);
    }

    public static ComposerIntentStore Create(CliEnvironmentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        string stateRoot = Path.GetDirectoryName(snapshot.UserConfigPath) ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".caicli");
        return new ComposerIntentStore(Path.Combine(stateRoot, "composer"));
    }

    public ComposerIntentStoreResult Get(
        string workspaceId,
        string workspaceRootIdentity,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateBinding(workspaceId, workspaceRootIdentity, threadId);
            string path = GetPath(threadId);
            if (!File.Exists(path)) return ComposerIntentStoreResult.Success(Empty(workspaceId, workspaceRootIdentity, threadId));
            EnsureSafeChain(path);
            string json = File.ReadAllText(path, Encoding.UTF8);
            cancellationToken.ThrowIfCancellationRequested();
            ComposerQueueRecord queue = JsonSerializer.Deserialize<ComposerQueueRecord>(json, JsonOptions) ??
                throw new JsonException();
            ComposerIntentContractValidator.ValidateQueue(queue);
            if (queue.WorkspaceId != workspaceId || queue.WorkspaceRootIdentity != workspaceRootIdentity || queue.ThreadId != threadId)
            {
                return ComposerIntentStoreResult.Failure(ComposerIntentErrorCode.RevisionConflict, "Composer queue belongs to a different workspace or thread.");
            }
            return ComposerIntentStoreResult.Success(queue);
        }
        catch (OperationCanceledException) { throw; }
        catch (ComposerIntentContractException exception) { return ComposerIntentStoreResult.Failure(exception.ErrorCode, exception.Message); }
        catch (JsonException) { return ComposerIntentStoreResult.Failure(ComposerIntentErrorCode.Corrupt, "Composer queue JSON is corrupt."); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return ComposerIntentStoreResult.Failure(ComposerIntentErrorCode.Unavailable, "Composer queue could not be read.");
        }
    }

    public ComposerIntentStoreResult Enqueue(
        string workspaceId,
        string workspaceRootIdentity,
        string threadId,
        long expectedRevision,
        string mutationId,
        PendingComposerIntentRecord intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        return Mutate(workspaceId, workspaceRootIdentity, threadId, expectedRevision, mutationId, "enqueue", intent, queue =>
        {
            if (queue.PendingIntent is not null)
                throw new ComposerIntentContractException(ComposerIntentErrorCode.AlreadyPending, "Thread already has a pending composer intent.");
            return queue with { PendingIntent = intent };
        });
    }

    public ComposerIntentStoreResult Clear(
        string workspaceId,
        string workspaceRootIdentity,
        string threadId,
        long expectedRevision,
        string mutationId)
    {
        return Mutate(workspaceId, workspaceRootIdentity, threadId, expectedRevision, mutationId, "clear", null, queue =>
        {
            if (queue.PendingIntent is null)
                throw new ComposerIntentContractException(ComposerIntentErrorCode.NotFound, "Thread has no pending composer intent.");
            return queue with { PendingIntent = null };
        });
    }

    private ComposerIntentStoreResult Mutate(
        string workspaceId,
        string workspaceRootIdentity,
        string threadId,
        long expectedRevision,
        string mutationId,
        string operation,
        PendingComposerIntentRecord? intent,
        Func<ComposerQueueRecord, ComposerQueueRecord> mutation)
    {
        try
        {
            ValidateBinding(workspaceId, workspaceRootIdentity, threadId);
            if (expectedRevision < 0 || !ComposerIntentContractValidator.IsSafeMutationId(mutationId))
                throw new ComposerIntentContractException(ComposerIntentErrorCode.Invalid, "Composer queue mutation is invalid.");
            if (intent is not null) ComposerIntentContractValidator.ValidateIntent(intent);
            string payloadHash = HashPayload(operation, intent);
            Directory.CreateDirectory(root);
            EnsureSafeChain(root);
            string path = GetPath(threadId);
            string lockPath = path + ".lock";
            using FileStream mutationLock = new(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            ComposerIntentStoreResult currentResult = Get(workspaceId, workspaceRootIdentity, threadId);
            if (!currentResult.Succeeded || currentResult.Queue is null) return currentResult;
            ComposerQueueRecord current = currentResult.Queue;
            ComposerMutationReceiptRecord? receipt = current.MutationReceipts.SingleOrDefault(item => item.MutationId == mutationId);
            if (receipt is not null)
            {
                return receipt.PayloadSha256 == payloadHash && receipt.Operation == operation
                    ? ComposerIntentStoreResult.Success(current, idempotent: true)
                    : ComposerIntentStoreResult.Failure(ComposerIntentErrorCode.MutationConflict, "Composer mutation id was reused with different content.");
            }
            if (current.Revision != expectedRevision)
                return ComposerIntentStoreResult.Failure(ComposerIntentErrorCode.RevisionConflict, "Composer queue revision changed.");
            ComposerQueueRecord updated = mutation(current) with
            {
                Revision = current.Revision + 1,
                MutationReceipts = current.MutationReceipts.Append(new ComposerMutationReceiptRecord
                {
                    MutationId = mutationId,
                    PayloadSha256 = payloadHash,
                    Operation = operation,
                    ResultRevision = current.Revision + 1
                }).TakeLast(ComposerIntentLimits.MaxMutationReceipts).ToArray()
            };
            ComposerIntentContractValidator.ValidateQueue(updated);
            WriteAtomically(path, JsonSerializer.Serialize(updated, JsonOptions));
            return ComposerIntentStoreResult.Success(updated);
        }
        catch (ComposerIntentContractException exception) { return ComposerIntentStoreResult.Failure(exception.ErrorCode, exception.Message); }
        catch (IOException) { return ComposerIntentStoreResult.Failure(ComposerIntentErrorCode.WriteFailed, "Composer queue could not be written."); }
        catch (Exception exception) when (exception is UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return ComposerIntentStoreResult.Failure(ComposerIntentErrorCode.WriteFailed, "Composer queue could not be written.");
        }
    }

    private ComposerQueueRecord Empty(string workspaceId, string rootIdentity, string threadId) => new()
    {
        WorkspaceId = workspaceId,
        WorkspaceRootIdentity = rootIdentity,
        ThreadId = threadId
    };

    private string GetPath(string threadId)
    {
        if (!ThreadIdentity.IsThreadId(threadId)) throw new ComposerIntentContractException(ComposerIntentErrorCode.Invalid, "Thread id is invalid.");
        string path = Path.GetFullPath(Path.Combine(root, threadId + ".json"));
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new ComposerIntentContractException(ComposerIntentErrorCode.Invalid, "Composer queue path is invalid.");
        return path;
    }

    private static void ValidateBinding(string workspaceId, string rootIdentity, string threadId)
    {
        if (string.IsNullOrWhiteSpace(workspaceId) || string.IsNullOrWhiteSpace(rootIdentity) || !ThreadIdentity.IsThreadId(threadId))
            throw new ComposerIntentContractException(ComposerIntentErrorCode.Invalid, "Composer queue binding is invalid.");
    }

    private static string HashPayload(string operation, PendingComposerIntentRecord? intent)
    {
        PendingComposerIntentRecord? semanticIntent = intent is null ? null : intent with
        {
            IntentId = string.Empty,
            CreatedAtUtc = DateTimeOffset.UnixEpoch
        };
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new { operation, intent = semanticIntent }, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static void WriteAtomically(string path, string json)
    {
        string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (StreamWriter writer = new(stream, new UTF8Encoding(false)))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void EnsureSafeChain(string path)
    {
        string full = Path.GetFullPath(path);
        string? root = Path.GetPathRoot(full);
        if (root is null) throw new IOException();
        string current = root;
        foreach (string segment in Path.GetRelativePath(root, full).Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if ((File.Exists(current) || Directory.Exists(current)) && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                throw new IOException();
        }
    }
}
