using System.Text.Json.Nodes;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class PipelineTests
{
    private static readonly DateTimeOffset FixedUtc = DateTimeOffset.Parse("2026-07-13T08:00:00Z");

    [Fact]
    public void Built_in_catalog_has_expected_role_order_and_read_only_boundaries()
    {
        IReadOnlyList<PipelineManifest> pipelines = BuiltInPipelineCatalog.List();

        Assert.Equal(["fix-review-test", "review-test", "security-review"], pipelines.Select(item => item.Name));
        Assert.Equal(
            ["implementer", "reviewer", "tester"],
            pipelines.Single(item => item.Name == "fix-review-test").Steps.Select(step => step.Role));
        Assert.All(
            pipelines.SelectMany(item => item.Steps).Where(step => step.Role is "reviewer" or "security"),
            step =>
            {
                Assert.True(step.Boundary.IsReadOnly);
                Assert.False(step.Boundary.AllowWrites);
                Assert.False(step.Boundary.AllowShell);
                Assert.False(step.Boundary.AllowMcp);
                Assert.False(step.Boundary.AllowMcpDiscovery);
                Assert.Contains("workspace.apply_patch", step.Boundary.DisabledTools);
                Assert.Contains("workspace.run_shell", step.Boundary.DisabledTools);
                Assert.Contains("mcp.*", step.Boundary.DisabledTools);
            });
    }

    [Fact]
    public void Reviewer_or_security_step_rejects_write_capable_boundary()
    {
        PipelineRoleBoundary writeCapable = new(
            IsReadOnly: false,
            AllowWrites: true,
            AllowShell: true,
            AllowMcp: true,
            AllowMcpDiscovery: true,
            DisabledTools: [],
            Summary: "write capable");

        Assert.Throws<ArgumentException>(() => new PipelineRoleStep(
            "review",
            "reviewer",
            PipelineCommandFamily.Exec,
            "reviewer",
            "review",
            writeCapable));
        Assert.Throws<ArgumentException>(() => new PipelineRoleStep(
            "security",
            "security",
            PipelineCommandFamily.Exec,
            "security",
            "audit",
            writeCapable));
    }

    [Fact]
    public void Final_report_schema_has_stable_v1_contract()
    {
        JsonObject schema = Assert.IsType<JsonObject>(JsonNode.Parse(PipelineJsonSchema.CreateFinalReportSchema()));

        Assert.Equal("https://c-aicli.local/schemas/pipeline-final-report.v1.json", schema["$id"]?.GetValue<string>());
        Assert.Equal(1, schema["properties"]?["schemaVersion"]?["const"]?.GetValue<int>());
        Assert.Contains("roles", Assert.IsType<JsonArray>(schema["required"]).Select(node => node?.GetValue<string>()));
        Assert.Contains("remainingRisks", Assert.IsType<JsonArray>(schema["required"]).Select(node => node?.GetValue<string>()));
        Assert.Contains("redaction", Assert.IsType<JsonArray>(schema["required"]).Select(node => node?.GetValue<string>()));
    }

    [Fact]
    public void Runner_executes_roles_in_order_and_merges_job_evidence()
    {
        PipelineManifest pipeline = Get("fix-review-test");
        PipelinePlan plan = new(pipeline, "Fix tests", "C:\\workspace");
        List<PipelineRoleExecutionRequest> requests = [];
        DelegatePipelineRoleExecutor executor = new(request =>
        {
            requests.Add(request);
            return Complete(request, succeeded: true);
        });

        PipelineFinalReport report = new PipelineRunner(executor, () => FixedUtc).Run(plan);

        Assert.Equal(PipelineStatus.Succeeded, report.Status);
        Assert.Equal(PipelineStopReason.Completed, report.StopReason);
        Assert.Equal(["implementer", "reviewer", "tester"], requests.Select(request => request.Step.Role));
        Assert.Equal(3, report.Roles.Count);
        Assert.Equal(3, report.Artifacts.Count);
        Assert.All(report.Roles, role =>
        {
            Assert.Equal(TaskQueueStatus.Succeeded, role.Status);
            Assert.NotNull(role.JobId);
            Assert.Single(role.TaskReport!.Commands);
            Assert.Single(role.TaskReport.Verification);
        });
        Assert.Contains("Prior role evidence:", requests[1].Task, StringComparison.Ordinal);
        Assert.Contains(report.Roles[0].JobId!, requests[1].Task, StringComparison.Ordinal);
        Assert.Contains("implementer: risk-implementer", report.RemainingRisks);
    }

    [Fact]
    public void Runner_short_circuits_and_preserves_prior_artifacts_and_risks()
    {
        PipelinePlan plan = new(Get("fix-review-test"), "Fix tests", "C:\\workspace");
        int calls = 0;
        DelegatePipelineRoleExecutor executor = new(request =>
        {
            calls++;
            return Complete(request, succeeded: request.Step.Role != "reviewer");
        });

        PipelineFinalReport report = new PipelineRunner(executor, () => FixedUtc).Run(plan);

        Assert.Equal(PipelineStatus.Failed, report.Status);
        Assert.Equal(PipelineStopReason.RoleFailed, report.StopReason);
        Assert.Equal(2, calls);
        Assert.Equal(["implementer", "reviewer"], report.Roles.Select(role => role.Role));
        Assert.Equal(2, report.Artifacts.Count);
        Assert.Contains(report.Artifacts, artifact => artifact.Role == "implementer");
        Assert.Contains(report.RemainingRisks, risk => risk.Contains("risk-implementer", StringComparison.Ordinal));
        Assert.Contains(report.RemainingRisks, risk => risk.Contains("tester", StringComparison.Ordinal));
        Assert.Contains(report.Warnings, warning => warning.Contains("skipped roles: tester", StringComparison.Ordinal));
    }

    private static PipelineManifest Get(string name)
    {
        Assert.True(BuiltInPipelineCatalog.TryGet(name, out PipelineManifest? pipeline));
        return Assert.IsType<PipelineManifest>(pipeline);
    }

    private static PipelineRoleExecutionResult Complete(PipelineRoleExecutionRequest request, bool succeeded)
    {
        DateTimeOffset now = FixedUtc.AddSeconds(request.StepIndex);
        string queueId = TaskQueueIdGenerator.Create(now);
        string jobId = JobIdGenerator.Create(now);
        TaskQueueRequest queueRequest = new(
            request.Step.CommandFamily == PipelineCommandFamily.Skill ? TaskQueueCommandFamily.Skill : TaskQueueCommandFamily.Exec,
            request.Task,
            request.Plan.WorkspaceRoot,
            Skill: request.Step.Skill,
            Expert: request.Step.CommandFamily == PipelineCommandFamily.Exec ? request.Step.Expert : null);
        TaskQueueItem item = TaskQueueItem.CreatePending(queueId, now, queueRequest)
            .StartAttempt(now)
            .CompleteAttempt(
                now,
                succeeded ? 0 : 1,
                jobId,
                succeeded ? null : "fake-role-failed",
                succeeded ? "completed" : "failed");
        JobTaskReportSummary taskReport = new(
            succeeded ? "success" : "failure",
            succeeded ? "completed" : "tool-failure",
            succeeded ? "completed" : "failed",
            succeeded ? null : "fake-role-failed",
            ChangedFileCount: request.Step.Role == "implementer" ? 1 : 0,
            CommandCount: 1,
            VerificationCount: 1,
            RiskCount: 1,
            ReferenceCount: 0,
            SecretPresenceCount: 0,
            ChangedFiles: request.Step.Role == "implementer" ? ["src/file.cs status=modified"] : [],
            VerificationStatuses: [succeeded ? "passed" : "failed"],
            Risks: [$"risk-{request.Step.Role}"],
            Commands: ["source=workspace.run_shell command=dotnet test"],
            Verification: [$"status={(succeeded ? "passed" : "failed")} source=workspace.run_shell command=dotnet test"]);
        JobRecord job = JobRecord.CreateRunning(
                jobId,
                now,
                new JobCommandSummary("exec", request.Task, Expert: request.Step.Expert))
            .WithStatus(
                succeeded ? JobStatus.Succeeded : JobStatus.Failed,
                now,
                succeeded ? 0 : 1,
                succeeded ? "completed" : "tool-failure",
                succeeded ? null : "fake-role-failed",
                succeeded ? "completed" : "failed",
                taskReport,
                [new JobArtifact(JobArtifactKind.TaskReport, "inline:taskReport", true, $"role={request.Step.Role}")]);
        return new PipelineRoleExecutionResult(item, job);
    }
}
