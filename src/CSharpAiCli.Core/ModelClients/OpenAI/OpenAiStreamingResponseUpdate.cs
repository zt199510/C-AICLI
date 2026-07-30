namespace CSharpAiCli.Core;

public enum OpenAiStreamingResponseUpdateKind
{
    OutputTextDelta,
    Completed
}

public sealed record OpenAiStreamingResponseUpdate(
    OpenAiStreamingResponseUpdateKind Kind,
    string? TextDelta,
    string? ResponseId,
    string? Model,
    OpenAiResponseEnvelope? Response = null)
{
    public static OpenAiStreamingResponseUpdate OutputTextDelta(string textDelta)
    {
        return new OpenAiStreamingResponseUpdate(
            Kind: OpenAiStreamingResponseUpdateKind.OutputTextDelta,
            TextDelta: textDelta,
            ResponseId: null,
            Model: null,
            Response: null);
    }

    public static OpenAiStreamingResponseUpdate Completed(string responseId, string model)
    {
        return new OpenAiStreamingResponseUpdate(
            Kind: OpenAiStreamingResponseUpdateKind.Completed,
            TextDelta: null,
            ResponseId: responseId,
            Model: model,
            Response: null);
    }

    public static OpenAiStreamingResponseUpdate Completed(OpenAiResponseEnvelope response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return new OpenAiStreamingResponseUpdate(
            Kind: OpenAiStreamingResponseUpdateKind.Completed,
            TextDelta: null,
            ResponseId: response.ResponseId,
            Model: response.Model,
            Response: response);
    }
}
