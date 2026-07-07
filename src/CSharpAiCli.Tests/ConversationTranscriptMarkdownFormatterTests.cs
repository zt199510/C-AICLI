using CSharpAiCli.Core;

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
            line => line.Contains("workspace.read_text succeeded:", StringComparison.Ordinal));
        Assert.Contains("first api_key=[redacted] OPENAI_API_KEY=[redacted] second Bearer [redacted] \"apiKey\":\"[redacted]\" token=[redacted] password=[redacted] secret=[redacted]", toolLine, StringComparison.Ordinal);
        Assert.DoesNotContain("ghp_secret", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-openai-secret", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("abc123", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("json-secret", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("raw-argument-secret", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("ArgumentsJson", markdown, StringComparison.Ordinal);
    }

    private static string RemoveFencedBlocks(string markdown)
    {
        string[] lines = markdown.Split(Environment.NewLine);
        List<string> outsideLines = [];
        bool inFence = false;
        foreach (string line in lines)
        {
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }

            if (!inFence)
            {
                outsideLines.Add(line);
            }
        }

        return string.Join(Environment.NewLine, outsideLines);
    }
}
