using System.Text.Json;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class TaskQueueRecordTests
{
    [Fact]
    public void Queue_id_and_json_schema_describe_stable_v1_contract()
    {
        string queueId = TaskQueueIdGenerator.Create(DateTimeOffset.Parse("2026-07-13T01:02:03.456Z"));
        string schema = TaskQueueItemJsonSchema.Render();

        Assert.Matches("^queue_20260713T010203456Z_[a-f0-9]{8}$", queueId);
        Assert.True(TaskQueueIdGenerator.IsValid(queueId));
        Assert.False(TaskQueueIdGenerator.IsValid("../secret"));
        Assert.Contains("https://c-aicli.local/schemas/task-queue-item.v1.json", schema, StringComparison.Ordinal);
        Assert.Contains("\"pending\"", schema, StringComparison.Ordinal);
        Assert.Contains("\"running\"", schema, StringComparison.Ordinal);
        Assert.Contains("\"succeeded\"", schema, StringComparison.Ordinal);
        Assert.Contains("\"failed\"", schema, StringComparison.Ordinal);
        Assert.Contains("\"canceled\"", schema, StringComparison.Ordinal);
        Assert.Contains("\"attempts\"", schema, StringComparison.Ordinal);
    }

    [Fact]
    public void Queue_request_redacts_secret_like_content_and_stores_no_approval_override()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-13T01:02:03Z");
        TaskQueueItem item = TaskQueueItem.CreatePending(
            TaskQueueIdGenerator.Create(now),
            now,
            new TaskQueueRequest(
                TaskQueueCommandFamily.Exec,
                "review apiKey=plain-secret Authorization: Bearer bearer-secret",
                "C:/work",
                Expert: "reviewer"));

        string json = JsonSerializer.Serialize(item, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.DoesNotContain("plain-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("bearer-secret", json, StringComparison.Ordinal);
        Assert.Contains("[redacted]", json, StringComparison.Ordinal);
        Assert.False(item.Redaction.RawReferencesStored);
        Assert.False(item.Redaction.RawToolArgumentsStored);
        Assert.False(item.Redaction.ApprovalOverridesStored);
    }

    [Fact]
    public void Queue_item_supports_failed_retry_and_successful_second_attempt()
    {
        DateTimeOffset created = DateTimeOffset.Parse("2026-07-13T01:02:03Z");
        TaskQueueItem item = TaskQueueItem.CreatePending(
            TaskQueueIdGenerator.Create(created),
            created,
            new TaskQueueRequest(TaskQueueCommandFamily.Exec, "task", "C:/work"));
        TaskQueueItem firstRunning = item.StartAttempt(created.AddSeconds(1));
        string firstJobId = JobIdGenerator.Create(created.AddSeconds(1));
        TaskQueueItem firstFailed = firstRunning.CompleteAttempt(
            created.AddSeconds(2),
            1,
            firstJobId,
            "tool-failure",
            "failed");
        TaskQueueItem secondRunning = firstFailed.StartAttempt(created.AddSeconds(3));
        string secondJobId = JobIdGenerator.Create(created.AddSeconds(3));
        TaskQueueItem succeeded = secondRunning.CompleteAttempt(
            created.AddSeconds(4),
            0,
            secondJobId,
            null,
            "done");

        Assert.Equal(TaskQueueStatus.Succeeded, succeeded.Status);
        Assert.Equal(2, succeeded.Attempts.Count);
        Assert.Equal(TaskQueueStatus.Failed, succeeded.Attempts[0].Status);
        Assert.Equal(TaskQueueStatus.Succeeded, succeeded.Attempts[1].Status);
        Assert.Equal(secondJobId, succeeded.LatestJobId);
    }

    [Fact]
    public void Cancel_is_pending_only()
    {
        DateTimeOffset created = DateTimeOffset.Parse("2026-07-13T01:02:03Z");
        TaskQueueItem pending = TaskQueueItem.CreatePending(
            TaskQueueIdGenerator.Create(created),
            created,
            new TaskQueueRequest(TaskQueueCommandFamily.Exec, "task", "C:/work"));
        TaskQueueItem canceled = pending.Cancel(created.AddSeconds(1));

        Assert.Equal(TaskQueueStatus.Canceled, canceled.Status);
        Assert.Throws<InvalidOperationException>(() => canceled.Cancel(created.AddSeconds(2)));
        Assert.Throws<InvalidOperationException>(() => pending.StartAttempt(created.AddSeconds(1)).Cancel(created.AddSeconds(2)));
    }
}
