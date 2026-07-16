using System.Text.Json.Nodes;
using CSharpAiCli.Application;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class SessionMigrationTests
{
    [Fact]
    public void Bounded_loader_is_strict_fingerprinted_and_record_limited()
    {
        using Fixture fixture = Fixture.Create();
        ConversationSessionName name = ConversationSessionName.Parse("review");
        ConversationTranscript transcript = fixture.CreateTranscript(name.Value);
        string path = fixture.Sessions.Save(name, transcript);

        ConversationTranscriptSnapshot first = fixture.Sessions.ReadBounded(name);
        ConversationTranscriptSnapshot second = fixture.Sessions.ReadBounded(name);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(4, first.RecordCount);

        ConversationTranscriptReadException limited = Assert.Throws<ConversationTranscriptReadException>(() =>
            fixture.Sessions.ReadBounded(name, maxRecords: 3));
        Assert.Equal(ThreadErrorCode.SessionImportLimitExceeded, limited.ErrorCode);

        JsonObject json = Assert.IsType<JsonObject>(JsonNode.Parse(File.ReadAllText(path)));
        json["unknownField"] = "rejected";
        File.WriteAllText(path, json.ToJsonString());
        ConversationTranscriptReadException strict = Assert.Throws<ConversationTranscriptReadException>(() =>
            fixture.Sessions.ReadBounded(name));
        Assert.Equal(ConversationTranscriptReadErrorCode.Corrupt, strict.ErrorCode);
    }

    [Fact]
    public void Preview_is_read_only_import_is_idempotent_and_source_is_preserved()
    {
        using Fixture fixture = Fixture.Create();
        ConversationSessionName name = ConversationSessionName.Parse("review");
        string sourcePath = fixture.Sessions.Save(name, fixture.CreateTranscript(name.Value));
        byte[] beforeBytes = File.ReadAllBytes(sourcePath);
        DateTime beforeWrite = File.GetLastWriteTimeUtc(sourcePath);
        ThreadApplicationService service = fixture.CreateService();

        ApplicationResult<SessionImportPreviewProjection> firstPreview = service.PreviewSessionImport(
            new SessionImportPreviewRequest(fixture.Snapshot, name.Value));
        ApplicationResult<SessionImportPreviewProjection> secondPreview = service.PreviewSessionImport(
            new SessionImportPreviewRequest(fixture.Snapshot, name.Value));
        Assert.True(firstPreview.Succeeded, firstPreview.Error?.SafeMessage);
        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(firstPreview.Data),
            System.Text.Json.JsonSerializer.Serialize(secondPreview.Data));
        Assert.False(Directory.Exists(fixture.Store.ThreadsRoot));

        ApplicationResult<SessionImportProjection> imported = service.ImportSession(new SessionImportRequest(
            fixture.Snapshot, name.Value, firstPreview.Data!.Fingerprint));
        ApplicationResult<SessionImportProjection> retry = service.ImportSession(new SessionImportRequest(
            fixture.Snapshot, name.Value, firstPreview.Data.Fingerprint));

        Assert.True(imported.Succeeded, imported.Error?.SafeMessage);
        Assert.False(imported.Data?.Idempotent);
        Assert.True(retry.Data?.Idempotent);
        Assert.Equal(imported.Data?.Thread.ThreadId, retry.Data?.Thread.ThreadId);
        Assert.Equal(beforeBytes, File.ReadAllBytes(sourcePath));
        Assert.Equal(beforeWrite, File.GetLastWriteTimeUtc(sourcePath));
        Assert.True(File.Exists(sourcePath));

        ThreadStoreReadResult persisted = fixture.Store.Read(imported.Data!.Thread.ThreadId);
        Assert.True(persisted.Succeeded, persisted.Diagnostic?.SafeMessage);
        Assert.Equal("session-import", persisted.Aggregate?.Record.Origin.Kind);
        Assert.Single(persisted.Aggregate!.Turns);
        Assert.Equal(firstPreview.Data.TimelineItemCount, persisted.Aggregate.Record.TimelineItemCount);
        Assert.DoesNotContain("secret-tool-argument", File.ReadAllText(fixture.Store.GetLayout(imported.Data.Thread.ThreadId).ManifestPath), StringComparison.Ordinal);
    }

    [Fact]
    public void Import_rechecks_preview_fingerprint_and_changed_source_conflicts_without_partial_thread()
    {
        using Fixture fixture = Fixture.Create();
        ConversationSessionName name = ConversationSessionName.Parse("changed");
        fixture.Sessions.Save(name, fixture.CreateTranscript(name.Value));
        ThreadApplicationService service = fixture.CreateService();
        ApplicationResult<SessionImportPreviewProjection> preview = service.PreviewSessionImport(
            new SessionImportPreviewRequest(fixture.Snapshot, name.Value));

        ConversationTranscript changed = fixture.Sessions.LoadOrCreate(name, fixture.Now);
        changed.AddUserMessage("changed after preview", fixture.Now.AddMinutes(1));
        fixture.Sessions.Save(name, changed);
        ApplicationResult<SessionImportProjection> result = service.ImportSession(new SessionImportRequest(
            fixture.Snapshot, name.Value, preview.Data!.Fingerprint));

        Assert.False(result.Succeeded);
        Assert.Equal(ThreadErrorCode.SessionImportSourceChanged, result.Error?.Code);
        Assert.Equal(ApplicationErrorCategory.Conflict, result.Error?.Category);
        Assert.Empty(Directory.Exists(fixture.Store.ThreadsRoot)
            ? Directory.EnumerateDirectories(fixture.Store.ThreadsRoot, "thread_*")
            : []);
    }

    [Fact]
    public void Imported_thread_can_be_rolled_back_without_deleting_session_source()
    {
        using Fixture fixture = Fixture.Create();
        ConversationSessionName name = ConversationSessionName.Parse("rollback");
        string sourcePath = fixture.Sessions.Save(name, fixture.CreateTranscript(name.Value));
        byte[] sourceBytes = File.ReadAllBytes(sourcePath);
        ThreadApplicationService service = fixture.CreateService();
        SessionImportPreviewProjection preview = service.PreviewSessionImport(
            new SessionImportPreviewRequest(fixture.Snapshot, name.Value)).Data!;
        ThreadSummaryProjection imported = service.ImportSession(new SessionImportRequest(
            fixture.Snapshot, name.Value, preview.Fingerprint)).Data!.Thread;

        fixture.Advance();
        ThreadSummaryProjection archived = service.Archive(new ThreadArchiveRequest(
            fixture.Snapshot, imported.ThreadId, imported.Revision)).Data!;
        ApplicationResult<ThreadDeleteProjection> deleted = service.Delete(new ThreadDeleteRequest(
            fixture.Snapshot, imported.ThreadId, archived.Revision, imported.ThreadId));

        Assert.True(deleted.Data?.Deleted);
        Assert.True(File.Exists(sourcePath));
        Assert.Equal(sourceBytes, File.ReadAllBytes(sourcePath));
        Assert.Equal(preview.Fingerprint, service.PreviewSessionImport(
            new SessionImportPreviewRequest(fixture.Snapshot, name.Value)).Data?.Fingerprint);
    }

    [Fact]
    public void Deterministic_projector_is_order_independent_redacted_and_typed()
    {
        string threadId = ThreadIdentity.CreateThreadId();
        string turnId = ThreadIdentity.CreateTurnId();
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-16T01:02:03Z");
        TimelineProjectionInput message = new(
            "message", 0, now, TimelineItemType.UserMessage, "recorded", "apiKey=projector-secret",
            new TimelinePayloadRecord { Message = new TimelineMessagePayloadRecord("apiKey=projector-secret") },
            Redacted: true);
        TimelineProjectionInput warning = new(
            "warning", 0, now, TimelineItemType.WarningRaised, "failed", "warning",
            new TimelinePayloadRecord { Warning = new TimelineWarningPayloadRecord("controlled") });

        IReadOnlyList<TimelineItemRecord> first = DeterministicTimelineProjector.Project(
            threadId, turnId, "seed", [warning, message]);
        IReadOnlyList<TimelineItemRecord> second = DeterministicTimelineProjector.Project(
            threadId, turnId, "seed", [message, warning]);

        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(first),
            System.Text.Json.JsonSerializer.Serialize(second));
        Assert.Equal(new long[] { 1, 2 }, first.Select(item => item.Sequence));
        Assert.DoesNotContain("projector-secret", System.Text.Json.JsonSerializer.Serialize(first), StringComparison.Ordinal);
        Assert.NotNull(first[0].Payload.Message);
        Assert.NotNull(first[1].Payload.Warning);
    }

    [Fact]
    public void Bounded_loader_rejects_session_reparse_without_reading_target()
    {
        using Fixture fixture = Fixture.Create();
        string outside = Path.Combine(fixture.Root, "outside-session");
        Directory.CreateDirectory(outside);
        ConversationSessionName name = ConversationSessionName.Parse("linked");
        FileConversationStore outsideStore = new(outside);
        outsideStore.Save(name, fixture.CreateTranscript(name.Value));
        string sessionRoot = Path.Combine(fixture.Root, "linked-sessions");
        try
        {
            Directory.CreateSymbolicLink(sessionRoot, outside);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        FileConversationStore linked = new(sessionRoot);
        ConversationTranscriptReadException readException = Assert.Throws<ConversationTranscriptReadException>(() =>
            linked.ReadBounded(name));
        Assert.Equal(ConversationTranscriptReadErrorCode.ReparsePoint, readException.ErrorCode);
    }

    private sealed class Fixture : IDisposable
    {
        private DateTimeOffset current = DateTimeOffset.Parse("2026-07-16T01:02:03Z");

        private Fixture(string root, CliEnvironmentSnapshot snapshot, ThreadStore store, FileConversationStore sessions)
        {
            Root = root;
            Snapshot = snapshot;
            Store = store;
            Sessions = sessions;
        }

        public string Root { get; }
        public CliEnvironmentSnapshot Snapshot { get; }
        public ThreadStore Store { get; }
        public FileConversationStore Sessions { get; }
        public DateTimeOffset Now => current;

        public static Fixture Create()
        {
            string root = Path.Combine(Path.GetTempPath(), "caicli-session-migration-tests-" + Guid.NewGuid().ToString("N"));
            string workspace = Path.Combine(root, "workspace");
            string profile = Path.Combine(root, "profile");
            Directory.CreateDirectory(workspace);
            Directory.CreateDirectory(profile);
            CliEnvironmentSnapshot snapshot = CliEnvironmentSnapshot.Create(
                workspace,
                currentDirectory: workspace,
                userProfile: profile,
                dotnetSdkVersion: "9.0.308",
                dotnetRuntime: ".NET 9",
                openAiApiKey: null,
                hasGlobalJson: false);
            string state = Path.Combine(root, "state");
            return new Fixture(
                root,
                snapshot,
                new ThreadStore(Path.Combine(state, "threads")),
                new FileConversationStore(Path.Combine(state, "sessions")));
        }

        public void Advance() => current = current.AddMinutes(1);

        public ThreadApplicationService CreateService() => new(_ => Store, _ => Sessions, () => current);

        public ConversationTranscript CreateTranscript(string name)
        {
            ConversationTranscript transcript = ConversationTranscript.Create(name, current);
            transcript.AddUserMessage("Review apiKey=session-secret", current.AddSeconds(1));
            transcript.Messages.Add(new ConversationMessage(
                "assistant", current.AddSeconds(2), "Safe answer", "openai", "model", "response"));
            transcript.AddToolCall(new ConversationToolCall(
                current.AddSeconds(3),
                "call-1",
                "read_file",
                "{\"token\":\"secret-tool-argument\"}",
                "not-required",
                current.AddSeconds(4),
                true,
                "Read completed apiKey=tool-secret",
                null,
                null,
                false));
            transcript.AddAgentRun(new ConversationAgentRun(
                current.AddSeconds(5),
                "success",
                "completed",
                null,
                "Completed",
                2,
                1,
                PlanSummary: "Review files",
                ChangedFiles: [new ChangedFileSummary("src/file.cs", "modified", "call-1")],
                TaskReport: new AgentTaskReport(
                    "success",
                    "completed",
                    "raw prompt",
                    "plan",
                    [],
                    [],
                    [],
                    [],
                    [],
                    null,
                    Report: new ExecReportMetadata("agent", true, "private/report.md", "written", null, "done"))));
            return transcript;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch
            {
            }
        }
    }
}
