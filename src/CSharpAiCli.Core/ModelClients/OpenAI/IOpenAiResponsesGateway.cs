namespace CSharpAiCli.Core;

public interface IOpenAiResponsesGateway
{
    OpenAiResponseEnvelope CreateAgentResponse(
        OpenAiAgentRequest request,
        CancellationToken cancellationToken = default);

    OpenAiResponseEnvelope CreateResponse(
        string model,
        string prompt,
        string? instructions = null,
        CancellationToken cancellationToken = default);

    IEnumerable<OpenAiStreamingResponseUpdate> CreateResponseStreaming(
        string model,
        string prompt,
        string? instructions = null,
        CancellationToken cancellationToken = default);
}
