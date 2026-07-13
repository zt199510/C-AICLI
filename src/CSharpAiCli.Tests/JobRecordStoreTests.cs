using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class JobRecordStoreTests
{
    [Fact]
    public void List_returns_empty_for_missing_directory()
    {
        using TempDirectory temp = TempDirectory.Create();
        JobRecordStore store = new(Path.Combine(temp.Path, "jobs"));

        JobRecordListResult result = store.List();

        Assert.Empty(result.Records);
        Assert.Empty(result.Diagnostics);
        Assert.False(Directory.Exists(store.JobDirectory));
    }

    [Fact]
    public void Create_update_read_and_list_round_trip_record()
    {
        using TempDirectory temp = TempDirectory.Create();
        JobRecordStore store = new(Path.Combine(temp.Path, "jobs"));
        DateTimeOffset created = DateTimeOffset.Parse("2026-07-13T01:02:03Z");
        JobRecord record = JobRecord.CreateRunning(
            JobIdGenerator.Create(created),
            created,
            new JobCommandSummary("exec", Task: "summarize", WorkspaceRoot: temp.Path));

        string path = store.Create(record);
        JobRecord completed = record.WithStatus(
            JobStatus.Succeeded,
            DateTimeOffset.Parse("2026-07-13T01:02:04Z"),
            exitCode: 0,
            stopReason: "completed",
            summary: "done");
        store.Update(completed);

        JobRecordReadResult read = store.Read(record.JobId);
        JobRecordListResult list = store.List();

        Assert.True(File.Exists(path));
        Assert.True(read.Succeeded);
        Assert.Equal(JobStatus.Succeeded, read.Record?.Status);
        Assert.Equal(0, read.Record?.ExitCode);
        JobRecord listed = Assert.Single(list.Records);
        Assert.Equal(record.JobId, listed.JobId);
        Assert.Empty(list.Diagnostics);
    }

    [Fact]
    public void List_reports_corrupt_records_and_keeps_valid_records()
    {
        using TempDirectory temp = TempDirectory.Create();
        JobRecordStore store = new(Path.Combine(temp.Path, "jobs"));
        DateTimeOffset created = DateTimeOffset.Parse("2026-07-13T01:02:03Z");
        JobRecord record = JobRecord.CreateRunning(
            JobIdGenerator.Create(created),
            created,
            new JobCommandSummary("exec", Task: "summarize", WorkspaceRoot: temp.Path));
        store.Create(record);
        Directory.CreateDirectory(store.JobDirectory);
        File.WriteAllText(
            Path.Combine(store.JobDirectory, "job_20260713T010204000Z_deadbeef.job.json"),
            "{ not-json");

        JobRecordListResult result = store.List();

        Assert.Single(result.Records);
        JobRecordDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("corrupt-job-record", diagnostic.ErrorCode);
    }

    [Fact]
    public void Read_rejects_invalid_or_missing_job_id_as_not_found()
    {
        using TempDirectory temp = TempDirectory.Create();
        JobRecordStore store = new(Path.Combine(temp.Path, "jobs"));

        Assert.Equal("job-not-found", store.Read("../secret").Diagnostic?.ErrorCode);
        Assert.Equal(
            "job-not-found",
            store.Read("job_20260713T010204000Z_deadbeef").Diagnostic?.ErrorCode);
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempDirectory Create()
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "caicli-job-store-tests-" + Guid.NewGuid().ToString("N"));
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
