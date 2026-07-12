using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class AgentTaskReportTests
{
    [Fact]
    public void Build_success_report_captures_core_fields_and_secret_presence_without_values()
    {
        WorkspaceContext workspace = CreateWorkspace();
        AgentRunRequest request = new(
            Prompt: "update project apiKey=prompt-secret",
            Workspace: workspace);
        ConversationToolCall shellCall = new(
            CreatedAtUtc: DateTimeOffset.Parse("2024-01-01T00:00:01Z"),
            CallId: "call_shell",
            ToolName: "workspace.run_shell",
            ArgumentsJson: """{"command":"dotnet test","cwd":"."}""",
            ApprovalStatus: "approved",
            CompletedAtUtc: DateTimeOffset.Parse("2024-01-01T00:00:02Z"),
            Succeeded: true,
            OutputSummary: "passed",
            FailureReason: null,
            ErrorCode: null,
            Retryable: false);
        AgentRunResult result = AgentRunResult.Success(
            "done password=result-secret",
            [shellCall],
            [
                new AgentRunEvent(
                    Type: "plan",
                    Sequence: 0,
                    Timestamp: DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                    Summary: "Goal: edit apiKey=plan-secret",
                    Payload: new Dictionary<string, string>
                    {
                        ["risks"] = "token=plan-risk-secret;manual review"
                    })
            ],
            changedFiles:
            [
                new ChangedFileSummary(
                    Path: "src/App.cs",
                    Status: "modified",
                    SourceToolCallId: "call_patch",
                    DiffStat: "1 file changed apiKey=diff-secret")
            ],
            verificationResults:
            [
                new VerificationResultSummary(
                    Status: "success",
                    Source: "project-instructions",
                    Command: "dotnet test",
                    WorkingDirectory: ".",
                    Succeeded: true,
                    ApprovalStatus: "approved",
                    ErrorCode: null,
                    ExitCode: 0,
                    TimedOut: false,
                    StdoutTruncated: false,
                    StderrTruncated: false,
                    Stdout: "passed",
                    Stderr: "",
                    Summary: "Shell command completed password=verification-secret.")
            ]);

        AgentTaskReport report = AgentTaskReportBuilder.Build(
            request,
            result,
            tracePath: "D:/trace.log",
            reviewGate: new AgentTaskReviewGateReport(
                Status: "success",
                Summary: "diff summary access_token=review-secret",
                HasDiff: true,
                Truncated: false));

        Assert.Equal("success", report.Status);
        Assert.Equal("completed", report.StopReason);
        Assert.Contains("[secret-present]", report.Prompt, StringComparison.Ordinal);
        Assert.Contains("[secret-present]", report.Plan, StringComparison.Ordinal);
        Assert.Equal("workspace.run_shell", Assert.Single(report.Tools));
        Assert.Equal("src/App.cs", Assert.Single(report.ChangedFiles).Path);
        Assert.Contains("[secret-present]", report.ChangedFiles[0].DiffStat, StringComparison.Ordinal);
        Assert.Equal("dotnet test", Assert.Single(report.Commands).Command);
        Assert.Equal("success", Assert.Single(report.Verification).Status);
        Assert.Contains("manual review", report.Risks);
        Assert.Contains("[secret-present]", report.ReviewGate?.Summary, StringComparison.Ordinal);
        Assert.Contains(report.Secrets, secret => secret.Source == "prompt");
        Assert.Contains(report.Secrets, secret => secret.Source == "summary");

        string flattened = string.Join(
            Environment.NewLine,
            report.Prompt,
            report.Plan,
            report.Summary,
            report.ChangedFiles[0].DiffStat,
            report.Verification[0].Summary,
            report.ReviewGate?.Summary,
            string.Join(Environment.NewLine, report.Risks));
        Assert.DoesNotContain("prompt-secret", flattened, StringComparison.Ordinal);
        Assert.DoesNotContain("result-secret", flattened, StringComparison.Ordinal);
        Assert.DoesNotContain("plan-secret", flattened, StringComparison.Ordinal);
        Assert.DoesNotContain("diff-secret", flattened, StringComparison.Ordinal);
        Assert.DoesNotContain("verification-secret", flattened, StringComparison.Ordinal);
        Assert.DoesNotContain("review-secret", flattened, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_no_change_report_records_empty_sections_and_task_report_event()
    {
        AgentRunRequest request = new("inspect workspace", CreateWorkspace());
        AgentRunResult result = AgentRunResult.Success("nothing changed", [], []);

        AgentTaskReport report = AgentTaskReportBuilder.Build(request, result);
        AgentRunEvent taskReportEvent = AgentTaskReportBuilder.CreateTaskReportEvent(
            report,
            sequence: 3,
            timestampUtc: DateTimeOffset.Parse("2024-01-01T00:00:03Z"));

        Assert.Empty(report.ChangedFiles);
        Assert.Empty(report.Commands);
        Assert.Empty(report.Verification);
        Assert.Empty(report.Risks);
        Assert.Equal("taskReport", taskReportEvent.Type);
        Assert.Equal("0", taskReportEvent.Payload?["changedFileCount"]);
        Assert.Equal("0", taskReportEvent.Payload?["commandCount"]);
        Assert.Equal("0", taskReportEvent.Payload?["verificationCount"]);
    }

    [Fact]
    public void Build_failed_report_records_error_and_remaining_risks()
    {
        AgentFailureSummary failureSummary = new(
            FailureKind: AgentFailureKind.Verification,
            StopReason: AgentStopReason.RetryBudgetExhausted,
            ErrorCode: "agent-retry-budget-exhausted",
            Message: "Agent retry budget was exhausted.",
            RetryBudget: 1,
            RetryCount: 1,
            RemainingRetries: 0,
            RemainingRisk: "Review changed files before continuing.",
            Commands: ["dotnet test"],
            ChangedFiles: ["src/App.cs"]);
        AgentRunResult result = AgentRunResult.Failure(
            new AgentError(
                "agent-retry-budget-exhausted",
                "Agent retry budget was exhausted.",
                Retryable: false),
            [],
            [],
            changedFiles:
            [
                new ChangedFileSummary(
                    Path: "src/App.cs",
                    Status: "modified",
                    SourceToolCallId: "call_patch")
            ],
            verificationResults:
            [
                new VerificationResultSummary(
                    Status: "failure",
                    Source: "project-instructions",
                    Command: "dotnet test",
                    WorkingDirectory: ".",
                    Succeeded: false,
                    ApprovalStatus: "approved",
                    ErrorCode: ToolErrorCode.ShellExitCode,
                    ExitCode: 1,
                    TimedOut: false,
                    StdoutTruncated: false,
                    StderrTruncated: false,
                    Stdout: "",
                    Stderr: "failed",
                    Summary: "Shell command failed.")
            ],
            failureSummary: failureSummary,
            stopReason: AgentStopReason.RetryBudgetExhausted);

        AgentTaskReport report = AgentTaskReportBuilder.Build(
            new AgentRunRequest("fix tests", CreateWorkspace()),
            result);

        Assert.Equal("failure", report.Status);
        Assert.Equal("agent-retry-budget-exhausted", report.ErrorCode);
        Assert.Contains("Review changed files before continuing.", report.Risks);
        Assert.Contains("Verification did not succeed: failure", report.Risks);
        Assert.Equal("dotnet test", Assert.Single(report.Commands).Command);
        Assert.Equal("failure", Assert.Single(report.Verification).Status);
    }

    private static WorkspaceContext CreateWorkspace()
    {
        return new WorkspaceContext(
            RootPath: Path.GetTempPath(),
            ConfigPath: Path.Combine(Path.GetTempPath(), ".caicli", "config.json"),
            Status: WorkspaceStatus.Ready);
    }
}
