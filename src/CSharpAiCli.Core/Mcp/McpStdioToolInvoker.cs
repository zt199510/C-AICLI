using System.Text;
using System.Text.Json;

namespace CSharpAiCli.Core;

public sealed class McpStdioToolInvoker : IMcpToolInvoker
{
    private const int MaxSummaryLength = 1000;

    private readonly IMcpClientSessionFactory sessionFactory;

    public McpStdioToolInvoker(IMcpClientSessionFactory sessionFactory)
    {
        ArgumentNullException.ThrowIfNull(sessionFactory);
        this.sessionFactory = sessionFactory;
    }

    public ToolExecutionResult Invoke(
        McpToolRequest request,
        WorkspaceContext workspace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(workspace);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryParseArguments(request.ArgumentsJson, out JsonDocument? arguments, out ToolExecutionResult? argumentsFailure))
        {
            return argumentsFailure;
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
                    return ToolExecutionResult.Failure(
                        openResult.ErrorCode ?? McpErrorCode.StartFailed,
                        openResult.SafeMessage);
                }

                using IDisposable? sessionLease = openResult.Session as IDisposable;
                McpProtocolClient client = new(openResult.Session);
                McpInitializeResult initializeResult = client.Initialize(cancellationToken);
                if (!initializeResult.Succeeded)
                {
                    return ToolExecutionResult.Failure(
                        initializeResult.ErrorCode ?? McpErrorCode.InvalidResponse,
                        initializeResult.SafeMessage);
                }

                McpToolCallResult callResult = client.CallTool(
                    request.RemoteToolName,
                    argumentsDocument.RootElement,
                    cancellationToken);
                if (!callResult.Succeeded)
                {
                    return ToolExecutionResult.Failure(
                        callResult.ErrorCode ?? McpErrorCode.InvalidResponse,
                        callResult.SafeMessage);
                }

                IReadOnlyDictionary<string, JsonElement>? payload = CreateStructuredPayload(callResult);
                string summary = CreateSummary(
                    callResult,
                    request.RemoteToolName,
                    callResult.IsToolError
                        ? $"MCP tool '{request.RemoteToolName}' returned an error."
                        : $"MCP tool '{request.RemoteToolName}' completed.");

                return callResult.IsToolError
                    ? ToolExecutionResult.Failure(McpErrorCode.ToolError, summary, structuredPayload: payload)
                    : ToolExecutionResult.Success(summary, structuredPayload: payload);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return ToolExecutionResult.Failure(
                    McpErrorCode.ClientFailed,
                    "MCP tool invocation failed.");
            }
        }
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
