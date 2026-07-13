using CSharpAiCli.Core;
using System.Text.RegularExpressions;

namespace CSharpAiCli.Tests;

public sealed class ConversationTranscriptMarkdownFormatterTests
{
    [Fact]
    public void Format_uses_summary_turn_count_in_metadata()
    {
        ConversationTranscript transcript = ConversationTranscript.Create(
            "smoke",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        transcript.AddUserMessage("hello", DateTimeOffset.Parse("2024-01-01T00:00:01Z"));
        transcript.AddAssistantMessage(
            new ChatResponse("openai", "gpt-test", "hello back", "resp_test"),
            DateTimeOffset.Parse("2024-01-01T00:00:02Z"));

        string markdown = ConversationTranscriptMarkdownFormatter.Format(transcript);

        Assert.Contains("- Turns: 1", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("- Turns: 2", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_escapes_hostile_session_name_metadata_outside_fences()
    {
        ConversationTranscript transcript = ConversationTranscript.Create(
            "session <script>alert(1)</script> [click me](https://example.test) ![alt](image.png) # heading - item",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));

        string markdown = ConversationTranscriptMarkdownFormatter.Format(transcript);

        string outsideFences = RemoveFencedBlocks(markdown);
        AssertHostileMetadataIsNotLive(outsideFences);
        Assert.Contains("session", outsideFences, StringComparison.Ordinal);
        Assert.Contains("heading", outsideFences, StringComparison.Ordinal);
        Assert.Contains("item", outsideFences, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_escapes_hostile_message_role_metadata_outside_fences()
    {
        ConversationTranscript transcript = ConversationTranscript.Create(
            "smoke",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        transcript.Messages.Add(new ConversationMessage(
            Role: "operator <div>raw</div> [click me](https://example.test) ![alt](image.png) # heading - item",
            CreatedAtUtc: DateTimeOffset.Parse("2024-01-01T00:00:01Z"),
            Content: "hello",
            Provider: null,
            Model: null,
            ResponseId: null));

        string markdown = ConversationTranscriptMarkdownFormatter.Format(transcript);

        string outsideFences = RemoveFencedBlocks(markdown);
        AssertHostileMetadataIsNotLive(outsideFences);
        Assert.Contains("Operator", outsideFences, StringComparison.Ordinal);
        Assert.Contains("heading", outsideFences, StringComparison.Ordinal);
        Assert.Contains("item", outsideFences, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_escapes_hostile_error_code_metadata_outside_fences()
    {
        ConversationTranscript transcript = ConversationTranscript.Create(
            "smoke",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        transcript.Errors.Add(new ConversationError(
            CreatedAtUtc: DateTimeOffset.Parse("2024-01-01T00:00:02Z"),
            Provider: "openai",
            Operation: "chat",
            StatusCode: 500,
            LocalErrorCode: "model_failed <script>alert(1)</script> [click me](https://example.test) ![alt](image.png) # heading - item",
            SafeMessage: "safe",
            Retryable: false));

        string markdown = ConversationTranscriptMarkdownFormatter.Format(transcript);

        string outsideFences = RemoveFencedBlocks(markdown);
        AssertHostileMetadataIsNotLive(outsideFences);
        Assert.Contains("model_failed", outsideFences, StringComparison.Ordinal);
        Assert.Contains("heading", outsideFences, StringComparison.Ordinal);
        Assert.Contains("item", outsideFences, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_escapes_hostile_tool_name_and_error_code_metadata_outside_fences()
    {
        ConversationTranscript transcript = ConversationTranscript.Create(
            "smoke",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        transcript.AddToolCall(new ConversationToolCall(
            CreatedAtUtc: DateTimeOffset.Parse("2024-01-01T00:00:04Z"),
            CallId: "call_read",
            ToolName: "workspace.read_text <div>raw</div> [click me](https://example.test) ![alt](image.png) # heading - item",
            ArgumentsJson: "{}",
            ApprovalStatus: "approved",
            CompletedAtUtc: DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
            Succeeded: false,
            OutputSummary: null,
            FailureReason: "failed",
            ErrorCode: "tool_failed <script>alert(1)</script> [click me](https://example.test) ![alt](image.png) ## heading * item",
            Retryable: false));

        string markdown = ConversationTranscriptMarkdownFormatter.Format(transcript);

        string outsideFences = RemoveFencedBlocks(markdown);
        AssertHostileMetadataIsNotLive(outsideFences);
        Assert.Contains("workspace.read_text", outsideFences, StringComparison.Ordinal);
        Assert.Contains("tool_failed", outsideFences, StringComparison.Ordinal);
        Assert.Contains("heading", outsideFences, StringComparison.Ordinal);
        Assert.Contains("item", outsideFences, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_fences_multiline_markdown_and_raw_html_message_content()
    {
        ConversationTranscript transcript = ConversationTranscript.Create(
            "smoke",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        transcript.AddUserMessage("""
        # injected heading
        <div onclick="steal()">raw html</div>
        ```
        nested fence
        ```
        """, DateTimeOffset.Parse("2024-01-01T00:00:01Z"));

        string markdown = ConversationTranscriptMarkdownFormatter.Format(transcript);

        Assert.Contains("### User - 2024-01-01T00:00:01.0000000+00:00", markdown, StringComparison.Ordinal);
        string normalizedMarkdown = markdown.Replace("\r\n", "\n", StringComparison.Ordinal);
        string expectedFencedContent = """
        ````
        # injected heading
        <div onclick="steal()">raw html</div>
        ```
        nested fence
        ```
        ````
        """.Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains(expectedFencedContent, normalizedMarkdown, StringComparison.Ordinal);

        string outsideFences = RemoveFencedBlocks(markdown);
        Assert.DoesNotContain("# injected heading", outsideFences, StringComparison.Ordinal);
        Assert.DoesNotContain("<div", outsideFences, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Format_redacts_and_normalizes_tool_summary_without_raw_arguments()
    {
        ConversationTranscript transcript = ConversationTranscript.Create(
            "smoke",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        transcript.AddToolCall(new ConversationToolCall(
            CreatedAtUtc: DateTimeOffset.Parse("2024-01-01T00:00:04Z"),
            CallId: "call_read",
            ToolName: "workspace.read_text",
            ArgumentsJson: """{"apiKey":"raw-argument-secret","path":"note.txt"}""",
            ApprovalStatus: "approved",
            CompletedAtUtc: DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
            Succeeded: true,
            OutputSummary: """
            first api_key=ghp_secret OPENAI_API_KEY=sk-openai-secret
            second Bearer abc123 "apiKey":"json-secret" token=plain password=pass secret=hidden
            """,
            FailureReason: null,
            ErrorCode: null,
            Retryable: false));

        string markdown = ConversationTranscriptMarkdownFormatter.Format(transcript);

        string toolLine = Assert.Single(
            markdown.Split(Environment.NewLine),
            line => line.Contains("workspace.read_text succeeded", StringComparison.Ordinal));
        Assert.DoesNotContain("first api_key=", toolLine, StringComparison.Ordinal);
        Assert.Contains("first api_key=[redacted] OPENAI_API_KEY=[redacted]", markdown, StringComparison.Ordinal);
        Assert.Contains("second Bearer [redacted] \"apiKey\":\"[redacted]\" token=[redacted] password=[redacted] secret=[redacted]", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("ghp_secret", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-openai-secret", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("abc123", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("json-secret", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("raw-argument-secret", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("ArgumentsJson", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_includes_agent_run_changed_files_and_verification_summary()
    {
        ConversationTranscript transcript = ConversationTranscript.Create(
            "smoke",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        transcript.AddAgentRun(new ConversationAgentRun(
            CompletedAtUtc: DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
            Status: "success",
            StopReason: "completed",
            ErrorCode: null,
            Summary: "done",
            EventCount: 4,
            ToolCallCount: 1,
            ChangedFiles:
            [
                new ChangedFileSummary(
                    Path: "src/App.cs",
                    Status: "modified",
                    SourceToolCallId: "call_patch")
            ],
            VerificationResults:
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
                    Summary: "Shell command completed.")
            ]));

        string markdown = ConversationTranscriptMarkdownFormatter.Format(transcript);

        Assert.Contains("changedFiles: src/App.cs (modified)", markdown, StringComparison.Ordinal);
        Assert.Contains("verification: status=success approvalStatus=approved", markdown, StringComparison.Ordinal);
        Assert.Contains("dotnet test", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_includes_agent_run_task_report_summary()
    {
        ConversationTranscript transcript = ConversationTranscript.Create(
            "smoke",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        transcript.AddAgentRun(new ConversationAgentRun(
            CompletedAtUtc: DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
            Status: "success",
            StopReason: "completed",
            ErrorCode: null,
            Summary: "done",
            EventCount: 2,
            ToolCallCount: 0,
            TaskReport: new AgentTaskReport(
                Status: "success",
                StopReason: "completed",
                Prompt: "inspect workspace",
                Plan: null,
                Tools: [],
                ChangedFiles: [],
                Commands: [],
                Verification: [],
                Risks: [],
                TracePath: "D:/trace.log",
                Summary: "nothing changed",
                Expert: new AgentTaskExpertReport(
                    Name: "reviewer",
                    DisplayName: "Reviewer",
                    ToolBoundary: "read-only",
                    BoundarySummary: "read-only tools only",
                    ReportFocus: "findings"),
                Report: new ExecReportMetadata(
                    Mode: "markdown",
                    Generated: true,
                    Path: ".caicli/reports/run.md",
                    WriteStatus: "written"))));

        string markdown = ConversationTranscriptMarkdownFormatter.Format(transcript);

        Assert.Contains("taskReport: status=success stopReason=completed expert=reviewer report=markdown changedFiles=0 commands=0 verification=0 risks=0", markdown, StringComparison.Ordinal);
        Assert.Contains("tracePath: D:/trace.log", markdown, StringComparison.Ordinal);
        Assert.Contains("reportPath: .caicli/reports/run.md", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_redacts_common_oauth_and_cloud_secret_names()
    {
        ConversationTranscript transcript = ConversationTranscript.Create(
            "smoke",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        transcript.AddUserMessage(
            """
            message access_token=access-secret "client_secret":"json-client-secret"
            GOOGLE_API_KEY=google-secret ANTHROPIC_API_KEY=anthropic-secret GITHUB_TOKEN=github-secret AZURE_CLIENT_SECRET=azure-secret
            "GOOGLE_API_KEY":"json-google-secret" \"AZURE_CLIENT_SECRET\":\"escaped-azure-secret\"
            """,
            DateTimeOffset.Parse("2024-01-01T00:00:01Z"));
        transcript.Errors.Add(new ConversationError(
            CreatedAtUtc: DateTimeOffset.Parse("2024-01-01T00:00:02Z"),
            Provider: "openai",
            Operation: "chat",
            StatusCode: 500,
            LocalErrorCode: "model_failed",
            SafeMessage: "error refresh_token=refresh-secret",
            Retryable: false));
        transcript.AddToolCall(new ConversationToolCall(
            CreatedAtUtc: DateTimeOffset.Parse("2024-01-01T00:00:04Z"),
            CallId: "call_read",
            ToolName: "workspace.read_text",
            ArgumentsJson: "{}",
            ApprovalStatus: "approved",
            CompletedAtUtc: DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
            Succeeded: true,
            OutputSummary: "output client_secret=client-secret",
            FailureReason: null,
            ErrorCode: null,
            Retryable: false));
        transcript.AddToolCall(new ConversationToolCall(
            CreatedAtUtc: DateTimeOffset.Parse("2024-01-01T00:00:06Z"),
            CallId: "call_write",
            ToolName: "workspace.write_text",
            ArgumentsJson: "{}",
            ApprovalStatus: "approved",
            CompletedAtUtc: DateTimeOffset.Parse("2024-01-01T00:00:07Z"),
            Succeeded: false,
            OutputSummary: null,
            FailureReason: "failure AWS_SECRET_ACCESS_KEY=aws-secret",
            ErrorCode: "tool_failed",
            Retryable: false));

        string markdown = ConversationTranscriptMarkdownFormatter.Format(transcript);

        Assert.Contains("access_token=[redacted]", markdown, StringComparison.Ordinal);
        Assert.Contains("\"client_secret\":\"[redacted]\"", markdown, StringComparison.Ordinal);
        Assert.Contains("refresh_token=[redacted]", markdown, StringComparison.Ordinal);
        Assert.Contains("client_secret=[redacted]", markdown, StringComparison.Ordinal);
        Assert.Contains("AWS_SECRET_ACCESS_KEY=[redacted]", markdown, StringComparison.Ordinal);
        Assert.Contains("GOOGLE_API_KEY=[redacted]", markdown, StringComparison.Ordinal);
        Assert.Contains("ANTHROPIC_API_KEY=[redacted]", markdown, StringComparison.Ordinal);
        Assert.Contains("GITHUB_TOKEN=[redacted]", markdown, StringComparison.Ordinal);
        Assert.Contains("AZURE_CLIENT_SECRET=[redacted]", markdown, StringComparison.Ordinal);
        Assert.Contains("\"GOOGLE_API_KEY\":\"[redacted]\"", markdown, StringComparison.Ordinal);
        Assert.Contains("\\\"AZURE_CLIENT_SECRET\\\":\\\"[redacted]\\\"", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("access-secret", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("json-client-secret", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("refresh-secret", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("client-secret", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("aws-secret", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("google-secret", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("anthropic-secret", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("github-secret", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("azure-secret", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("json-google-secret", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("escaped-azure-secret", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_redacts_escaped_quotes_inside_json_secret_values()
    {
        ConversationTranscript transcript = ConversationTranscript.Create(
            "smoke",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        transcript.AddUserMessage(
            """payload {\"password\":\"pa\\\"ss\",\"client_secret\":\"cli\\\"ent-secret\"} done""",
            DateTimeOffset.Parse("2024-01-01T00:00:01Z"));

        string markdown = ConversationTranscriptMarkdownFormatter.Format(transcript);

        Assert.Contains("""{\"password\":\"[redacted]\",\"client_secret\":\"[redacted]\"}""", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("""pa\\""", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("""\"ss""", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("""cli\\""", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("ent-secret", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_redacts_escaped_quotes_inside_key_value_secret_values()
    {
        ConversationTranscript transcript = ConversationTranscript.Create(
            "smoke",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        transcript.AddUserMessage(
            """password=\"pa\\\"ss\" access_token=\"tok\\\"en-secret\" done""",
            DateTimeOffset.Parse("2024-01-01T00:00:01Z"));

        string markdown = ConversationTranscriptMarkdownFormatter.Format(transcript);

        Assert.Contains("""password=[redacted] access_token=[redacted] done""", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("""pa\\""", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("""\"ss""", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("""tok\\""", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("en-secret", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_uses_safe_fallbacks_for_null_message_role_and_tool_name()
    {
        ConversationTranscript transcript = ConversationTranscript.Create(
            "smoke",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        transcript.Messages.Add(new ConversationMessage(
            Role: null!,
            CreatedAtUtc: DateTimeOffset.Parse("2024-01-01T00:00:01Z"),
            Content: "hello",
            Provider: null,
            Model: null,
            ResponseId: null));
        transcript.AddToolCall(new ConversationToolCall(
            CreatedAtUtc: DateTimeOffset.Parse("2024-01-01T00:00:04Z"),
            CallId: "call_read",
            ToolName: null!,
            ArgumentsJson: "{}",
            ApprovalStatus: "approved",
            CompletedAtUtc: DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
            Succeeded: true,
            OutputSummary: "ok",
            FailureReason: null,
            ErrorCode: null,
            Retryable: false));

        string markdown = ConversationTranscriptMarkdownFormatter.Format(transcript);

        Assert.Contains("### Message - 2024-01-01T00:00:01.0000000+00:00", markdown, StringComparison.Ordinal);
        Assert.Contains("- 2024-01-01T00:00:05.0000000+00:00 tool succeeded", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_fences_error_safe_message_markdown_and_raw_html()
    {
        ConversationTranscript transcript = ConversationTranscript.Create(
            "smoke",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        transcript.Errors.Add(new ConversationError(
            CreatedAtUtc: DateTimeOffset.Parse("2024-01-01T00:00:02Z"),
            Provider: "openai",
            Operation: "chat",
            StatusCode: 500,
            LocalErrorCode: "model_failed",
            SafeMessage: """
            # injected heading
            <script>alert(1)</script>
            [click me](https://example.test)
            ![alt](https://example.test/image.png)
            """,
            Retryable: false));

        string markdown = ConversationTranscriptMarkdownFormatter.Format(transcript);

        Assert.Contains("- 2024-01-01T00:00:02.0000000+00:00 model_failed", markdown, StringComparison.Ordinal);
        string outsideFences = RemoveFencedBlocks(markdown);
        Assert.DoesNotContain("# injected heading", outsideFences, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", outsideFences, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[click me]", outsideFences, StringComparison.Ordinal);
        Assert.DoesNotContain("![alt]", outsideFences, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_fences_tool_output_summary_markdown_and_raw_html()
    {
        ConversationTranscript transcript = ConversationTranscript.Create(
            "smoke",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        transcript.AddToolCall(new ConversationToolCall(
            CreatedAtUtc: DateTimeOffset.Parse("2024-01-01T00:00:04Z"),
            CallId: "call_read",
            ToolName: "workspace.read_text",
            ArgumentsJson: "{}",
            ApprovalStatus: "approved",
            CompletedAtUtc: DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
            Succeeded: true,
            OutputSummary: """
            # injected heading
            <div onclick="steal()">raw html</div>
            [click me](https://example.test)
            ![alt](https://example.test/image.png)
            """,
            FailureReason: null,
            ErrorCode: null,
            Retryable: false));

        string markdown = ConversationTranscriptMarkdownFormatter.Format(transcript);

        Assert.Contains("- 2024-01-01T00:00:05.0000000+00:00 workspace.read_text succeeded", markdown, StringComparison.Ordinal);
        string outsideFences = RemoveFencedBlocks(markdown);
        Assert.DoesNotContain("# injected heading", outsideFences, StringComparison.Ordinal);
        Assert.DoesNotContain("<div", outsideFences, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[click me]", outsideFences, StringComparison.Ordinal);
        Assert.DoesNotContain("![alt]", outsideFences, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_fences_tool_failure_reason_markdown_and_raw_html()
    {
        ConversationTranscript transcript = ConversationTranscript.Create(
            "smoke",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        transcript.AddToolCall(new ConversationToolCall(
            CreatedAtUtc: DateTimeOffset.Parse("2024-01-01T00:00:04Z"),
            CallId: "call_read",
            ToolName: "workspace.read_text",
            ArgumentsJson: "{}",
            ApprovalStatus: "approved",
            CompletedAtUtc: DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
            Succeeded: false,
            OutputSummary: null,
            FailureReason: """
            # injected heading
            <div onclick="steal()">raw html</div>
            [click me](https://example.test)
            ![alt](https://example.test/image.png)
            """,
            ErrorCode: "tool_failed",
            Retryable: false));

        string markdown = ConversationTranscriptMarkdownFormatter.Format(transcript);

        Assert.Contains("- 2024-01-01T00:00:05.0000000+00:00 workspace.read_text failed", markdown, StringComparison.Ordinal);
        string outsideFences = RemoveFencedBlocks(markdown);
        Assert.DoesNotContain("# injected heading", outsideFences, StringComparison.Ordinal);
        Assert.DoesNotContain("<div", outsideFences, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[click me]", outsideFences, StringComparison.Ordinal);
        Assert.DoesNotContain("![alt]", outsideFences, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_redacts_full_bearer_value_after_token_key()
    {
        ConversationTranscript transcript = ConversationTranscript.Create(
            "smoke",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        transcript.AddUserMessage("token=Bearer abc123", DateTimeOffset.Parse("2024-01-01T00:00:01Z"));

        string markdown = ConversationTranscriptMarkdownFormatter.Format(transcript);

        Assert.Contains("token=[redacted]", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("abc123", markdown, StringComparison.Ordinal);
    }

    private static string RemoveFencedBlocks(string markdown)
    {
        string[] lines = markdown.Split(Environment.NewLine);
        List<string> outsideLines = [];
        string? activeFence = null;
        foreach (string line in lines)
        {
            Match fenceMatch = FenceLinePattern.Match(line);
            if (fenceMatch.Success)
            {
                string fence = fenceMatch.Groups[1].Value;
                if (activeFence is null)
                {
                    activeFence = fence;
                    continue;
                }

                if (fence.Length >= activeFence.Length)
                {
                    activeFence = null;
                    continue;
                }
            }

            if (activeFence is null)
            {
                outsideLines.Add(line);
            }
        }

        return string.Join(Environment.NewLine, outsideLines);
    }

    private static void AssertHostileMetadataIsNotLive(string outsideFences)
    {
        Assert.DoesNotContain("<script", outsideFences, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<div", outsideFences, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[click me]", outsideFences, StringComparison.Ordinal);
        Assert.DoesNotContain("![alt]", outsideFences, StringComparison.Ordinal);
        Assert.DoesNotContain("# heading", outsideFences, StringComparison.Ordinal);
        Assert.DoesNotContain("## heading", outsideFences, StringComparison.Ordinal);
        Assert.DoesNotContain("- item", outsideFences, StringComparison.Ordinal);
        Assert.DoesNotContain("* item", outsideFences, StringComparison.Ordinal);
    }

    private static readonly Regex FenceLinePattern = new(
        @"^\s*(`{3,})(?:[^`]*)$",
        RegexOptions.CultureInvariant);
}
