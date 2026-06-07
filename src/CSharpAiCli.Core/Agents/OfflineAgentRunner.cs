namespace CSharpAiCli.Core;

public sealed class OfflineAgentRunner : IAgentRunner
{
    private readonly IToolCallingModel model;
    private readonly IToolExecutor toolExecutor;
    private readonly Func<DateTimeOffset> utcNowProvider;
    private readonly int maxIterations;

    public OfflineAgentRunner(
        IToolCallingModel model,
        IToolExecutor toolExecutor,
        Func<DateTimeOffset>? utcNowProvider = null,
        int maxIterations = 8)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(toolExecutor);
        if (maxIterations <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxIterations), "Max iterations must be greater than zero.");
        }

        this.model = model;
        this.toolExecutor = toolExecutor;
        this.utcNowProvider = utcNowProvider ?? (() => DateTimeOffset.UtcNow);
        this.maxIterations = maxIterations;
    }

    public AgentRunResult Run(
        AgentRunRequest request,
        ConversationTranscript? transcript = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        List<ConversationToolCall> recordedToolCalls = [];
        AgentModelTurn turn = model.Start(request, cancellationToken);

        for (int iteration = 0; iteration < maxIterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (turn.IsFinal)
            {
                return AgentRunResult.Success(turn.FinalText!, recordedToolCalls);
            }

            if (turn.ToolCalls.Count == 0)
            {
                return AgentRunResult.Failure(
                    new AgentError(
                        "agent-empty-turn",
                        "Agent model returned neither a final response nor tool calls.",
                        Retryable: false),
                    recordedToolCalls);
            }

            List<AgentToolCallResult> toolResults = [];
            foreach (AgentToolCallRequest toolCall in turn.ToolCalls)
            {
                ToolExecutionContext context = new(
                    CallId: toolCall.CallId,
                    Workspace: request.Workspace,
                    ArgumentsJson: toolCall.ArgumentsJson);
                ToolExecutionResult executionResult = toolExecutor.Execute(
                    toolCall.ToolName,
                    context,
                    cancellationToken);
                AgentToolCallResult toolResult = new(toolCall, executionResult);
                toolResults.Add(toolResult);

                DateTimeOffset nowUtc = utcNowProvider();
                ConversationToolCall transcriptToolCall = ConversationToolCall.FromExecution(
                    toolCall.CallId,
                    toolCall.ToolName,
                    context.ArgumentsJson,
                    executionResult,
                    nowUtc);
                recordedToolCalls.Add(transcriptToolCall);
                transcript?.AddToolCall(transcriptToolCall);
            }

            turn = model.Continue(request, toolResults, cancellationToken);
        }

        return AgentRunResult.Failure(
            new AgentError(
                "agent-loop-limit-reached",
                "Agent loop reached the maximum iteration limit.",
                Retryable: false),
            recordedToolCalls);
    }
}
