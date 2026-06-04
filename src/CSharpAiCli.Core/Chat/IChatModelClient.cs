namespace CSharpAiCli.Core;

public interface IChatModelClient
{
    ChatModelResult Send(ChatRequest request, CancellationToken cancellationToken = default);

    ChatModelResult SendStreaming(
        ChatRequest request,
        IChatStreamingRenderer renderer,
        CancellationToken cancellationToken = default);
}
