using System.Globalization;

namespace CSharpAiCli.Core;

public sealed class ExecTextRenderer
{
    private readonly TextWriter writer;

    public ExecTextRenderer(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        this.writer = writer;
    }

    public void WriteEvent(ExecEvent execEvent)
    {
        ArgumentNullException.ThrowIfNull(execEvent);

        writer.WriteLine(FormatEvent(execEvent));
    }

    public void WriteResult(ExecResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        string status = result.IsSuccess ? "success" : "failure";
        writer.WriteLine(
            $"result: {status} exitCode={result.ExitCode}{FormatOptional("summary", result.Summary, redact: true)}{FormatOptional("errorCode", result.ErrorCode)}{FormatOptional("approvalStatus", result.ApprovalStatus)} events={result.Events.Count}");
    }

    private static string FormatEvent(ExecEvent execEvent)
    {
        List<string> parts = new()
        {
            $"event: {execEvent.Type}",
            $"seq={execEvent.Sequence}",
            $"ts={execEvent.Timestamp:O}"
        };

        if (!string.IsNullOrEmpty(execEvent.Status))
        {
            parts.Add($"status={execEvent.Status}");
        }

        if (execEvent.DurationMs.HasValue)
        {
            parts.Add($"durationMs={execEvent.DurationMs.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        if (!string.IsNullOrEmpty(execEvent.Message))
        {
            parts.Add($"message={ExecOutputRedactor.Redact(execEvent.Message)}");
        }

        if (!string.IsNullOrEmpty(execEvent.Summary))
        {
            parts.Add($"summary={ExecOutputRedactor.Redact(execEvent.Summary)}");
        }

        if (!string.IsNullOrEmpty(execEvent.ErrorCode))
        {
            parts.Add($"errorCode={execEvent.ErrorCode}");
        }

        if (!string.IsNullOrEmpty(execEvent.ApprovalStatus))
        {
            parts.Add($"approvalStatus={execEvent.ApprovalStatus}");
        }

        if (execEvent.ApprovalDurationMs.HasValue)
        {
            parts.Add($"approvalDurationMs={execEvent.ApprovalDurationMs.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        if (execEvent.Payload is not null)
        {
            foreach (KeyValuePair<string, string> pair in ExecOutputRedactor.RedactPayload(execEvent.Payload))
            {
                parts.Add($"payload.{pair.Key}={pair.Value}");
            }
        }

        return string.Join(' ', parts);
    }

    private static string FormatOptional(string name, string? value, bool redact = false)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        string safeValue = redact ? ExecOutputRedactor.Redact(value) : value;
        return $" {name}={safeValue}";
    }
}
