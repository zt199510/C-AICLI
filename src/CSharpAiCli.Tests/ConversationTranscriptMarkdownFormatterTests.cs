using CSharpAiCli.Core;
using System.Text.RegularExpressions;

namespace CSharpAiCli.Tests;

public sealed class ConversationTranscriptMarkdownFormatterTests
{
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
        Assert.Contains("""
        ````
        # injected heading
        <div onclick="steal()">raw html</div>
        ```
        nested fence
        ```
        ````
        """, normalizedMarkdown, StringComparison.Ordinal);

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

    private static readonly Regex FenceLinePattern = new(
        @"^\s*(`{3,})(?:[^`]*)$",
        RegexOptions.CultureInvariant);
}
