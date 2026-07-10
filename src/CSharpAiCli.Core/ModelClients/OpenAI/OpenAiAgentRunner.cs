using System.ClientModel;

namespace CSharpAiCli.Core;

public sealed class OpenAiAgentRunner : IAgentRunner
{
    private readonly IAgentRunner innerRunner;

    public OpenAiAgentRunner(
        string model,
        string? instructions,
        IToolRegistry registry,
        IOpenAiResponsesGateway gateway,
        IToolExecutor toolExecutor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(toolExecutor);

        OpenAiToolCallingModel toolCallingModel = new(model, instructions, registry, gateway);
        innerRunner = new OfflineAgentRunner(toolCallingModel, toolExecutor);
    }

    public AgentRunResult Run(
        AgentRunRequest request,
        ConversationTranscript? transcript = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return innerRunner.Run(request, transcript, cancellationToken);
        }
        catch (ClientResultException exception)
        {
            return CreateFailure(
                new AgentError(
                    "openai-http-error",
                    SafeHttpMessage(exception.Status),
                    Retryable: exception.Status is 429 or >= 500),
                new Dictionary<string, string>
                {
                    ["statusCode"] = exception.Status.ToString(System.Globalization.CultureInfo.InvariantCulture)
                });
        }
        catch (NotSupportedException exception) when (IsAgentGatewayNotSupported(exception))
        {
            return CreateFailure(new AgentError(
                "agent-backend-unavailable",
                "Agent backend is unavailable.",
                Retryable: false));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return CreateFailure(new AgentError(
                "agent-model-call-canceled",
                "Agent model call was canceled before it completed.",
                Retryable: true));
        }
        catch
        {
            return CreateFailure(new AgentError(
                "openai-client-error",
                "OpenAI agent model call failed before a response was completed.",
                Retryable: true));
        }
    }

    private static AgentRunResult CreateFailure(
        AgentError error,
        IReadOnlyDictionary<string, string>? payload = null)
    {
        string stopReason = AgentStopReason.FromErrorCode(error.LocalErrorCode);
        AgentRunEvent errorEvent = new(
            Type: "agent.error",
            Sequence: 0,
            Timestamp: DateTimeOffset.UtcNow,
            Message: error.SafeMessage,
            Payload: payload,
            ErrorCode: error.LocalErrorCode,
            Status: DiagnosticEventStatus.Failure,
            StopReason: stopReason);

        return AgentRunResult.Failure(
            error,
            [],
            [errorEvent],
            stopReason: stopReason,
            status: DiagnosticEventStatus.Failure);
    }

    private static bool IsAgentGatewayNotSupported(NotSupportedException exception)
    {
        return exception.Message.Contains(
            "OpenAI agent tool continuation is not implemented",
            StringComparison.OrdinalIgnoreCase);
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
            _ => "OpenAI agent model call failed before a response was completed."
        };
    }
}
