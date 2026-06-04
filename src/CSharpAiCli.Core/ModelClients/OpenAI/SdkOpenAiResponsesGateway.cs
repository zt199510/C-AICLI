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

    public OpenAiResponseEnvelope CreateResponse(string model, string prompt, CancellationToken cancellationToken = default)
    {
        ClientResult<ResponseResult> result = client.CreateResponse(
            model,
            prompt,
            cancellationToken: cancellationToken);

        ResponseResult response = result.Value;
        string text = response.GetOutputText();

        return new OpenAiResponseEnvelope(
            ResponseId: response.Id ?? "unknown",
            Model: response.Model ?? model,
            Text: text);
    }
}
#pragma warning restore OPENAI001
