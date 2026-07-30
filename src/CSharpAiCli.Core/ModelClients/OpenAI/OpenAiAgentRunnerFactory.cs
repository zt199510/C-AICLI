namespace CSharpAiCli.Core;

public sealed class OpenAiAgentRunnerFactory
{
    private readonly Func<string, string, IOpenAiResponsesGateway> gatewayFactory;

    public OpenAiAgentRunnerFactory()
        : this((apiKey, baseUrl) => new SdkOpenAiResponsesGateway(apiKey, baseUrl))
    {
    }

    public OpenAiAgentRunnerFactory(
        Func<string, string, IOpenAiResponsesGateway> gatewayFactory)
    {
        this.gatewayFactory = gatewayFactory ?? throw new ArgumentNullException(nameof(gatewayFactory));
    }

    public IAgentRunner Create(
        CliEnvironmentSnapshot snapshot,
        IToolRegistry registry,
        IToolExecutor executor,
        IAgentRunEventObserver? eventObserver = null,
        IProviderAttemptObserver? providerAttemptObserver = null,
        IProviderRetryDelay? retryDelay = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(executor);

        string model = snapshot.Configuration.Model;
        if (string.IsNullOrWhiteSpace(model) ||
            string.Equals(model, "not configured", StringComparison.OrdinalIgnoreCase))
        {
            return Failure(
                "missing-model",
                "Model is not configured. Set model in .caicli/config.json before running exec.",
                eventObserver);
        }

        SecretValue? apiKey = snapshot.Configuration.ApiKey;
        if (apiKey is null)
        {
            return Failure(
                "missing-openai-api-key",
                "OpenAI API key is missing. Set OPENAI_API_KEY or user config apiKey.",
                eventObserver);
        }

        if (!IsSupportedApiKeySource(snapshot.Configuration.ApiKeySource))
        {
            return Failure(
                "unsupported-api-key-source",
                "Workspace config apiKey is not used for model calls. Set OPENAI_API_KEY or user config apiKey.",
                eventObserver);
        }

        try
        {
            IOpenAiResponsesGateway gateway = gatewayFactory(
                apiKey.Value,
                snapshot.Configuration.BaseUrl);
            return new OpenAiAgentRunner(
                model,
                snapshot.Instructions.Instructions,
                registry,
                gateway,
                executor,
                eventObserver,
                providerAttemptObserver,
                retryDelay);
        }
        catch
        {
            return Failure(
                "agent-factory-unavailable",
                "OpenAI agent runtime could not be configured safely.",
                eventObserver);
        }
    }

    public static bool IsSupportedApiKeySource(string apiKeySource) =>
        apiKeySource is "OPENAI_API_KEY" or "user config";

    private static FailedAgentRunner Failure(
        string code,
        string safeMessage,
        IAgentRunEventObserver? eventObserver) =>
        new(new AgentError(code, safeMessage, Retryable: false), eventObserver);
}
