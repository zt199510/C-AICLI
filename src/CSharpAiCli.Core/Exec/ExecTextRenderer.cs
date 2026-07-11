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
            $"result: {status} exitCode={result.ExitCode}{FormatOptional("summary", result.Summary, redact: true)}{FormatOptional("errorCode", result.ErrorCode)}{FormatOptional("approvalStatus", result.ApprovalStatus)}{FormatOptional("stopReason", result.StopReason)}{FormatOptional("changedFiles", FormatChangedFiles(result.ChangedFiles), redact: true)}{FormatOptional("verificationStatus", FormatVerificationStatus(result.VerificationResults))}{FormatOptional("retryCount", FormatRetryCount(result.RetryAttempts))}{FormatOptional("failureKind", result.FailureSummary?.FailureKind)}{FormatOptional("commands", FormatCommands(result.FailureSummary?.Commands), redact: true)}{FormatOptional("remainingRisk", result.FailureSummary?.RemainingRisk, redact: true)} events={result.Events.Count}");
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

        if (execEvent.StepIndex.HasValue)
        {
            parts.Add($"stepIndex={execEvent.StepIndex.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        if (!string.IsNullOrEmpty(execEvent.StopReason))
        {
            parts.Add($"stopReason={execEvent.StopReason}");
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

    private static string? FormatChangedFiles(IReadOnlyList<ChangedFileSummary> changedFiles)
    {
        return changedFiles.Count == 0
            ? null
            : string.Join(",", changedFiles.Select(file => file.Path));
    }

    private static string? FormatVerificationStatus(IReadOnlyList<VerificationResultSummary> verificationResults)
    {
        return verificationResults.Count == 0
            ? null
            : verificationResults[^1].Status;
    }

    private static string? FormatRetryCount(IReadOnlyList<AgentRetryAttempt> retryAttempts)
    {
        return retryAttempts.Count == 0
            ? null
            : retryAttempts.Count.ToString(CultureInfo.InvariantCulture);
    }

    private static string? FormatCommands(IReadOnlyList<string>? commands)
    {
        return commands is null || commands.Count == 0
            ? null
            : string.Join(",", commands);
    }
}
