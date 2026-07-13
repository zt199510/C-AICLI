using System.Text.Json;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class JobRecordTests
{
    [Fact]
    public void Job_id_uses_sortable_stable_format()
    {
        string jobId = JobIdGenerator.Create(DateTimeOffset.Parse("2026-07-13T01:02:03.456Z"));

        Assert.Matches("^job_20260713T010203456Z_[a-f0-9]{8}$", jobId);
        Assert.True(JobIdGenerator.IsValid(jobId));
        Assert.False(JobIdGenerator.IsValid("../secret"));
    }

    [Fact]
    public void Json_schema_describes_v1_job_record_contract()
    {
        string schema = JobRecordJsonSchema.Render();

        Assert.Contains("\"$id\": \"https://c-aicli.local/schemas/job-record.v1.json\"", schema, StringComparison.Ordinal);
        Assert.Contains("\"jobId\"", schema, StringComparison.Ordinal);
        Assert.Contains("\"artifacts\"", schema, StringComparison.Ordinal);
        Assert.Contains("\"redaction\"", schema, StringComparison.Ordinal);
    }

    [Fact]
    public void Job_record_redacts_secret_like_metadata()
    {
        JobRecord record = JobRecord.CreateRunning(
            JobIdGenerator.Create(DateTimeOffset.Parse("2026-07-13T01:02:03Z")),
            DateTimeOffset.Parse("2026-07-13T01:02:03Z"),
            new JobCommandSummary(
                Family: "exec",
                Task: "review apiKey=plain-secret and Authorization: Bearer bearer-secret",
                WorkspaceRoot: "C:/work",
                Cwd: "src",
                Expert: "reviewer"),
            "job password=job-secret");
        JobRecord completed = record.WithStatus(
            JobStatus.Failed,
            DateTimeOffset.Parse("2026-07-13T01:02:04Z"),
            exitCode: 1,
            errorCode: "missing-model",
            summary: "failed with sk-test-secret");

        string json = JsonSerializer.Serialize(completed, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.DoesNotContain("plain-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("bearer-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("job-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-test-secret", json, StringComparison.Ordinal);
        Assert.Contains("[redacted]", json, StringComparison.Ordinal);
        Assert.False(completed.Redaction.RawReferencesStored);
        Assert.False(completed.Redaction.RawToolArgumentsStored);
        Assert.False(completed.Redaction.FullDiffStored);
    }
}
