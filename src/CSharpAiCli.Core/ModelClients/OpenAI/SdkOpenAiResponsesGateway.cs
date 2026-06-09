using System.ClientModel;
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
}
#pragma warning restore OPENAI001
