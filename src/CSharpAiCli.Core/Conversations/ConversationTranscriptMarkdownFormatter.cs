using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CSharpAiCli.Core;

public static class ConversationTranscriptMarkdownFormatter
{
    private const string SecretKeyNamePattern =
        "OPENAI_API_KEY|apiKey|api_key|api-key|api[_-]?key|access[_-]?token|accessToken|refresh[_-]?token|refreshToken|client[_-]?secret|clientSecret|AWS_SECRET_ACCESS_KEY|aws[_-]?secret[_-]?access[_-]?key|awsSecretAccessKey|token|password|secret";

    private static readonly Regex WhitespacePattern = new(@"\s+", RegexOptions.CultureInvariant);
    private static readonly Regex JsonSecretPattern = new(
        $$"""("(?i:{{SecretKeyNamePattern}})"\s*:\s*")[^"]*(")""",
        RegexOptions.CultureInvariant);
    private static readonly Regex KeyValueSecretPattern = new(
        $$"""\b(?i:{{SecretKeyNamePattern}})\b(\s*[:=]\s*)(?:"[^"]*"|'[^']*'|Bearer\s+[A-Za-z0-9._~+/=-]+|[^\s,;]+)""",
        RegexOptions.CultureInvariant);
    private static readonly Regex BearerTokenPattern = new(
        @"\bBearer\s+[A-Za-z0-9._~+/=-]+",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex OpenAiKeyPattern = new(
        @"\bsk-[A-Za-z0-9._-]+",
        RegexOptions.CultureInvariant);
    private static readonly Regex GitHubTokenPattern = new(
        @"\bgh[pousr]_[A-Za-z0-9_]+",
        RegexOptions.CultureInvariant);

    public static string Format(ConversationTranscript transcript)
    {
        ArgumentNullException.ThrowIfNull(transcript);

        StringBuilder builder = new();
        builder.AppendLine($"# Session: {NormalizeMarkdownMetadata(transcript.SessionName)}");
        builder.AppendLine();
        builder.AppendLine($"- Created: {transcript.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)}");
        builder.AppendLine($"- Updated: {transcript.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture)}");
        builder.AppendLine($"- Turns: {transcript.Messages.Count}");
        builder.AppendLine($"- Tool calls: {transcript.ToolCalls.Count}");
        builder.AppendLine();
        builder.AppendLine("## Messages");

        foreach (ConversationMessage message in transcript.Messages)
        {
            string content = RedactSecrets(message.Content);
            string fence = CreateFence(content);
            builder.AppendLine();
            builder.AppendLine($"### {ToDisplayRole(message.Role)} - {message.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)}");
            builder.AppendLine(fence);
            builder.AppendLine(content);
            builder.AppendLine(fence);
        }

        if (transcript.Errors.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Errors");
            builder.AppendLine();
            foreach (ConversationError error in transcript.Errors)
            {
                string errorCode = string.IsNullOrWhiteSpace(error.LocalErrorCode)
                    ? "error"
                    : NormalizeMarkdownMetadata(error.LocalErrorCode);
                builder.AppendLine($"- {error.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)} {errorCode}");
                AppendFencedBlock(builder, RedactSecrets(error.SafeMessage));
            }
        }

        if (transcript.ToolCalls.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Tool Calls");
            builder.AppendLine();
            foreach (ConversationToolCall toolCall in transcript.ToolCalls)
            {
                string status = toolCall.Succeeded ? "succeeded" : "failed";
                string summary = toolCall.Succeeded
                    ? toolCall.OutputSummary ?? string.Empty
                    : toolCall.FailureReason ?? toolCall.ErrorCode ?? string.Empty;
                string errorCode = toolCall.Succeeded || string.IsNullOrWhiteSpace(toolCall.ErrorCode)
                    ? string.Empty
                    : $" {NormalizeMarkdownMetadata(toolCall.ErrorCode)}";
                builder.AppendLine(
                    $"- {toolCall.CompletedAtUtc.ToString("O", CultureInfo.InvariantCulture)} {NormalizeMarkdownMetadata(toolCall.ToolName)} {status}{errorCode}");
                AppendFencedBlock(builder, RedactSecrets(summary));
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string ToDisplayRole(string role)
    {
        string normalizedRole = NormalizeMarkdownMetadata(role);
        if (string.IsNullOrWhiteSpace(normalizedRole))
        {
            return "Message";
        }

        return char.ToUpperInvariant(normalizedRole[0]) + normalizedRole[1..];
    }

    private static string NormalizeSingleLine(string value)
    {
        return WhitespacePattern.Replace(RedactSecrets(value), " ").Trim();
    }

    private static string NormalizeMarkdownMetadata(string value)
    {
        return EscapeMarkdownMetadata(NormalizeSingleLine(value));
    }

    private static string EscapeMarkdownMetadata(string value)
    {
        StringBuilder escaped = new(value.Length);
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            escaped.Append(character switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '[' => "&#91;",
                ']' => "&#93;",
                '(' => "&#40;",
                ')' => "&#41;",
                '!' => "&#33;",
                '#' => "&#35;",
                '*' => "&#42;",
                '-' when index + 1 < value.Length && value[index + 1] == ' ' => "&#45;",
                '+' => "&#43;",
                '`' => "&#96;",
                '\\' => "&#92;",
                _ => character,
            });
        }

        return escaped.ToString();
    }

    private static string RedactSecrets(string value)
    {
        string redacted = value ?? string.Empty;
        redacted = JsonSecretPattern.Replace(redacted, "$1[redacted]$2");
        redacted = KeyValueSecretPattern.Replace(redacted, match =>
        {
            Group separator = match.Groups[1];
            if (!separator.Success)
            {
                return match.Value;
            }

            int prefixLength = separator.Index - match.Index;
            return match.Value[..prefixLength] + separator.Value + "[redacted]";
        });
        redacted = BearerTokenPattern.Replace(redacted, "Bearer [redacted]");
        redacted = OpenAiKeyPattern.Replace(redacted, "[redacted]");
        redacted = GitHubTokenPattern.Replace(redacted, "[redacted]");
        return redacted;
    }

    private static void AppendFencedBlock(StringBuilder builder, string content)
    {
        string fence = CreateFence(content);
        builder.AppendLine(fence);
        builder.AppendLine(content);
        builder.AppendLine(fence);
    }

    private static string CreateFence(string content)
    {
        int longestRun = 0;
        int currentRun = 0;
        foreach (char character in content)
        {
            if (character == '`')
            {
                currentRun++;
                longestRun = Math.Max(longestRun, currentRun);
                continue;
            }

            currentRun = 0;
        }

        return new string('`', Math.Max(3, longestRun + 1));
    }
}
