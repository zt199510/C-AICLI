using System.Text;
using System.Text.Json;
using CSharpAiCli.AppHost.Protocol;
using CSharpAiCli.AppHost.Protocol.Generated;

namespace CSharpAiCli.Tests;

public sealed class DesktopRpcRuntimeTests
{
    [Fact]
    public void In_flight_registry_enforces_id_and_capacity_and_cancels_targets()
    {
        List<CancellationTokenSource> requests = Enumerable.Range(1, 9)
            .Select(_ => new CancellationTokenSource())
            .ToList();
        try
        {
            using (DesktopRpcInFlightRegistry registry = new(maxInFlight: 8))
            {
                for (int id = 1; id <= 8; id++)
                {
                    Assert.Equal(DesktopRpcAdmission.Registered, registry.TryRegister(id, requests[id - 1]));
                }

                Assert.Equal(DesktopRpcAdmission.Duplicate, registry.TryRegister(1, requests[8]));
                Assert.Equal(DesktopRpcAdmission.Busy, registry.TryRegister(9, requests[8]));
                Assert.True(registry.TryCancel(4));
                Assert.True(requests[3].IsCancellationRequested);
                Assert.False(registry.TryCancel(99));

                Assert.True(registry.Complete(4, requests[3]));
                Assert.Equal(DesktopRpcAdmission.Registered, registry.TryRegister(9, requests[8]));
            }
        }
        finally
        {
            foreach (CancellationTokenSource request in requests)
            {
                request.Dispose();
            }
        }
    }

    [Fact]
    public void In_flight_cancel_tolerates_a_request_disposed_during_completion()
    {
        using DesktopRpcInFlightRegistry registry = new(maxInFlight: 1);
        CancellationTokenSource completed = new();
        Assert.Equal(DesktopRpcAdmission.Registered, registry.TryRegister(1, completed));
        completed.Dispose();

        Assert.False(registry.TryCancel(1));
        registry.CancelAll();
        using CancellationTokenSource next = new();
        Assert.Equal(DesktopRpcAdmission.Registered, registry.TryRegister(2, next));
        Assert.True(registry.Complete(2, next));
    }

    [Fact]
    public void In_flight_completion_cannot_remove_a_reused_request_id()
    {
        using DesktopRpcInFlightRegistry registry = new(maxInFlight: 1);
        using CancellationTokenSource original = new();
        using CancellationTokenSource replacement = new();
        using CancellationTokenSource duplicate = new();
        Assert.Equal(DesktopRpcAdmission.Registered, registry.TryRegister(1, original));
        Assert.True(registry.Complete(1, original));
        Assert.Equal(DesktopRpcAdmission.Registered, registry.TryRegister(1, replacement));

        Assert.False(registry.Complete(1, original));
        Assert.Equal(DesktopRpcAdmission.Duplicate, registry.TryRegister(1, duplicate));
        Assert.True(registry.Complete(1, replacement));
    }

    [Fact]
    public async Task Output_queue_enforces_frame_and_byte_limits_and_prioritizes_responses()
    {
        byte[] notification = Encoding.UTF8.GetBytes("{\"event\":1}");
        byte[] response = Encoding.UTF8.GetBytes("{\"result\":1}");
        int notificationBytes = DesktopRpcOutputQueue.GetFramedSize(notification.Length);
        int responseBytes = DesktopRpcOutputQueue.GetFramedSize(response.Length);
        using DesktopRpcOutputQueue queue = new(
            maxFrames: 2,
            maxBytes: notificationBytes + responseBytes);

        Assert.True(queue.TryEnqueueNotification(notification));
        Assert.True(queue.TryEnqueueResponse(response));
        Assert.False(queue.TryEnqueueResponse("{}"u8.ToArray()));

        DesktopRpcOutboundFrame first = await queue.DequeueAsync(CancellationToken.None);
        DesktopRpcOutboundFrame second = await queue.DequeueAsync(CancellationToken.None);
        Assert.Equal(DesktopRpcOutboundKind.Response, first.Kind);
        Assert.Equal(response, first.Payload);
        Assert.Equal(DesktopRpcOutboundKind.Notification, second.Kind);
        Assert.Equal(notification, second.Payload);
        Assert.Equal(2, queue.FrameCount);
        queue.CompleteWrite(first);
        queue.CompleteWrite(second);
        Assert.Equal(0, queue.FrameCount);
        Assert.Equal(0, queue.ByteCount);
    }

    [Fact]
    public void Output_queue_rejects_a_single_frame_over_the_byte_budget()
    {
        byte[] payload = Encoding.UTF8.GetBytes("{\"result\":true}");
        int framedSize = DesktopRpcOutputQueue.GetFramedSize(payload.Length);
        using DesktopRpcOutputQueue queue = new(maxFrames: 64, maxBytes: framedSize - 1);

        Assert.False(queue.TryEnqueueResponse(payload));
        Assert.Equal(0, queue.FrameCount);
        Assert.Equal(0, queue.ByteCount);
    }

    [Fact]
    public void Output_queue_counts_the_complete_content_length_frame()
    {
        Assert.Equal(34, DesktopRpcOutputQueue.GetFramedSize(payloadBytes: 12));
    }

    [Fact]
    public async Task Runtime_admits_eight_handlers_and_rejects_the_ninth_as_busy()
    {
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int enteredCount = 0;
        using DesktopRpcOutputQueue output = new(maxFrames: 64, maxBytes: 4 * 1024 * 1024);
        using DesktopRpcRuntime runtime = new(
            maxInFlight: 8,
            output,
            async (payload, cancellationToken) =>
            {
                if (Interlocked.Increment(ref enteredCount) == 8) entered.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
                return payload.ToArray();
            });

        Task[] admitted = Enumerable.Range(1, 8)
            .Select(id => runtime.ProcessFrameAsync(Request(id), CancellationToken.None).AsTask())
            .ToArray();
        await entered.Task;

        await runtime.ProcessFrameAsync(Request(9), CancellationToken.None);
        DesktopRpcOutboundFrame busy = await output.DequeueAsync(CancellationToken.None);
        using JsonDocument busyResponse = JsonDocument.Parse(busy.Payload);
        Assert.Equal("server-busy", busyResponse.RootElement.GetProperty("error")
            .GetProperty("data").GetProperty("errorCode").GetString());
        output.CompleteWrite(busy);

        release.TrySetResult();
        await Task.WhenAll(admitted);
        Assert.Equal(8, output.FrameCount);
    }

    [Fact]
    public async Task Runtime_admits_cancel_control_when_business_capacity_is_full()
    {
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int enteredCount = 0;
        using DesktopRpcOutputQueue output = new(maxFrames: 64, maxBytes: 4 * 1024 * 1024);
        using DesktopRpcRuntime runtime = new(
            maxInFlight: 8,
            output,
            async (payload, cancellationToken) =>
            {
                using JsonDocument request = JsonDocument.Parse(payload);
                if (request.RootElement.GetProperty("method").GetString() == DesktopProtocolDefinition.CancelMethod)
                {
                    return payload.ToArray();
                }

                if (Interlocked.Increment(ref enteredCount) == 8) entered.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
                return payload.ToArray();
            });
        Task[] admitted = Enumerable.Range(1, 8)
            .Select(id => runtime.ProcessFrameAsync(Request(id), CancellationToken.None).AsTask())
            .ToArray();
        await entered.Task;

        await runtime.ProcessFrameAsync(Request(
            9,
            DesktopProtocolDefinition.CancelMethod,
            new { schemaVersion = 1, targetRequestId = 1 }), CancellationToken.None);
        DesktopRpcOutboundFrame cancel = await output.DequeueAsync(CancellationToken.None);
        using JsonDocument cancelResponse = JsonDocument.Parse(cancel.Payload);
        Assert.Equal(DesktopProtocolDefinition.CancelMethod,
            cancelResponse.RootElement.GetProperty("method").GetString());
        output.CompleteWrite(cancel);

        release.TrySetResult();
        await Task.WhenAll(admitted);
    }

    [Fact]
    public async Task Output_queue_signals_exactly_when_all_frames_are_drained()
    {
        using DesktopRpcOutputQueue queue = new(maxFrames: 2, maxBytes: 1024);
        await queue.WaitUntilEmptyAsync(CancellationToken.None);

        Assert.True(queue.TryEnqueueResponse("{}"u8.ToArray()));
        Task draining = queue.WaitUntilEmptyAsync(CancellationToken.None).AsTask();
        Assert.False(draining.IsCompleted);

        DesktopRpcOutboundFrame frame = await queue.DequeueAsync(CancellationToken.None);
        Assert.False(draining.IsCompleted);
        queue.CompleteWrite(frame);
        await draining;
        Assert.True(draining.IsCompletedSuccessfully);
    }

    [Fact]
    public void Rate_limiter_refills_from_an_injected_monotonic_clock()
    {
        ManualTimeProvider time = new();
        DesktopRpcRateLimiter limiter = new(capacity: 2, tokensPerSecond: 1, time);

        Assert.True(limiter.TryAcquire());
        Assert.True(limiter.TryAcquire());
        Assert.False(limiter.TryAcquire());

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(limiter.TryAcquire());
        Assert.False(limiter.TryAcquire());
    }

    [Fact]
    public async Task Runtime_distinguishes_target_cancel_from_deadline_expiry()
    {
        ControlledDeadlineScheduler deadlines = new();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using DesktopRpcOutputQueue output = new(maxFrames: 8, maxBytes: 4096);
        using DesktopRpcRuntime runtime = new(
            maxInFlight: 8,
            output,
            async (_, cancellationToken) =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return "{}"u8.ToArray();
            },
            deadlines);

        Task canceled = runtime.ProcessFrameAsync(Request(1), CancellationToken.None).AsTask();
        await entered.Task;
        Assert.True(runtime.TryCancel(1));
        await canceled;
        DesktopRpcOutboundFrame canceledFrame = await output.DequeueAsync(CancellationToken.None);
        Assert.Equal("request-canceled", ResponseErrorCode(canceledFrame));
        output.CompleteWrite(canceledFrame);

        entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task timedOut = runtime.ProcessFrameAsync(Request(2), CancellationToken.None).AsTask();
        await entered.Task;
        deadlines.Last.Expire();
        await timedOut;
        DesktopRpcOutboundFrame timeoutFrame = await output.DequeueAsync(CancellationToken.None);
        Assert.Equal("request-timeout", ResponseErrorCode(timeoutFrame));
        output.CompleteWrite(timeoutFrame);
    }

    [Fact]
    public async Task Runtime_cancel_all_propagates_to_every_active_handler()
    {
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int enteredCount = 0;
        using DesktopRpcOutputQueue output = new(maxFrames: 8, maxBytes: 4096);
        using DesktopRpcRuntime runtime = new(
            maxInFlight: 8,
            output,
            async (_, cancellationToken) =>
            {
                if (Interlocked.Increment(ref enteredCount) == 2) entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return "{}"u8.ToArray();
            });
        Task first = runtime.ProcessFrameAsync(Request(1), CancellationToken.None).AsTask();
        Task second = runtime.ProcessFrameAsync(Request(2), CancellationToken.None).AsTask();
        await entered.Task;

        runtime.CancelAll();
        await Task.WhenAll(first, second);
        Assert.Equal(2, output.FrameCount);
        for (int index = 0; index < 2; index++)
        {
            DesktopRpcOutboundFrame frame = await output.DequeueAsync(CancellationToken.None);
            Assert.Equal("request-canceled", ResponseErrorCode(frame));
            output.CompleteWrite(frame);
        }
    }

    [Fact]
    public async Task Task_drain_returns_when_a_non_cooperative_handler_exceeds_the_deadline()
    {
        ControlledDeadlineScheduler deadlines = new();
        DesktopRpcTaskDrain drain = new(deadlines);
        TaskCompletionSource nonCooperative = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<bool> waiting = drain.WaitAsync(
            [nonCooperative.Task],
            TimeSpan.FromSeconds(2),
            CancellationToken.None).AsTask();
        Assert.False(waiting.IsCompleted);
        deadlines.Last.Expire();

        Assert.False(await waiting);
        nonCooperative.TrySetResult();
    }

    [Fact]
    public async Task Task_drain_can_share_one_end_to_end_shutdown_deadline()
    {
        ControlledDeadlineScheduler deadlines = new();
        DesktopRpcTaskDrain drain = new(deadlines);
        using IDesktopRpcDeadline shutdownDeadline = deadlines.Create(TimeSpan.FromSeconds(2));
        Assert.True(await drain.WaitAsync(
            [Task.CompletedTask],
            shutdownDeadline,
            CancellationToken.None));
        TaskCompletionSource nonCooperative = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<bool> waiting = drain.WaitAsync(
            [nonCooperative.Task],
            shutdownDeadline,
            CancellationToken.None).AsTask();
        deadlines.Last.Expire();

        Assert.False(await waiting);
        Assert.Equal(1, deadlines.CreateCount);
        nonCooperative.TrySetResult();
    }

    [Fact]
    public void Notification_sequencer_emits_only_for_successful_mutations_with_continuous_ids()
    {
        FixedTimeProvider time = new(new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero));
        DesktopThreadNotificationSequencer sequencer = new(time);
        byte[] request = JsonSerializer.SerializeToUtf8Bytes(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "thread.create",
            @params = new { schemaVersion = 1, title = "thread" }
        });
        byte[] success = JsonSerializer.SerializeToUtf8Bytes(new
        {
            jsonrpc = "2.0",
            id = 1,
            result = new
            {
                schemaVersion = 1,
                succeeded = true,
                data = new { threadId = "thread_0123456789abcdef01234567", revision = 0 }
            }
        });
        byte[] failure = JsonSerializer.SerializeToUtf8Bytes(new
        {
            jsonrpc = "2.0",
            id = 2,
            result = new { schemaVersion = 1, succeeded = false, data = (object?)null }
        });

        Assert.Null(sequencer.TryCreate("ws_0123456789abcdef01234567", request, failure));
        byte[] first = Assert.IsType<byte[]>(sequencer.TryCreate(
            "ws_0123456789abcdef01234567", request, success));
        byte[] second = Assert.IsType<byte[]>(sequencer.TryCreate(
            "ws_0123456789abcdef01234567", request, success));
        using JsonDocument firstEvent = JsonDocument.Parse(first);
        using JsonDocument secondEvent = JsonDocument.Parse(second);
        Assert.Equal(1, firstEvent.RootElement.GetProperty("params").GetProperty("eventSequence").GetInt64());
        Assert.Equal(2, secondEvent.RootElement.GetProperty("params").GetProperty("eventSequence").GetInt64());
        Assert.Equal("created", firstEvent.RootElement.GetProperty("params").GetProperty("changeKind").GetString());
        Assert.Equal("2026-07-16T12:00:00+00:00",
            firstEvent.RootElement.GetProperty("params").GetProperty("emittedAtUtc").GetString());
    }

    [Fact]
    public async Task Runtime_enqueues_a_mutation_response_before_its_notification()
    {
        byte[] response = "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}"u8.ToArray();
        byte[] notification = "{\"jsonrpc\":\"2.0\",\"method\":\"thread.changed\",\"params\":{}}"u8.ToArray();
        using DesktopRpcOutputQueue output = new(maxFrames: 4, maxBytes: 4096);
        using DesktopRpcRuntime runtime = new(
            maxInFlight: 8,
            output,
            (_, _) => ValueTask.FromResult(response),
            notificationFactory: (_, _) => notification);

        await runtime.ProcessFrameAsync(Request(1), CancellationToken.None);
        DesktopRpcOutboundFrame first = await output.DequeueAsync(CancellationToken.None);
        DesktopRpcOutboundFrame second = await output.DequeueAsync(CancellationToken.None);
        Assert.Equal(DesktopRpcOutboundKind.Response, first.Kind);
        Assert.Equal(DesktopRpcOutboundKind.Notification, second.Kind);
        output.CompleteWrite(first);
        output.CompleteWrite(second);
    }

    [Fact]
    public async Task Runtime_serializes_notification_creation_with_enqueue_order()
    {
        byte[] response = "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}"u8.ToArray();
        using ManualResetEventSlim firstFactoryEntered = new();
        using ManualResetEventSlim releaseFirstFactory = new();
        using ManualResetEventSlim secondFactoryEntered = new();
        int factoryInvocation = 0;
        using DesktopRpcOutputQueue output = new(maxFrames: 8, maxBytes: 4096);
        using DesktopRpcRuntime runtime = new(
            maxInFlight: 8,
            output,
            (_, _) => ValueTask.FromResult(response),
            notificationFactory: (_, _) =>
            {
                int invocation = Interlocked.Increment(ref factoryInvocation);
                if (invocation == 1)
                {
                    firstFactoryEntered.Set();
                    Assert.True(releaseFirstFactory.Wait(TimeSpan.FromSeconds(5)));
                }
                else
                {
                    secondFactoryEntered.Set();
                }

                return Encoding.UTF8.GetBytes($"{{\"eventSequence\":{invocation}}}");
            });

        Task first = Task.Factory.StartNew(
            () => runtime.ProcessFrameAsync(Request(1), CancellationToken.None).AsTask(),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();
        Assert.True(firstFactoryEntered.Wait(TimeSpan.FromSeconds(5)));
        Task second = Task.Factory.StartNew(
            () => runtime.ProcessFrameAsync(Request(2), CancellationToken.None).AsTask(),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();

        try
        {
            Assert.False(secondFactoryEntered.Wait(TimeSpan.FromMilliseconds(250)));
        }
        finally
        {
            releaseFirstFactory.Set();
        }

        await Task.WhenAll(first, second);
        DesktopRpcOutboundFrame firstResponse = await output.DequeueAsync(CancellationToken.None);
        DesktopRpcOutboundFrame secondResponse = await output.DequeueAsync(CancellationToken.None);
        DesktopRpcOutboundFrame firstNotification = await output.DequeueAsync(CancellationToken.None);
        DesktopRpcOutboundFrame secondNotification = await output.DequeueAsync(CancellationToken.None);
        Assert.Equal(DesktopRpcOutboundKind.Response, firstResponse.Kind);
        Assert.Equal(DesktopRpcOutboundKind.Response, secondResponse.Kind);
        Assert.Equal("{\"eventSequence\":1}", Encoding.UTF8.GetString(firstNotification.Payload));
        Assert.Equal("{\"eventSequence\":2}", Encoding.UTF8.GetString(secondNotification.Payload));
        output.CompleteWrite(firstResponse);
        output.CompleteWrite(secondResponse);
        output.CompleteWrite(firstNotification);
        output.CompleteWrite(secondNotification);
    }

    private static byte[] Request(long id) => Request(
        id,
        DesktopProtocolDefinition.ThreadListMethod,
        new { schemaVersion = 1, pageSize = 50 });

    private static byte[] Request(long id, string method, object parameters) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        jsonrpc = "2.0",
        id,
        method,
        @params = parameters
    });

    private static string ResponseErrorCode(DesktopRpcOutboundFrame frame)
    {
        using JsonDocument response = JsonDocument.Parse(frame.Payload);
        return response.RootElement.GetProperty("error").GetProperty("data")
            .GetProperty("errorCode").GetString()!;
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => timestamp;

        public void Advance(TimeSpan duration) => timestamp += duration.Ticks;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset now;

        public FixedTimeProvider(DateTimeOffset now) => this.now = now;

        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class ControlledDeadlineScheduler : IDesktopRpcDeadlineScheduler
    {
        public ControlledDeadline Last { get; private set; } = new();

        public int CreateCount { get; private set; }

        public IDesktopRpcDeadline Create(TimeSpan timeout)
        {
            CreateCount++;
            Last = new ControlledDeadline();
            return Last;
        }
    }

    private sealed class ControlledDeadline : IDesktopRpcDeadline
    {
        private readonly CancellationTokenSource cancellation = new();

        public CancellationToken Token => cancellation.Token;

        public bool IsExpired { get; private set; }

        public void Expire()
        {
            IsExpired = true;
            cancellation.Cancel();
        }

        public void Dispose() => cancellation.Dispose();
    }
}
