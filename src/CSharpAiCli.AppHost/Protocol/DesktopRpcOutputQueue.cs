using System.Globalization;

namespace CSharpAiCli.AppHost.Protocol;

internal enum DesktopRpcOutboundKind
{
    Response,
    Notification
}

internal sealed record DesktopRpcOutboundFrame(
    DesktopRpcOutboundKind Kind,
    byte[] Payload,
    int FramedSize);

internal sealed class DesktopRpcOutputQueue : IDisposable
{
    private const int FixedHeaderBytes = 20;
    private readonly object sync = new();
    private readonly int maxFrames;
    private readonly int maxBytes;
    private readonly Queue<DesktopRpcOutboundFrame> responses = [];
    private readonly Queue<DesktopRpcOutboundFrame> notifications = [];
    private readonly HashSet<DesktopRpcOutboundFrame> inProgress = [];
    private readonly SemaphoreSlim available = new(0);
    private TaskCompletionSource empty = CompletedSignal();
    private bool disposed;
    private int frameCount;
    private int byteCount;

    public DesktopRpcOutputQueue(int maxFrames, int maxBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFrames, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBytes, 1);
        this.maxFrames = maxFrames;
        this.maxBytes = maxBytes;
    }

    public int FrameCount
    {
        get { lock (sync) return frameCount; }
    }

    public int ByteCount
    {
        get { lock (sync) return byteCount; }
    }

    public static int GetFramedSize(int payloadBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(payloadBytes);
        int digits = payloadBytes.ToString(CultureInfo.InvariantCulture).Length;
        return checked(FixedHeaderBytes + digits + payloadBytes);
    }

    public bool TryEnqueueResponse(ReadOnlySpan<byte> payload) =>
        TryEnqueue(DesktopRpcOutboundKind.Response, payload);

    public bool TryEnqueueNotification(ReadOnlySpan<byte> payload) =>
        TryEnqueue(DesktopRpcOutboundKind.Notification, payload);

    public async ValueTask<DesktopRpcOutboundFrame> DequeueAsync(CancellationToken cancellationToken)
    {
        await available.WaitAsync(cancellationToken).ConfigureAwait(false);
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            DesktopRpcOutboundFrame frame = responses.Count > 0
                ? responses.Dequeue()
                : notifications.Dequeue();
            inProgress.Add(frame);
            return frame;
        }
    }

    public void CompleteWrite(DesktopRpcOutboundFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!inProgress.Remove(frame))
            {
                throw new InvalidOperationException("Outbound frame is not owned by the writer.");
            }

            frameCount--;
            byteCount -= frame.FramedSize;
            if (frameCount == 0)
            {
                empty.TrySetResult();
            }

        }
    }

    public ValueTask WaitUntilEmptyAsync(CancellationToken cancellationToken)
    {
        Task task;
        lock (sync)
        {
            task = empty.Task;
        }

        return new ValueTask(task.WaitAsync(cancellationToken));
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            responses.Clear();
            notifications.Clear();
            inProgress.Clear();
            frameCount = 0;
            byteCount = 0;
            empty.TrySetResult();
        }

        available.Dispose();
    }

    private bool TryEnqueue(DesktopRpcOutboundKind kind, ReadOnlySpan<byte> payload)
    {
        int framedSize = GetFramedSize(payload.Length);
        lock (sync)
        {
            if (disposed || frameCount >= maxFrames || framedSize > maxBytes - byteCount)
            {
                return false;
            }

            DesktopRpcOutboundFrame frame = new(kind, payload.ToArray(), framedSize);
            if (frameCount == 0)
            {
                empty = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            if (kind == DesktopRpcOutboundKind.Response)
            {
                responses.Enqueue(frame);
            }
            else
            {
                notifications.Enqueue(frame);
            }

            frameCount++;
            byteCount += framedSize;
        }

        available.Release();
        return true;
    }

    private static TaskCompletionSource CompletedSignal()
    {
        TaskCompletionSource signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        signal.SetResult();
        return signal;
    }
}
