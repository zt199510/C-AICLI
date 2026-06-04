namespace CSharpAiCli.Core;

public interface IChatModelClient
{
    ChatModelResult Send(ChatRequest request, CancellationToken cancellationToken = default);
}
