using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class MarkdownTaskReportRendererTests
{
    [Fact]
    public void Render_writes_expected_sections_and_metadata_without_raw_reference_content()
    {
        AgentTaskReport report = new(
            Status: "success",
            StopReason: "completed",
            Prompt: "review @file:secret.txt apiKey=prompt-secret",
            Plan: "Read referenced metadata only.",
            Tools: ["workspace.read_text"],
            ChangedFiles: [],
            Commands:
            [
                new AgentTaskCommandReport(
                    Source: "verification",
                    Command: "dotnet test --password command-secret",
                    Status: "success")
            ],
            Verification:
            [
                new AgentTaskVerificationReport(
                    Status: "success",
                    Source: "project-instructions",
                    Command: "dotnet test",
                    WorkingDirectory: ".",
                    Succeeded: true,
                    ApprovalStatus: "approved",
                    ErrorCode: null,
                    ExitCode: 0,
                    TimedOut: false,
                    Summary: "passed")
            ],
            Risks: ["risk token=risk-secret"],
            TracePath: "D:/trace.log",
            Secrets: [new AgentTaskSecretPresence("prompt", "key-value")],
            References:
            [
                new AgentTaskReferenceReport(
                    Kind: "file",
                    RequestedPath: "secret.txt",
                    ResolvedPath: "secret.txt",
                    Status: "included",
                    IncludedFileCount: 1,
                    SkippedFileCount: 0,
                    ByteCount: 20,
                    Truncated: false,
                    Warnings: [])
            ],
            Summary: "done",
            ReviewGate: new AgentTaskReviewGateReport(
                Status: "success",
                Summary: "No diff.",
                HasDiff: false,
                Truncated: false),
            Expert: new AgentTaskExpertReport(
                Name: "security",
                DisplayName: "Security",
                ToolBoundary: "read-only",
                BoundarySummary: "read-only tools only",
                ReportFocus: "risks and secrets"),
            Report: new ExecReportMetadata(
                Mode: "markdown",
                Generated: true,
                Path: ".caicli/reports/run.md",
                WriteStatus: "written"),
            WorkspaceRoot: "D:/workspace",
            SessionName: "smoke");

        string markdown = new MarkdownTaskReportRenderer().Render(report);

        Assert.Contains("# C# AI CLI Task Report", markdown, StringComparison.Ordinal);
        foreach (string heading in new[]
        {
            "## Summary",
            "## Prompt",
            "## Workspace",
            "## Expert",
            "## References",
            "## Plan",
            "## Changed Files",
            "## Commands",
            "## Verification",
            "## Review Gate",
            "## Remaining Risks",
            "## Trace And Session",
            "## Redaction",
            "## Report"
        })
        {
            Assert.Contains(heading, markdown, StringComparison.Ordinal);
        }

        Assert.Contains("- Selected: security", markdown, StringComparison.Ordinal);
        Assert.Contains("secret.txt", markdown, StringComparison.Ordinal);
        Assert.Contains("- Path: .caicli/reports/run.md", markdown, StringComparison.Ordinal);
        Assert.Contains("[redacted]", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("prompt-secret", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("command-secret", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("risk-secret", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("raw referenced content", markdown, StringComparison.Ordinal);
    }
}

