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
            envelope["summary"] = result.Summary;
        }

        if (!string.IsNullOrEmpty(result.ErrorCode))
        {
            envelope["errorCode"] = result.ErrorCode;
        }

        if (!string.IsNullOrEmpty(result.ApprovalStatus))
        {
            envelope["approvalStatus"] = result.ApprovalStatus;
        }

        envelope["payload"] = new Dictionary<string, object?>
        {
            ["status"] = result.IsSuccess ? "success" : "failure",
            ["exitCode"] = result.ExitCode,
            ["eventCount"] = result.Events.Count
        };

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
            envelope["message"] = execEvent.Message;
        }

        if (!string.IsNullOrEmpty(execEvent.Summary))
        {
            envelope["summary"] = execEvent.Summary;
        }

        if (!string.IsNullOrEmpty(execEvent.Status))
        {
            envelope["status"] = execEvent.Status;
        }

        if (execEvent.DurationMs.HasValue)
        {
            envelope["durationMs"] = execEvent.DurationMs.Value;
        }

        if (execEvent.Payload is not null)
        {
            envelope["payload"] = execEvent.Payload.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
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
