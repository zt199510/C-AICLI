namespace CSharpAiCli.Core;

public sealed class TerminalChatStreamingRenderer : IChatStreamingRenderer
{
    private readonly TextWriter writer;
    private bool started;

    public TerminalChatStreamingRenderer(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        this.writer = writer;
    }

    public void Start(CliEnvironmentSnapshot snapshot, string provider, string model)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        writer.WriteLine($"{ProductInfo.DisplayName} chat");
        writer.WriteLine($"workspace: {snapshot.CurrentDirectory}");
        writer.WriteLine($"workspace status: {FormatWorkspaceStatus(snapshot.WorkspaceStatus)}");
        writer.WriteLine($"api key: {(snapshot.Configuration.HasApiKey ? "present" : "missing")}");
        writer.WriteLine($"api key source: {snapshot.Configuration.ApiKeySource}");
        writer.WriteLine("status: streaming");
        writer.WriteLine($"provider: {provider}");
        writer.WriteLine($"model: {model}");
        writer.WriteLine();

        started = true;
    }

    public void WriteDelta(string textDelta)
    {
        if (string.IsNullOrEmpty(textDelta))
        {
            return;
        }

        writer.Write(textDelta);
    }

    public void Complete(ChatResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        writer.WriteLine();
        writer.WriteLine();
        writer.WriteLine("status: completed");
        writer.WriteLine($"provider: {response.Provider}");
        writer.WriteLine($"model: {response.Model}");
        writer.WriteLine($"responseId: {response.ResponseId}");
    }

    public void Fail(CliEnvironmentSnapshot snapshot, ModelError error)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(error);

        if (!started)
        {
            writer.Write(ChatModelReport.Create(snapshot, ChatModelResult.Failure(error)).ToDisplayText());
            return;
        }

        writer.WriteLine();
        writer.WriteLine();
        writer.WriteLine("status: failed");
        writer.WriteLine($"provider: {error.Provider}");
        writer.WriteLine($"operation: {error.Operation}");
        writer.WriteLine($"statusCode: {(error.StatusCode.HasValue ? error.StatusCode.Value.ToString() : "none")}");
        writer.WriteLine($"localErrorCode: {error.LocalErrorCode ?? "none"}");
        writer.WriteLine($"safeMessage: {error.SafeMessage}");
        writer.WriteLine($"retryable: {error.Retryable.ToString().ToLowerInvariant()}");
    }

    private static string FormatWorkspaceStatus(WorkspaceStatus status)
    {
        return status switch
        {
            WorkspaceStatus.Ready => "ready",
            WorkspaceStatus.Missing => "missing",
            WorkspaceStatus.NotDirectory => "not directory",
            _ => "unknown"
        };
    }
}
