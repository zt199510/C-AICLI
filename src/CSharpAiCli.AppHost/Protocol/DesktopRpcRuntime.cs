using System.Text.Json;
using CSharpAiCli.AppHost.Protocol.Generated;

namespace CSharpAiCli.AppHost.Protocol;

internal sealed class DesktopRpcRuntime : IDisposable
{
    private readonly object notificationSync = new();
    private readonly DesktopRpcInFlightRegistry inFlight;
    private readonly DesktopRpcOutputQueue output;
    private readonly Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask<byte[]>> handler;
    private readonly IDesktopRpcDeadlineScheduler deadlineScheduler;
    private readonly DesktopRpcRateLimiter? rateLimiter;
    private readonly Func<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>, byte[]?>? notificationFactory;
    private bool disposed;

    public DesktopRpcRuntime(
        int maxInFlight,
        DesktopRpcOutputQueue output,
        Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask<byte[]>> handler,
        IDesktopRpcDeadlineScheduler? deadlineScheduler = null,
        DesktopRpcRateLimiter? rateLimiter = null,
        Func<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>, byte[]?>? notificationFactory = null)
    {
        inFlight = new DesktopRpcInFlightRegistry(maxInFlight);
        this.output = output ?? throw new ArgumentNullException(nameof(output));
        this.handler = handler ?? throw new ArgumentNullException(nameof(handler));
        this.deadlineScheduler = deadlineScheduler ?? new DesktopRpcDeadlineScheduler(TimeProvider.System);
        this.rateLimiter = rateLimiter;
        this.notificationFactory = notificationFactory;
    }

    public async ValueTask ProcessFrameAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken sessionCancellation)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!TryReadRequest(payload, out long id, out string? method))
        {
            byte[] invalidResponse = await handler(payload, sessionCancellation).ConfigureAwait(false);
            Enqueue(invalidResponse);
            return;
        }

        if (rateLimiter is not null && !rateLimiter.TryAcquire())
        {
            Enqueue(Error(id, DesktopProtocolDefinition.RequestRateExceededRpcCode,
                DesktopProtocolDefinition.RequestRateExceededError, "Protocol request rate is exhausted."));
            return;
        }

        using IDesktopRpcDeadline deadline = deadlineScheduler.Create(TimeoutFor(method));
        using CancellationTokenSource requestCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(sessionCancellation, deadline.Token);
        DesktopRpcAdmission admission = inFlight.TryRegister(
            id,
            requestCancellation,
            allowBeyondCapacity: method == DesktopProtocolDefinition.CancelMethod);
        if (admission != DesktopRpcAdmission.Registered)
        {
            string errorCode = admission == DesktopRpcAdmission.Duplicate
                ? DesktopProtocolDefinition.DuplicateRequestIdError
                : DesktopProtocolDefinition.ServerBusyError;
            int rpcCode = admission == DesktopRpcAdmission.Duplicate
                ? DesktopProtocolDefinition.DuplicateRequestIdRpcCode
                : DesktopProtocolDefinition.ServerBusyRpcCode;
            Enqueue(Error(id, rpcCode, errorCode, admission == DesktopRpcAdmission.Duplicate
                ? "Protocol request id is already in flight."
                : "Protocol request capacity is exhausted."));
            return;
        }

        try
        {
            byte[] response = await handler(payload, requestCancellation.Token).ConfigureAwait(false);
            if (!sessionCancellation.IsCancellationRequested)
            {
                Enqueue(response);
                lock (notificationSync)
                {
                    byte[]? notification = notificationFactory?.Invoke(payload, response);
                    if (notification is not null && !output.TryEnqueueNotification(notification))
                    {
                        throw new DesktopProtocolException(
                            DesktopProtocolDefinition.OutputBackpressureError,
                            "Protocol output capacity is exhausted.");
                    }
                }
            }
        }
        catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
        {
            if (!sessionCancellation.IsCancellationRequested)
            {
                bool timedOut = deadline.IsExpired;
                Enqueue(Error(id,
                    timedOut
                        ? DesktopProtocolDefinition.RequestTimeoutRpcCode
                        : DesktopProtocolDefinition.RequestCanceledRpcCode,
                    timedOut
                        ? DesktopProtocolDefinition.RequestTimeoutError
                        : DesktopProtocolDefinition.RequestCanceledError,
                    timedOut
                        ? "Protocol request timed out."
                        : "Protocol request was canceled."));
            }
        }
        finally
        {
            _ = inFlight.Complete(id, requestCancellation);
        }
    }

    public bool TryCancel(long requestId) => !disposed && inFlight.TryCancel(requestId);

    public void CancelAll() => inFlight.CancelAll();

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        inFlight.Dispose();
    }

    private void Enqueue(ReadOnlySpan<byte> response)
    {
        if (!output.TryEnqueueResponse(response))
        {
            throw new DesktopProtocolException(
                DesktopProtocolDefinition.OutputBackpressureError,
                "Protocol output capacity is exhausted.");
        }
    }

    private static bool TryReadRequest(ReadOnlyMemory<byte> payload, out long id, out string? method)
    {
        id = 0;
        method = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("id", out JsonElement element) ||
                element.ValueKind != JsonValueKind.Number)
            {
                return false;
            }

            if (document.RootElement.TryGetProperty("method", out JsonElement methodElement) &&
                methodElement.ValueKind == JsonValueKind.String)
            {
                method = methodElement.GetString();
            }

            string raw = element.GetRawText();
            return raw.Length > 0 && raw.All(character => character is >= '0' and <= '9') &&
                element.TryGetInt64(out id) && id >= 1 && id <= DesktopProtocolDefinition.MaxRequestId;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static TimeSpan TimeoutFor(string? method) => DesktopProtocolDefinition.TimeoutClass(method) switch
    {
        "initialize" => TimeSpan.FromMilliseconds(DesktopProtocolDefinition.InitializeTimeoutMs),
        "mutation" => TimeSpan.FromMilliseconds(DesktopProtocolDefinition.MutationTimeoutMs),
        "shutdown" => TimeSpan.FromMilliseconds(DesktopProtocolDefinition.ShutdownDrainMs),
        _ => TimeSpan.FromMilliseconds(DesktopProtocolDefinition.DefaultQueryTimeoutMs)
    };

    private static byte[] Error(long? id, int code, string errorCode, string safeMessage) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            jsonrpc = "2.0",
            id,
            error = new
            {
                code,
                message = safeMessage,
                data = new { errorCode, safeMessage }
            }
        });
}
