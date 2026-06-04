namespace CSharpAiCli.Core;

public interface IChatStreamingRenderer
{
    void Start(CliEnvironmentSnapshot snapshot, string provider, string model);

    void WriteDelta(string textDelta);

    void Complete(ChatResponse response);

    void Fail(CliEnvironmentSnapshot snapshot, ModelError error);
}
