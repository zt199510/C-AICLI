using CSharpAiCli.Application;

namespace CSharpAiCli.AppHost.Protocol;

internal sealed class DesktopWriteExecutionSupervisor : IDisposable
{
    private readonly object sync = new();
    private readonly ITurnExecutionRuntime runtime;
    private readonly Func<TurnExecutionStateProjection, ValueTask> committed;
    private CancellationTokenSource? cancellation;
    private ApprovalWaiter? approvalWaiter;
    private Task? execution;
    private string? threadId;
    private string? turnId;
    private readonly Dictionary<string, ApplicationResult<TurnExecutionStateProjection>> decisions = new(StringComparer.Ordinal);

    public DesktopWriteExecutionSupervisor(
        Func<TurnExecutionStateProjection, ValueTask> committed,
        ITurnExecutionRuntime runtime)
    {
        this.committed = committed ?? throw new ArgumentNullException(nameof(committed));
        this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    internal ITurnExecutionRuntime Runtime => runtime;

    public bool IsBusy
    {
        get { lock (sync) return execution is { IsCompleted: false }; }
    }

    public bool TryStart(DesktopApplicationSession session, TurnExecutionStateProjection state)
    {
        lock (sync)
        {
            if (execution is { IsCompleted: false }) return false;
            cancellation?.Dispose();
            cancellation = new CancellationTokenSource();
            approvalWaiter = new ApprovalWaiter();
            decisions.Clear();
            threadId = state.ThreadId;
            turnId = state.TurnId;
            execution = RunAsync(session, state.ThreadId, state.TurnId, approvalWaiter, cancellation.Token);
            return true;
        }
    }

    public ApplicationResult<TurnExecutionStateProjection> ResolveApproval(
        DesktopApplicationSession session,
        ApprovalResolveRequest request)
    {
        lock (sync)
        {
            if (decisions.TryGetValue(request.ClientMutationId, out ApplicationResult<TurnExecutionStateProjection>? prior)) return prior;
            if (threadId != request.ThreadId || turnId != request.TurnId || approvalWaiter?.Matches(request.RequestId) != true)
                return Failure("approval-stale", "Approval request is not attached to the active execution.");
            ApplicationResult<TurnExecutionStateProjection> resolved = session.ResolveApproval(
                request.ThreadId, request.TurnId, request.RequestId, request.Decision,
                request.ExpectedThreadRevision, request.ExpectedTurnRevision, request.ExpectedApprovalRevision,
                request.ClientMutationId);
            if (!resolved.Succeeded) return resolved;
            if (!approvalWaiter.TryResolve(new InteractiveApprovalDecision(request.Decision, request.ClientMutationId,
                    request.ExpectedTurnRevision, request.ExpectedApprovalRevision)))
                return Failure("approval-stale", "Approval request was already resolved.");
            decisions[request.ClientMutationId] = resolved;
            return resolved;
        }
    }

    public ApplicationResult<TurnExecutionStateProjection> Cancel(
        DesktopApplicationSession session,
        TurnCancelRequest request)
    {
        lock (sync)
        {
            if (threadId != request.ThreadId || turnId != request.TurnId || cancellation is null)
                return Failure("turn-not-active", "Turn is not the active write execution.");
            ApplicationResult<TurnExecutionStateProjection> persisted = session.CancelTurn(
                request.ThreadId, request.TurnId, request.ExpectedThreadRevision, request.ExpectedTurnRevision,
                request.ClientMutationId);
            if (!persisted.Succeeded) return persisted;
            approvalWaiter?.Cancel();
            cancellation.Cancel();
            return persisted;
        }
    }

    public void Stop()
    {
        lock (sync)
        {
            approvalWaiter?.Cancel();
            cancellation?.Cancel();
        }
    }

    public void Dispose()
    {
        Stop();
        Task? pending;
        lock (sync)
        {
            pending = execution;
        }
        if (pending is not null)
        {
            try
            {
                pending.Wait(TurnExecutionLimits.CancelAcknowledgementTarget);
            }
            catch (AggregateException exception) when (
                exception.InnerExceptions.All(inner =>
                    inner is OperationCanceledException or TaskCanceledException))
            {
            }
        }
        lock (sync)
        {
            cancellation?.Dispose();
            cancellation = null;
            approvalWaiter = null;
            execution = null;
        }
    }

    private async Task RunAsync(DesktopApplicationSession session, string activeThreadId, string activeTurnId,
        ApprovalWaiter waiter, CancellationToken cancellationToken)
    {
        try
        {
            await session.ExecuteTurnAsync(activeThreadId, activeTurnId, runtime, waiter, committed,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (sync)
            {
                if (threadId == activeThreadId && turnId == activeTurnId)
                {
                    threadId = null;
                    turnId = null;
                    approvalWaiter = null;
                }
            }
        }
    }

    private static ApplicationResult<TurnExecutionStateProjection> Failure(string code, string message) =>
        ApplicationResult<TurnExecutionStateProjection>.Failure(new ApplicationError(
            code, ApplicationErrorCategory.Conflict, message, false));

    private sealed class ApprovalWaiter : IInteractiveApprovalWaiter
    {
        private readonly object sync = new();
        private TaskCompletionSource<InteractiveApprovalDecision>? completion;
        private string? requestId;

        public ValueTask<InteractiveApprovalDecision> WaitAsync(DurableApprovalProjection request, CancellationToken cancellationToken)
        {
            Task<InteractiveApprovalDecision> task;
            lock (sync)
            {
                if (completion is not null) throw new InvalidOperationException("Only one approval may be active per turn.");
                requestId = request.RequestId;
                completion = new TaskCompletionSource<InteractiveApprovalDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
                task = completion.Task;
            }
            return new ValueTask<InteractiveApprovalDecision>(task.WaitAsync(cancellationToken));
        }

        public bool Matches(string value)
        {
            lock (sync) return completion is not null && requestId == value && !completion.Task.IsCompleted;
        }

        public bool TryResolve(InteractiveApprovalDecision decision)
        {
            lock (sync) return completion?.TrySetResult(decision) == true;
        }

        public void Cancel()
        {
            lock (sync) completion?.TrySetCanceled();
        }
    }
}
