namespace CSharpAiCli.Core;

public interface IOpenAiResponsesGateway
{
    OpenAiResponseEnvelope CreateResponse(
        string model,
        string prompt,
        CancellationToken cancellationToken = default);

    IEnumerable<OpenAiStreamingResponseUpdate> CreateResponseStreaming(
        string model,
        string prompt,
        CancellationToken cancellationToken = default);
}
