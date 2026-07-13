using System.Collections.ObjectModel;
using System.Text.Json;

namespace CSharpAiCli.Core;

public sealed class OfflineAgentRunner : IAgentRunner
{
    private const string PatchToolName = "workspace.apply_patch";
    private const string ShellToolName = "workspace.run_shell";
    private const int MaxSummaryCharacters = 4096;
    private const int MaxVerificationOutputCharacters = 4096;

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
        List<ChangedFileSummary> changedFiles = [];
        List<VerificationResultSummary> verificationResults = [];
        List<AgentRetryAttempt> retryAttempts = [];
        if (request.TaskContext is not null)
        {
            RecordStartupContext(events, request);
        }

        if (TryCreateTimeoutResult(
            state,
            recordedToolCalls,
            events,
            changedFiles,
            verificationResults,
            retryAttempts,
            out AgentRunResult? timeoutResult))
        {
            return timeoutResult!;
        }

        if (!TryInvokeModelStart(
            request,
            state,
            cancellationToken,
            recordedToolCalls,
            events,
            changedFiles,
            verificationResults,
            retryAttempts,
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
            if (TryCreateTimeoutResult(
                state,
                recordedToolCalls,
                events,
                changedFiles,
                verificationResults,
                retryAttempts,
                out timeoutResult))
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
                    changedFiles,
                    verificationResults,
                    retryAttempts,
                    state.StopReason ?? AgentStopReason.Completed);
            }

            if (currentTurn.ToolCalls.Count == 0)
            {
                AgentLoopError error = state.StopEmptyTurn();
                RecordError(events, error, step: null);
                return CreateFailureResult(error, recordedToolCalls, events, state, changedFiles, verificationResults, retryAttempts);
            }

            if (!state.TryBeginStep(out AgentStep? step, out AgentLoopError? stepLimitError))
            {
                RecordError(events, stepLimitError!, step: null);
                return CreateFailureResult(stepLimitError!, recordedToolCalls, events, state, changedFiles, verificationResults, retryAttempts);
            }

            List<AgentToolCallResult> toolResults = [];
            string? retryStepStopReason = null;
            foreach (AgentToolCallRequest toolCall in currentTurn.ToolCalls)
            {
                if (!state.TryReserveToolCall(step, out AgentLoopError? toolCallLimitError))
                {
                    RecordError(events, toolCallLimitError!, step);
                    return CreateFailureResult(toolCallLimitError!, recordedToolCalls, events, state, changedFiles, verificationResults, retryAttempts);
                }

                if (TryCreateTimeoutResult(
                    state,
                    recordedToolCalls,
                    events,
                    changedFiles,
                    verificationResults,
                    retryAttempts,
                    out timeoutResult))
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
                    changedFiles,
                    verificationResults,
                    retryAttempts,
                    out ToolExecutionResult executionResult,
                    out long? toolDurationMs,
                    out AgentRunResult? toolTimeoutResult))
                {
                    return toolTimeoutResult!;
                }

                PostPatchWorkflowResult? postPatch = null;
                if (IsSuccessfulPatchToolResult(toolCall, executionResult))
                {
                    postPatch = RunPostPatchWorkflow(
                        request,
                        toolCall,
                        executionResult,
                        cancellationToken);
                    changedFiles.AddRange(postPatch.ChangedFiles);
                    verificationResults.Add(postPatch.VerificationResult);
                    executionResult = AugmentPatchResult(executionResult, postPatch);

                    if (TryCreateVerificationFailureInput(toolCall, postPatch.VerificationResult, out AgentRetryDecisionInput? verificationFailure))
                    {
                        AgentRetryDecision decision = AgentRetryPolicy.Decide(
                            verificationFailure!,
                            state.RemainingRetries,
                            state.Limits.MaxRetries == 0);
                        AgentLoopError? retryReserveError = null;
                        if (decision.ShouldRetry && state.TryReserveRetry(out retryReserveError))
                        {
                            AgentRetryAttempt attempt = CreateRetryAttempt(
                                verificationFailure!,
                                state.RetryCount,
                                changedFiles);
                            retryAttempts.Add(attempt);
                            executionResult = AddRetryFeedback(executionResult, attempt, decision);
                            retryStepStopReason = verificationFailure!.StopReason;
                        }
                        else
                        {
                            AgentLoopError verificationError = decision.BudgetExhausted || retryReserveError is not null
                                ? state.StopRetryBudgetExhausted()
                                : state.StopFailure(
                                    verificationFailure!.StopReason,
                                    verificationFailure.ErrorCode ?? "verification-failure",
                                    verificationFailure.Summary,
                                    retryable: false);
                            state.CompleteStep(step, verificationError.Status, verificationError.StopReason);
                            DateTimeOffset terminalNowUtc = utcNowProvider();
                            ConversationToolCall terminalTranscriptToolCall = ConversationToolCall.FromExecution(
                                toolCall.CallId,
                                toolCall.ToolName,
                                context.ArgumentsJson,
                                executionResult,
                                terminalNowUtc);
                            recordedToolCalls.Add(terminalTranscriptToolCall);
                            transcript?.AddToolCall(terminalTranscriptToolCall);
                            RecordToolResult(events, toolCall, executionResult, toolDurationMs, step);
                            RecordPatchDiagnosticEvents(events, toolCall, executionResult, step);
                            RecordChangedFilesEvent(events, toolCall, postPatch, step);
                            RecordVerificationResultEvent(events, toolCall, postPatch.VerificationResult, step);
                            if (decision.BudgetExhausted || retryReserveError is not null)
                            {
                                RecordRetryBudgetExhaustedEvent(events, verificationFailure!, decision, step);
                            }

                            RecordError(events, verificationError, step);
                            return CreateFailureResult(
                                verificationError,
                                recordedToolCalls,
                                events,
                                state,
                                changedFiles,
                                verificationResults,
                                retryAttempts,
                                verificationFailure);
                        }
                    }
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
                RecordPatchDiagnosticEvents(events, toolCall, executionResult, step);
                if (postPatch is not null)
                {
                    RecordChangedFilesEvent(events, toolCall, postPatch, step);
                    RecordVerificationResultEvent(events, toolCall, postPatch.VerificationResult, step);
                }
                if (string.Equals(toolCall.ToolName, AgentPlanTool.ToolName, StringComparison.Ordinal) &&
                    executionResult.Succeeded)
                {
                    RecordPlanToolEvent(events, toolCall, executionResult, step);
                }

                if (retryStepStopReason is not null)
                {
                    RecordRetryAttemptEvent(events, retryAttempts[^1], state, step);
                    break;
                }

                if (!executionResult.Succeeded)
                {
                    AgentRetryDecisionInput toolFailureInput = CreateToolFailureInput(toolCall, executionResult);
                    AgentRetryDecision decision = AgentRetryPolicy.Decide(
                        toolFailureInput,
                        state.RemainingRetries,
                        state.Limits.MaxRetries == 0);
                    AgentLoopError? retryReserveError = null;
                    if (decision.ShouldRetry && state.TryReserveRetry(out retryReserveError))
                    {
                        AgentRetryAttempt attempt = CreateRetryAttempt(
                            toolFailureInput,
                            state.RetryCount,
                            changedFiles);
                        retryAttempts.Add(attempt);
                        toolResults[^1] = new AgentToolCallResult(
                            toolCall,
                            AddRetryFeedback(executionResult, attempt, decision));
                        RecordRetryAttemptEvent(events, attempt, state, step);
                        retryStepStopReason = toolFailureInput.StopReason;
                        break;
                    }

                    AgentLoopError toolFailure = decision.BudgetExhausted || retryReserveError is not null
                        ? state.StopRetryBudgetExhausted()
                        : state.StopFailure(
                            toolFailureInput.StopReason,
                            toolFailureInput.ErrorCode ?? ToolErrorCode.ToolExecutionFailed,
                            toolFailureInput.Summary,
                            executionResult.Retryable);
                    state.CompleteStep(step, toolFailure.Status, toolFailure.StopReason);
                    if (decision.BudgetExhausted || retryReserveError is not null)
                    {
                        RecordRetryBudgetExhaustedEvent(events, toolFailureInput, decision, step);
                    }

                    RecordError(events, toolFailure, step);
                    return CreateFailureResult(
                        toolFailure,
                        recordedToolCalls,
                        events,
                        state,
                        changedFiles,
                        verificationResults,
                        retryAttempts,
                        toolFailureInput);
                }
            }

            state.CompleteStep(
                step,
                retryStepStopReason is null ? DiagnosticEventStatus.Success : DiagnosticEventStatus.Warning,
                retryStepStopReason);

            if (TryCreateTimeoutResult(
                state,
                recordedToolCalls,
                events,
                changedFiles,
                verificationResults,
                retryAttempts,
                out timeoutResult))
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
                changedFiles,
                verificationResults,
                retryAttempts,
                out turn,
                out modelCallDurationMs,
                out modelTimeoutResult))
            {
                return modelTimeoutResult!;
            }

            currentTurn = turn!;
            RecordModelTurn(events, currentTurn, modelCallDurationMs, step);
            if (TryCreateTimeoutResult(
                state,
                recordedToolCalls,
                events,
                changedFiles,
                verificationResults,
                retryAttempts,
                out timeoutResult))
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
                    changedFiles,
                    verificationResults,
                    retryAttempts,
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
        IReadOnlyList<ChangedFileSummary> changedFiles,
        IReadOnlyList<VerificationResultSummary> verificationResults,
        IReadOnlyList<AgentRetryAttempt> retryAttempts,
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
            result = CreateOverallTimeoutResult(state, recordedToolCalls, events, changedFiles, verificationResults, retryAttempts);
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
                result = CreateModelCallTimeoutResult(state, recordedToolCalls, events, changedFiles, verificationResults, retryAttempts, durationMs);
                return false;
            }

            if (overallTimeoutSource.IsCancellationRequested || utcNowProvider() > deadlineUtc)
            {
                result = CreateOverallTimeoutResult(state, recordedToolCalls, events, changedFiles, verificationResults, retryAttempts, durationMs);
                return false;
            }

            if (modelTimeoutSource.IsCancellationRequested)
            {
                result = CreateModelCallTimeoutResult(state, recordedToolCalls, events, changedFiles, verificationResults, retryAttempts, durationMs);
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
        IReadOnlyList<ChangedFileSummary> changedFiles,
        IReadOnlyList<VerificationResultSummary> verificationResults,
        IReadOnlyList<AgentRetryAttempt> retryAttempts,
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
            result = CreateOverallTimeoutResult(state, recordedToolCalls, events, changedFiles, verificationResults, retryAttempts, step: step);
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
                result = CreateModelCallTimeoutResult(state, recordedToolCalls, events, changedFiles, verificationResults, retryAttempts, durationMs, step);
                return false;
            }

            if (overallTimeoutSource.IsCancellationRequested || utcNowProvider() > deadlineUtc)
            {
                result = CreateOverallTimeoutResult(state, recordedToolCalls, events, changedFiles, verificationResults, retryAttempts, durationMs, step);
                return false;
            }

            if (modelTimeoutSource.IsCancellationRequested)
            {
                result = CreateModelCallTimeoutResult(state, recordedToolCalls, events, changedFiles, verificationResults, retryAttempts, durationMs, step);
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
        IReadOnlyList<ChangedFileSummary> changedFiles,
        IReadOnlyList<VerificationResultSummary> verificationResults,
        IReadOnlyList<AgentRetryAttempt> retryAttempts,
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
            result = CreateOverallTimeoutResult(state, recordedToolCalls, events, changedFiles, verificationResults, retryAttempts, step: step);
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
                result = CreateOverallTimeoutResult(state, recordedToolCalls, events, changedFiles, verificationResults, retryAttempts, durationMs, step);
                return false;
            }

            throw;
        }
    }

    private AgentRunResult CreateModelCallTimeoutResult(
        AgentRunState state,
        IReadOnlyList<ConversationToolCall> recordedToolCalls,
        List<AgentRunEvent> events,
        IReadOnlyList<ChangedFileSummary> changedFiles,
        IReadOnlyList<VerificationResultSummary> verificationResults,
        IReadOnlyList<AgentRetryAttempt> retryAttempts,
        long? durationMs = null,
        AgentStep? step = null)
    {
        AgentLoopError timeoutError = state.StopModelTimeout();
        state.CompleteStep(step, timeoutError.Status, timeoutError.StopReason);
        RecordError(events, timeoutError, step, durationMs);
        return CreateFailureResult(timeoutError, recordedToolCalls, events, state, changedFiles, verificationResults, retryAttempts);
    }

    private AgentRunResult CreateOverallTimeoutResult(
        AgentRunState state,
        IReadOnlyList<ConversationToolCall> recordedToolCalls,
        List<AgentRunEvent> events,
        IReadOnlyList<ChangedFileSummary> changedFiles,
        IReadOnlyList<VerificationResultSummary> verificationResults,
        IReadOnlyList<AgentRetryAttempt> retryAttempts,
        long? durationMs = null,
        AgentStep? step = null)
    {
        AgentLoopError timeoutError = state.StopOverallTimeout();
        state.CompleteStep(step, timeoutError.Status, timeoutError.StopReason);
        RecordError(events, timeoutError, step, durationMs);
        return CreateFailureResult(timeoutError, recordedToolCalls, events, state, changedFiles, verificationResults, retryAttempts);
    }

    private bool TryCreateTimeoutResult(
        AgentRunState state,
        IReadOnlyList<ConversationToolCall> recordedToolCalls,
        List<AgentRunEvent> events,
        IReadOnlyList<ChangedFileSummary> changedFiles,
        IReadOnlyList<VerificationResultSummary> verificationResults,
        IReadOnlyList<AgentRetryAttempt> retryAttempts,
        out AgentRunResult? result)
    {
        if (!state.TryCreateOverallTimeoutError(out AgentLoopError? error))
        {
            result = null;
            return false;
        }

        RecordError(events, error!, step: null);
        result = CreateFailureResult(error!, recordedToolCalls, events, state, changedFiles, verificationResults, retryAttempts);
        return true;
    }

    private static AgentRunResult CreateFailureResult(
        AgentLoopError error,
        IReadOnlyList<ConversationToolCall> recordedToolCalls,
        IReadOnlyList<AgentRunEvent> events,
        AgentRunState state,
        IReadOnlyList<ChangedFileSummary> changedFiles,
        IReadOnlyList<VerificationResultSummary> verificationResults,
        IReadOnlyList<AgentRetryAttempt> retryAttempts,
        AgentRetryDecisionInput? lastFailure = null)
    {
        AgentFailureSummary? failureSummary = retryAttempts.Count > 0 ||
            string.Equals(error.ErrorCode, "agent-retry-budget-exhausted", StringComparison.Ordinal)
                ? CreateFailureSummary(error, state, changedFiles, verificationResults, retryAttempts, lastFailure)
                : null;

        return AgentRunResult.Failure(
            error.ToAgentError(),
            recordedToolCalls,
            events,
            state.Steps,
            changedFiles,
            verificationResults,
            retryAttempts,
            failureSummary,
            error.StopReason,
            error.Status);
    }

    private static AgentFailureSummary CreateFailureSummary(
        AgentLoopError error,
        AgentRunState state,
        IReadOnlyList<ChangedFileSummary> changedFiles,
        IReadOnlyList<VerificationResultSummary> verificationResults,
        IReadOnlyList<AgentRetryAttempt> retryAttempts,
        AgentRetryDecisionInput? lastFailure)
    {
        List<string> commands = retryAttempts
            .SelectMany(attempt => attempt.Commands)
            .Concat(verificationResults
                .Select(result => result.Command)
                .Where(command => !string.IsNullOrWhiteSpace(command))
                .Select(command => command!))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (lastFailure is not null)
        {
            AddCommands(commands, lastFailure);
        }

        IReadOnlyList<string> changedFilePaths = retryAttempts
            .SelectMany(attempt => attempt.ChangedFiles)
            .Concat(changedFiles.Select(file => file.Path))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        string failureKind = lastFailure?.FailureKind ?? ClassifyErrorCode(error.ErrorCode);
        string remainingRisk = string.Equals(error.ErrorCode, "agent-retry-budget-exhausted", StringComparison.Ordinal)
            ? "Retry budget was exhausted; review changed files, verification output, and command history before continuing."
            : "The task stopped after retry feedback; review changed files and command output before continuing.";

        return new AgentFailureSummary(
            FailureKind: failureKind,
            StopReason: error.StopReason,
            ErrorCode: error.ErrorCode,
            Message: error.SafeMessage,
            RetryBudget: state.Limits.MaxRetries,
            RetryCount: state.RetryCount,
            RemainingRetries: state.RemainingRetries,
            RemainingRisk: remainingRisk,
            RetryAttempts: retryAttempts,
            Commands: commands,
            ChangedFiles: changedFilePaths);
    }

    private static bool TryCreateVerificationFailureInput(
        AgentToolCallRequest toolCall,
        VerificationResultSummary verification,
        out AgentRetryDecisionInput? input)
    {
        if (verification.Succeeded ||
            string.Equals(verification.Status, "skipped", StringComparison.Ordinal))
        {
            input = null;
            return false;
        }

        bool approvalDenied = string.Equals(verification.ErrorCode, ToolErrorCode.ApprovalDenied, StringComparison.Ordinal) ||
            verification.ApprovalStatus is "denied" or "approval-required" or "dangerous-shell-denied";
        string failureKind = approvalDenied ? AgentFailureKind.Approval : AgentFailureKind.Verification;
        string stopReason = approvalDenied ? AgentStopReason.ApprovalDenied : AgentStopReason.VerificationFailure;
        input = new AgentRetryDecisionInput(
            FailureKind: failureKind,
            StopReason: stopReason,
            ErrorCode: verification.ErrorCode,
            Summary: CreateVerificationFailureFeedback(verification),
            Retryable: !approvalDenied,
            ApprovalStatus: verification.ApprovalStatus,
            SourceToolCallId: toolCall.CallId,
            ToolName: toolCall.ToolName,
            Verification: verification);
        return true;
    }

    private static string CreateVerificationFailureFeedback(VerificationResultSummary verification)
    {
        List<string> lines =
        [
            "Automatic verification failed.",
            $"status: {verification.Status}"
        ];

        if (!string.IsNullOrWhiteSpace(verification.Command))
        {
            lines.Add($"command: {verification.Command}");
        }

        if (verification.ExitCode is not null)
        {
            lines.Add($"exitCode: {verification.ExitCode.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        }

        if (!string.IsNullOrWhiteSpace(verification.ErrorCode))
        {
            lines.Add($"errorCode: {verification.ErrorCode}");
        }

        if (!string.IsNullOrWhiteSpace(verification.Summary))
        {
            lines.Add("summary:");
            lines.Add(verification.Summary);
        }

        return Bound(string.Join(Environment.NewLine, lines), MaxSummaryCharacters).Text;
    }

    private static AgentRetryDecisionInput CreateToolFailureInput(
        AgentToolCallRequest toolCall,
        ToolExecutionResult result)
    {
        string failureKind = ClassifyToolFailure(toolCall.ToolName, result);
        return new AgentRetryDecisionInput(
            FailureKind: failureKind,
            StopReason: AgentStopReason.FromToolResult(result),
            ErrorCode: result.ErrorCode,
            Summary: Bound(result.Summary, MaxSummaryCharacters).Text,
            Retryable: result.Retryable,
            ApprovalStatus: result.ApprovalStatus,
            SourceToolCallId: toolCall.CallId,
            ToolName: toolCall.ToolName,
            StructuredPayload: result.StructuredPayload);
    }

    private static string ClassifyToolFailure(string toolName, ToolExecutionResult result)
    {
        if (string.Equals(result.ErrorCode, ToolErrorCode.ApprovalDenied, StringComparison.Ordinal) ||
            result.ApprovalStatus is "denied" or "approval-required" or "dangerous-shell-denied")
        {
            return AgentFailureKind.Approval;
        }

        if (string.Equals(toolName, ShellToolName, StringComparison.Ordinal) || IsShellError(result.ErrorCode))
        {
            return AgentFailureKind.Shell;
        }

        if (string.Equals(toolName, PatchToolName, StringComparison.Ordinal) || IsPatchError(result.ErrorCode))
        {
            return AgentFailureKind.Patch;
        }

        return AgentFailureKind.Tool;
    }

    private static AgentRetryAttempt CreateRetryAttempt(
        AgentRetryDecisionInput input,
        int retryIndex,
        IReadOnlyList<ChangedFileSummary> changedFiles)
    {
        List<string> commands = [];
        AddCommands(commands, input);

        IReadOnlyList<string> changedFilePaths = changedFiles
            .Select(file => file.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new AgentRetryAttempt(
            Index: retryIndex,
            FailureKind: input.FailureKind,
            StopReason: input.StopReason,
            ErrorCode: input.ErrorCode,
            SourceToolCallId: input.SourceToolCallId,
            ToolName: input.ToolName,
            Summary: Bound(input.Summary, 1024).Text,
            Commands: commands,
            ChangedFiles: changedFilePaths,
            VerificationStatus: input.Verification?.Status);
    }

    private static ToolExecutionResult AddRetryFeedback(
        ToolExecutionResult result,
        AgentRetryAttempt attempt,
        AgentRetryDecision decision)
    {
        Dictionary<string, JsonElement> payload = CopyBoundedFeedbackPayload(result.StructuredPayload, out bool payloadTruncated);
        BoundedText summary = Bound(result.Summary, MaxSummaryCharacters);
        payload["retryFeedback"] = JsonSerializer.SerializeToElement(new
        {
            attempt = attempt.Index,
            failureKind = attempt.FailureKind,
            stopReason = attempt.StopReason,
            errorCode = attempt.ErrorCode,
            remainingRetries = decision.RemainingRetries,
            summary = attempt.Summary,
            commands = attempt.Commands,
            changedFiles = attempt.ChangedFiles,
            verificationStatus = attempt.VerificationStatus
        }).Clone();
        payload["failureKind"] = JsonSerializer.SerializeToElement(attempt.FailureKind).Clone();
        payload["stopReason"] = JsonSerializer.SerializeToElement(attempt.StopReason).Clone();
        payload["remainingRetries"] = JsonSerializer.SerializeToElement(decision.RemainingRetries).Clone();
        payload["feedbackTruncated"] = JsonSerializer.SerializeToElement(summary.Truncated || payloadTruncated).Clone();

        return result with
        {
            Summary = summary.Text,
            StructuredPayload = new ReadOnlyDictionary<string, JsonElement>(payload)
        };
    }

    private static Dictionary<string, JsonElement> CopyBoundedFeedbackPayload(
        IReadOnlyDictionary<string, JsonElement>? structuredPayload,
        out bool truncated)
    {
        truncated = false;
        Dictionary<string, JsonElement> copy = new(StringComparer.Ordinal);
        if (structuredPayload is null)
        {
            return copy;
        }

        foreach (KeyValuePair<string, JsonElement> item in structuredPayload)
        {
            string text = item.Value.ValueKind == JsonValueKind.String
                ? item.Value.GetString() ?? string.Empty
                : item.Value.GetRawText();
            if (text.Length > MaxSummaryCharacters)
            {
                BoundedText bounded = Bound(text, MaxSummaryCharacters);
                copy[item.Key] = JsonSerializer.SerializeToElement(bounded.Text).Clone();
                copy[item.Key + "FeedbackTruncated"] = JsonSerializer.SerializeToElement(true).Clone();
                truncated = true;
                continue;
            }

            copy[item.Key] = item.Value.Clone();
        }

        return copy;
    }

    private static void AddCommands(List<string> commands, AgentRetryDecisionInput input)
    {
        if (!string.IsNullOrWhiteSpace(input.Verification?.Command))
        {
            AddDistinct(commands, input.Verification.Command!);
        }

        string? command = ReadString(input.StructuredPayload, "command");
        if (!string.IsNullOrWhiteSpace(command))
        {
            AddDistinct(commands, command!);
        }
    }

    private static void AddDistinct(List<string> values, string value)
    {
        if (!values.Contains(value, StringComparer.Ordinal))
        {
            values.Add(value);
        }
    }

    private static string ClassifyErrorCode(string? errorCode)
    {
        if (errorCode is "agent-loop-limit-reached"
            or "agent-tool-call-limit-reached"
            or "agent-overall-timeout-reached"
            or "agent-model-call-timeout-reached"
            or "agent-retry-budget-exhausted")
        {
            return AgentFailureKind.Budget;
        }

        if (errorCode is "openai-http-error" or "openai-client-error" or "agent-model-call-canceled")
        {
            return AgentFailureKind.Model;
        }

        if (string.Equals(errorCode, ToolErrorCode.ApprovalDenied, StringComparison.Ordinal))
        {
            return AgentFailureKind.Approval;
        }

        if (IsShellError(errorCode))
        {
            return AgentFailureKind.Shell;
        }

        if (IsPatchError(errorCode))
        {
            return AgentFailureKind.Patch;
        }

        return AgentFailureKind.Tool;
    }

    private static bool IsShellError(string? errorCode)
    {
        return errorCode is ToolErrorCode.ShellCommandFailed
            or ToolErrorCode.ShellCwdDenied
            or ToolErrorCode.ShellPolicyDenied
            or ToolErrorCode.DangerousCommandDenied
            or ToolErrorCode.ShellTimeout
            or ToolErrorCode.ShellExitCode
            or ToolErrorCode.ShellExecutionFailed;
    }

    private static bool IsPatchError(string? errorCode)
    {
        return errorCode is ToolErrorCode.PatchApplyFailed
            or ToolErrorCode.InvalidPatch
            or ToolErrorCode.PatchContextNotFound
            or ToolErrorCode.PatchTargetChanged;
    }

    private PostPatchWorkflowResult RunPostPatchWorkflow(
        AgentRunRequest request,
        AgentToolCallRequest patchToolCall,
        ToolExecutionResult patchResult,
        CancellationToken cancellationToken)
    {
        ToolExecutionResult gitStatus = toolExecutor.Execute(
            "git.status",
            new ToolExecutionContext(
                patchToolCall.CallId + "_changed_status",
                request.Workspace,
                "{}",
                ToolExecutionPhase.Planning),
            cancellationToken);
        ToolExecutionResult gitDiff = toolExecutor.Execute(
            "git.diff",
            new ToolExecutionContext(
                patchToolCall.CallId + "_changed_diff",
                request.Workspace,
                """{"stat":true}""",
                ToolExecutionPhase.Planning),
            cancellationToken);
        IReadOnlyList<ChangedFileSummary> files = CreateChangedFiles(
            patchToolCall,
            patchResult,
            gitStatus,
            gitDiff);
        VerificationResultSummary verification = RunVerification(
            request,
            patchToolCall,
            cancellationToken);

        return new PostPatchWorkflowResult(files, gitStatus, gitDiff, verification);
    }

    private VerificationResultSummary RunVerification(
        AgentRunRequest request,
        AgentToolCallRequest patchToolCall,
        CancellationToken cancellationToken)
    {
        if (!AgentVerificationCommandSelector.TrySelect(
            request,
            out AgentVerificationCommand? command,
            out string skippedReason) ||
            command is null)
        {
            return new VerificationResultSummary(
                Status: "skipped",
                Source: "not-configured",
                Command: null,
                WorkingDirectory: null,
                Succeeded: false,
                ApprovalStatus: "not-required",
                ErrorCode: null,
                ExitCode: null,
                TimedOut: false,
                StdoutTruncated: false,
                StderrTruncated: false,
                Stdout: null,
                Stderr: null,
                Summary: skippedReason);
        }

        string cwd = GetVerificationWorkingDirectory(request);
        string argumentsJson = JsonSerializer.Serialize(new
        {
            command = command.Command,
            cwd,
            timeoutMilliseconds = WorkspaceShellTool.DefaultTimeoutMilliseconds,
            maxStdoutBytes = WorkspaceShellTool.DefaultMaxOutputBytes,
            maxStderrBytes = WorkspaceShellTool.DefaultMaxOutputBytes
        });
        ToolExecutionResult shellResult = toolExecutor.Execute(
            ShellToolName,
            new ToolExecutionContext(
                patchToolCall.CallId + "_verification",
                request.Workspace,
                argumentsJson),
            cancellationToken);

        return CreateVerificationResult(command, cwd, shellResult);
    }

    private static VerificationResultSummary CreateVerificationResult(
        AgentVerificationCommand command,
        string cwd,
        ToolExecutionResult shellResult)
    {
        IReadOnlyDictionary<string, JsonElement>? payload = shellResult.StructuredPayload;
        int? exitCode = ReadNullableInt(payload, "exitCode");
        bool timedOut = ReadBool(payload, "timedOut");
        BoundedText stdout = Bound(ReadString(payload, "stdout") ?? string.Empty, MaxVerificationOutputCharacters);
        BoundedText stderr = Bound(ReadString(payload, "stderr") ?? string.Empty, MaxVerificationOutputCharacters);
        bool stdoutTruncated = ReadBool(payload, "stdoutTruncated") || stdout.Truncated;
        bool stderrTruncated = ReadBool(payload, "stderrTruncated") || stderr.Truncated;
        string status = shellResult.Succeeded
            ? DiagnosticEventStatus.Success
            : timedOut ? DiagnosticEventStatus.Timeout : DiagnosticEventStatus.Failure;

        return new VerificationResultSummary(
            Status: status,
            Source: command.ProfileName is null ? command.Source : command.Source + ":" + command.ProfileName,
            Command: command.Command,
            WorkingDirectory: cwd,
            Succeeded: shellResult.Succeeded,
            ApprovalStatus: shellResult.ApprovalStatus,
            ErrorCode: shellResult.ErrorCode,
            ExitCode: exitCode,
            TimedOut: timedOut,
            StdoutTruncated: stdoutTruncated,
            StderrTruncated: stderrTruncated,
            Stdout: stdout.Text,
            Stderr: stderr.Text,
            Summary: shellResult.Summary);
    }

    private static string GetVerificationWorkingDirectory(AgentRunRequest request)
    {
        AgentTaskContext? taskContext = request.TaskContext;
        if (taskContext is not null &&
            taskContext.CurrentDirectoryErrorCode is null &&
            !string.IsNullOrWhiteSpace(taskContext.CurrentDirectory))
        {
            return taskContext.CurrentDirectory;
        }

        return ".";
    }

    private static IReadOnlyList<ChangedFileSummary> CreateChangedFiles(
        AgentToolCallRequest patchToolCall,
        ToolExecutionResult patchResult,
        ToolExecutionResult gitStatus,
        ToolExecutionResult gitDiff)
    {
        string? patchPath = ReadString(patchResult.StructuredPayload, "path");
        BoundedText diffStat = Bound(gitDiff.Succeeded ? gitDiff.Summary : string.Empty, MaxSummaryCharacters);
        bool diffTruncated = ReadBool(gitDiff.StructuredPayload, "truncated") || diffStat.Truncated;
        string? gitErrorCode = gitStatus.Succeeded
            ? gitDiff.ErrorCode
            : gitStatus.ErrorCode ?? gitDiff.ErrorCode;

        List<(string Path, string Status)> parsedStatus = gitStatus.Succeeded
            ? ParseGitStatus(gitStatus.Summary)
            : [];
        if (parsedStatus.Count > 0)
        {
            return parsedStatus
                .Select(file => new ChangedFileSummary(
                    file.Path,
                    file.Status,
                    patchToolCall.CallId,
                    string.IsNullOrWhiteSpace(diffStat.Text) ? null : diffStat.Text,
                    diffTruncated,
                    gitErrorCode))
                .ToArray();
        }

        if (!string.IsNullOrWhiteSpace(patchPath))
        {
            return
            [
                new ChangedFileSummary(
                    patchPath!,
                    gitStatus.Succeeded ? "changed" : "unknown",
                    patchToolCall.CallId,
                    string.IsNullOrWhiteSpace(diffStat.Text) ? null : diffStat.Text,
                    diffTruncated,
                    gitErrorCode)
            ];
        }

        return [];
    }

    private static List<(string Path, string Status)> ParseGitStatus(string summary)
    {
        List<(string Path, string Status)> files = [];
        foreach (string rawLine in summary.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.TrimEnd();
            if (string.Equals(line.Trim(), "working tree clean", StringComparison.OrdinalIgnoreCase) ||
                line.Length < 3)
            {
                continue;
            }

            string statusCode = line[..2].Trim();
            string path = line[2..].Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            if (path.Contains(" -> ", StringComparison.Ordinal))
            {
                path = path[(path.LastIndexOf(" -> ", StringComparison.Ordinal) + 4)..].Trim();
            }

            files.Add((path, NormalizeGitStatus(statusCode)));
        }

        return files;
    }

    private static string NormalizeGitStatus(string statusCode)
    {
        return statusCode switch
        {
            "??" => "untracked",
            "A" => "added",
            "D" => "deleted",
            "R" => "renamed",
            "C" => "copied",
            "M" => "modified",
            "" => "changed",
            _ when statusCode.Contains('M') => "modified",
            _ when statusCode.Contains('A') => "added",
            _ when statusCode.Contains('D') => "deleted",
            _ when statusCode.Contains('R') => "renamed",
            _ => statusCode
        };
    }

    private static ToolExecutionResult AugmentPatchResult(
        ToolExecutionResult result,
        PostPatchWorkflowResult postPatch)
    {
        Dictionary<string, JsonElement> payload = CopyStructuredPayload(result.StructuredPayload);
        payload["changedFileCount"] = JsonSerializer.SerializeToElement(postPatch.ChangedFiles.Count).Clone();
        payload["changedFiles"] = JsonSerializer.SerializeToElement(
            postPatch.ChangedFiles.Select(file => new
            {
                path = file.Path,
                status = file.Status,
                sourceToolCallId = file.SourceToolCallId,
                diffStat = file.DiffStat,
                diffStatTruncated = file.DiffStatTruncated,
                errorCode = file.ErrorCode
            }).ToArray()).Clone();
        payload["gitStatusSucceeded"] = JsonSerializer.SerializeToElement(postPatch.GitStatusResult.Succeeded).Clone();
        payload["gitStatusSummary"] = JsonSerializer.SerializeToElement(Bound(postPatch.GitStatusResult.Summary, MaxSummaryCharacters).Text).Clone();
        payload["gitStatusErrorCode"] = JsonSerializer.SerializeToElement(postPatch.GitStatusResult.ErrorCode).Clone();
        payload["gitDiffSucceeded"] = JsonSerializer.SerializeToElement(postPatch.GitDiffResult.Succeeded).Clone();
        payload["gitDiffSummary"] = JsonSerializer.SerializeToElement(Bound(postPatch.GitDiffResult.Summary, MaxSummaryCharacters).Text).Clone();
        payload["gitDiffErrorCode"] = JsonSerializer.SerializeToElement(postPatch.GitDiffResult.ErrorCode).Clone();
        payload["verificationStatus"] = JsonSerializer.SerializeToElement(postPatch.VerificationResult.Status).Clone();
        payload["verification"] = JsonSerializer.SerializeToElement(CreateVerificationPayload(postPatch.VerificationResult)).Clone();

        return result with
        {
            StructuredPayload = new ReadOnlyDictionary<string, JsonElement>(payload)
        };
    }

    private static object CreateVerificationPayload(VerificationResultSummary verification)
    {
        return new
        {
            status = verification.Status,
            source = verification.Source,
            command = verification.Command,
            cwd = verification.WorkingDirectory,
            succeeded = verification.Succeeded,
            approvalStatus = verification.ApprovalStatus,
            errorCode = verification.ErrorCode,
            exitCode = verification.ExitCode,
            timedOut = verification.TimedOut,
            stdoutTruncated = verification.StdoutTruncated,
            stderrTruncated = verification.StderrTruncated,
            stdout = verification.Stdout,
            stderr = verification.Stderr,
            summary = verification.Summary
        };
    }

    private static Dictionary<string, JsonElement> CopyStructuredPayload(
        IReadOnlyDictionary<string, JsonElement>? structuredPayload)
    {
        Dictionary<string, JsonElement> copy = new(StringComparer.Ordinal);
        if (structuredPayload is null)
        {
            return copy;
        }

        foreach (KeyValuePair<string, JsonElement> item in structuredPayload)
        {
            copy[item.Key] = item.Value.Clone();
        }

        return copy;
    }

    private static bool IsSuccessfulPatchToolResult(
        AgentToolCallRequest toolCall,
        ToolExecutionResult result)
    {
        return string.Equals(toolCall.ToolName, PatchToolName, StringComparison.Ordinal) &&
            result.Succeeded;
    }

    private readonly record struct BoundedText(string Text, bool Truncated);

    private sealed record PostPatchWorkflowResult(
        IReadOnlyList<ChangedFileSummary> ChangedFiles,
        ToolExecutionResult GitStatusResult,
        ToolExecutionResult GitDiffResult,
        VerificationResultSummary VerificationResult);

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
        RecordReferenceContext(events, taskContext);
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

    private void RecordReferenceContext(
        List<AgentRunEvent> events,
        AgentTaskContext taskContext)
    {
        WorkflowReferenceResolution references = taskContext.References ?? WorkflowReferenceResolution.Empty;
        if (!references.HasReferences)
        {
            return;
        }

        events.Add(WorkflowReferenceEventFactory.Create(
            references,
            events.Count,
            utcNowProvider()));
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

    private void RecordRetryAttemptEvent(
        List<AgentRunEvent> events,
        AgentRetryAttempt attempt,
        AgentRunState state,
        AgentStep? step)
    {
        Dictionary<string, string> payload = new(StringComparer.Ordinal)
        {
            ["attempt"] = attempt.Index.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["failureKind"] = attempt.FailureKind,
            ["stopReason"] = attempt.StopReason,
            ["remainingRetries"] = state.RemainingRetries.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["commands"] = string.Join(";", attempt.Commands),
            ["changedFiles"] = string.Join(";", attempt.ChangedFiles)
        };
        AddPayloadValue(payload, "errorCode", attempt.ErrorCode);
        AddPayloadValue(payload, "sourceToolCallId", attempt.SourceToolCallId);
        AddPayloadValue(payload, "toolName", attempt.ToolName);
        AddPayloadValue(payload, "verificationStatus", attempt.VerificationStatus);

        events.Add(new AgentRunEvent(
            Type: "retry.attempt",
            Sequence: events.Count,
            Timestamp: utcNowProvider(),
            Message: "Retry budget reserved for model feedback.",
            Summary: attempt.Summary,
            Payload: payload,
            ErrorCode: attempt.ErrorCode,
            Status: DiagnosticEventStatus.Warning,
            StepIndex: step?.Index,
            StopReason: attempt.StopReason));
    }

    private void RecordRetryBudgetExhaustedEvent(
        List<AgentRunEvent> events,
        AgentRetryDecisionInput input,
        AgentRetryDecision decision,
        AgentStep? step)
    {
        Dictionary<string, string> payload = new(StringComparer.Ordinal)
        {
            ["failureKind"] = input.FailureKind,
            ["stopReason"] = input.StopReason,
            ["remainingRetries"] = decision.RemainingRetries.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        AddPayloadValue(payload, "errorCode", input.ErrorCode);
        AddPayloadValue(payload, "sourceToolCallId", input.SourceToolCallId);
        AddPayloadValue(payload, "toolName", input.ToolName);
        AddPayloadValue(payload, "verificationStatus", input.Verification?.Status);

        events.Add(new AgentRunEvent(
            Type: "retry.exhausted",
            Sequence: events.Count,
            Timestamp: utcNowProvider(),
            Message: "Retry budget was exhausted.",
            Summary: decision.Reason,
            Payload: payload,
            ErrorCode: "agent-retry-budget-exhausted",
            Status: DiagnosticEventStatus.Failure,
            StepIndex: step?.Index,
            StopReason: AgentStopReason.RetryBudgetExhausted));
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
        AddStructuredDiagnosticPayload(payload, result.StructuredPayload, "path");
        AddStructuredDiagnosticPayload(payload, result.StructuredPayload, "replacements");
        AddStructuredDiagnosticPayload(payload, result.StructuredPayload, "hasDiff");
        AddStructuredDiagnosticPayload(payload, result.StructuredPayload, "changedFileCount");
        AddStructuredDiagnosticPayload(payload, result.StructuredPayload, "verificationStatus");

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

    private void RecordPatchDiagnosticEvents(
        List<AgentRunEvent> events,
        AgentToolCallRequest toolCall,
        ToolExecutionResult result,
        AgentStep? step)
    {
        if (!string.Equals(toolCall.ToolName, PatchToolName, StringComparison.Ordinal))
        {
            return;
        }

        IReadOnlyDictionary<string, JsonElement>? structuredPayload = result.StructuredPayload;
        string? path = ReadString(structuredPayload, "path");
        Dictionary<string, string> previewPayload = new(StringComparer.Ordinal)
        {
            ["callId"] = toolCall.CallId,
            ["toolName"] = toolCall.ToolName,
            ["approvalStatus"] = result.ApprovalStatus,
            ["succeeded"] = result.Succeeded ? "true" : "false"
        };
        AddPayloadValue(previewPayload, "path", path);
        AddPayloadValue(previewPayload, "replacements", ReadRawText(structuredPayload, "replacements"));
        AddPayloadValue(previewPayload, "hasDiff", ReadRawText(structuredPayload, "hasDiff"));
        AddPayloadValue(previewPayload, "dryRunPreview", ReadRawText(structuredPayload, "dryRunPreview"));

        bool hasPreview = structuredPayload is not null &&
            structuredPayload.ContainsKey("dryRunPreview");
        events.Add(new AgentRunEvent(
            Type: "patch.preview",
            Sequence: events.Count,
            Timestamp: utcNowProvider(),
            Message: hasPreview ? "Patch preview completed." : "Patch preview failed.",
            Summary: hasPreview
                ? string.IsNullOrWhiteSpace(path) ? "Patch preview completed." : $"Patch preview for {path}."
                : result.Summary,
            Payload: previewPayload,
            ErrorCode: hasPreview ? null : result.ErrorCode,
            ApprovalStatus: result.ApprovalStatus,
            Status: hasPreview ? DiagnosticEventStatus.Success : DiagnosticEventStatus.Failure,
            StepIndex: step?.Index));

        if (!hasPreview)
        {
            return;
        }

        bool approved = string.Equals(result.ApprovalStatus, "approved", StringComparison.Ordinal);
        events.Add(new AgentRunEvent(
            Type: "patch.approval",
            Sequence: events.Count,
            Timestamp: utcNowProvider(),
            Message: approved ? "Patch was approved." : "Patch was not approved.",
            Summary: approved ? "Patch approval granted." : result.Summary,
            Payload: new Dictionary<string, string>
            {
                ["callId"] = toolCall.CallId,
                ["toolName"] = toolCall.ToolName,
                ["approvalStatus"] = result.ApprovalStatus
            },
            ErrorCode: approved ? null : result.ErrorCode,
            ApprovalStatus: result.ApprovalStatus,
            Status: approved ? DiagnosticEventStatus.Success : DiagnosticEventStatus.Failure,
            StepIndex: step?.Index));

        Dictionary<string, string> applyPayload = new(StringComparer.Ordinal)
        {
            ["callId"] = toolCall.CallId,
            ["toolName"] = toolCall.ToolName,
            ["approvalStatus"] = result.ApprovalStatus,
            ["succeeded"] = result.Succeeded ? "true" : "false"
        };
        AddPayloadValue(applyPayload, "path", path);
        AddPayloadValue(applyPayload, "replacements", ReadRawText(structuredPayload, "replacements"));
        AddPayloadValue(applyPayload, "hasDiff", ReadRawText(structuredPayload, "hasDiff"));

        events.Add(new AgentRunEvent(
            Type: "patch.apply",
            Sequence: events.Count,
            Timestamp: utcNowProvider(),
            Message: result.Succeeded ? "Patch apply completed." : "Patch apply failed.",
            Summary: result.Summary,
            Payload: applyPayload,
            ErrorCode: result.ErrorCode,
            ApprovalStatus: result.ApprovalStatus,
            Status: result.Succeeded ? DiagnosticEventStatus.Success : DiagnosticEventStatus.Failure,
            StepIndex: step?.Index));
    }

    private void RecordChangedFilesEvent(
        List<AgentRunEvent> events,
        AgentToolCallRequest toolCall,
        PostPatchWorkflowResult postPatch,
        AgentStep? step)
    {
        string paths = string.Join(";", postPatch.ChangedFiles.Select(file => file.Path));
        string statuses = string.Join(";", postPatch.ChangedFiles.Select(file => file.Status));
        Dictionary<string, string> payload = new(StringComparer.Ordinal)
        {
            ["sourceToolCallId"] = toolCall.CallId,
            ["count"] = postPatch.ChangedFiles.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["paths"] = paths,
            ["statuses"] = statuses,
            ["gitStatusSucceeded"] = postPatch.GitStatusResult.Succeeded ? "true" : "false",
            ["gitDiffSucceeded"] = postPatch.GitDiffResult.Succeeded ? "true" : "false",
            ["gitDiffTruncated"] = ReadBool(postPatch.GitDiffResult.StructuredPayload, "truncated") ? "true" : "false"
        };
        AddPayloadValue(payload, "gitStatusErrorCode", postPatch.GitStatusResult.ErrorCode);
        AddPayloadValue(payload, "gitDiffErrorCode", postPatch.GitDiffResult.ErrorCode);
        AddPayloadValue(payload, "gitDiffSummary", Bound(postPatch.GitDiffResult.Summary, MaxSummaryCharacters).Text);

        bool succeeded = postPatch.GitStatusResult.Succeeded && postPatch.GitDiffResult.Succeeded;
        events.Add(new AgentRunEvent(
            Type: "changed.files",
            Sequence: events.Count,
            Timestamp: utcNowProvider(),
            Message: "Collected changed-file summary after patch.",
            Summary: postPatch.ChangedFiles.Count == 0
                ? "No changed files reported after patch."
                : "Changed files: " + paths,
            Payload: payload,
            ErrorCode: succeeded ? null : postPatch.GitStatusResult.ErrorCode ?? postPatch.GitDiffResult.ErrorCode,
            Status: succeeded ? DiagnosticEventStatus.Success : DiagnosticEventStatus.Warning,
            StepIndex: step?.Index));
    }

    private void RecordVerificationResultEvent(
        List<AgentRunEvent> events,
        AgentToolCallRequest toolCall,
        VerificationResultSummary verification,
        AgentStep? step)
    {
        Dictionary<string, string> payload = new(StringComparer.Ordinal)
        {
            ["sourceToolCallId"] = toolCall.CallId,
            ["status"] = verification.Status,
            ["source"] = verification.Source,
            ["succeeded"] = verification.Succeeded ? "true" : "false",
            ["approvalStatus"] = verification.ApprovalStatus,
            ["timedOut"] = verification.TimedOut ? "true" : "false",
            ["stdoutTruncated"] = verification.StdoutTruncated ? "true" : "false",
            ["stderrTruncated"] = verification.StderrTruncated ? "true" : "false"
        };
        AddPayloadValue(payload, "command", verification.Command);
        AddPayloadValue(payload, "cwd", verification.WorkingDirectory);
        AddPayloadValue(payload, "errorCode", verification.ErrorCode);
        AddPayloadValue(payload, "exitCode", verification.ExitCode?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddPayloadValue(payload, "stdout", verification.Stdout);
        AddPayloadValue(payload, "stderr", verification.Stderr);

        events.Add(new AgentRunEvent(
            Type: "verification.result",
            Sequence: events.Count,
            Timestamp: utcNowProvider(),
            Message: verification.Status == "skipped"
                ? "Automatic verification was skipped."
                : "Automatic verification completed.",
            Summary: verification.Summary,
            Payload: payload,
            ErrorCode: verification.ErrorCode,
            ApprovalStatus: verification.ApprovalStatus,
            Status: ToDiagnosticStatus(verification),
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

    private static string ToDiagnosticStatus(VerificationResultSummary verification)
    {
        if (string.Equals(verification.Status, "skipped", StringComparison.Ordinal))
        {
            return DiagnosticEventStatus.Warning;
        }

        if (verification.TimedOut)
        {
            return DiagnosticEventStatus.Timeout;
        }

        return verification.Succeeded
            ? DiagnosticEventStatus.Success
            : DiagnosticEventStatus.Failure;
    }

    private static void AddPayloadValue(
        Dictionary<string, string> payload,
        string key,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            payload[key] = value;
        }
    }

    private static string? ReadString(
        IReadOnlyDictionary<string, JsonElement>? structuredPayload,
        string key)
    {
        if (structuredPayload is null ||
            !structuredPayload.TryGetValue(key, out JsonElement value) ||
            value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.GetRawText();
    }

    private static string? ReadRawText(
        IReadOnlyDictionary<string, JsonElement>? structuredPayload,
        string key)
    {
        if (structuredPayload is null ||
            !structuredPayload.TryGetValue(key, out JsonElement value) ||
            value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.GetRawText();
    }

    private static bool ReadBool(
        IReadOnlyDictionary<string, JsonElement>? structuredPayload,
        string key)
    {
        if (structuredPayload is null ||
            !structuredPayload.TryGetValue(key, out JsonElement value))
        {
            return false;
        }

        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean();
        }

        return value.ValueKind == JsonValueKind.String &&
            bool.TryParse(value.GetString(), out bool parsed) &&
            parsed;
    }

    private static int? ReadNullableInt(
        IReadOnlyDictionary<string, JsonElement>? structuredPayload,
        string key)
    {
        if (structuredPayload is null ||
            !structuredPayload.TryGetValue(key, out JsonElement value) ||
            value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String &&
            int.TryParse(value.GetString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out number)
            ? number
            : null;
    }

    private static BoundedText Bound(string? value, int maxCharacters)
    {
        string text = value ?? string.Empty;
        if (text.Length <= maxCharacters)
        {
            return new BoundedText(text, false);
        }

        return new BoundedText(text[..maxCharacters], true);
    }
}
