namespace CSharpAiCli.Core;

public sealed class OfflineAgentRunner : IAgentRunner
{
    private readonly IToolCallingModel model;
    private readonly IToolExecutor toolExecutor;
    private readonly Func<DateTimeOffset> utcNowProvider;
    private readonly DiagnosticDurationClock durationClock;
    private readonly int maxIterations;

    public OfflineAgentRunner(
        IToolCallingModel model,
        IToolExecutor toolExecutor,
        Func<DateTimeOffset>? utcNowProvider = null,
        int maxIterations = 8,
        Func<long>? timestampProvider = null)
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
        durationClock = new DiagnosticDurationClock(timestampProvider);
        this.maxIterations = maxIterations;
    }

    public AgentRunResult Run(
        AgentRunRequest request,
        ConversationTranscript? transcript = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        AgentRunLimits limits = (request.Limits ?? AgentRunLimits.Default).MergeWith(new AgentRunLimits(MaxSteps: maxIterations));
        AgentRunState state = new(limits, utcNowProvider);
        DateTimeOffset deadlineUtc = state.DeadlineUtc;
        List<ConversationToolCall> recordedToolCalls = [];
        List<AgentRunEvent> events = [];
        if (request.TaskContext is not null)
        {
            RecordStartupContext(events, request);
        }

        if (TryCreateTimeoutResult(state, recordedToolCalls, events, out AgentRunResult? timeoutResult))
        {
            return timeoutResult!;
        }

        if (!TryInvokeModelStart(
            request,
            state,
            cancellationToken,
            recordedToolCalls,
            events,
            out AgentModelTurn? turn,
            out long? modelCallDurationMs,
            out AgentRunResult? modelTimeoutResult))
        {
            return modelTimeoutResult!;
        }

        AgentModelTurn currentTurn = turn!;
        RecordModelTurn(events, currentTurn, modelCallDurationMs, step: null);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryCreateTimeoutResult(state, recordedToolCalls, events, out timeoutResult))
            {
                return timeoutResult!;
            }

            if (currentTurn.IsFinal)
            {
                state.StopSuccess();
                RecordFinalResponse(events, currentTurn.FinalText!, state);
                return AgentRunResult.Success(
                    currentTurn.FinalText!,
                    recordedToolCalls,
                    events,
                    state.Steps,
                    state.StopReason ?? AgentStopReason.Completed);
            }

            if (currentTurn.ToolCalls.Count == 0)
            {
                AgentLoopError error = state.StopEmptyTurn();
                RecordError(events, error, step: null);
                return CreateFailureResult(error, recordedToolCalls, events, state);
            }

            if (!state.TryBeginStep(out AgentStep? step, out AgentLoopError? stepLimitError))
            {
                RecordError(events, stepLimitError!, step: null);
                return CreateFailureResult(stepLimitError!, recordedToolCalls, events, state);
            }

            List<AgentToolCallResult> toolResults = [];
            foreach (AgentToolCallRequest toolCall in currentTurn.ToolCalls)
            {
                if (!state.TryReserveToolCall(step, out AgentLoopError? toolCallLimitError))
                {
                    RecordError(events, toolCallLimitError!, step);
                    return CreateFailureResult(toolCallLimitError!, recordedToolCalls, events, state);
                }

                if (TryCreateTimeoutResult(state, recordedToolCalls, events, out timeoutResult))
                {
                    return timeoutResult!;
                }

                RecordToolCall(events, toolCall, step);
                ToolExecutionContext context = new(
                    CallId: toolCall.CallId,
                    Workspace: request.Workspace,
                    ArgumentsJson: toolCall.ArgumentsJson);
                if (!TryExecuteTool(
                    toolCall,
                    context,
                    state,
                    cancellationToken,
                    recordedToolCalls,
                    events,
                    step,
                    out ToolExecutionResult executionResult,
                    out long? toolDurationMs,
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
                RecordToolResult(events, toolCall, executionResult, toolDurationMs, step);
                if (string.Equals(toolCall.ToolName, AgentPlanTool.ToolName, StringComparison.Ordinal) &&
                    executionResult.Succeeded)
                {
                    RecordPlanToolEvent(events, toolCall, executionResult, step);
                }

                if (!executionResult.Succeeded)
                {
                    AgentLoopError toolFailure = state.StopFailure(
                        AgentStopReason.FromToolResult(executionResult),
                        executionResult.ErrorCode ?? ToolErrorCode.ToolExecutionFailed,
                        executionResult.Summary,
                        executionResult.Retryable);
                    state.CompleteStep(step, toolFailure.Status, toolFailure.StopReason);
                    RecordError(events, toolFailure, step);
                    return CreateFailureResult(toolFailure, recordedToolCalls, events, state);
                }
            }

            state.CompleteStep(step, DiagnosticEventStatus.Success);

            if (TryCreateTimeoutResult(state, recordedToolCalls, events, out timeoutResult))
            {
                return timeoutResult!;
            }

            if (!TryInvokeModelContinue(
                request,
                toolResults,
                state,
                cancellationToken,
                recordedToolCalls,
                events,
                step,
                out turn,
                out modelCallDurationMs,
                out modelTimeoutResult))
            {
                return modelTimeoutResult!;
            }

            currentTurn = turn!;
            RecordModelTurn(events, currentTurn, modelCallDurationMs, step);
            if (TryCreateTimeoutResult(state, recordedToolCalls, events, out timeoutResult))
            {
                return timeoutResult!;
            }

            if (currentTurn.IsFinal)
            {
                state.StopSuccess();
                RecordFinalResponse(events, currentTurn.FinalText!, state);
                return AgentRunResult.Success(
                    currentTurn.FinalText!,
                    recordedToolCalls,
                    events,
                    state.Steps,
                    state.StopReason ?? AgentStopReason.Completed);
            }
        }
    }

    private bool TryInvokeModelStart(
        AgentRunRequest request,
        AgentRunState state,
        CancellationToken cancellationToken,
        IReadOnlyList<ConversationToolCall> recordedToolCalls,
        List<AgentRunEvent> events,
        out AgentModelTurn? turn,
        out long? durationMs,
        out AgentRunResult? result)
    {
        AgentRunLimits limits = state.Limits;
        DateTimeOffset deadlineUtc = state.DeadlineUtc;
        TimeSpan remainingOverallTimeout = deadlineUtc - utcNowProvider();
        if (remainingOverallTimeout <= TimeSpan.Zero)
        {
            turn = null;
            durationMs = null;
            result = CreateOverallTimeoutResult(state, recordedToolCalls, events);
            return false;
        }

        bool modelTimeoutWins = limits.ModelCallTimeout <= remainingOverallTimeout;
        long startedTimestamp = durationClock.GetTimestamp();
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
            durationMs = durationClock.GetElapsedMilliseconds(startedTimestamp);
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
            durationMs = durationClock.GetElapsedMilliseconds(startedTimestamp);
            if (modelTimeoutWins
                && (modelTimeoutSource.IsCancellationRequested
                    || overallTimeoutSource.IsCancellationRequested
                    || utcNowProvider() > deadlineUtc))
            {
                result = CreateModelCallTimeoutResult(state, recordedToolCalls, events, durationMs);
                return false;
            }

            if (overallTimeoutSource.IsCancellationRequested || utcNowProvider() > deadlineUtc)
            {
                result = CreateOverallTimeoutResult(state, recordedToolCalls, events, durationMs);
                return false;
            }

            if (modelTimeoutSource.IsCancellationRequested)
            {
                result = CreateModelCallTimeoutResult(state, recordedToolCalls, events, durationMs);
                return false;
            }

            throw;
        }
    }

    private bool TryInvokeModelContinue(
        AgentRunRequest request,
        IReadOnlyList<AgentToolCallResult> toolResults,
        AgentRunState state,
        CancellationToken cancellationToken,
        IReadOnlyList<ConversationToolCall> recordedToolCalls,
        List<AgentRunEvent> events,
        AgentStep? step,
        out AgentModelTurn? turn,
        out long? durationMs,
        out AgentRunResult? result)
    {
        AgentRunLimits limits = state.Limits;
        DateTimeOffset deadlineUtc = state.DeadlineUtc;
        TimeSpan remainingOverallTimeout = deadlineUtc - utcNowProvider();
        if (remainingOverallTimeout <= TimeSpan.Zero)
        {
            turn = null;
            durationMs = null;
            result = CreateOverallTimeoutResult(state, recordedToolCalls, events, step: step);
            return false;
        }

        bool modelTimeoutWins = limits.ModelCallTimeout <= remainingOverallTimeout;
        long startedTimestamp = durationClock.GetTimestamp();
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
            durationMs = durationClock.GetElapsedMilliseconds(startedTimestamp);
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
            durationMs = durationClock.GetElapsedMilliseconds(startedTimestamp);
            if (modelTimeoutWins
                && (modelTimeoutSource.IsCancellationRequested
                    || overallTimeoutSource.IsCancellationRequested
                    || utcNowProvider() > deadlineUtc))
            {
                result = CreateModelCallTimeoutResult(state, recordedToolCalls, events, durationMs, step);
                return false;
            }

            if (overallTimeoutSource.IsCancellationRequested || utcNowProvider() > deadlineUtc)
            {
                result = CreateOverallTimeoutResult(state, recordedToolCalls, events, durationMs, step);
                return false;
            }

            if (modelTimeoutSource.IsCancellationRequested)
            {
                result = CreateModelCallTimeoutResult(state, recordedToolCalls, events, durationMs, step);
                return false;
            }

            throw;
        }
    }

    private bool TryExecuteTool(
        AgentToolCallRequest toolCall,
        ToolExecutionContext context,
        AgentRunState state,
        CancellationToken cancellationToken,
        IReadOnlyList<ConversationToolCall> recordedToolCalls,
        List<AgentRunEvent> events,
        AgentStep? step,
        out ToolExecutionResult executionResult,
        out long? durationMs,
        out AgentRunResult? result)
    {
        DateTimeOffset deadlineUtc = state.DeadlineUtc;
        TimeSpan remainingOverallTimeout = deadlineUtc - utcNowProvider();
        if (remainingOverallTimeout <= TimeSpan.Zero)
        {
            executionResult = null!;
            durationMs = null;
            RecordToolTimeout(events, toolCall, durationMs, step);
            result = CreateOverallTimeoutResult(state, recordedToolCalls, events, step: step);
            return false;
        }

        long startedTimestamp = durationClock.GetTimestamp();
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
            durationMs = durationClock.GetElapsedMilliseconds(startedTimestamp);
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
            durationMs = durationClock.GetElapsedMilliseconds(startedTimestamp);
            if (overallTimeoutSource.IsCancellationRequested || utcNowProvider() > deadlineUtc)
            {
                RecordToolTimeout(events, toolCall, durationMs, step);
                result = CreateOverallTimeoutResult(state, recordedToolCalls, events, durationMs, step);
                return false;
            }

            throw;
        }
    }

    private AgentRunResult CreateModelCallTimeoutResult(
        AgentRunState state,
        IReadOnlyList<ConversationToolCall> recordedToolCalls,
        List<AgentRunEvent> events,
        long? durationMs = null,
        AgentStep? step = null)
    {
        AgentLoopError timeoutError = state.StopModelTimeout();
        state.CompleteStep(step, timeoutError.Status, timeoutError.StopReason);
        RecordError(events, timeoutError, step, durationMs);
        return CreateFailureResult(timeoutError, recordedToolCalls, events, state);
    }

    private AgentRunResult CreateOverallTimeoutResult(
        AgentRunState state,
        IReadOnlyList<ConversationToolCall> recordedToolCalls,
        List<AgentRunEvent> events,
        long? durationMs = null,
        AgentStep? step = null)
    {
        AgentLoopError timeoutError = state.StopOverallTimeout();
        state.CompleteStep(step, timeoutError.Status, timeoutError.StopReason);
        RecordError(events, timeoutError, step, durationMs);
        return CreateFailureResult(timeoutError, recordedToolCalls, events, state);
    }

    private bool TryCreateTimeoutResult(
        AgentRunState state,
        IReadOnlyList<ConversationToolCall> recordedToolCalls,
        List<AgentRunEvent> events,
        out AgentRunResult? result)
    {
        if (!state.TryCreateOverallTimeoutError(out AgentLoopError? error))
        {
            result = null;
            return false;
        }

        RecordError(events, error!, step: null);
        result = CreateFailureResult(error!, recordedToolCalls, events, state);
        return true;
    }

    private static AgentRunResult CreateFailureResult(
        AgentLoopError error,
        IReadOnlyList<ConversationToolCall> recordedToolCalls,
        IReadOnlyList<AgentRunEvent> events,
        AgentRunState state)
    {
        return AgentRunResult.Failure(
            error.ToAgentError(),
            recordedToolCalls,
            events,
            state.Steps,
            error.StopReason,
            error.Status);
    }

    private void RecordModelTurn(
        List<AgentRunEvent> events,
        AgentModelTurn turn,
        long? durationMs,
        AgentStep? step)
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
            Payload: payload,
            Status: DiagnosticEventStatus.Success,
            DurationMs: durationMs,
            StepIndex: step?.Index));
    }

    private void RecordStartupContext(
        List<AgentRunEvent> events,
        AgentRunRequest request)
    {
        AgentTaskContext taskContext = request.TaskContext!;
        RecordWorkspaceContext(events, taskContext);
        RecordInstructionContext(events, taskContext);
        RecordSessionContext(events, taskContext);
        RecordGitStatusContext(events, taskContext);
        RecordGitDiffContext(events, taskContext);
        RecordStartupPlan(events, request.Prompt, taskContext);
    }

    private void RecordWorkspaceContext(
        List<AgentRunEvent> events,
        AgentTaskContext taskContext)
    {
        Dictionary<string, string> payload = new()
        {
            ["workspaceRoot"] = taskContext.WorkspaceRoot,
            ["currentDirectory"] = taskContext.CurrentDirectory,
            ["workspaceStatus"] = taskContext.WorkspaceStatus,
            ["currentDirectoryAllowed"] = taskContext.CurrentDirectoryErrorCode is null ? "true" : "false"
        };
        if (taskContext.CurrentDirectoryErrorCode is not null)
        {
            payload["currentDirectoryErrorCode"] = taskContext.CurrentDirectoryErrorCode;
        }

        events.Add(new AgentRunEvent(
            Type: "context.workspace",
            Sequence: events.Count,
            Timestamp: utcNowProvider(),
            Message: "Collected bounded workspace context.",
            Summary: $"workspace={taskContext.WorkspaceRoot}",
            Payload: payload,
            ErrorCode: taskContext.CurrentDirectoryErrorCode,
            Status: taskContext.CurrentDirectoryErrorCode is null
                ? DiagnosticEventStatus.Success
                : DiagnosticEventStatus.Warning));
    }

    private void RecordInstructionContext(
        List<AgentRunEvent> events,
        AgentTaskContext taskContext)
    {
        Dictionary<string, string> payload = new()
        {
            ["hasInstructions"] = string.IsNullOrWhiteSpace(taskContext.Instructions) ? "false" : "true",
            ["instructionLength"] = (taskContext.Instructions?.Length ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["sourceCount"] = taskContext.InstructionSources.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["warningCount"] = taskContext.InstructionWarnings.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        if (taskContext.InstructionSources.Count > 0)
        {
            payload["sources"] = string.Join(";", taskContext.InstructionSources.Select(source => source.SourcePath));
        }

        if (taskContext.InstructionWarnings.Count > 0)
        {
            payload["warnings"] = string.Join(" | ", taskContext.InstructionWarnings);
        }

        events.Add(new AgentRunEvent(
            Type: "context.instructions",
            Sequence: events.Count,
            Timestamp: utcNowProvider(),
            Message: "Collected project instruction sources.",
            Summary: taskContext.InstructionSources.Count == 0
                ? "No project instruction file loaded."
                : $"Loaded {taskContext.InstructionSources.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} instruction source(s).",
            Payload: payload,
            Status: taskContext.InstructionWarnings.Count == 0
                ? DiagnosticEventStatus.Success
                : DiagnosticEventStatus.Warning));
    }

    private void RecordSessionContext(
        List<AgentRunEvent> events,
        AgentTaskContext taskContext)
    {
        Dictionary<string, string> payload = new()
        {
            ["hasSession"] = string.IsNullOrWhiteSpace(taskContext.SessionName) ? "false" : "true",
            ["hasTranscriptContext"] = taskContext.HasTranscriptContext ? "true" : "false"
        };
        if (!string.IsNullOrWhiteSpace(taskContext.SessionName))
        {
            payload["sessionName"] = taskContext.SessionName!;
        }

        events.Add(new AgentRunEvent(
            Type: "context.session",
            Sequence: events.Count,
            Timestamp: utcNowProvider(),
            Message: "Collected session resume context.",
            Summary: taskContext.HasTranscriptContext
                ? "Resumed transcript context is available."
                : "No resumed transcript context.",
            Payload: payload,
            Status: DiagnosticEventStatus.Success));
    }

    private void RecordGitStatusContext(
        List<AgentRunEvent> events,
        AgentTaskContext taskContext)
    {
        AgentGitContextSummary git = taskContext.Git;
        Dictionary<string, string> payload = new()
        {
            ["succeeded"] = git.StatusSucceeded ? "true" : "false",
            ["dirty"] = git.IsDirty ? "true" : "false",
            ["summaryTruncated"] = git.StatusSummaryTruncated ? "true" : "false"
        };

        events.Add(new AgentRunEvent(
            Type: "context.git.status",
            Sequence: events.Count,
            Timestamp: utcNowProvider(),
            Message: "Collected bounded git status summary.",
            Summary: git.StatusSummary,
            Payload: payload,
            ErrorCode: git.StatusErrorCode,
            Status: git.StatusSucceeded
                ? git.IsDirty || git.StatusSummaryTruncated
                    ? DiagnosticEventStatus.Warning
                    : DiagnosticEventStatus.Success
                : DiagnosticEventStatus.Failure));
    }

    private void RecordGitDiffContext(
        List<AgentRunEvent> events,
        AgentTaskContext taskContext)
    {
        AgentGitContextSummary git = taskContext.Git;
        Dictionary<string, string> payload = new()
        {
            ["succeeded"] = git.DiffSucceeded ? "true" : "false",
            ["outputTruncated"] = git.DiffOutputTruncated ? "true" : "false",
            ["summaryTruncated"] = git.DiffSummaryTruncated ? "true" : "false"
        };

        events.Add(new AgentRunEvent(
            Type: "context.git.diff",
            Sequence: events.Count,
            Timestamp: utcNowProvider(),
            Message: "Collected bounded git diff summary.",
            Summary: git.DiffSummary,
            Payload: payload,
            ErrorCode: git.DiffErrorCode,
            Status: git.DiffSucceeded
                ? git.DiffOutputTruncated || git.DiffSummaryTruncated
                    ? DiagnosticEventStatus.Warning
                    : DiagnosticEventStatus.Success
                : DiagnosticEventStatus.Failure));
    }

    private void RecordStartupPlan(
        List<AgentRunEvent> events,
        string prompt,
        AgentTaskContext taskContext)
    {
        AgentStartupPlan plan = AgentStartupPlanBuilder.Build(prompt, taskContext);
        events.Add(new AgentRunEvent(
            Type: "plan",
            Sequence: events.Count,
            Timestamp: utcNowProvider(),
            Message: "Created read-only startup plan.",
            Summary: plan.Summary,
            Payload: new Dictionary<string, string>
            {
                ["source"] = "startup",
                ["goal"] = plan.Goal,
                ["candidateFiles"] = string.Join(";", plan.CandidateFiles),
                ["expectedTools"] = string.Join(";", plan.ExpectedTools),
                ["risks"] = string.Join(";", plan.Risks),
                ["truncated"] = plan.Truncated ? "true" : "false"
            },
            Status: plan.Truncated ? DiagnosticEventStatus.Warning : DiagnosticEventStatus.Success));
    }

    private void RecordToolCall(
        List<AgentRunEvent> events,
        AgentToolCallRequest toolCall,
        AgentStep? step)
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
            },
            Status: DiagnosticEventStatus.Started,
            StepIndex: step?.Index));
    }

    private void RecordToolResult(
        List<AgentRunEvent> events,
        AgentToolCallRequest toolCall,
        ToolExecutionResult result,
        long? durationMs,
        AgentStep? step)
    {
        Dictionary<string, string> payload = new()
        {
            ["callId"] = toolCall.CallId,
            ["toolName"] = toolCall.ToolName,
            ["succeeded"] = result.Succeeded ? "true" : "false"
        };
        AddStructuredDiagnosticPayload(payload, result.StructuredPayload, "mcpStatus");
        AddStructuredDiagnosticPayload(payload, result.StructuredPayload, "mcpDurationMs");

        events.Add(new AgentRunEvent(
            Type: "tool.result",
            Sequence: events.Count,
            Timestamp: utcNowProvider(),
            Message: result.Succeeded
                ? $"Tool '{toolCall.ToolName}' completed."
                : $"Tool '{toolCall.ToolName}' failed.",
            Summary: result.Summary,
            Payload: payload,
            ErrorCode: result.ErrorCode,
            ApprovalStatus: result.ApprovalStatus,
            Status: result.Succeeded ? DiagnosticEventStatus.Success : DiagnosticEventStatus.Failure,
            DurationMs: durationMs,
            ApprovalDurationMs: result.ApprovalDurationMs,
            StepIndex: step?.Index));
    }

    private void RecordPlanToolEvent(
        List<AgentRunEvent> events,
        AgentToolCallRequest toolCall,
        ToolExecutionResult result,
        AgentStep? step)
    {
        Dictionary<string, string> payload = new()
        {
            ["source"] = "tool",
            ["callId"] = toolCall.CallId,
            ["toolName"] = toolCall.ToolName
        };
        AddStructuredDiagnosticPayload(payload, result.StructuredPayload, "goal");
        AddStructuredDiagnosticPayload(payload, result.StructuredPayload, "truncated");

        bool truncated = payload.TryGetValue("truncated", out string? truncatedText) &&
            string.Equals(truncatedText, "true", StringComparison.OrdinalIgnoreCase);
        events.Add(new AgentRunEvent(
            Type: "plan",
            Sequence: events.Count,
            Timestamp: utcNowProvider(),
            Message: "Recorded agent-provided plan.",
            Summary: result.Summary,
            Payload: payload,
            Status: truncated ? DiagnosticEventStatus.Warning : DiagnosticEventStatus.Success,
            StepIndex: step?.Index));
    }

    private void RecordToolTimeout(
        List<AgentRunEvent> events,
        AgentToolCallRequest toolCall,
        long? durationMs,
        AgentStep? step)
    {
        events.Add(new AgentRunEvent(
            Type: "tool.result",
            Sequence: events.Count,
            Timestamp: utcNowProvider(),
            Message: $"Tool '{toolCall.ToolName}' timed out.",
            Summary: "Tool execution reached the overall timeout.",
            Payload: new Dictionary<string, string>
            {
                ["callId"] = toolCall.CallId,
                ["toolName"] = toolCall.ToolName,
                ["succeeded"] = "false"
            },
            ErrorCode: "agent-overall-timeout-reached",
            Status: DiagnosticEventStatus.Timeout,
            DurationMs: durationMs,
            StepIndex: step?.Index,
            StopReason: AgentStopReason.ToolTimeout));
    }

    private void RecordFinalResponse(
        List<AgentRunEvent> events,
        string finalText,
        AgentRunState state)
    {
        events.Add(new AgentRunEvent(
            Type: "final.response",
            Sequence: events.Count,
            Timestamp: utcNowProvider(),
            Summary: finalText,
            Status: DiagnosticEventStatus.Success,
            StopReason: state.StopReason ?? AgentStopReason.Completed));
    }

    private void RecordError(
        List<AgentRunEvent> events,
        AgentLoopError error,
        AgentStep? step,
        long? durationMs = null)
    {
        events.Add(new AgentRunEvent(
            Type: "agent.error",
            Sequence: events.Count,
            Timestamp: utcNowProvider(),
            Message: error.SafeMessage,
            ErrorCode: error.ErrorCode,
            Status: error.Status,
            DurationMs: durationMs,
            StepIndex: step?.Index,
            StopReason: error.StopReason));
    }

    private static void AddStructuredDiagnosticPayload(
        Dictionary<string, string> payload,
        IReadOnlyDictionary<string, System.Text.Json.JsonElement>? structuredPayload,
        string key)
    {
        if (structuredPayload is null || !structuredPayload.TryGetValue(key, out System.Text.Json.JsonElement value))
        {
            return;
        }

        if (value.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            payload[key] = value.GetString() ?? string.Empty;
            return;
        }

        payload[key] = value.GetRawText();
    }
}
