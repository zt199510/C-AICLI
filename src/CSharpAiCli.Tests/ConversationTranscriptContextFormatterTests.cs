using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ConversationTranscriptContextFormatterTests
{
    [Fact]
    public void Format_includes_safe_transcript_context_without_raw_tool_arguments()
    {
        ConversationTranscript transcript = ConversationTranscript.Create(
            "smoke",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        transcript.AddUserMessage("previous question", DateTimeOffset.Parse("2024-01-01T00:00:01Z"));
        transcript.AddAssistantMessage(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_previous",
            Text: "previous answer"), DateTimeOffset.Parse("2024-01-01T00:00:02Z"));
        transcript.AddError(new ModelError(
            Provider: "openai",
            Operation: "responses.create",
            StatusCode: null,
            LocalErrorCode: "missing-openai-api-key",
            SafeMessage: "OpenAI API key is missing.",
            Retryable: false), DateTimeOffset.Parse("2024-01-01T00:00:03Z"));
        transcript.AddToolCall(new ConversationToolCall(
            CreatedAtUtc: DateTimeOffset.Parse("2024-01-01T00:00:04Z"),
            CallId: "call_search",
            ToolName: "workspace.search",
            ArgumentsJson: """{"query":"secret raw query"}""",
            ApprovalStatus: "approved",
            CompletedAtUtc: DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
            Succeeded: true,
            OutputSummary: "3 matching files",
            FailureReason: null,
            ErrorCode: null,
            Retryable: false));
        transcript.AddToolCall(new ConversationToolCall(
            CreatedAtUtc: DateTimeOffset.Parse("2024-01-01T00:00:06Z"),
            CallId: "call_shell",
            ToolName: "workspace.shell",
            ArgumentsJson: """{"command":"rm secret"}""",
            ApprovalStatus: "denied",
            CompletedAtUtc: DateTimeOffset.Parse("2024-01-01T00:00:07Z"),
            Succeeded: false,
            OutputSummary: null,
            FailureReason: "Shell command was denied.",
            ErrorCode: "shell-denied",
            Retryable: false));

        string context = ConversationTranscriptContextFormatter.Format(transcript);

        Assert.Contains("session: smoke", context, StringComparison.Ordinal);
        Assert.Contains("user: previous question", context, StringComparison.Ordinal);
        Assert.Contains("assistant: previous answer", context, StringComparison.Ordinal);
        Assert.Contains("missing-openai-api-key", context, StringComparison.Ordinal);
        Assert.Contains("OpenAI API key is missing.", context, StringComparison.Ordinal);
        Assert.Contains("workspace.search", context, StringComparison.Ordinal);
        Assert.Contains("succeeded", context, StringComparison.Ordinal);
        Assert.Contains("3 matching files", context, StringComparison.Ordinal);
        Assert.Contains("workspace.shell", context, StringComparison.Ordinal);
        Assert.Contains("failed", context, StringComparison.Ordinal);
        Assert.Contains("shell-denied", context, StringComparison.Ordinal);
        Assert.Contains("Shell command was denied.", context, StringComparison.Ordinal);
        Assert.DoesNotContain("secret raw query", context, StringComparison.Ordinal);
        Assert.DoesNotContain("rm secret", context, StringComparison.Ordinal);
        Assert.DoesNotContain("ArgumentsJson", context, StringComparison.Ordinal);
    }
}
