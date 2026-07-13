using CSharpAiCli.Core;
using System.Text.Json.Nodes;

namespace CSharpAiCli.Tests;

public sealed class TaskQueueStoreTests
{
    [Fact]
    public void List_is_empty_and_does_not_create_missing_directory()
    {
        using TempDirectory temp = TempDirectory.Create();
        TaskQueueStore store = new(Path.Combine(temp.Path, "queue"));

        TaskQueueListResult result = store.List();

        Assert.Empty(result.Items);
        Assert.Empty(result.Diagnostics);
        Assert.False(Directory.Exists(store.QueueDirectory));
    }

    [Fact]
    public void Create_read_start_complete_and_retry_round_trip()
    {
        using TempDirectory temp = TempDirectory.Create();
        TaskQueueStore store = new(Path.Combine(temp.Path, "queue"));
        DateTimeOffset created = DateTimeOffset.Parse("2026-07-13T01:02:03Z");
        TaskQueueItem pending = CreatePending(created);
        store.Create(pending);

        TaskQueueTransitionResult firstStart = store.Start(pending.QueueId, created.AddSeconds(1));
        TaskQueueTransitionResult firstComplete = store.Complete(
            pending.QueueId,
            1,
            created.AddSeconds(2),
            1,
            JobIdGenerator.Create(created.AddSeconds(1)),
            "tool-failure",
            "failed");
        TaskQueueTransitionResult secondStart = store.Start(pending.QueueId, created.AddSeconds(3));
        string secondJobId = JobIdGenerator.Create(created.AddSeconds(3));
        TaskQueueTransitionResult secondComplete = store.Complete(
            pending.QueueId,
            2,
            created.AddSeconds(4),
            0,
            secondJobId,
            null,
            "done");
        TaskQueueReadResult read = store.Read(pending.QueueId);

        Assert.True(firstStart.Succeeded);
        Assert.Equal(TaskQueueStatus.Failed, firstComplete.Item?.Status);
        Assert.Equal(2, secondStart.Item?.Attempts.Count);
        Assert.True(secondComplete.Succeeded);
        Assert.Equal(TaskQueueStatus.Succeeded, read.Item?.Status);
        Assert.Equal(secondJobId, read.Item?.LatestJobId);
    }

    [Fact]
    public void Cancel_rejects_non_pending_item_with_stable_error_code()
    {
        using TempDirectory temp = TempDirectory.Create();
        TaskQueueStore store = new(Path.Combine(temp.Path, "queue"));
        DateTimeOffset created = DateTimeOffset.Parse("2026-07-13T01:02:03Z");
        TaskQueueItem pending = CreatePending(created);
        store.Create(pending);
        TaskQueueTransitionResult first = store.Cancel(pending.QueueId, created.AddSeconds(1));
        TaskQueueTransitionResult second = store.Cancel(pending.QueueId, created.AddSeconds(2));

        Assert.True(first.Succeeded);
        Assert.Equal(TaskQueueStatus.Canceled, first.Item?.Status);
        Assert.False(second.Succeeded);
        Assert.Equal(TaskQueueErrorCode.InvalidState, second.Diagnostic?.ErrorCode);
    }

    [Fact]
    public void Cleanup_deletes_only_requested_old_terminal_items_and_preserves_running_and_unknown()
    {
        using TempDirectory temp = TempDirectory.Create();
        TaskQueueStore store = new(Path.Combine(temp.Path, "queue"));
        DateTimeOffset old = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        TaskQueueItem succeeded = CreatePending(old);
        store.Create(succeeded);
        store.Start(succeeded.QueueId, old.AddMinutes(1));
        store.Complete(succeeded.QueueId, 1, old.AddMinutes(2), 0, JobIdGenerator.Create(old), null, "done");

        TaskQueueItem running = CreatePending(old.AddSeconds(1));
        store.Create(running);
        store.Start(running.QueueId, old.AddMinutes(1));

        TaskQueueItem pending = CreatePending(old.AddSeconds(2));
        store.Create(pending);

        string unknownId = TaskQueueIdGenerator.Create(old.AddSeconds(3));
        string unknownPath = Path.Combine(store.QueueDirectory, unknownId + ".queue.json");
        File.WriteAllText(unknownPath, $$"""
            {"schemaVersion":1,"queueId":"{{unknownId}}","status":"unknown"}
            """);

        TaskQueueCleanupResult unsafeResult = store.Cleanup(TaskQueueStatus.Running, old.AddYears(1));
        TaskQueueCleanupResult result = store.Cleanup(TaskQueueStatus.Succeeded, old.AddYears(1));

        Assert.False(unsafeResult.Succeeded);
        Assert.Equal(TaskQueueErrorCode.UnsafeCleanupStatus, unsafeResult.ErrorCode);
        Assert.True(result.Succeeded);
        Assert.Equal(1, result.DeletedCount);
        Assert.Equal(TaskQueueErrorCode.NotFound, store.Read(succeeded.QueueId).Diagnostic?.ErrorCode);
        Assert.Equal(TaskQueueStatus.Running, store.Read(running.QueueId).Item?.Status);
        Assert.Equal(TaskQueueStatus.Pending, store.Read(pending.QueueId).Item?.Status);
        Assert.True(File.Exists(unknownPath));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.ErrorCode == TaskQueueErrorCode.CorruptRecord);
    }

    [Fact]
    public void List_reports_corrupt_records_without_hiding_valid_items()
    {
        using TempDirectory temp = TempDirectory.Create();
        TaskQueueStore store = new(Path.Combine(temp.Path, "queue"));
        DateTimeOffset created = DateTimeOffset.Parse("2026-07-13T01:02:03Z");
        TaskQueueItem pending = CreatePending(created);
        store.Create(pending);
        File.WriteAllText(
            Path.Combine(store.QueueDirectory, TaskQueueIdGenerator.Create(created.AddSeconds(1)) + ".queue.json"),
            "{ not-json");

        TaskQueueListResult result = store.List();

        Assert.Single(result.Items);
        Assert.Equal(TaskQueueErrorCode.CorruptRecord, Assert.Single(result.Diagnostics).ErrorCode);
    }

    [Fact]
    public void Json_list_and_cleanup_redact_corrupt_record_diagnostics()
    {
        using TempDirectory temp = TempDirectory.Create();
        TaskQueueStore store = new(Path.Combine(temp.Path, "queue"));
        Directory.CreateDirectory(store.QueueDirectory);
        const string secret = "queue-diagnostic-secret";
        File.WriteAllText(
            Path.Combine(store.QueueDirectory, $"apiKey={secret}.queue.json"),
            "{ not-json");
        TaskQueueListResult list = store.List();
        TaskQueueCleanupResult cleanup = store.Cleanup(
            TaskQueueStatus.Failed,
            DateTimeOffset.MaxValue);
        using StringWriter listOutput = new();
        using StringWriter cleanupOutput = new();

        new TaskQueueJsonRenderer(listOutput).WriteList(list);
        new TaskQueueJsonRenderer(cleanupOutput).WriteCleanup(cleanup);
        JsonObject listJson = Assert.IsType<JsonObject>(JsonNode.Parse(listOutput.ToString()));
        JsonObject cleanupJson = Assert.IsType<JsonObject>(JsonNode.Parse(cleanupOutput.ToString()));

        Assert.Single(Assert.IsType<JsonArray>(listJson["diagnostics"]));
        Assert.Single(Assert.IsType<JsonArray>(cleanupJson["diagnostics"]));
        Assert.DoesNotContain(secret, listOutput.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(secret, cleanupOutput.ToString(), StringComparison.Ordinal);
        Assert.Contains("[redacted]", listOutput.ToString(), StringComparison.Ordinal);
        Assert.Contains("[redacted]", cleanupOutput.ToString(), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(store.QueueDirectory, $"apiKey={secret}.queue.json")));
    }

    private static TaskQueueItem CreatePending(DateTimeOffset created) =>
        TaskQueueItem.CreatePending(
            TaskQueueIdGenerator.Create(created),
            created,
            new TaskQueueRequest(TaskQueueCommandFamily.Exec, "task", "C:/work"));

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;

        public string Path { get; }

        public static TempDirectory Create()
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "caicli-queue-store-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
