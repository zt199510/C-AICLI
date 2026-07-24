namespace CSharpAiCli.Core;

public sealed class OpenAiToolCallingModel : IToolCallingModel
{
    private readonly string model;
    private readonly string? instructions;
    private readonly IToolRegistry registry;
    private readonly IOpenAiResponsesGateway gateway;
    private readonly List<OpenAiToolResultInput> toolResultHistory = [];
    private string? currentPrompt;

    public OpenAiToolCallingModel(
        string model,
        string? instructions,
        IToolRegistry registry,
        IOpenAiResponsesGateway gateway)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(gateway);

        this.model = model;
        this.instructions = instructions;
        this.registry = registry;
        this.gateway = gateway;
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

        OpenAiResponseEnvelope response = gateway.CreateAgentResponse(
            agentRequest,
            cancellationToken);
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

        toolResultHistory.AddRange(toolResults.Select(ToToolResultInput));
        OpenAiAgentRequest agentRequest = new(
            Model: model,
            Prompt: currentPrompt,
            PreviousResponseId: null,
            Instructions: request.Instructions ?? instructions,
            Tools: OpenAiToolDefinitionMapper.FromRegistry(registry),
            ToolResults: toolResultHistory.ToArray());

        OpenAiResponseEnvelope response = gateway.CreateAgentResponse(
            agentRequest,
            cancellationToken);
        return OpenAiResponseParser.ToAgentModelTurn(response);
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
