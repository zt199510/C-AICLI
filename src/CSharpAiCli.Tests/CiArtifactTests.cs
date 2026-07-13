using System.Text.Json;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class CiArtifactTests
{
    [Fact]
    public void Schema_defines_strict_summary_check_annotation_and_redaction_contract()
    {
        string schema = CiArtifactJsonSchema.Render();

        Assert.Contains("https://c-aicli.local/schemas/ci-artifact.v1.json", schema, StringComparison.Ordinal);
        Assert.Contains("\"additionalProperties\": false", schema, StringComparison.Ordinal);
        Assert.Contains("\"annotations\"", schema, StringComparison.Ordinal);
        Assert.Contains("\"recommendedExitCode\"", schema, StringComparison.Ordinal);
        Assert.Contains("\"rawReferencesStored\"", schema, StringComparison.Ordinal);
        Assert.Contains("\"const\": false", schema, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(JobStatus.Succeeded, 0, CiCheckOutcome.Success, 0)]
    [InlineData(JobStatus.Running, null, CiCheckOutcome.Warning, 0)]
    [InlineData(JobStatus.Failed, 1, CiCheckOutcome.Failure, 1)]
    [InlineData(JobStatus.Canceled, 1, CiCheckOutcome.Failure, 1)]
    [InlineData("unexpected", null, CiCheckOutcome.ConfigError, 2)]
    public void Renderer_maps_job_state_to_deterministic_outcome_and_exit_code(
        string status,
        int? jobExitCode,
        string expectedOutcome,
        int expectedExitCode)
    {
        JobRecord record = CreateRecord(status, jobExitCode);

        CiArtifact artifact = new CiArtifactRenderer().Render(record);

        Assert.Equal(expectedOutcome, artifact.Check.Outcome);
        Assert.Equal(expectedExitCode, artifact.Check.RecommendedExitCode);
        Assert.Equal(expectedExitCode, CiExitCodePolicy.Evaluate(artifact));
    }

    [Fact]
    public void Fail_on_policy_promotes_only_selected_warning_threshold()
    {
        JobTaskReportSummary taskReport = new(
            Status: "success",
            StopReason: "completed",
            Summary: "done",
            ErrorCode: null,
            ChangedFileCount: 0,
            CommandCount: 0,
            VerificationCount: 1,
            RiskCount: 1,
            ReferenceCount: 0,
            SecretPresenceCount: 0,
            Risks: ["review deployment settings"]);
        JobRecord record = CreateRecord(JobStatus.Succeeded, 0, taskReport: taskReport);
        CiArtifact artifact = new CiArtifactRenderer().Render(record);

        Assert.Equal(CiCheckOutcome.Warning, artifact.Check.Outcome);
        Assert.Equal(0, CiExitCodePolicy.Evaluate(artifact, CiFailOn.None));
        Assert.Equal(1, CiExitCodePolicy.Evaluate(artifact, CiFailOn.Risks));
        Assert.Equal(1, CiExitCodePolicy.Evaluate(artifact, CiFailOn.Warnings));
        Assert.Equal(2, CiExitCodePolicy.Evaluate(artifact, "invalid"));
        CiArtifact applied = CiExitCodePolicy.Apply(artifact, CiFailOn.Risks);
        Assert.Equal(1, applied.Check.RecommendedExitCode);
        Assert.EndsWith("failOn=risks", applied.Check.Policy, StringComparison.Ordinal);
    }

    [Fact]
    public void Json_and_markdown_renderers_are_stable_and_parseable()
    {
        JobRecord record = CreateRecord(JobStatus.Succeeded, 0);
        CiArtifact artifact = new CiArtifactRenderer().Render(record);
        using StringWriter jsonOutput = new();

        new CiJsonRenderer(jsonOutput).Write(artifact);
        using JsonDocument document = JsonDocument.Parse(jsonOutput.ToString());
        string markdown = new CiMarkdownRenderer().Render(artifact);

        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(CiArtifactType.Summary, document.RootElement.GetProperty("type").GetString());
        Assert.Equal(record.UpdatedAtUtc, document.RootElement.GetProperty("generatedAtUtc").GetDateTimeOffset());
        Assert.Contains("# C-AICLI CI summary", markdown, StringComparison.Ordinal);
        Assert.Contains("Outcome: **success**", markdown, StringComparison.Ordinal);
        Assert.EndsWith("\n", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Renderer_excludes_raw_source_fields_and_redacts_exported_metadata()
    {
        const string rawReference = "RAW_REFERENCE_SENTINEL";
        const string rawToolArguments = "RAW_TOOL_ARGUMENTS_SENTINEL";
        const string fullDiff = "FULL_DIFF_SENTINEL";
        const string secret = "ci-plain-secret";
        DateTimeOffset created = DateTimeOffset.Parse("2026-07-13T00:00:00Z");
        JobTaskReportSummary taskReport = new(
            "success",
            "completed",
            $"summary apiKey={secret}",
            null,
            0,
            1,
            1,
            1,
            1,
            1,
            Risks: [$"risk Authorization: Bearer {secret}"],
            Commands: [rawToolArguments],
            Verification: [$"command={rawToolArguments}"]);
        JobRecord record = new(
            JobRecord.CurrentSchemaVersion,
            JobIdGenerator.Create(created),
            JobStatus.Succeeded,
            created,
            created.AddSeconds(1),
            new JobCommandSummary("exec", Task: $"{rawReference} {fullDiff}"),
            CompletedAtUtc: created.AddSeconds(1),
            ExitCode: 0,
            Summary: $"done password={secret}",
            TaskReport: taskReport,
            Artifacts: [new JobArtifact("trace", $"trace?token={secret}", true, $"apiKey={secret}")]);
        CiArtifact artifact = new CiArtifactRenderer().Render(record);
        using StringWriter jsonOutput = new();

        new CiJsonRenderer(jsonOutput).Write(artifact);
        string json = jsonOutput.ToString();
        string markdown = new CiMarkdownRenderer().Render(artifact);

        Assert.DoesNotContain(rawReference, json, StringComparison.Ordinal);
        Assert.DoesNotContain(rawToolArguments, json, StringComparison.Ordinal);
        Assert.DoesNotContain(fullDiff, json, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, json, StringComparison.Ordinal);
        Assert.DoesNotContain(rawReference, markdown, StringComparison.Ordinal);
        Assert.DoesNotContain(rawToolArguments, markdown, StringComparison.Ordinal);
        Assert.DoesNotContain(fullDiff, markdown, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, markdown, StringComparison.Ordinal);
        Assert.Contains("[redacted]", json, StringComparison.Ordinal);
        Assert.True(artifact.Redaction.SecretsRedacted);
        Assert.False(artifact.Redaction.RawReferencesStored);
        Assert.False(artifact.Redaction.RawToolArgumentsStored);
        Assert.False(artifact.Redaction.FullDiffStored);
    }

    [Fact]
    public void Unsafe_source_storage_boundary_returns_config_error_without_artifact_pointers()
    {
        JobRedactionSummary unsafeBoundary = new(
            SecretsRedacted: false,
            RawReferencesStored: true,
            RawToolArgumentsStored: true,
            FullDiffStored: true,
            Policy: "unsafe");
        JobRecord record = CreateRecord(
            JobStatus.Succeeded,
            0,
            redaction: unsafeBoundary);

        CiArtifact artifact = new CiArtifactRenderer().Render(record);

        Assert.Equal(CiCheckOutcome.ConfigError, artifact.Check.Outcome);
        Assert.Equal(2, artifact.Check.RecommendedExitCode);
        Assert.Empty(artifact.Artifacts);
        CiAnnotation annotation = Assert.Single(artifact.Annotations);
        Assert.Equal("unsafe-redaction-boundary", annotation.Code);
    }

    internal static JobRecord CreateRecord(
        string status,
        int? exitCode,
        JobTaskReportSummary? taskReport = null,
        IReadOnlyList<string>? warnings = null,
        JobRedactionSummary? redaction = null,
        string? jobName = null)
    {
        DateTimeOffset created = DateTimeOffset.Parse("2026-07-13T00:00:00Z");
        JobRecord running = new(
            JobRecord.CurrentSchemaVersion,
            JobIdGenerator.Create(created),
            JobStatus.Running,
            created,
            created,
            new JobCommandSummary("exec", Task: "bounded task", WorkspaceRoot: "C:/work"),
            jobName,
            StartedAtUtc: created,
            Redaction: redaction);
        return running.WithStatus(
            status,
            created.AddSeconds(1),
            exitCode,
            stopReason: status == JobStatus.Succeeded ? "completed" : status,
            errorCode: status == JobStatus.Failed ? "agent-failed" : null,
            summary: status == JobStatus.Failed ? "job failed" : "job completed",
            taskReport: taskReport,
            warnings: warnings);
    }
}
