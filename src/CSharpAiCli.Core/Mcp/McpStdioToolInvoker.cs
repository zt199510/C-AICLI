using System.Text;
using System.Text.Json;

namespace CSharpAiCli.Core;

public sealed class McpStdioToolInvoker : IMcpToolInvoker
{
    private const int MaxSummaryLength = 1000;

    private readonly IMcpClientSessionFactory sessionFactory;
    private readonly DiagnosticDurationClock durationClock;

    public McpStdioToolInvoker(IMcpClientSessionFactory sessionFactory)
        : this(sessionFactory, null)
    {
    }

    public McpStdioToolInvoker(
        IMcpClientSessionFactory sessionFactory,
        Func<DateTimeOffset>? utcNowProvider,
        Func<long>? timestampProvider = null)
    {
        ArgumentNullException.ThrowIfNull(sessionFactory);
        this.sessionFactory = sessionFactory;
        durationClock = new DiagnosticDurationClock(timestampProvider);
    }

    public ToolExecutionResult Invoke(
        McpToolRequest request,
        WorkspaceContext workspace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(workspace);
        cancellationToken.ThrowIfCancellationRequested();

        long startedTimestamp = durationClock.GetTimestamp();
        if (!TryParseArguments(request.ArgumentsJson, out JsonDocument? arguments, out ToolExecutionResult? argumentsFailure))
        {
            return Complete(argumentsFailure, DiagnosticEventStatus.Failure, startedTimestamp);
        }

        JsonDocument argumentsDocument = arguments
            ?? throw new InvalidOperationException("Parsed MCP tool arguments were unexpectedly unavailable.");
        using (argumentsDocument)
        {
            try
            {
                McpClientSessionOpenResult openResult = sessionFactory.OpenSession(request.Server, workspace, cancellationToken);
                if (!openResult.Succeeded || openResult.Session is null)
                {
                    return Complete(
                        ToolExecutionResult.Failure(
                            openResult.ErrorCode ?? McpErrorCode.StartFailed,
                            openResult.SafeMessage),
                        DiagnosticEventStatus.Failure,
                        startedTimestamp);
                }

                using IDisposable? sessionLease = openResult.Session as IDisposable;
                McpProtocolClient client = new(openResult.Session);
                McpInitializeResult initializeResult = client.Initialize(cancellationToken);
                if (!initializeResult.Succeeded)
                {
                    return Complete(
                        ToolExecutionResult.Failure(
                            initializeResult.ErrorCode ?? McpErrorCode.InvalidResponse,
                            initializeResult.SafeMessage),
                        initializeResult.TimedOut ? DiagnosticEventStatus.Timeout : DiagnosticEventStatus.Failure,
                        startedTimestamp);
                }

                McpToolCallResult callResult = client.CallTool(
                    request.RemoteToolName,
                    argumentsDocument.RootElement,
                    cancellationToken);
                if (!callResult.Succeeded)
                {
                    return Complete(
                        ToolExecutionResult.Failure(
                            callResult.ErrorCode ?? McpErrorCode.InvalidResponse,
                            callResult.SafeMessage),
                        callResult.TimedOut ? DiagnosticEventStatus.Timeout : DiagnosticEventStatus.Failure,
                        startedTimestamp);
                }

                IReadOnlyDictionary<string, JsonElement>? payload = CreateStructuredPayload(callResult);
                string summary = CreateSummary(
                    callResult,
                    request.RemoteToolName,
                    callResult.IsToolError
                        ? $"MCP tool '{request.RemoteToolName}' returned an error."
                        : $"MCP tool '{request.RemoteToolName}' completed.");

                ToolExecutionResult result = callResult.IsToolError
                    ? ToolExecutionResult.Failure(McpErrorCode.ToolError, summary, structuredPayload: payload)
                    : ToolExecutionResult.Success(summary, structuredPayload: payload);
                return Complete(
                    result,
                    callResult.IsToolError ? DiagnosticEventStatus.Failure : DiagnosticEventStatus.Success,
                    startedTimestamp);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return Complete(
                    ToolExecutionResult.Failure(
                        McpErrorCode.ClientFailed,
                        "MCP tool invocation failed."),
                    DiagnosticEventStatus.Failure,
                    startedTimestamp);
            }
        }
    }

    private ToolExecutionResult Complete(
        ToolExecutionResult result,
        string status,
        long startedTimestamp)
    {
        return result with
        {
            StructuredPayload = AddMcpDiagnostics(
                result.StructuredPayload,
                status,
                durationClock.GetElapsedMilliseconds(startedTimestamp))
        };
    }

    private static IReadOnlyDictionary<string, JsonElement> AddMcpDiagnostics(
        IReadOnlyDictionary<string, JsonElement>? payload,
        string status,
        long durationMs)
    {
        Dictionary<string, JsonElement> copy = payload is null
            ? new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            : new Dictionary<string, JsonElement>(payload, StringComparer.Ordinal);

        copy["mcpStatus"] = JsonSerializer.SerializeToElement(status);
        copy["mcpDurationMs"] = JsonSerializer.SerializeToElement(durationMs);
        return copy;
    }

    private static bool TryParseArguments(
        string? argumentsJson,
        out JsonDocument? arguments,
        out ToolExecutionResult failure)
    {
        arguments = null;
        failure = null!;

        try
        {
            arguments = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
        }
        catch (JsonException)
        {
            failure = ToolExecutionResult.Failure(
                ToolErrorCode.InvalidToolArguments,
                "MCP tool arguments must be valid JSON.");
            return false;
        }

        if (arguments.RootElement.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        arguments.Dispose();
        arguments = null;
        failure = ToolExecutionResult.Failure(
            ToolErrorCode.InvalidToolArguments,
            "MCP tool arguments must be a JSON object.");
        return false;
    }

    private static IReadOnlyDictionary<string, JsonElement>? CreateStructuredPayload(McpToolCallResult result)
    {
        Dictionary<string, JsonElement> payload = new(StringComparer.Ordinal);
        if (result.StructuredContent.HasValue)
        {
            payload["structuredContent"] = result.StructuredContent.Value.Clone();
        }

        if (result.RawResult.HasValue)
        {
            payload["mcpResult"] = result.RawResult.Value.Clone();
        }

        return payload.Count == 0 ? null : payload;
    }

    private static string CreateSummary(
        McpToolCallResult result,
        string remoteToolName,
        string fallback)
    {
        StringBuilder builder = new();
        foreach (JsonElement contentBlock in result.Content)
        {
            if (!contentBlock.TryGetProperty("type", out JsonElement typeElement) ||
                !string.Equals(typeElement.GetString(), "text", StringComparison.Ordinal) ||
                !contentBlock.TryGetProperty("text", out JsonElement textElement) ||
                textElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            string text = SanitizeSummaryText(textElement.GetString() ?? string.Empty);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            int remaining = MaxSummaryLength - builder.Length;
            if (remaining <= 0)
            {
                break;
            }

            builder.Append(text.Length > remaining ? text[..remaining] : text);
        }

        string summary = builder.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(summary))
        {
            return summary;
        }

        return string.IsNullOrWhiteSpace(remoteToolName) ? fallback : fallback;
    }

    private static string SanitizeSummaryText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        StringBuilder builder = new(Math.Min(text.Length, MaxSummaryLength));
        foreach (char character in text)
        {
            if (builder.Length >= MaxSummaryLength)
            {
                break;
            }

            builder.Append(char.IsControl(character) && character is not '\r' and not '\n' and not '\t'
                ? ' '
                : character);
        }

        return builder.ToString().Trim();
    }
}
