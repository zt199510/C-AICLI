namespace CSharpAiCli.AppHost.Protocol;

internal interface IDesktopRpcDeadline : IDisposable
{
    CancellationToken Token { get; }

    bool IsExpired { get; }
}

internal interface IDesktopRpcDeadlineScheduler
{
    IDesktopRpcDeadline Create(TimeSpan timeout);
}

internal sealed class DesktopRpcDeadlineScheduler : IDesktopRpcDeadlineScheduler
{
    private readonly TimeProvider timeProvider;

    public DesktopRpcDeadlineScheduler(TimeProvider timeProvider)
    {
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public IDesktopRpcDeadline Create(TimeSpan timeout) => new Deadline(timeout, timeProvider);

    private sealed class Deadline : IDesktopRpcDeadline
    {
        private readonly CancellationTokenSource cancellation;

        public Deadline(TimeSpan timeout, TimeProvider timeProvider)
        {
            cancellation = new CancellationTokenSource(timeout, timeProvider);
        }

        public CancellationToken Token => cancellation.Token;

        public bool IsExpired => cancellation.IsCancellationRequested;

        public void Dispose() => cancellation.Dispose();
    }
}
