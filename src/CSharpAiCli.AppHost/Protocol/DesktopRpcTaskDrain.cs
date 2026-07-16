namespace CSharpAiCli.AppHost.Protocol;

internal sealed class DesktopRpcTaskDrain
{
    private readonly IDesktopRpcDeadlineScheduler deadlineScheduler;

    public DesktopRpcTaskDrain(IDesktopRpcDeadlineScheduler deadlineScheduler)
    {
        this.deadlineScheduler = deadlineScheduler ?? throw new ArgumentNullException(nameof(deadlineScheduler));
    }

    public async ValueTask<bool> WaitAsync(
        IReadOnlyCollection<Task> tasks,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using IDesktopRpcDeadline deadline = deadlineScheduler.Create(timeout);
        return await WaitAsync(tasks, deadline, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> WaitAsync(
        IReadOnlyCollection<Task> tasks,
        IDesktopRpcDeadline deadline,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(deadline);
        if (tasks.Count == 0)
        {
            return true;
        }

        using CancellationTokenSource waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            deadline.Token);
        Task completion = Task.WhenAll(tasks);
        try
        {
            await completion.WaitAsync(waitCancellation.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception) when (completion.IsCompleted)
        {
            return true;
        }
        catch (OperationCanceledException) when (deadline.IsExpired && !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
