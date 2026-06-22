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
        List<AgentRunEvent> events = [];
        AgentModelTurn turn = model.Start(request, cancellationToken);
        RecordModelTurn(events, turn);

        for (int iteration = 0; iteration < maxIterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (turn.IsFinal)
            {
                RecordFinalResponse(events, turn.FinalText!);
                return AgentRunResult.Success(turn.FinalText!, recordedToolCalls, events);
            }

            if (turn.ToolCalls.Count == 0)
            {
                AgentError error = new(
                    "agent-empty-turn",
                    "Agent model returned neither a final response nor tool calls.",
                    Retryable: false);
                RecordError(events, error);
                return AgentRunResult.Failure(
                    error,
                    recordedToolCalls,
                    events);
            }

            List<AgentToolCallResult> toolResults = [];
            foreach (AgentToolCallRequest toolCall in turn.ToolCalls)
            {
                RecordToolCall(events, toolCall);
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
                RecordToolResult(events, toolCall, executionResult);
            }

            turn = model.Continue(request, toolResults, cancellationToken);
            RecordModelTurn(events, turn);
        }

        AgentError loopLimitError = new(
            "agent-loop-limit-reached",
            "Agent loop reached the maximum iteration limit.",
            Retryable: false);
        RecordError(events, loopLimitError);
        return AgentRunResult.Failure(
            loopLimitError,
            recordedToolCalls,
            events);
    }

    private void RecordModelTurn(List<AgentRunEvent> events, AgentModelTurn turn)
    {
        Dictionary<string, string> payload = new()
        {
            ["toolCallCount"] = turn.ToolCalls.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["isFinal"] = turn.IsFinal ? "true" : "false"
        };

        events.Add(new AgentRunEvent(
            Type: "model.turn",
            Sequence: events.Count,
            Timestamp: utcNowProvider(),
            Summary: turn.IsFinal ? turn.FinalText : null,
            Payload: payload));
    }

    private void RecordToolCall(List<AgentRunEvent> events, AgentToolCallRequest toolCall)
    {
        events.Add(new AgentRunEvent(
            Type: "tool.call",
            Sequence: events.Count,
            Timestamp: utcNowProvider(),
            Message: $"Calling tool '{toolCall.ToolName}'.",
            Payload: new Dictionary<string, string>
            {
                ["callId"] = toolCall.CallId,
                ["toolName"] = toolCall.ToolName,
                ["argumentsJson"] = toolCall.ArgumentsJson
            }));
    }

    private void RecordToolResult(
        List<AgentRunEvent> events,
        AgentToolCallRequest toolCall,
        ToolExecutionResult result)
    {
        events.Add(new AgentRunEvent(
            Type: "tool.result",
            Sequence: events.Count,
            Timestamp: utcNowProvider(),
            Message: result.Succeeded
                ? $"Tool '{toolCall.ToolName}' completed."
                : $"Tool '{toolCall.ToolName}' failed.",
            Summary: result.Summary,
            Payload: new Dictionary<string, string>
            {
                ["callId"] = toolCall.CallId,
                ["toolName"] = toolCall.ToolName,
                ["succeeded"] = result.Succeeded ? "true" : "false"
            },
            ErrorCode: result.ErrorCode,
            ApprovalStatus: result.ApprovalStatus));
    }

    private void RecordFinalResponse(List<AgentRunEvent> events, string finalText)
    {
        events.Add(new AgentRunEvent(
            Type: "final.response",
            Sequence: events.Count,
            Timestamp: utcNowProvider(),
            Summary: finalText));
    }

    private void RecordError(List<AgentRunEvent> events, AgentError error)
    {
        events.Add(new AgentRunEvent(
            Type: "agent.error",
            Sequence: events.Count,
            Timestamp: utcNowProvider(),
            Message: error.SafeMessage,
            ErrorCode: error.LocalErrorCode));
    }
}
