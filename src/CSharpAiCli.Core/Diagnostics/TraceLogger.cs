using System.Globalization;
using System.Text.Json;

namespace CSharpAiCli.Core;

public static class TraceLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void AppendCommandEvent(
        string commandName,
        CliEnvironmentSnapshot snapshot,
        DiagnosticContext context,
        string type,
        long sequence,
        string status,
        string? summary = null,
        string? errorCode = null,
        DateTimeOffset? timestampUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(status);

        DateTimeOffset timestamp = timestampUtc ?? context.TimestampUtc;
        Dictionary<string, object?> record = CreateBaseRecord(
            commandName,
            context,
            timestamp,
            type,
            sequence);
        record["status"] = Sanitize(status);
        AddIfPresent(record, "summary", summary);
        AddIfPresent(record, "errorCode", errorCode);

        AppendRecords(snapshot, timestamp, [record]);
    }

    public static void AppendExecResult(
        string commandName,
        CliEnvironmentSnapshot snapshot,
        DiagnosticContext context,
        ExecResult result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(result);

        List<Dictionary<string, object?>> records = [];
        foreach (ExecEvent execEvent in result.Events)
        {
            records.Add(CreateEventRecord(commandName, context, execEvent));
        }

        records.Add(CreateResultRecord(commandName, context, result));
        AppendRecords(snapshot, context.TimestampUtc, records);
    }

    private static Dictionary<string, object?> CreateEventRecord(
        string commandName,
        DiagnosticContext context,
        ExecEvent execEvent)
    {
        Dictionary<string, object?> record = CreateBaseRecord(
            commandName,
            context,
            execEvent.Timestamp,
            execEvent.Type,
            execEvent.Sequence);
        AddIfPresent(record, "message", execEvent.Message);
        AddIfPresent(record, "summary", execEvent.Summary);
        AddIfPresent(record, "status", execEvent.Status);
        AddIfPresent(record, "errorCode", execEvent.ErrorCode);
        AddIfPresent(record, "approvalStatus", execEvent.ApprovalStatus);
        if (execEvent.DurationMs.HasValue)
        {
            record["durationMs"] = execEvent.DurationMs.Value;
        }

        if (execEvent.ApprovalDurationMs.HasValue)
        {
            record["approvalDurationMs"] = execEvent.ApprovalDurationMs.Value;
        }

        if (execEvent.Payload is not null)
        {
            record["payload"] = CreateSafePayload(execEvent.Payload);
        }

        return record;
    }

    private static Dictionary<string, object?> CreateResultRecord(
        string commandName,
        DiagnosticContext context,
        ExecResult result)
    {
        DateTimeOffset timestamp = result.Events.Count == 0
            ? context.TimestampUtc
            : result.Events[^1].Timestamp;
        long sequence = result.Events.Count == 0
            ? 0
            : result.Events[^1].Sequence + 1;
        Dictionary<string, object?> record = CreateBaseRecord(
            commandName,
            context,
            timestamp,
            "exec.result",
            sequence);
        record["status"] = result.IsSuccess ? "success" : "failure";
        AddIfPresent(record, "summary", result.Summary);
        AddIfPresent(record, "errorCode", result.ErrorCode);
        AddIfPresent(record, "approvalStatus", result.ApprovalStatus);
        record["payload"] = new Dictionary<string, object?>
        {
            ["exitCode"] = result.ExitCode,
            ["eventCount"] = result.Events.Count
        };
        return record;
    }

    private static Dictionary<string, object?> CreateBaseRecord(
        string commandName,
        DiagnosticContext context,
        DateTimeOffset timestamp,
        string type,
        long sequence)
    {
        return new Dictionary<string, object?>
        {
            ["timestampUtc"] = FormatUtc(timestamp),
            ["command"] = Sanitize(commandName),
            ["commandId"] = Sanitize(context.CommandId),
            ["sessionId"] = Sanitize(context.SessionId),
            ["workspace"] = Sanitize(context.Workspace),
            ["type"] = Sanitize(type),
            ["sequence"] = sequence
        };
    }

    private static Dictionary<string, string> CreateSafePayload(IReadOnlyDictionary<string, string> payload)
    {
        Dictionary<string, string> safePayload = new(StringComparer.Ordinal);
        foreach ((string key, string value) in payload.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            string safeKey = MakeUniqueKey(safePayload, Sanitize(key));
            safePayload[safeKey] = DiagnosticSecretRedactor.IsSecretName(key) ? "[redacted]" : Sanitize(value);
        }

        return safePayload;
    }

    private static string MakeUniqueKey(IReadOnlyDictionary<string, string> payload, string key)
    {
        if (!payload.ContainsKey(key))
        {
            return key;
        }

        for (int suffix = 2; ; suffix++)
        {
            string candidate = key + "_" + suffix.ToString(CultureInfo.InvariantCulture);
            if (!payload.ContainsKey(candidate))
            {
                return candidate;
            }
        }
    }

    private static void AddIfPresent(Dictionary<string, object?> record, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            record[name] = Sanitize(value);
        }
    }

    private static void AppendRecords(
        CliEnvironmentSnapshot snapshot,
        DateTimeOffset timestampUtc,
        IReadOnlyList<Dictionary<string, object?>> records)
    {
        string logDirectory = LogPathResolver.ResolveLogDirectory(snapshot);
        Directory.CreateDirectory(logDirectory);

        string fileName = timestampUtc.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".trace.log";
        string logPath = Path.Combine(logDirectory, fileName);
        string content = string.Join(
            Environment.NewLine,
            records.Select(record => JsonSerializer.Serialize(record, JsonOptions)));
        File.AppendAllText(logPath, content + Environment.NewLine);
    }

    private static string FormatUtc(DateTimeOffset timestamp)
    {
        return timestamp.ToUniversalTime().UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
    }

    private static string Sanitize(string value)
    {
        return DiagnosticSecretRedactor.Redact(value);
    }
}
