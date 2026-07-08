using System.Text.Json;

namespace CSharpAiCli.Core;

public sealed record ReviewReport(
    string Status,
    string? Provider = null,
    string? Model = null,
    string? ResponseId = null,
    string? FindingsText = null,
    string? ErrorCode = null,
    string? SafeMessage = null,
    string? Operation = null,
    int? StatusCode = null,
    bool? Retryable = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static ReviewReport Completed(ChatResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return new ReviewReport(
            Status: "completed",
            Provider: response.Provider,
            Model: response.Model,
            ResponseId: response.ResponseId,
            FindingsText: response.Text);
    }

    public static ReviewReport ModelFailure(ChatModelResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        ModelError error = result.Error ?? new ModelError(
            Provider: "unknown",
            Operation: "unknown",
            StatusCode: null,
            LocalErrorCode: "model-call-failed",
            SafeMessage: "Model call failed without a detailed error.",
            Retryable: false);

        return new ReviewReport(
            Status: "failed",
            Provider: error.Provider,
            ErrorCode: string.IsNullOrWhiteSpace(error.LocalErrorCode) ? "model-call-failed" : error.LocalErrorCode,
            SafeMessage: error.SafeMessage,
            Operation: error.Operation,
            StatusCode: error.StatusCode,
            Retryable: error.Retryable);
    }

    public static ReviewReport ToolFailure(ToolExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new ReviewReport(
            Status: "failed",
            ErrorCode: string.IsNullOrWhiteSpace(result.ErrorCode) ? "tool-failed" : result.ErrorCode,
            SafeMessage: result.Summary,
            Retryable: result.Retryable);
    }

    public string ToDisplayText()
    {
        return string.Equals(Status, "completed", StringComparison.Ordinal)
            ? ToCompletedDisplayText()
            : ToFailureDisplayText();
    }

    public string ToJson()
    {
        Dictionary<string, object?> envelope = new(StringComparer.Ordinal)
        {
            ["type"] = "review.result",
            ["status"] = Status
        };

        AddIfPresent(envelope, "provider", Provider);
        AddIfPresent(envelope, "model", Model);
        AddIfPresent(envelope, "responseId", ResponseId);
        AddIfPresent(envelope, "errorCode", ErrorCode);
        AddIfPresent(envelope, "operation", Operation);
        if (string.Equals(Status, "completed", StringComparison.Ordinal))
        {
            envelope["findingsText"] = FindingsText ?? string.Empty;
        }
        else
        {
            envelope["safeMessage"] = SafeMessage ?? "Review failed.";
        }

        if (StatusCode is not null)
        {
            envelope["statusCode"] = StatusCode.Value;
        }

        if (Retryable is not null)
        {
            envelope["retryable"] = Retryable.Value;
        }

        return JsonSerializer.Serialize(envelope, JsonOptions);
    }

    private string ToCompletedDisplayText()
    {
        List<string> lines =
        [
            TrimTrailingNewLines(FindingsText ?? string.Empty),
            string.Empty,
            $"status: {Status}"
        ];

        AddLineIfPresent(lines, "provider", Provider);
        AddLineIfPresent(lines, "model", Model);
        AddLineIfPresent(lines, "responseId", ResponseId);

        return string.Join(Environment.NewLine, lines);
    }

    private string ToFailureDisplayText()
    {
        List<string> lines =
        [
            $"status: {Status}"
        ];

        AddLineIfPresent(lines, "errorCode", ErrorCode);
        lines.Add("summary:");
        lines.Add(SafeMessage ?? "Review failed.");
        AddLineIfPresent(lines, "provider", Provider);
        AddLineIfPresent(lines, "operation", Operation);
        if (StatusCode is not null)
        {
            lines.Add($"statusCode: {StatusCode.Value}");
        }

        if (Retryable is not null)
        {
            lines.Add($"retryable: {Retryable.Value.ToString().ToLowerInvariant()}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static void AddIfPresent(Dictionary<string, object?> envelope, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            envelope[key] = value;
        }
    }

    private static void AddLineIfPresent(List<string> lines, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            lines.Add($"{key}: {value}");
        }
    }

    private static string TrimTrailingNewLines(string text)
    {
        return text.TrimEnd('\r', '\n');
    }
}
