using System.Security.Cryptography;
using System.Text;
using CSharpAiCli.Application;
using CSharpAiCli.Core;
using CSharpAiCli.ProjectPacks.Runtime;

namespace CSharpAiCli.Tests;

public sealed class ApplicationQueryServiceTests
{
    [Fact]
    public void Report_list_and_get_reuse_job_and_session_truth()
    {
        using TempDirectory temp = TempDirectory.Create();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(temp);
        JobRecordStore jobs = JobRecordStore.Create(snapshot);
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-16T01:02:03Z");
        JobRecord job = JobRecord.CreateRunning(
            JobIdGenerator.Create(now),
            now,
            new JobCommandSummary("exec", Task: "review", WorkspaceRoot: temp.Workspace));
        jobs.Create(job.WithStatus(
            JobStatus.Succeeded,
            now.AddSeconds(1),
            exitCode: 0,
            stopReason: "completed",
            summary: "job summary apiKey=job-secret"));

        ConversationTranscript transcript = ConversationTranscript.Create("review", now);
        transcript.AddAgentRun(new ConversationAgentRun(
            now.AddSeconds(2),
            "success",
            "completed",
            null,
            "session summary",
            1,
            0,
            TaskReport: new AgentTaskReport(
                "success",
                "completed",
                "prompt",
                null,
                [],
                [],
                [new AgentTaskCommandReport("verification", "dotnet test")],
                [],
                ["apiKey=session-secret"],
                null,
                Summary: "session details apiKey=session-secret")));
        FakeConversationStore sessions = new(transcript);
        ReportApplicationService service = new(_ => jobs, _ => sessions);

        ApplicationResult<ReportListProjection> list = service.List(new ReportListRequest(snapshot, 10));
        ApplicationResult<ReportDetailProjection> jobDetail = service.Get(
            new ReportGetRequest(snapshot, "job:" + job.JobId));
        ApplicationResult<ReportDetailProjection> sessionDetail = service.Get(
            new ReportGetRequest(snapshot, "session:review"));

        Assert.Equal(2, list.Data?.Reports.Count);
        Assert.Equal(job.JobId, jobDetail.Data?.Metadata.SourceId);
        Assert.Equal("review", sessionDetail.Data?.Metadata.SourceId);
        Assert.Contains("dotnet test", sessionDetail.Data?.Commands ?? []);
        string serialized = System.Text.Json.JsonSerializer.Serialize(new { list, jobDetail, sessionDetail });
        Assert.DoesNotContain("job-secret", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("session-secret", serialized, StringComparison.Ordinal);
        Assert.Contains("[redacted]", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void Report_query_distinguishes_missing_corrupt_and_cancelled_state()
    {
        using TempDirectory temp = TempDirectory.Create();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(temp);
        JobRecordStore jobs = JobRecordStore.Create(snapshot);
        Directory.CreateDirectory(jobs.JobDirectory);
        File.WriteAllText(
            Path.Combine(jobs.JobDirectory, "job_20260716T010203000Z_deadbeef.job.json"),
            "{ invalid");
        ReportApplicationService service = new(_ => jobs, _ => new ThrowingConversationStore());

        ApplicationResult<ReportListProjection> list = service.List(new ReportListRequest(snapshot));
        ApplicationResult<ReportDetailProjection> missing = service.Get(
            new ReportGetRequest(snapshot, "job:missing"));
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Assert.Contains(list.Diagnostics, diagnostic => diagnostic.Category == ApplicationErrorCategory.CorruptState);
        Assert.Contains(list.Diagnostics, diagnostic => diagnostic.Code == "session-transcript-invalid");
        Assert.Equal(ApplicationErrorCategory.NotFound, missing.Error?.Category);
        Assert.Throws<OperationCanceledException>(() =>
            service.List(new ReportListRequest(snapshot), cancellation.Token));
    }

    [Fact]
    public void Report_get_redacts_and_truncates_oversize_session_summary()
    {
        using TempDirectory temp = TempDirectory.Create();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(temp);
        string oversize = "apiKey=report-oversize-secret " +
            new string('x', ApplicationLimits.MaxReportSummaryBytes * 2);
        ConversationTranscript transcript = ConversationTranscript.Create(
            "oversize",
            DateTimeOffset.Parse("2026-07-16T01:02:03Z"));
        transcript.AddAgentRun(new ConversationAgentRun(
            DateTimeOffset.Parse("2026-07-16T01:02:04Z"),
            "success",
            "completed",
            null,
            "done",
            1,
            0,
            TaskReport: new AgentTaskReport(
                "success",
                "completed",
                "prompt",
                null,
                [],
                [],
                [],
                [],
                [],
                null,
                Summary: oversize)));
        ReportApplicationService service = new(
            _ => JobRecordStore.Create(snapshot),
            _ => new FakeConversationStore(transcript));

        ApplicationResult<ReportDetailProjection> result = service.Get(
            new ReportGetRequest(snapshot, "session:oversize"));

        Assert.True(result.Succeeded);
        Assert.True(result.Truncated);
        Assert.True(result.Data?.SummaryTruncated);
        Assert.DoesNotContain("report-oversize-secret", result.Data?.Summary, StringComparison.Ordinal);
        Assert.True(Encoding.UTF8.GetByteCount(result.Data?.Summary ?? string.Empty) <=
            ApplicationLimits.MaxReportSummaryBytes);
    }

    [Fact]
    public void Artifact_list_and_get_return_metadata_without_reading_content()
    {
        using ArtifactFixture fixture = ArtifactFixture.Create();
        ArtifactApplicationService service = new(_ => new ManagedArtifactStore(fixture.Store));
        using FileStream exclusiveContentLock = new(
            fixture.ArtifactPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);

        ApplicationResult<ArtifactListProjection> list = service.List(
            new ArtifactListRequest(fixture.Snapshot, RunId: fixture.RunId));
        string artifactId = Assert.Single(list.Data!.Artifacts).ArtifactId;
        ApplicationResult<ArtifactMetadataProjection> get = service.Get(
            new ArtifactGetRequest(fixture.Snapshot, artifactId));

        Assert.True(list.Succeeded);
        Assert.True(get.Succeeded);
        Assert.Equal(fixture.RunId, get.Data?.Owner.RunId);
        Assert.Equal("reports/result.json", get.Data?.RelativePath);
        Assert.Equal(fixture.Sha256, get.Data?.Sha256);
        Assert.Equal(ManagedArtifactAvailability.Available, get.Data?.Availability);
    }

    [Fact]
    public void Artifact_query_preserves_store_diagnostics_bounds_and_cancellation()
    {
        using ArtifactFixture fixture = ArtifactFixture.Create();
        ArtifactApplicationService service = new(_ => new ManagedArtifactStore(fixture.Store));
        File.WriteAllText(fixture.Store.GetLayout(fixture.RunId).ArtifactManifestPath, "{ invalid");

        ApplicationResult<ArtifactListProjection> list = service.List(
            new ArtifactListRequest(fixture.Snapshot, PageSize: 1));
        ApplicationResult<ArtifactMetadataProjection> missing = service.Get(
            new ArtifactGetRequest(fixture.Snapshot, "artifact_000000000000000000000000"));
        ApplicationResult<ArtifactListProjection> invalidPage = service.List(
            new ArtifactListRequest(fixture.Snapshot, PageSize: 0));
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Assert.Contains(list.Diagnostics, diagnostic => diagnostic.Code == ManagedArtifactErrorCode.ManifestCorrupt);
        Assert.Equal(ApplicationErrorCategory.NotFound, missing.Error?.Category);
        Assert.Equal(ApplicationErrorCategory.Validation, invalidPage.Error?.Category);
        Assert.Throws<OperationCanceledException>(() =>
            service.List(new ArtifactListRequest(fixture.Snapshot), cancellation.Token));
    }

    private static CliEnvironmentSnapshot CreateSnapshot(TempDirectory temp) => CliEnvironmentSnapshot.Create(
        temp.Workspace,
        currentDirectory: temp.Workspace,
        userProfile: temp.Profile,
        dotnetSdkVersion: "9.0.308",
        dotnetRuntime: ".NET 9",
        openAiApiKey: null,
        hasGlobalJson: false);

    private sealed class FakeConversationStore(ConversationTranscript transcript) : IConversationStore
    {
        public IReadOnlyList<ConversationTranscriptSummary> ListSummaries() =>
            [ConversationTranscriptSummary.FromTranscript(transcript)];

        public bool TryLoad(ConversationSessionName sessionName, out ConversationTranscript? loaded)
        {
            loaded = sessionName.Value == transcript.SessionName ? transcript : null;
            return loaded is not null;
        }

        public ConversationTranscript LoadOrCreate(ConversationSessionName sessionName, DateTimeOffset nowUtc) => transcript;

        public string Save(ConversationSessionName sessionName, ConversationTranscript value) => string.Empty;
    }

    private sealed class ThrowingConversationStore : IConversationStore
    {
        public IReadOnlyList<ConversationTranscriptSummary> ListSummaries() =>
            throw new InvalidOperationException("unsafe parser detail");

        public ConversationTranscript LoadOrCreate(ConversationSessionName sessionName, DateTimeOffset nowUtc) =>
            throw new NotSupportedException();

        public string Save(ConversationSessionName sessionName, ConversationTranscript transcript) =>
            throw new NotSupportedException();
    }

    private sealed class ArtifactFixture : IDisposable
    {
        private readonly TempDirectory temp;

        private ArtifactFixture(
            TempDirectory temp,
            CliEnvironmentSnapshot snapshot,
            ManagedProjectPackRunStore store,
            string runId,
            string artifactPath,
            string sha256)
        {
            this.temp = temp;
            Snapshot = snapshot;
            Store = store;
            RunId = runId;
            ArtifactPath = artifactPath;
            Sha256 = sha256;
        }

        public CliEnvironmentSnapshot Snapshot { get; }

        public ManagedProjectPackRunStore Store { get; }

        public string RunId { get; }

        public string ArtifactPath { get; }

        public string Sha256 { get; }

        public static ArtifactFixture Create()
        {
            TempDirectory temp = TempDirectory.Create();
            CliEnvironmentSnapshot snapshot = CreateSnapshot(temp);
            ManagedProjectPackRunStore store = ManagedProjectPackRunStore.Create(snapshot);
            DateTimeOffset now = DateTimeOffset.Parse("2026-07-16T02:03:04Z");
            string runId = ProjectPackRunId.Create(now);
            string planHash = Hash("plan");
            string policyHash = Hash("policy");
            ProjectPackRunRecord record = new(
                ProjectPackRunRecord.CurrentSchemaVersion,
                runId,
                0,
                "gerber-tiff",
                "1.0.0",
                "artifact-test",
                planHash,
                policyHash,
                ProjectPackRunState.Created,
                now,
                now);
            ProjectPackRunCheckpoint checkpoint = new(
                ProjectPackRunCheckpoint.CurrentSchemaVersion,
                runId,
                0,
                ProjectPackRunState.Created,
                planHash,
                policyHash,
                now);
            ProjectPackRunMutationResult created = store.CreateRun(record, checkpoint, "{}", "{}");
            Assert.True(created.Succeeded);
            ManagedProjectPackRunLayout layout = store.GetLayout(runId);
            string path = Path.Combine(layout.RunRoot, "reports", "result.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{\"status\":\"ok\"}");
            FileInfo info = new(path);
            string sha256 = Hash(File.ReadAllBytes(path));
            ProjectPackRunArtifactPointer pointer = new(
                "result",
                "json-report",
                "managed-run",
                "reports/result.json",
                true,
                info.Length,
                sha256);
            ProjectPackRunRecord updated = created.Record!.WithArtifacts([pointer], now.AddSeconds(1));
            ProjectPackRunCheckpoint updatedCheckpoint = new(
                ProjectPackRunCheckpoint.CurrentSchemaVersion,
                runId,
                updated.Revision,
                updated.State,
                planHash,
                policyHash,
                now.AddSeconds(1));
            Assert.True(store.Update(updated, updatedCheckpoint, created.Record.Revision).Succeeded);
            return new ArtifactFixture(temp, snapshot, store, runId, path, sha256);
        }

        public void Dispose() => temp.Dispose();

        private static string Hash(string value) => Hash(Encoding.UTF8.GetBytes(value));

        private static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value));
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path)
        {
            Path = path;
            Workspace = System.IO.Path.Combine(path, "workspace");
            Profile = System.IO.Path.Combine(path, "profile");
            Directory.CreateDirectory(Workspace);
            Directory.CreateDirectory(Profile);
        }

        public string Path { get; }

        public string Workspace { get; }

        public string Profile { get; }

        public static TempDirectory Create()
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "caicli-application-query-tests-" + Guid.NewGuid().ToString("N"));
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
