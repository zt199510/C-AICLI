using System.ClientModel;
using OpenAI.Responses;

namespace CSharpAiCli.Core;

#pragma warning disable OPENAI001
public sealed class SdkOpenAiResponsesGateway : IOpenAiResponsesGateway
{
    private readonly ResponsesClient client;

    public SdkOpenAiResponsesGateway(string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        client = new ResponsesClient(apiKey);
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
}
#pragma warning restore OPENAI001
