using System.Text.Json;

namespace CSharpAiCli.Core;

public sealed class ExecJsonRenderer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly TextWriter writer;

    public ExecJsonRenderer(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        this.writer = writer;
    }

    public void WriteEvent(ExecEvent execEvent)
    {
        ArgumentNullException.ThrowIfNull(execEvent);

        writer.WriteLine(JsonSerializer.Serialize(CreateEventEnvelope(execEvent), JsonOptions));
    }

    public void WriteResult(ExecResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        long sequence = result.Events.Count == 0 ? 0 : result.Events[^1].Sequence + 1;
        DateTimeOffset timestamp = result.Events.Count == 0 ? DateTimeOffset.UnixEpoch : result.Events[^1].Timestamp;

        Dictionary<string, object?> envelope = new()
        {
            ["type"] = "exec.result",
            ["sequence"] = sequence,
            ["timestamp"] = timestamp.ToString("O")
        };

        if (!string.IsNullOrEmpty(result.Summary))
        {
            envelope["summary"] = ExecOutputRedactor.Redact(result.Summary);
        }

        if (!string.IsNullOrEmpty(result.ErrorCode))
        {
            envelope["errorCode"] = result.ErrorCode;
        }

        if (!string.IsNullOrEmpty(result.ApprovalStatus))
        {
            envelope["approvalStatus"] = result.ApprovalStatus;
        }

        if (!string.IsNullOrEmpty(result.StopReason))
        {
            envelope["stopReason"] = result.StopReason;
        }

        Dictionary<string, object?> payload = new()
        {
            ["status"] = result.IsSuccess ? "success" : "failure",
            ["exitCode"] = result.ExitCode,
            ["eventCount"] = result.Events.Count
        };

        if (result.ChangedFiles.Count > 0)
        {
            payload["changedFileCount"] = result.ChangedFiles.Count;
            payload["changedFiles"] = result.ChangedFiles.Select(file => new Dictionary<string, object?>
            {
                ["path"] = ExecOutputRedactor.Redact(file.Path),
                ["status"] = file.Status,
                ["sourceToolCallId"] = file.SourceToolCallId,
                ["diffStatTruncated"] = file.DiffStatTruncated,
                ["errorCode"] = file.ErrorCode
            }).ToArray();
        }

        if (result.VerificationResults.Count > 0)
        {
            payload["verificationResults"] = result.VerificationResults.Select(verification => new Dictionary<string, object?>
            {
                ["status"] = verification.Status,
                ["source"] = verification.Source,
                ["command"] = ExecOutputRedactor.Redact(verification.Command ?? string.Empty),
                ["succeeded"] = verification.Succeeded,
                ["approvalStatus"] = verification.ApprovalStatus,
                ["errorCode"] = verification.ErrorCode,
                ["exitCode"] = verification.ExitCode,
                ["timedOut"] = verification.TimedOut,
                ["stdoutTruncated"] = verification.StdoutTruncated,
                ["stderrTruncated"] = verification.StderrTruncated
            }).ToArray();
        }

        envelope["payload"] = payload;

        writer.WriteLine(JsonSerializer.Serialize(envelope, JsonOptions));
    }

    private static Dictionary<string, object?> CreateEventEnvelope(ExecEvent execEvent)
    {
        Dictionary<string, object?> envelope = new()
        {
            ["type"] = execEvent.Type,
            ["sequence"] = execEvent.Sequence,
            ["timestamp"] = execEvent.Timestamp.ToString("O")
        };

        if (!string.IsNullOrEmpty(execEvent.Message))
        {
            envelope["message"] = ExecOutputRedactor.Redact(execEvent.Message);
        }

        if (!string.IsNullOrEmpty(execEvent.Summary))
        {
            envelope["summary"] = ExecOutputRedactor.Redact(execEvent.Summary);
        }

        if (!string.IsNullOrEmpty(execEvent.Status))
        {
            envelope["status"] = execEvent.Status;
        }

        if (execEvent.StepIndex.HasValue)
        {
            envelope["stepIndex"] = execEvent.StepIndex.Value;
        }

        if (!string.IsNullOrEmpty(execEvent.StopReason))
        {
            envelope["stopReason"] = execEvent.StopReason;
        }

        if (execEvent.DurationMs.HasValue)
        {
            envelope["durationMs"] = execEvent.DurationMs.Value;
        }

        if (execEvent.Payload is not null)
        {
            envelope["payload"] = ExecOutputRedactor.RedactPayload(execEvent.Payload);
        }

        if (!string.IsNullOrEmpty(execEvent.ErrorCode))
        {
            envelope["errorCode"] = execEvent.ErrorCode;
        }

        if (!string.IsNullOrEmpty(execEvent.ApprovalStatus))
        {
            envelope["approvalStatus"] = execEvent.ApprovalStatus;
        }

        if (execEvent.ApprovalDurationMs.HasValue)
        {
            envelope["approvalDurationMs"] = execEvent.ApprovalDurationMs.Value;
        }

        return envelope;
    }
}

internal static class ExecOutputRedactor
{
    public static string Redact(string value)
    {
        return DiagnosticSecretRedactor.Redact(value);
    }

    public static Dictionary<string, string> RedactPayload(IReadOnlyDictionary<string, string> payload)
    {
        Dictionary<string, string> redacted = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> pair in payload.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            string safeKey = Redact(pair.Key);
            string safeValue = DiagnosticSecretRedactor.IsSecretName(pair.Key) ||
                DiagnosticSecretRedactor.IsSecretName(safeKey)
                    ? "[redacted]"
                    : Redact(pair.Value);

            AddCollisionSafe(redacted, safeKey, safeValue);
        }

        return redacted;
    }

    private static void AddCollisionSafe(Dictionary<string, string> payload, string key, string value)
    {
        if (!payload.ContainsKey(key))
        {
            payload[key] = value;
            return;
        }

        for (int suffix = 2; ; suffix++)
        {
            string candidate = key + "#" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!payload.ContainsKey(candidate))
            {
                payload[candidate] = value;
                return;
            }
        }
    }
}
