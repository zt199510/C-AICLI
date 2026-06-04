namespace CSharpAiCli.Core;

public interface IOpenAiResponsesGateway
{
    OpenAiResponseEnvelope CreateResponse(
        string model,
        string prompt,
        CancellationToken cancellationToken = default);
}
