namespace CSharpAiCli.Core;

public sealed class OpenAiToolCallingModel : IToolCallingModel, IProviderAttemptContextReceiver
{
    private readonly string model;
    private readonly string? instructions;
    private readonly IToolRegistry registry;
    private readonly IOpenAiResponsesGateway gateway;
    private readonly IProviderAttemptObserver? attemptObserver;
    private readonly List<OpenAiToolResultInput> toolResultHistory = [];
    private string? currentPrompt;
    private int currentAttempt = 1;
    private int maxAdditionalRetries = ProviderRequestRetryLimits.MaxAdditionalRetries;

    public OpenAiToolCallingModel(
        string model,
        string? instructions,
        IToolRegistry registry,
        IOpenAiResponsesGateway gateway,
        IProviderAttemptObserver? attemptObserver = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(gateway);

        this.model = model;
        this.instructions = instructions;
        this.registry = registry;
        this.gateway = gateway;
        this.attemptObserver = attemptObserver;
    }

    public AgentModelTurn Start(
        AgentRunRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string prompt = request.TranscriptContext is null
            ? request.Prompt
            : ConversationTranscriptContextFormatter.FormatWithCurrentPrompt(
                request.TranscriptContext,
                request.Prompt);
        if (request.TaskContext is not null)
        {
            prompt = AgentTaskContextPromptFormatter.FormatWithCurrentPrompt(
                request.TaskContext,
                prompt,
                request.ExpertProfile);
        }
        else
        {
            prompt = ExpertProfilePromptFormatter.FormatWithCurrentPrompt(
                request.ExpertProfile,
                prompt);
        }

        currentPrompt = prompt;
        toolResultHistory.Clear();
        OpenAiAgentRequest agentRequest = new(
            Model: model,
            Prompt: prompt,
            PreviousResponseId: null,
            Instructions: request.Instructions ?? instructions,
            Tools: OpenAiToolDefinitionMapper.FromRegistry(registry),
            ToolResults: []);

        OpenAiResponseEnvelope response = SendStreaming(agentRequest, cancellationToken);
        return OpenAiResponseParser.ToAgentModelTurn(response);
    }

    public AgentModelTurn Continue(
        AgentRunRequest request,
        IReadOnlyList<AgentToolCallResult> toolResults,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(toolResults);

        if (currentPrompt is null)
        {
            throw new InvalidOperationException(
                "OpenAI tool calling model must be started before continuing.");
        }

        foreach (AgentToolCallResult toolResult in toolResults)
        {
            if (toolResultHistory.All(existing => existing.CallId != toolResult.Request.CallId))
            {
                toolResultHistory.Add(ToToolResultInput(toolResult));
            }
        }
        OpenAiAgentRequest agentRequest = new(
            Model: model,
            Prompt: currentPrompt,
            PreviousResponseId: null,
            Instructions: request.Instructions ?? instructions,
            Tools: OpenAiToolDefinitionMapper.FromRegistry(registry),
            ToolResults: toolResultHistory.ToArray());

        OpenAiResponseEnvelope response = SendStreaming(agentRequest, cancellationToken);
        return OpenAiResponseParser.ToAgentModelTurn(response);
    }

    public void BeginProviderAttempt(int attempt, int maximumAdditionalRetries)
    {
        currentAttempt = attempt;
        maxAdditionalRetries = maximumAdditionalRetries;
    }

    private OpenAiResponseEnvelope SendStreaming(
        OpenAiAgentRequest request,
        CancellationToken cancellationToken)
    {
        attemptObserver?.OnProviderAttempt(new ProviderAttemptEvent(
            currentAttempt,
            maxAdditionalRetries,
            ProviderAttemptPhase.Thinking));
        const int streamingSnapshotCharacterInterval = 96;
        var text = new System.Text.StringBuilder();
        int emittedLength = 0;
        long lastEmissionTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        OpenAiResponseEnvelope? completed = null;
        foreach (OpenAiStreamingResponseUpdate update in gateway.CreateAgentResponseStreaming(
            request,
            cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (update.Kind == OpenAiStreamingResponseUpdateKind.OutputTextDelta)
            {
                string delta = update.TextDelta ?? string.Empty;
                if (delta.Length == 0)
                {
                    continue;
                }
                text.Append(delta);
                bool emissionDelayElapsed = System.Diagnostics.Stopwatch.GetElapsedTime(lastEmissionTimestamp) >=
                    TimeSpan.FromMilliseconds(120);
                if (emittedLength == 0 ||
                    text.Length - emittedLength >= streamingSnapshotCharacterInterval ||
                    emissionDelayElapsed)
                {
                    EmitStreaming(text.ToString());
                    emittedLength = text.Length;
                    lastEmissionTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
                }
            }
            else if (update.Kind == OpenAiStreamingResponseUpdateKind.Completed)
            {
                completed = update.Response ?? new OpenAiResponseEnvelope(
                    update.ResponseId ?? "unknown",
                    update.Model ?? request.Model,
                    text.ToString());
            }
        }

        if (completed is null)
        {
            throw new IOException("Provider stream ended before completion.");
        }
        if (text.Length == 0 && !string.IsNullOrEmpty(completed.Text))
        {
            text.Append(completed.Text);
            EmitStreaming(text.ToString());
        }
        else if (text.Length > emittedLength)
        {
            EmitStreaming(text.ToString());
        }
        if (text.Length > 0 && string.IsNullOrWhiteSpace(completed.Text))
        {
            completed = completed with { Text = text.ToString() };
        }
        return completed;

        void EmitStreaming(string content)
        {
            attemptObserver?.OnProviderAttempt(new ProviderAttemptEvent(
                currentAttempt,
                maxAdditionalRetries,
                ProviderAttemptPhase.Streaming,
                HasStreamContent: true,
                Content: content));
        }
    }

    private static OpenAiToolResultInput ToToolResultInput(AgentToolCallResult toolResult)
    {
        return new OpenAiToolResultInput(
            CallId: toolResult.Request.CallId,
            ToolName: toolResult.Request.ToolName,
            Succeeded: toolResult.Result.Succeeded,
            Summary: toolResult.Result.Summary,
            ErrorCode: toolResult.Result.ErrorCode,
            ApprovalStatus: toolResult.Result.ApprovalStatus,
            Retryable: toolResult.Result.Retryable,
            StructuredPayload: toolResult.Result.StructuredPayload,
            ArgumentsJson: toolResult.Request.ArgumentsJson);
    }
}
