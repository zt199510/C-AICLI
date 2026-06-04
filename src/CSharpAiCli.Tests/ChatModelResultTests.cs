using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ChatModelResultTests
{
    [Fact]
    public void Success_marks_result_successful_and_exposes_response()
    {
        ChatResponse response = new(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_123",
            Text: "hello");

        ChatModelResult result = ChatModelResult.Success(response);

        Assert.True(result.IsSuccess);
        Assert.Equal(response, result.Response);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Failure_marks_result_failed_and_exposes_safe_error()
    {
        ModelError error = new(
            Provider: "openai",
            Operation: "responses.create",
            StatusCode: null,
            LocalErrorCode: "missing-openai-api-key",
            SafeMessage: "OpenAI API key is missing.",
            Retryable: false);

        ChatModelResult result = ChatModelResult.Failure(error);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Response);
        Assert.Equal(error, result.Error);
    }
}
