using System.Text;
using System.Text.Json;

namespace CSharpAiCli.Core;

public sealed record TaskQueueDiagnostic(
    string ErrorCode,
    string Summary,
    string? Path = null,
    string? QueueId = null);

public sealed record TaskQueueReadResult(
    bool Succeeded,
    TaskQueueItem? Item,
    TaskQueueDiagnostic? Diagnostic)
{
    public static TaskQueueReadResult Success(TaskQueueItem item) => new(true, item, null);

    public static TaskQueueReadResult Failure(
        string errorCode,
        string summary,
        string? path = null,
        string? queueId = null) =>
        new(false, null, new TaskQueueDiagnostic(errorCode, summary, path, queueId));
}

public sealed record TaskQueueListResult(
    IReadOnlyList<TaskQueueItem> Items,
    IReadOnlyList<TaskQueueDiagnostic> Diagnostics);

public sealed record TaskQueueTransitionResult(
    bool Succeeded,
    TaskQueueItem? Item,
    TaskQueueDiagnostic? Diagnostic)
{
    public static TaskQueueTransitionResult Success(TaskQueueItem item) => new(true, item, null);

    public static TaskQueueTransitionResult Failure(TaskQueueDiagnostic diagnostic) => new(false, null, diagnostic);
}

public sealed record TaskQueueCleanupResult(
    bool Succeeded,
    int DeletedCount,
    IReadOnlyList<string> DeletedQueueIds,
    IReadOnlyList<TaskQueueDiagnostic> Diagnostics,
    string? ErrorCode = null,
    string? Summary = null);

public sealed class TaskQueueStore
{
    private const string QueueFileSuffix = ".queue.json";
    private const string InvalidQueueRecordSummary = "Queue record is missing or uses an unsupported schema.";
    private static readonly object TransitionGate = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string queueDirectory;

    public TaskQueueStore(string queueDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueDirectory);
        this.queueDirectory = queueDirectory;
    }

    public string QueueDirectory => queueDirectory;

    public static TaskQueueStore Create(CliEnvironmentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new TaskQueueStore(Path.Combine(ResolveLocalStateRoot(snapshot), "queue"));
    }

    public string Create(TaskQueueItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Directory.CreateDirectory(queueDirectory);
        string path = GetPath(item.QueueId);
        WriteJsonAtomically(path, JsonSerializer.Serialize(item, JsonOptions), overwrite: false);
        return path;
    }

    public TaskQueueReadResult Read(string queueId)
    {
        if (!TaskQueueIdGenerator.IsValid(queueId))
        {
            return TaskQueueReadResult.Failure(
                TaskQueueErrorCode.NotFound,
                "Queue item was not found.",
                queueId: queueId);
        }

        string path = GetPath(queueId);
        if (!File.Exists(path))
        {
            return TaskQueueReadResult.Failure(
                TaskQueueErrorCode.NotFound,
                "Queue item was not found.",
                path,
                queueId);
        }

        return TryLoad(path, queueId);
    }

    public TaskQueueListResult List(int? limit = null, string? status = null)
    {
        if (!Directory.Exists(queueDirectory))
        {
            return new TaskQueueListResult([], []);
        }

        List<TaskQueueItem> items = [];
        List<TaskQueueDiagnostic> diagnostics = [];
        foreach (string path in EnumerateQueueFilesBestEffort(queueDirectory))
        {
            TaskQueueReadResult result = TryLoad(path);
            if (result.Succeeded && result.Item is not null)
            {
                if (string.IsNullOrWhiteSpace(status) || result.Item.Status == status)
                {
                    items.Add(result.Item);
                }
            }
            else if (result.Diagnostic is not null)
            {
                diagnostics.Add(result.Diagnostic);
            }
        }

        IOrderedEnumerable<TaskQueueItem> ordered = items
            .OrderByDescending(item => item.CreatedAtUtc)
            .ThenByDescending(item => item.QueueId, StringComparer.Ordinal);
        IReadOnlyList<TaskQueueItem> bounded = limit is > 0
            ? ordered.Take(limit.Value).ToArray()
            : ordered.ToArray();
        return new TaskQueueListResult(bounded, diagnostics);
    }

    public TaskQueueTransitionResult Start(string queueId, DateTimeOffset nowUtc)
    {
        return Transition(
            queueId,
            item => TaskQueueStatus.CanRun(item.Status),
            item => item.StartAttempt(nowUtc),
            "Only pending or failed queue items can be run.");
    }

    public TaskQueueTransitionResult Complete(
        string queueId,
        int attempt,
        DateTimeOffset nowUtc,
        int exitCode,
        string? jobId,
        string? errorCode,
        string? summary)
    {
        return Transition(
            queueId,
            item => item.Status == TaskQueueStatus.Running &&
                item.Attempts.Count > 0 &&
                item.Attempts[^1].Attempt == attempt,
            item => item.CompleteAttempt(nowUtc, exitCode, jobId, errorCode, summary),
            "Queue item is not running the expected attempt.");
    }

    public TaskQueueTransitionResult Cancel(string queueId, DateTimeOffset nowUtc)
    {
        return Transition(
            queueId,
            item => item.Status == TaskQueueStatus.Pending,
            item => item.Cancel(nowUtc),
            "Only a pending queue item can be canceled.");
    }

    public TaskQueueCleanupResult Cleanup(string status, DateTimeOffset olderThanUtc)
    {
        if (!TaskQueueStatus.IsTerminal(status))
        {
            return new TaskQueueCleanupResult(
                false,
                0,
                [],
                [],
                TaskQueueErrorCode.UnsafeCleanupStatus,
                "Cleanup only accepts succeeded, failed, or canceled queue items.");
        }

        if (!Directory.Exists(queueDirectory))
        {
            return new TaskQueueCleanupResult(true, 0, [], []);
        }

        List<string> deleted = [];
        List<TaskQueueDiagnostic> diagnostics = [];
        lock (TransitionGate)
        {
            foreach (string path in EnumerateQueueFilesBestEffort(queueDirectory))
            {
                TaskQueueReadResult read = TryLoad(path);
                if (!read.Succeeded || read.Item is null)
                {
                    if (read.Diagnostic is not null)
                    {
                        diagnostics.Add(read.Diagnostic);
                    }

                    continue;
                }

                TaskQueueItem item = read.Item;
                DateTimeOffset completedAtUtc = item.CompletedAtUtc ?? item.UpdatedAtUtc;
                if (item.Status != status || !TaskQueueStatus.IsTerminal(item.Status) || completedAtUtc > olderThanUtc)
                {
                    continue;
                }

                try
                {
                    File.Delete(path);
                    deleted.Add(item.QueueId);
                }
                catch (Exception exception) when (IsBestEffortStoreException(exception))
                {
                    diagnostics.Add(new TaskQueueDiagnostic(
                        TaskQueueErrorCode.RecordUnreadable,
                        "Queue record could not be deleted.",
                        path,
                        item.QueueId));
                }
            }
        }

        return new TaskQueueCleanupResult(true, deleted.Count, deleted, diagnostics);
    }

    private TaskQueueTransitionResult Transition(
        string queueId,
        Func<TaskQueueItem, bool> canTransition,
        Func<TaskQueueItem, TaskQueueItem> transition,
        string invalidStateSummary)
    {
        lock (TransitionGate)
        {
            TaskQueueReadResult read = Read(queueId);
            if (!read.Succeeded || read.Item is null)
            {
                return TaskQueueTransitionResult.Failure(
                    read.Diagnostic ?? new TaskQueueDiagnostic(
                        TaskQueueErrorCode.NotFound,
                        "Queue item was not found.",
                        QueueId: queueId));
            }

            if (!canTransition(read.Item))
            {
                return TaskQueueTransitionResult.Failure(new TaskQueueDiagnostic(
                    TaskQueueErrorCode.InvalidState,
                    invalidStateSummary,
                    GetPath(queueId),
                    queueId));
            }

            try
            {
                TaskQueueItem updated = transition(read.Item);
                WriteJsonAtomically(
                    GetPath(queueId),
                    JsonSerializer.Serialize(updated, JsonOptions),
                    overwrite: true);
                return TaskQueueTransitionResult.Success(updated);
            }
            catch (Exception exception) when (IsBestEffortStoreException(exception) || exception is ArgumentException or InvalidOperationException)
            {
                return TaskQueueTransitionResult.Failure(new TaskQueueDiagnostic(
                    TaskQueueErrorCode.RecordWriteFailed,
                    "Queue record could not be updated.",
                    GetPath(queueId),
                    queueId));
            }
        }
    }

    private TaskQueueReadResult TryLoad(string path, string? expectedQueueId = null)
    {
        try
        {
            string json = File.ReadAllText(path);
            using JsonDocument document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("schemaVersion", out JsonElement versionElement) ||
                versionElement.ValueKind != JsonValueKind.Number ||
                !versionElement.TryGetInt32(out int schemaVersion) ||
                schemaVersion != TaskQueueItem.CurrentSchemaVersion)
            {
                return TaskQueueReadResult.Failure(
                    TaskQueueErrorCode.CorruptRecord,
                    InvalidQueueRecordSummary,
                    path,
                    expectedQueueId);
            }

            TaskQueueItem? item = JsonSerializer.Deserialize<TaskQueueItem>(json, JsonOptions);
            if (item is null || !TaskQueueIdGenerator.IsValid(item.QueueId))
            {
                return TaskQueueReadResult.Failure(
                    TaskQueueErrorCode.CorruptRecord,
                    InvalidQueueRecordSummary,
                    path,
                    expectedQueueId);
            }

            string pathQueueId = Path.GetFileName(path);
            if (pathQueueId.EndsWith(QueueFileSuffix, StringComparison.OrdinalIgnoreCase))
            {
                pathQueueId = pathQueueId[..^QueueFileSuffix.Length];
            }

            StringComparison comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!string.Equals(pathQueueId, item.QueueId, comparison) ||
                (expectedQueueId is not null && !string.Equals(expectedQueueId, item.QueueId, comparison)))
            {
                return TaskQueueReadResult.Failure(
                    TaskQueueErrorCode.CorruptRecord,
                    "Queue record id does not match its file path.",
                    path,
                    expectedQueueId ?? item.QueueId);
            }

            return TaskQueueReadResult.Success(item);
        }
        catch (JsonException exception)
        {
            return TaskQueueReadResult.Failure(
                TaskQueueErrorCode.CorruptRecord,
                InvalidQueueRecordSummary + " " + exception.GetType().Name,
                path,
                expectedQueueId);
        }
        catch (Exception exception) when (IsBestEffortStoreException(exception) || exception is ArgumentException or InvalidOperationException)
        {
            return TaskQueueReadResult.Failure(
                exception is ArgumentException or InvalidOperationException
                    ? TaskQueueErrorCode.CorruptRecord
                    : TaskQueueErrorCode.RecordUnreadable,
                InvalidQueueRecordSummary,
                path,
                expectedQueueId);
        }
    }

    private static IReadOnlyList<string> EnumerateQueueFilesBestEffort(string directory)
    {
        List<string> paths = [];
        IEnumerator<string>? enumerator = null;
        try
        {
            enumerator = Directory.EnumerateFiles(directory, "*" + QueueFileSuffix, SearchOption.TopDirectoryOnly)
                .GetEnumerator();
            while (true)
            {
                try
                {
                    if (!enumerator.MoveNext())
                    {
                        break;
                    }
                }
                catch (Exception exception) when (IsBestEffortStoreException(exception))
                {
                    break;
                }

                paths.Add(enumerator.Current);
            }
        }
        catch (Exception exception) when (IsBestEffortStoreException(exception))
        {
        }
        finally
        {
            enumerator?.Dispose();
        }

        paths.Sort(StringComparer.Ordinal);
        return paths;
    }

    private static void WriteJsonAtomically(string path, string json, bool overwrite)
    {
        string? directory = Path.GetDirectoryName(path);
        string fileName = Path.GetFileName(path);
        string temporaryPath = string.IsNullOrWhiteSpace(directory)
            ? $"{fileName}.{Guid.NewGuid():N}.tmp"
            : Path.Combine(directory, $"{fileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(temporaryPath, json, Encoding.UTF8);
            try
            {
                File.Move(temporaryPath, path, overwrite);
            }
            catch (IOException) when (!overwrite && File.Exists(path))
            {
                throw new IOException("Queue record already exists.");
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
        catch (Exception exception) when (IsBestEffortStoreException(exception))
        {
        }
    }

    private string GetPath(string queueId)
    {
        if (!TaskQueueIdGenerator.IsValid(queueId))
        {
            throw new ArgumentException("Queue id format is invalid.", nameof(queueId));
        }

        string fullDirectory = Path.GetFullPath(queueDirectory);
        string path = Path.GetFullPath(Path.Combine(fullDirectory, queueId + QueueFileSuffix));
        string rootedDirectory = fullDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? fullDirectory
            : fullDirectory + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!path.StartsWith(rootedDirectory, comparison))
        {
            throw new InvalidOperationException("Queue record path must remain inside the queue directory.");
        }

        return path;
    }

    private static string ResolveLocalStateRoot(CliEnvironmentSnapshot snapshot)
    {
        string? userConfigDirectory = Path.GetDirectoryName(snapshot.UserConfigPath);
        return string.IsNullOrWhiteSpace(userConfigDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".caicli")
            : userConfigDirectory;
    }

    private static bool IsBestEffortStoreException(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or DirectoryNotFoundException
            or FileNotFoundException
            or NotSupportedException;
}
