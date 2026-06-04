using System.ClientModel;

namespace CSharpAiCli.Core;

public sealed class OpenAiResponsesModelClient : IChatModelClient
{
    private const string Provider = "openai";
    private const string Operation = "responses.create";

    private readonly CliEnvironmentSnapshot snapshot;
    private readonly Func<string, IOpenAiResponsesGateway> gatewayFactory;

    public OpenAiResponsesModelClient(
        CliEnvironmentSnapshot snapshot,
        Func<string, IOpenAiResponsesGateway> gatewayFactory)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(gatewayFactory);

        this.snapshot = snapshot;
        this.gatewayFactory = gatewayFactory;
    }

    public static OpenAiResponsesModelClient Create(CliEnvironmentSnapshot snapshot)
    {
        return new OpenAiResponsesModelClient(snapshot, apiKey => new SdkOpenAiResponsesGateway(apiKey));
    }

    public ChatModelResult Send(ChatRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string prompt = request.Prompt.Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return Failure(
                statusCode: null,
                localErrorCode: "empty-prompt",
                safeMessage: "Chat prompt is empty. Pass a prompt as caicli chat \"<prompt>\".",
                retryable: false);
        }

        string model = snapshot.Configuration.Model;
        if (string.IsNullOrWhiteSpace(model)
            || string.Equals(model, "not configured", StringComparison.OrdinalIgnoreCase))
        {
            return Failure(
                statusCode: null,
                localErrorCode: "missing-model",
                safeMessage: "Model is not configured. Set model in .caicli/config.json before running chat.",
                retryable: false);
        }

        SecretValue? apiKey = snapshot.Configuration.ApiKey;
        if (apiKey is null)
        {
            return Failure(
                statusCode: null,
                localErrorCode: "missing-openai-api-key",
                safeMessage: "OpenAI API key is missing. Set OPENAI_API_KEY or user config apiKey.",
                retryable: false);
        }

        if (!IsSupportedApiKeySource(snapshot.Configuration.ApiKeySource))
        {
            return Failure(
                statusCode: null,
                localErrorCode: "unsupported-api-key-source",
                safeMessage: "Workspace config apiKey is not used for model calls. Set OPENAI_API_KEY or user config apiKey.",
                retryable: false);
        }

        try
        {
            IOpenAiResponsesGateway gateway = gatewayFactory(apiKey.Value);
            OpenAiResponseEnvelope response = gateway.CreateResponse(model, prompt, cancellationToken);

            if (string.IsNullOrWhiteSpace(response.Text))
            {
                return Failure(
                    statusCode: null,
                    localErrorCode: "empty-model-response",
                    safeMessage: "Model response did not include output text.",
                    retryable: true);
            }

            return ChatModelResult.Success(new ChatResponse(
                Provider: Provider,
                Model: response.Model,
                ResponseId: response.ResponseId,
                Text: response.Text));
        }
        catch (ClientResultException exception)
        {
            return Failure(
                statusCode: exception.Status,
                localErrorCode: null,
                safeMessage: SafeHttpMessage(exception.Status),
                retryable: exception.Status is 429 or >= 500);
        }
        catch (OperationCanceledException)
        {
            return Failure(
                statusCode: null,
                localErrorCode: "model-call-canceled",
                safeMessage: "Model call was canceled before it completed.",
                retryable: true);
        }
        catch
        {
            return Failure(
                statusCode: null,
                localErrorCode: "openai-client-error",
                safeMessage: "OpenAI model call failed before a response was completed.",
                retryable: true);
        }
    }

    private static bool IsSupportedApiKeySource(string apiKeySource)
    {
        return apiKeySource is "OPENAI_API_KEY" or "user config";
    }

    private static ChatModelResult Failure(
        int? statusCode,
        string? localErrorCode,
        string safeMessage,
        bool retryable)
    {
        return ChatModelResult.Failure(new ModelError(
            Provider: Provider,
            Operation: Operation,
            StatusCode: statusCode,
            LocalErrorCode: localErrorCode,
            SafeMessage: safeMessage,
            Retryable: retryable));
    }

    private static string SafeHttpMessage(int statusCode)
    {
        return statusCode switch
        {
            401 => "OpenAI rejected the API key. Check OPENAI_API_KEY or user config apiKey.",
            403 => "OpenAI rejected this request for the configured API key.",
            404 => "OpenAI model or endpoint was not found. Check the configured model.",
            429 => "OpenAI rate limit or quota was reached. Try again later.",
            >= 500 => "OpenAI service returned a temporary server error. Try again later.",
            _ => "OpenAI model call failed before a response was completed."
        };
    }
}
