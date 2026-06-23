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

        AgentRunLimits limits = (request.Limits ?? AgentRunLimits.Default).MergeWith(new AgentRunLimits(MaxTurns: maxIterations));
        DateTimeOffset deadlineUtc = utcNowProvider().Add(limits.OverallTimeout);
        List<ConversationToolCall> recordedToolCalls = [];
        List<AgentRunEvent> events = [];
        if (TryCreateTimeoutResult(deadlineUtc, recordedToolCalls, events, out AgentRunResult? timeoutResult))
        {
            return timeoutResult!;
        }

        if (!TryInvokeModelStart(request, limits, deadlineUtc, cancellationToken, recordedToolCalls, events, out AgentModelTurn? turn, out AgentRunResult? modelTimeoutResult))
        {
            return modelTimeoutResult!;
        }

        AgentModelTurn currentTurn = turn!;
        RecordModelTurn(events, currentTurn);
        int toolCallCount = 0;

        for (int iteration = 0; iteration < limits.MaxTurns; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryCreateTimeoutResult(deadlineUtc, recordedToolCalls, events, out timeoutResult))
            {
                return timeoutResult!;
            }

            if (currentTurn.IsFinal)
            {
                RecordFinalResponse(events, currentTurn.FinalText!);
                return AgentRunResult.Success(currentTurn.FinalText!, recordedToolCalls, events);
            }

            if (currentTurn.ToolCalls.Count == 0)
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
            foreach (AgentToolCallRequest toolCall in currentTurn.ToolCalls)
            {
                if (toolCallCount >= limits.MaxToolCalls)
                {
                    AgentError toolCallLimitError = new(
                        "agent-tool-call-limit-reached",
                        "Agent loop reached the maximum tool call limit.",
                        Retryable: false);
                    RecordError(events, toolCallLimitError);
                    return AgentRunResult.Failure(
                        toolCallLimitError,
                        recordedToolCalls,
                        events);
                }

                if (TryCreateTimeoutResult(deadlineUtc, recordedToolCalls, events, out timeoutResult))
                {
                    return timeoutResult!;
                }

                RecordToolCall(events, toolCall);
                ToolExecutionContext context = new(
                    CallId: toolCall.CallId,
                    Workspace: request.Workspace,
                    ArgumentsJson: toolCall.ArgumentsJson);
                if (!TryExecuteTool(
                    toolCall,
                    context,
                    deadlineUtc,
                    cancellationToken,
                    recordedToolCalls,
                    events,
                    out ToolExecutionResult executionResult,
                    out AgentRunResult? toolTimeoutResult))
                {
                    return toolTimeoutResult!;
                }

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
                toolCallCount++;
            }

            if (TryCreateTimeoutResult(deadlineUtc, recordedToolCalls, events, out timeoutResult))
            {
                return timeoutResult!;
            }

            if (!TryInvokeModelContinue(request, toolResults, limits, deadlineUtc, cancellationToken, recordedToolCalls, events, out turn, out modelTimeoutResult))
            {
                return modelTimeoutResult!;
            }

            currentTurn = turn!;
            RecordModelTurn(events, currentTurn);
            if (TryCreateTimeoutResult(deadlineUtc, recordedToolCalls, events, out timeoutResult))
            {
                return timeoutResult!;
            }

            if (currentTurn.IsFinal)
            {
                RecordFinalResponse(events, currentTurn.FinalText!);
                return AgentRunResult.Success(currentTurn.FinalText!, recordedToolCalls, events);
            }
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

    private bool TryInvokeModelStart(
        AgentRunRequest request,
        AgentRunLimits limits,
        DateTimeOffset deadlineUtc,
        CancellationToken cancellationToken,
        IReadOnlyList<ConversationToolCall> recordedToolCalls,
        List<AgentRunEvent> events,
        out AgentModelTurn? turn,
        out AgentRunResult? result)
    {
        TimeSpan remainingOverallTimeout = deadlineUtc - utcNowProvider();
        if (remainingOverallTimeout <= TimeSpan.Zero)
        {
            turn = null;
            result = CreateOverallTimeoutResult(recordedToolCalls, events);
            return false;
        }

        bool modelTimeoutWins = limits.ModelCallTimeout <= remainingOverallTimeout;
        using CancellationTokenSource overallTimeoutSource = new();
        using CancellationTokenSource modelTimeoutSource = new();
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            overallTimeoutSource.Token,
            modelTimeoutSource.Token);
        overallTimeoutSource.CancelAfter(remainingOverallTimeout);
        modelTimeoutSource.CancelAfter(limits.ModelCallTimeout);
        try
        {
            turn = model.Start(request, timeoutSource.Token);
            result = null;
            return true;
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            turn = null;
            if (modelTimeoutWins
                && (modelTimeoutSource.IsCancellationRequested
                    || overallTimeoutSource.IsCancellationRequested
                    || utcNowProvider() > deadlineUtc))
            {
                result = CreateModelCallTimeoutResult(recordedToolCalls, events);
                return false;
            }

            if (overallTimeoutSource.IsCancellationRequested || utcNowProvider() > deadlineUtc)
            {
                result = CreateOverallTimeoutResult(recordedToolCalls, events);
                return false;
            }

            if (modelTimeoutSource.IsCancellationRequested)
            {
                result = CreateModelCallTimeoutResult(recordedToolCalls, events);
                return false;
            }

            throw;
        }
    }

    private bool TryInvokeModelContinue(
        AgentRunRequest request,
        IReadOnlyList<AgentToolCallResult> toolResults,
        AgentRunLimits limits,
        DateTimeOffset deadlineUtc,
        CancellationToken cancellationToken,
        IReadOnlyList<ConversationToolCall> recordedToolCalls,
        List<AgentRunEvent> events,
        out AgentModelTurn? turn,
        out AgentRunResult? result)
    {
        TimeSpan remainingOverallTimeout = deadlineUtc - utcNowProvider();
        if (remainingOverallTimeout <= TimeSpan.Zero)
        {
            turn = null;
            result = CreateOverallTimeoutResult(recordedToolCalls, events);
            return false;
        }

        bool modelTimeoutWins = limits.ModelCallTimeout <= remainingOverallTimeout;
        using CancellationTokenSource overallTimeoutSource = new();
        using CancellationTokenSource modelTimeoutSource = new();
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            overallTimeoutSource.Token,
            modelTimeoutSource.Token);
        overallTimeoutSource.CancelAfter(remainingOverallTimeout);
        modelTimeoutSource.CancelAfter(limits.ModelCallTimeout);
        try
        {
            turn = model.Continue(request, toolResults, timeoutSource.Token);
            result = null;
            return true;
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            turn = null;
            if (modelTimeoutWins
                && (modelTimeoutSource.IsCancellationRequested
                    || overallTimeoutSource.IsCancellationRequested
                    || utcNowProvider() > deadlineUtc))
            {
                result = CreateModelCallTimeoutResult(recordedToolCalls, events);
                return false;
            }

            if (overallTimeoutSource.IsCancellationRequested || utcNowProvider() > deadlineUtc)
            {
                result = CreateOverallTimeoutResult(recordedToolCalls, events);
                return false;
            }

            if (modelTimeoutSource.IsCancellationRequested)
            {
                result = CreateModelCallTimeoutResult(recordedToolCalls, events);
                return false;
            }

            throw;
        }
    }

    private bool TryExecuteTool(
        AgentToolCallRequest toolCall,
        ToolExecutionContext context,
        DateTimeOffset deadlineUtc,
        CancellationToken cancellationToken,
        IReadOnlyList<ConversationToolCall> recordedToolCalls,
        List<AgentRunEvent> events,
        out ToolExecutionResult executionResult,
        out AgentRunResult? result)
    {
        TimeSpan remainingOverallTimeout = deadlineUtc - utcNowProvider();
        if (remainingOverallTimeout <= TimeSpan.Zero)
        {
            executionResult = null!;
            result = CreateOverallTimeoutResult(recordedToolCalls, events);
            return false;
        }

        using CancellationTokenSource overallTimeoutSource = new();
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            overallTimeoutSource.Token);
        overallTimeoutSource.CancelAfter(remainingOverallTimeout);
        try
        {
            executionResult = toolExecutor.Execute(
                toolCall.ToolName,
                context,
                timeoutSource.Token);
            result = null;
            return true;
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            executionResult = null!;
            if (overallTimeoutSource.IsCancellationRequested || utcNowProvider() > deadlineUtc)
            {
                result = CreateOverallTimeoutResult(recordedToolCalls, events);
                return false;
            }

            throw;
        }
    }

    private AgentRunResult CreateModelCallTimeoutResult(
        IReadOnlyList<ConversationToolCall> recordedToolCalls,
        List<AgentRunEvent> events)
    {
        AgentError timeoutError = new(
            "agent-model-call-timeout-reached",
            "Agent model call reached the timeout.",
            Retryable: false);
        RecordError(events, timeoutError);
        return AgentRunResult.Failure(timeoutError, recordedToolCalls, events);
    }

    private AgentRunResult CreateOverallTimeoutResult(
        IReadOnlyList<ConversationToolCall> recordedToolCalls,
        List<AgentRunEvent> events)
    {
        AgentError timeoutError = new(
            "agent-overall-timeout-reached",
            "Agent loop reached the overall timeout.",
            Retryable: false);
        RecordError(events, timeoutError);
        return AgentRunResult.Failure(timeoutError, recordedToolCalls, events);
    }

    private bool TryCreateTimeoutResult(
        DateTimeOffset deadlineUtc,
        IReadOnlyList<ConversationToolCall> recordedToolCalls,
        List<AgentRunEvent> events,
        out AgentRunResult? result)
    {
        if (utcNowProvider() <= deadlineUtc)
        {
            result = null;
            return false;
        }

        result = CreateOverallTimeoutResult(recordedToolCalls, events);
        return true;
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
