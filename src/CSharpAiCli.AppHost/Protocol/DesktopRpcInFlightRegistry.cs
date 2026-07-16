namespace CSharpAiCli.AppHost.Protocol;

internal enum DesktopRpcAdmission
{
    Registered,
    Duplicate,
    Busy
}

internal sealed class DesktopRpcInFlightRegistry : IDisposable
{
    private readonly object sync = new();
    private readonly int maxInFlight;
    private readonly Dictionary<long, CancellationTokenSource> requests = [];
    private bool disposed;

    public DesktopRpcInFlightRegistry(int maxInFlight)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxInFlight, 1);
        this.maxInFlight = maxInFlight;
    }

    public DesktopRpcAdmission TryRegister(
        long id,
        CancellationTokenSource cancellation,
        bool allowBeyondCapacity = false)
    {
        ArgumentNullException.ThrowIfNull(cancellation);
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (requests.ContainsKey(id))
            {
                return DesktopRpcAdmission.Duplicate;
            }

            if (!allowBeyondCapacity && requests.Count >= maxInFlight)
            {
                return DesktopRpcAdmission.Busy;
            }

            requests.Add(id, cancellation);
            return DesktopRpcAdmission.Registered;
        }
    }

    public bool TryCancel(long id)
    {
        CancellationTokenSource? cancellation;
        lock (sync)
        {
            if (disposed || !requests.TryGetValue(id, out cancellation))
            {
                return false;
            }
        }

        if (TryCancelSource(cancellation))
        {
            return true;
        }

        _ = Complete(id, cancellation);
        return false;
    }

    public bool Complete(long id, CancellationTokenSource cancellation)
    {
        ArgumentNullException.ThrowIfNull(cancellation);
        lock (sync)
        {
            if (disposed || !requests.TryGetValue(id, out CancellationTokenSource? current) ||
                !ReferenceEquals(current, cancellation))
            {
                return false;
            }

            return requests.Remove(id);
        }
    }

    public void CancelAll()
    {
        KeyValuePair<long, CancellationTokenSource>[] active;
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            active = requests.ToArray();
        }

        foreach ((long id, CancellationTokenSource cancellation) in active)
        {
            if (!TryCancelSource(cancellation))
            {
                _ = Complete(id, cancellation);
            }
        }
    }

    public void Dispose()
    {
        CancellationTokenSource[] active;
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            active = requests.Values.ToArray();
            requests.Clear();
        }

        foreach (CancellationTokenSource cancellation in active)
        {
            _ = TryCancelSource(cancellation);
        }
    }

    private static bool TryCancelSource(CancellationTokenSource cancellation)
    {
        try
        {
            _ = cancellation.Token;
            cancellation.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }
}
