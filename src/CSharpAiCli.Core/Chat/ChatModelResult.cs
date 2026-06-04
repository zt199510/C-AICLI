namespace CSharpAiCli.Core;

public sealed record ChatModelResult(ChatResponse? Response, ModelError? Error)
{
    public bool IsSuccess => Response is not null;

    public static ChatModelResult Success(ChatResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return new ChatModelResult(response, Error: null);
    }

    public static ChatModelResult Failure(ModelError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new ChatModelResult(Response: null, Error: error);
    }
}
