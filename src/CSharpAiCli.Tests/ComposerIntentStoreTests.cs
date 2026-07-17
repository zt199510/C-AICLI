using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ComposerIntentStoreTests
{
    [Fact]
    public void Enqueue_Get_Clear_ArePersistentRevisionedAndIdempotent()
    {
        using TempRoot temp = new();
        ComposerIntentStore store = new(Path.Combine(temp.Path, "composer"));
        string threadId = ThreadIdentity.CreateThreadId();
        PendingComposerIntentRecord intent = CreateIntent(threadId, temp.Path);

        ComposerIntentStoreResult enqueued = store.Enqueue("ws_test", temp.Path, threadId, 0, "enqueue-1", intent);
        Assert.True(enqueued.Succeeded);
        Assert.Equal(1, enqueued.Queue!.Revision);
        Assert.Equal(intent, enqueued.Queue.PendingIntent);

        ComposerIntentStoreResult duplicate = store.Enqueue("ws_test", temp.Path, threadId, 0, "enqueue-1", intent);
        Assert.True(duplicate.Succeeded);
        Assert.True(duplicate.Idempotent);
        Assert.Equal(1, duplicate.Queue!.Revision);

        ComposerIntentStore restarted = new(Path.Combine(temp.Path, "composer"));
        ComposerIntentStoreResult read = restarted.Get("ws_test", temp.Path, threadId);
        Assert.True(read.Succeeded);
        Assert.Equal(intent.IntentId, read.Queue!.PendingIntent!.IntentId);

        ComposerIntentStoreResult cleared = restarted.Clear("ws_test", temp.Path, threadId, 1, "clear-1");
        Assert.True(cleared.Succeeded);
        Assert.Equal(2, cleared.Queue!.Revision);
        Assert.Null(cleared.Queue.PendingIntent);
    }

    [Fact]
    public void Enqueue_RejectsConflictsAndCorruption()
    {
        using TempRoot temp = new();
        string root = Path.Combine(temp.Path, "composer");
        ComposerIntentStore store = new(root);
        string threadId = ThreadIdentity.CreateThreadId();
        PendingComposerIntentRecord intent = CreateIntent(threadId, temp.Path);
        Assert.True(store.Enqueue("ws_test", temp.Path, threadId, 0, "same", intent).Succeeded);

        Assert.Equal(ComposerIntentErrorCode.AlreadyPending,
            store.Enqueue("ws_test", temp.Path, threadId, 1, "second", intent with { IntentId = "intent_" + Guid.NewGuid().ToString("N") }).Diagnostic!.ErrorCode);
        Assert.Equal(ComposerIntentErrorCode.MutationConflict,
            store.Clear("ws_test", temp.Path, threadId, 1, "same").Diagnostic!.ErrorCode);
        Assert.Equal(ComposerIntentErrorCode.RevisionConflict,
            store.Clear("ws_test", temp.Path, threadId, 0, "clear").Diagnostic!.ErrorCode);

        File.WriteAllText(Path.Combine(root, threadId + ".json"), "{ truncated");
        Assert.Equal(ComposerIntentErrorCode.Corrupt, store.Get("ws_test", temp.Path, threadId).Diagnostic!.ErrorCode);
    }

    private static PendingComposerIntentRecord CreateIntent(string threadId, string root) => new()
    {
        IntentId = "intent_" + Guid.NewGuid().ToString("N"),
        WorkspaceId = "ws_test",
        WorkspaceRootIdentity = root,
        ThreadId = threadId,
        Prompt = "Explain the change",
        EffectiveModel = "gpt-test",
        ModelSource = "test",
        ApprovalMode = "OnRequest",
        ApprovalModeSource = "test",
        CreatedAtUtc = DateTimeOffset.UtcNow
    };

    private sealed class TempRoot : IDisposable
    {
        public TempRoot() { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "caicli-composer-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path); }
        public string Path { get; }
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }
}
