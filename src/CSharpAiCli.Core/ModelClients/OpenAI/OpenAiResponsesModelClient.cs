using System.ClientModel;
using System.Text;

namespace CSharpAiCli.Core;

public sealed class OpenAiResponsesModelClient : IChatModelClient
{
    private const string Provider = "openai";
    private const string Operation = "responses.create";

    private readonly CliEnvironmentSnapshot snapshot;
    private readonly Func<string, string, IOpenAiResponsesGateway> gatewayFactory;

    public OpenAiResponsesModelClient(
        CliEnvironmentSnapshot snapshot,
        Func<string, IOpenAiResponsesGateway> gatewayFactory)
        : this(snapshot, AdaptGatewayFactory(gatewayFactory))
    {
    }

    public OpenAiResponsesModelClient(
        CliEnvironmentSnapshot snapshot,
        Func<string, string, IOpenAiResponsesGateway> gatewayFactory)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(gatewayFactory);

        this.snapshot = snapshot;
        this.gatewayFactory = gatewayFactory;
    }

    private static Func<string, string, IOpenAiResponsesGateway> AdaptGatewayFactory(
        Func<string, IOpenAiResponsesGateway> gatewayFactory)
    {
        ArgumentNullException.ThrowIfNull(gatewayFactory);
        return (apiKey, _) => gatewayFactory(apiKey);
    }

    public static OpenAiResponsesModelClient Create(CliEnvironmentSnapshot snapshot)
    {
        return new OpenAiResponsesModelClient(
            snapshot,
            (apiKey, baseUrl) => new SdkOpenAiResponsesGateway(apiKey, baseUrl));
    }

    public ChatModelResult Send(ChatRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        ModelError? validationError = ValidateRequest(request, out string prompt, out string model, out SecretValue? apiKey);
        if (validationError is not null)
        {
            return ChatModelResult.Failure(validationError);
        }

        try
        {
            IOpenAiResponsesGateway gateway = gatewayFactory(apiKey!.Value, snapshot.Configuration.BaseUrl);
            OpenAiResponseEnvelope response = gateway.CreateResponse(
                model,
                prompt,
                request.Instructions,
                cancellationToken);

            if (string.IsNullOrWhiteSpace(response.Text))
            {
                return FailureResult(
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
        catch (Exception exception)
        {
            return MapException(exception);
        }
    }

    public ChatModelResult SendStreaming(
        ChatRequest request,
        IChatStreamingRenderer renderer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(renderer);

        ModelError? validationError = ValidateRequest(request, out string prompt, out string model, out SecretValue? apiKey);
        if (validationError is not null)
        {
            renderer.Fail(snapshot, validationError);
            return ChatModelResult.Failure(validationError);
        }

        renderer.Start(snapshot, Provider, model);

        ChatModelResult? failureResult = null;
        ChatResponse? response = null;

        try
        {
            IOpenAiResponsesGateway gateway = gatewayFactory(apiKey!.Value, snapshot.Configuration.BaseUrl);
            StringBuilder text = new();
            string responseId = "unknown";
            string responseModel = model;

            foreach (OpenAiStreamingResponseUpdate update in gateway.CreateResponseStreaming(
                model,
                prompt,
                request.Instructions,
                cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (update.Kind == OpenAiStreamingResponseUpdateKind.OutputTextDelta)
                {
                    string? delta = update.TextDelta;
                    if (string.IsNullOrEmpty(delta))
                    {
                        continue;
                    }

                    text.Append(delta);
                    renderer.WriteDelta(delta);
                }
                else if (update.Kind == OpenAiStreamingResponseUpdateKind.Completed)
                {
                    if (!string.IsNullOrWhiteSpace(update.ResponseId))
                    {
                        responseId = update.ResponseId;
                    }

                    if (!string.IsNullOrWhiteSpace(update.Model))
                    {
                        responseModel = update.Model;
                    }
                }
            }

            string responseText = text.ToString();
            if (string.IsNullOrWhiteSpace(responseText))
            {
                failureResult = FailureResult(
                    statusCode: null,
                    localErrorCode: "empty-model-response",
                    safeMessage: "Model response did not include output text.",
                    retryable: true);
            }
            else
            {
                response = new ChatResponse(
                    Provider: Provider,
                    Model: responseModel,
                    ResponseId: responseId,
                    Text: responseText);
            }
        }
        catch (Exception exception)
        {
            failureResult = MapException(exception);
        }

        if (failureResult is not null)
        {
            renderer.Fail(snapshot, failureResult.Error!);
            return failureResult;
        }

        renderer.Complete(response!);
        return ChatModelResult.Success(response!);
    }

    private ModelError? ValidateRequest(
        ChatRequest request,
        out string prompt,
        out string model,
        out SecretValue? apiKey)
    {
        string currentPrompt = request.Prompt.Trim();
        prompt = currentPrompt;
        model = snapshot.Configuration.Model;
        apiKey = snapshot.Configuration.ApiKey;

        if (string.IsNullOrWhiteSpace(currentPrompt))
        {
            return CreateError(
                statusCode: null,
                localErrorCode: "empty-prompt",
                safeMessage: "Chat prompt is empty. Pass a prompt as caicli chat \"<prompt>\".",
                retryable: false);
        }

        if (request.TranscriptContext is not null)
        {
            prompt = ConversationTranscriptContextFormatter.FormatWithCurrentPrompt(
                request.TranscriptContext,
                currentPrompt);
        }

        if (string.IsNullOrWhiteSpace(model)
            || string.Equals(model, "not configured", StringComparison.OrdinalIgnoreCase))
        {
            return CreateError(
                statusCode: null,
                localErrorCode: "missing-model",
                safeMessage: "Model is not configured. Set model in .caicli/config.json before running chat.",
                retryable: false);
        }

        if (apiKey is null)
        {
            return CreateError(
                statusCode: null,
                localErrorCode: "missing-openai-api-key",
                safeMessage: "OpenAI API key is missing. Set OPENAI_API_KEY or user config apiKey.",
                retryable: false);
        }

        if (!IsSupportedApiKeySource(snapshot.Configuration.ApiKeySource))
        {
            return CreateError(
                statusCode: null,
                localErrorCode: "unsupported-api-key-source",
                safeMessage: "Workspace config apiKey is not used for model calls. Set OPENAI_API_KEY or user config apiKey.",
                retryable: false);
        }

        return null;
    }

    private static ChatModelResult MapException(Exception exception)
    {
        if (exception is ClientResultException clientResultException)
        {
            return FailureResult(
                statusCode: clientResultException.Status,
                localErrorCode: null,
                safeMessage: SafeHttpMessage(clientResultException.Status),
                retryable: clientResultException.Status is 429 or >= 500);
        }

        if (exception is OperationCanceledException)
        {
            return FailureResult(
                statusCode: null,
                localErrorCode: "model-call-canceled",
                safeMessage: "Model call was canceled before it completed.",
                retryable: true);
        }

        return FailureResult(
            statusCode: null,
            localErrorCode: "openai-client-error",
            safeMessage: "OpenAI model call failed before a response was completed.",
            retryable: true);
    }

    private static bool IsSupportedApiKeySource(string apiKeySource)
    {
        return apiKeySource is "OPENAI_API_KEY" or "user config";
    }

    private static ChatModelResult FailureResult(
        int? statusCode,
        string? localErrorCode,
        string safeMessage,
        bool retryable)
    {
        return ChatModelResult.Failure(CreateError(
            statusCode,
            localErrorCode,
            safeMessage,
            retryable));
    }

    private static ModelError CreateError(
        int? statusCode,
        string? localErrorCode,
        string safeMessage,
        bool retryable)
    {
        return new ModelError(
            Provider: Provider,
            Operation: Operation,
            StatusCode: statusCode,
            LocalErrorCode: localErrorCode,
            SafeMessage: safeMessage,
            Retryable: retryable);
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
