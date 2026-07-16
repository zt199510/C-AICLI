namespace CSharpAiCli.AppHost.Protocol;

internal sealed class DesktopRpcRateLimiter
{
    private readonly object sync = new();
    private readonly double capacity;
    private readonly double tokensPerSecond;
    private readonly TimeProvider timeProvider;
    private double tokens;
    private long lastTimestamp;

    public DesktopRpcRateLimiter(int capacity, int tokensPerSecond, TimeProvider timeProvider)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(tokensPerSecond, 1);
        this.capacity = capacity;
        this.tokensPerSecond = tokensPerSecond;
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        tokens = capacity;
        lastTimestamp = timeProvider.GetTimestamp();
    }

    public bool TryAcquire()
    {
        lock (sync)
        {
            long now = timeProvider.GetTimestamp();
            double elapsedSeconds = timeProvider.GetElapsedTime(lastTimestamp, now).TotalSeconds;
            if (elapsedSeconds > 0)
            {
                tokens = Math.Min(capacity, tokens + (elapsedSeconds * tokensPerSecond));
                lastTimestamp = now;
            }

            if (tokens < 1)
            {
                return false;
            }

            tokens--;
            return true;
        }
    }
}
