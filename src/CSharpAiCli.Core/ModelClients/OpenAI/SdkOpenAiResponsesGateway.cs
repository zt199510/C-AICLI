using System.ClientModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenAI;
using OpenAI.Responses;

namespace CSharpAiCli.Core;

#pragma warning disable OPENAI001
public sealed class SdkOpenAiResponsesGateway : IOpenAiResponsesGateway
{
    private const int MaximumApiToolNameLength = 64;
    private const int ToolNameHashLength = 12;

    private readonly ResponsesClient client;

    public SdkOpenAiResponsesGateway(string apiKey)
        : this(apiKey, ConfigLoader.DefaultOpenAiBaseUrl)
    {
    }

    public SdkOpenAiResponsesGateway(string apiKey, string baseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        if (!ConfigLoader.TryNormalizeBaseUrl(baseUrl, out string? normalizedBaseUrl)
            || normalizedBaseUrl is null)
        {
            throw new ArgumentException("OpenAI base URL must be an absolute http or https URL without user info, query, or fragment.", nameof(baseUrl));
        }

        client = new ResponsesClient(
            new ApiKeyCredential(apiKey),
            new OpenAIClientOptions
            {
                Endpoint = new Uri(normalizedBaseUrl, UriKind.Absolute)
            });
    }

    public Uri Endpoint => client.Endpoint;

    public OpenAiResponseEnvelope CreateAgentResponse(
        OpenAiAgentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        IReadOnlyDictionary<string, string> apiToolNames = CreateApiToolNameMap(request.Tools);
        CreateResponseOptions options = CreateAgentOptions(request, apiToolNames);
        ClientResult<ResponseResult> result = client.CreateResponse(
            options,
            cancellationToken: cancellationToken);

        IReadOnlyDictionary<string, string> canonicalToolNames = apiToolNames
            .ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);
        return ToEnvelope(result.Value, request.Model, canonicalToolNames);
    }

    public IEnumerable<OpenAiStreamingResponseUpdate> CreateAgentResponseStreaming(
        OpenAiAgentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        IReadOnlyDictionary<string, string> apiToolNames = CreateApiToolNameMap(request.Tools);
        CreateResponseOptions options = CreateAgentOptions(request, apiToolNames);
        options.StreamingEnabled = true;
        IReadOnlyDictionary<string, string> canonicalToolNames = apiToolNames
            .ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

        foreach (StreamingResponseUpdate update in client.CreateResponseStreaming(
            options,
            cancellationToken: cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (update is StreamingResponseOutputTextDeltaUpdate delta)
            {
                yield return OpenAiStreamingResponseUpdate.OutputTextDelta(delta.Delta);
            }
            else if (update is StreamingResponseCompletedUpdate completed)
            {
                yield return OpenAiStreamingResponseUpdate.Completed(
                    ToEnvelope(completed.Response, request.Model, canonicalToolNames));
            }
        }
    }

    public OpenAiResponseEnvelope CreateResponse(
        string model,
        string prompt,
        string? instructions = null,
        CancellationToken cancellationToken = default)
    {
        CreateResponseOptions options = CreateOptions(model, prompt, instructions, streamingEnabled: false);

        ClientResult<ResponseResult> result = client.CreateResponse(
            options,
            cancellationToken: cancellationToken);

        ResponseResult response = result.Value;
        string text = response.GetOutputText();

        return new OpenAiResponseEnvelope(
            ResponseId: response.Id ?? "unknown",
            Model: response.Model ?? model,
            Text: text);
    }

    public IEnumerable<OpenAiStreamingResponseUpdate> CreateResponseStreaming(
        string model,
        string prompt,
        string? instructions = null,
        CancellationToken cancellationToken = default)
    {
        CreateResponseOptions options = CreateOptions(model, prompt, instructions, streamingEnabled: true);

        foreach (StreamingResponseUpdate update in client.CreateResponseStreaming(
            options,
            cancellationToken: cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (update is StreamingResponseOutputTextDeltaUpdate delta)
            {
                yield return OpenAiStreamingResponseUpdate.OutputTextDelta(delta.Delta);
            }
            else if (update is StreamingResponseCompletedUpdate completed)
            {
                ResponseResult response = completed.Response;
                yield return OpenAiStreamingResponseUpdate.Completed(
                    response.Id ?? "unknown",
                    response.Model ?? model);
            }
        }
    }

    private static CreateResponseOptions CreateOptions(
        string model,
        string prompt,
        string? instructions,
        bool streamingEnabled)
    {
        CreateResponseOptions options = new()
        {
            Model = model,
            StreamingEnabled = streamingEnabled,
        };

        if (!string.IsNullOrWhiteSpace(instructions))
        {
            options.InputItems.Add(ResponseItem.CreateDeveloperMessageItem(instructions));
        }

        options.InputItems.Add(ResponseItem.CreateUserMessageItem(prompt));
        return options;
    }

    internal static CreateResponseOptions CreateAgentOptions(OpenAiAgentRequest request)
    {
        return CreateAgentOptions(request, CreateApiToolNameMap(request.Tools));
    }

    private static CreateResponseOptions CreateAgentOptions(
        OpenAiAgentRequest request,
        IReadOnlyDictionary<string, string> apiToolNames)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Model);
        ArgumentNullException.ThrowIfNull(apiToolNames);

        CreateResponseOptions options = new()
        {
            Model = request.Model,
            PreviousResponseId = request.PreviousResponseId,
            Instructions = string.IsNullOrWhiteSpace(request.Instructions)
                ? null
                : request.Instructions,
            StreamingEnabled = false
        };

        if (!string.IsNullOrWhiteSpace(request.Prompt))
        {
            options.InputItems.Add(ResponseItem.CreateUserMessageItem(request.Prompt));
        }

        foreach (OpenAiToolResultInput toolResult in request.ToolResults)
        {
            if (request.PreviousResponseId is null)
            {
                options.InputItems.Add(ResponseItem.CreateFunctionCallItem(
                    toolResult.CallId,
                    apiToolNames[toolResult.ToolName],
                    BinaryData.FromString(toolResult.ArgumentsJson)));
            }
            options.InputItems.Add(ResponseItem.CreateFunctionCallOutputItem(
                toolResult.CallId,
                CreateToolResultOutputJson(toolResult)));
        }

        foreach (OpenAiToolDefinition tool in request.Tools)
        {
            options.Tools.Add(ResponseTool.CreateFunctionTool(
                apiToolNames[tool.Name],
                BinaryData.FromString(tool.ParametersSchema),
                strictModeEnabled: null,
                functionDescription: tool.Description));
        }

        return options;
    }

    internal static OpenAiResponseEnvelope ToEnvelope(
        ResponseResult response,
        string fallbackModel,
        IReadOnlyDictionary<string, string>? canonicalToolNames = null)
    {
        ArgumentNullException.ThrowIfNull(response);

        OpenAiToolCall[] toolCalls = response.OutputItems
            .OfType<FunctionCallResponseItem>()
            .Select(toolCall => new OpenAiToolCall(
                CallId: toolCall.CallId,
                Name: ResolveCanonicalToolName(toolCall.FunctionName, canonicalToolNames),
                ArgumentsJson: toolCall.FunctionArguments?.ToString()))
            .ToArray();

        return new OpenAiResponseEnvelope(
            ResponseId: response.Id ?? "unknown",
            Model: response.Model ?? fallbackModel,
            Text: response.GetOutputText(),
            ToolCalls: toolCalls);
    }

    internal static IReadOnlyDictionary<string, string> CreateApiToolNameMap(
        IReadOnlyList<OpenAiToolDefinition> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        Dictionary<string, string> result = new(StringComparer.Ordinal);
        HashSet<string> usedApiNames = new(StringComparer.Ordinal);
        foreach (OpenAiToolDefinition tool in tools)
        {
            if (!result.TryAdd(tool.Name, CreateUniqueApiToolName(tool.Name, usedApiNames)))
            {
                throw new InvalidOperationException("OpenAI tool definitions contain a duplicate name.");
            }
        }

        return result;
    }

    private static string CreateUniqueApiToolName(string canonicalName, ISet<string> usedApiNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalName);

        if (canonicalName.Length <= MaximumApiToolNameLength
            && canonicalName.All(IsApiToolNameCharacter)
            && usedApiNames.Add(canonicalName))
        {
            return canonicalName;
        }

        string sanitizedPrefix = new(canonicalName
            .Select(character => IsApiToolNameCharacter(character) ? character : '_')
            .ToArray());
        string hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonicalName)))
            .ToLowerInvariant();

        for (int hashLength = ToolNameHashLength; hashLength <= hash.Length; hashLength += 4)
        {
            string suffix = "_" + hash[..hashLength];
            int prefixLength = Math.Min(
                sanitizedPrefix.Length,
                MaximumApiToolNameLength - suffix.Length);
            string candidate = sanitizedPrefix[..prefixLength] + suffix;
            if (usedApiNames.Add(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("OpenAI tool names could not be mapped uniquely.");
    }

    private static string ResolveCanonicalToolName(
        string apiToolName,
        IReadOnlyDictionary<string, string>? canonicalToolNames)
    {
        if (canonicalToolNames is not null
            && canonicalToolNames.TryGetValue(apiToolName, out string? canonicalToolName))
        {
            return canonicalToolName;
        }

        return apiToolName;
    }

    private static bool IsApiToolNameCharacter(char value)
    {
        return value is >= 'a' and <= 'z'
            or >= 'A' and <= 'Z'
            or >= '0' and <= '9'
            or '_'
            or '-';
    }

    internal static string CreateToolResultOutputJson(OpenAiToolResultInput toolResult)
    {
        ArgumentNullException.ThrowIfNull(toolResult);

        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("callId", toolResult.CallId);
            writer.WriteString("toolName", toolResult.ToolName);
            writer.WriteBoolean("succeeded", toolResult.Succeeded);
            writer.WriteString("summary", toolResult.Summary);
            writer.WriteString("errorCode", toolResult.ErrorCode);
            writer.WriteString("approvalStatus", toolResult.ApprovalStatus);
            writer.WriteBoolean("retryable", toolResult.Retryable);

            if (toolResult.StructuredPayload is not null)
            {
                writer.WritePropertyName("structuredPayload");
                writer.WriteStartObject();
                foreach (KeyValuePair<string, JsonElement> item in toolResult.StructuredPayload)
                {
                    writer.WritePropertyName(item.Key);
                    item.Value.WriteTo(writer);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
#pragma warning restore OPENAI001
