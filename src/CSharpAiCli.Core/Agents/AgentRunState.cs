using System.Collections.ObjectModel;

namespace CSharpAiCli.Core;

public sealed class AgentRunState
{
    public const string RunningStatus = "running";

    private readonly Func<DateTimeOffset> utcNowProvider;
    private readonly List<AgentStep> steps = [];

    public AgentRunState(AgentRunLimits limits, Func<DateTimeOffset> utcNowProvider)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(utcNowProvider);

        Limits = limits;
        this.utcNowProvider = utcNowProvider;
        StartedAtUtc = utcNowProvider();
        DeadlineUtc = StartedAtUtc.Add(limits.OverallTimeout);
    }

    public AgentRunLimits Limits { get; }

    public DateTimeOffset StartedAtUtc { get; }

    public DateTimeOffset DeadlineUtc { get; }

    public string Status { get; private set; } = RunningStatus;

    public string? StopReason { get; private set; }

    public AgentLoopError? Error { get; private set; }

    public int ToolCallCount { get; private set; }

    public IReadOnlyList<AgentStep> Steps => new ReadOnlyCollection<AgentStep>(steps.ToArray());

    public bool TryBeginStep(out AgentStep? step, out AgentLoopError? error)
    {
        if (steps.Count >= Limits.MaxSteps)
        {
            step = null;
            error = StopFailure(
                AgentStopReason.MaxStepsExceeded,
                "agent-loop-limit-reached",
                "Agent loop reached the maximum iteration limit.",
                retryable: false);
            return false;
        }

        step = new AgentStep(
            Index: steps.Count,
            StartedAtUtc: utcNowProvider(),
            Status: RunningStatus);
        steps.Add(step);
        error = null;
        return true;
    }

    public bool TryReserveToolCall(AgentStep? step, out AgentLoopError? error)
    {
        if (ToolCallCount >= Limits.MaxToolCalls)
        {
            error = StopFailure(
                AgentStopReason.MaxToolCallsExceeded,
                "agent-tool-call-limit-reached",
                "Agent loop reached the maximum tool call limit.",
                retryable: false);
            CompleteStep(step, DiagnosticEventStatus.Failure, AgentStopReason.MaxToolCallsExceeded);
            return false;
        }

        ToolCallCount++;
        if (step is not null)
        {
            UpdateStep(step with { ToolCallCount = step.ToolCallCount + 1 });
        }

        error = null;
        return true;
    }

    public bool TryCreateOverallTimeoutError(out AgentLoopError? error)
    {
        if (utcNowProvider() <= DeadlineUtc)
        {
            error = null;
            return false;
        }

        error = StopFailure(
            AgentStopReason.OverallTimeout,
            "agent-overall-timeout-reached",
            "Agent loop reached the overall timeout.",
            retryable: false,
            status: DiagnosticEventStatus.Timeout);
        return true;
    }

    public AgentLoopError StopModelTimeout()
    {
        return StopFailure(
            AgentStopReason.ModelTimeout,
            "agent-model-call-timeout-reached",
            "Agent model call reached the timeout.",
            retryable: false,
            status: DiagnosticEventStatus.Timeout);
    }

    public AgentLoopError StopOverallTimeout()
    {
        return StopFailure(
            AgentStopReason.OverallTimeout,
            "agent-overall-timeout-reached",
            "Agent loop reached the overall timeout.",
            retryable: false,
            status: DiagnosticEventStatus.Timeout);
    }

    public AgentLoopError StopEmptyTurn()
    {
        return StopFailure(
            AgentStopReason.EmptyTurn,
            "agent-empty-turn",
            "Agent model returned neither a final response nor tool calls.",
            retryable: false);
    }

    public void StopSuccess()
    {
        if (!string.Equals(Status, RunningStatus, StringComparison.Ordinal))
        {
            return;
        }

        Status = DiagnosticEventStatus.Success;
        StopReason = AgentStopReason.Completed;
    }

    public AgentLoopError StopFailure(
        string stopReason,
        string errorCode,
        string safeMessage,
        bool retryable,
        string status = DiagnosticEventStatus.Failure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stopReason);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);

        AgentLoopError error = new(
            StopReason: stopReason,
            ErrorCode: errorCode,
            SafeMessage: safeMessage ?? string.Empty,
            Retryable: retryable,
            Status: status);

        if (string.Equals(Status, RunningStatus, StringComparison.Ordinal))
        {
            Status = status;
            StopReason = stopReason;
            Error = error;
        }

        return error;
    }

    public void CompleteStep(AgentStep? step, string status, string? stopReason = null)
    {
        if (step is null || step.Index < 0 || step.Index >= steps.Count)
        {
            return;
        }

        AgentStep current = steps[step.Index];
        UpdateStep(current with
        {
            Status = status,
            CompletedAtUtc = utcNowProvider(),
            StopReason = stopReason
        });
    }

    private void UpdateStep(AgentStep step)
    {
        if (step.Index < 0 || step.Index >= steps.Count)
        {
            return;
        }

        steps[step.Index] = step;
    }
}
