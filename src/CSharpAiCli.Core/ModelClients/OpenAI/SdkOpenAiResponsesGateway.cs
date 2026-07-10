using System.ClientModel;
using System.Text;
using System.Text.Json;
using OpenAI;
using OpenAI.Responses;

namespace CSharpAiCli.Core;

#pragma warning disable OPENAI001
public sealed class SdkOpenAiResponsesGateway : IOpenAiResponsesGateway
{
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

        CreateResponseOptions options = CreateAgentOptions(request);
        ClientResult<ResponseResult> result = client.CreateResponse(
            options,
            cancellationToken: cancellationToken);

        return ToEnvelope(result.Value, request.Model);
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
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Model);

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
            options.InputItems.Add(ResponseItem.CreateFunctionCallOutputItem(
                toolResult.CallId,
                CreateToolResultOutputJson(toolResult)));
        }

        foreach (OpenAiToolDefinition tool in request.Tools)
        {
            options.Tools.Add(ResponseTool.CreateFunctionTool(
                tool.Name,
                BinaryData.FromString(tool.ParametersSchema),
                strictModeEnabled: null,
                functionDescription: tool.Description));
        }

        return options;
    }

    internal static OpenAiResponseEnvelope ToEnvelope(ResponseResult response, string fallbackModel)
    {
        ArgumentNullException.ThrowIfNull(response);

        OpenAiToolCall[] toolCalls = response.OutputItems
            .OfType<FunctionCallResponseItem>()
            .Select(toolCall => new OpenAiToolCall(
                CallId: toolCall.CallId,
                Name: toolCall.FunctionName,
                ArgumentsJson: toolCall.FunctionArguments?.ToString()))
            .ToArray();

        return new OpenAiResponseEnvelope(
            ResponseId: response.Id ?? "unknown",
            Model: response.Model ?? fallbackModel,
            Text: response.GetOutputText(),
            ToolCalls: toolCalls);
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
