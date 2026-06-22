namespace CSharpAiCli.Core;

public sealed class OpenAiToolCallingModel : IToolCallingModel
{
    private readonly string model;
    private readonly string? instructions;
    private readonly IToolRegistry registry;
    private readonly IOpenAiResponsesGateway gateway;
    private string? previousResponseId;

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

        OpenAiAgentRequest agentRequest = new(
            Model: model,
            Prompt: request.Prompt,
            PreviousResponseId: null,
            Instructions: request.Instructions ?? instructions,
            Tools: OpenAiToolDefinitionMapper.FromRegistry(registry),
            ToolResults: []);

        OpenAiResponseEnvelope response = gateway.CreateAgentResponse(
            agentRequest,
            cancellationToken);
        previousResponseId = response.ResponseId;
        return OpenAiResponseParser.ToAgentModelTurn(response);
    }

    public AgentModelTurn Continue(
        AgentRunRequest request,
        IReadOnlyList<AgentToolCallResult> toolResults,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(toolResults);

        OpenAiAgentRequest agentRequest = new(
            Model: model,
            Prompt: null,
            PreviousResponseId: previousResponseId,
            Instructions: request.Instructions ?? instructions,
            Tools: [],
            ToolResults: toolResults.Select(ToToolResultInput).ToArray());

        OpenAiResponseEnvelope response = gateway.CreateAgentResponse(
            agentRequest,
            cancellationToken);
        previousResponseId = response.ResponseId;
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
            ApprovalStatus: toolResult.Result.ApprovalStatus);
    }
}
