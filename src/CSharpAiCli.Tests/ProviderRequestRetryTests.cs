using System.Net;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ProviderRequestRetryTests
{
    [Fact]
    public void Initial_success_calls_provider_once()
    {
        ScriptedModel provider = new([AgentModelTurn.Final("done")]);
        RecordingObserver observer = new();
        RetryingToolCallingModel model = new(provider, observer, new RecordingDelay());

        AgentModelTurn result = model.Start(Request());

        Assert.Equal("done", result.FinalText);
        Assert.Equal(1, provider.StartCalls);
        Assert.Single(observer.Events, item => item.Phase == ProviderAttemptPhase.Connecting);
    }

    [Fact]
    public void First_retry_success_calls_provider_twice()
    {
        ScriptedModel provider = new([
            new HttpRequestException("offline"),
            AgentModelTurn.Final("done")
        ]);
        RecordingDelay delay = new();
        RetryingToolCallingModel model = new(provider, delay: delay);

        AgentModelTurn result = model.Start(Request());

        Assert.Equal("done", result.FinalText);
        Assert.Equal(2, provider.StartCalls);
        Assert.Single(delay.Delays);
    }

    [Fact]
    public void Fifth_retry_success_calls_provider_six_times()
    {
        ScriptedModel provider = new([
            new TimeoutException(),
            new HttpRequestException("offline"),
            new IOException("stream"),
            new HttpRequestException("busy", null, HttpStatusCode.TooManyRequests),
            new HttpRequestException("server", null, HttpStatusCode.ServiceUnavailable),
            AgentModelTurn.Final("done")
        ]);
        RetryingToolCallingModel model = new(provider, delay: new RecordingDelay());

        Assert.Equal("done", model.Start(Request()).FinalText);
        Assert.Equal(6, provider.StartCalls);
    }

    [Fact]
    public void Retry_exhaustion_stops_after_six_calls()
    {
        ScriptedModel provider = new(Enumerable.Range(0, 6)
            .Select(_ => (object)new HttpRequestException("offline"))
            .ToArray());
        RecordingObserver observer = new();
        RetryingToolCallingModel model = new(provider, observer, new RecordingDelay());

        ProviderRequestException error = Assert.Throws<ProviderRequestException>(
            () => model.Start(Request()));

        Assert.Equal(6, provider.StartCalls);
        Assert.Equal(6, error.AttemptCount);
        Assert.True(error.RetryExhausted);
        Assert.True(observer.Events[^1].RetryExhausted);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public void Non_retryable_http_status_calls_provider_once(HttpStatusCode status)
    {
        ScriptedModel provider = new([
            new HttpRequestException("rejected", null, status)
        ]);
        RetryingToolCallingModel model = new(provider, delay: new RecordingDelay());

        ProviderRequestException error = Assert.Throws<ProviderRequestException>(
            () => model.Start(Request()));

        Assert.False(error.Failure.Retryable);
        Assert.Equal(1, provider.StartCalls);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public void Retryable_http_status_uses_full_retry_budget(HttpStatusCode status)
    {
        ScriptedModel provider = new(Enumerable.Range(0, 6)
            .Select(_ => (object)new HttpRequestException("temporary", null, status))
            .ToArray());
        RetryingToolCallingModel model = new(provider, delay: new RecordingDelay());

        Assert.Throws<ProviderRequestException>(() => model.Start(Request()));
        Assert.Equal(6, provider.StartCalls);
    }

    [Fact]
    public void Cancel_during_request_does_not_retry()
    {
        using CancellationTokenSource cancellation = new();
        ScriptedModel provider = new([
            new Func<CancellationToken, AgentModelTurn>(token =>
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
                return AgentModelTurn.Final("unreachable");
            })
        ]);
        RetryingToolCallingModel model = new(provider, delay: new RecordingDelay());

        Assert.Throws<OperationCanceledException>(
            () => model.Start(Request(), cancellation.Token));
        Assert.Equal(1, provider.StartCalls);
    }

    [Fact]
    public void Cancel_during_backoff_prevents_next_provider_call()
    {
        using CancellationTokenSource cancellation = new();
        ScriptedModel provider = new([new HttpRequestException("offline")]);
        RecordingDelay delay = new(() => cancellation.Cancel());
        RetryingToolCallingModel model = new(provider, delay: delay);

        Assert.Throws<OperationCanceledException>(
            () => model.Start(Request(), cancellation.Token));
        Assert.Equal(1, provider.StartCalls);
    }

    [Fact]
    public void Continue_retry_does_not_replay_completed_tool_side_effect()
    {
        int completedToolExecutions = 1;
        ScriptedModel provider = new(
            start: [AgentModelTurn.RequestTools(new AgentToolCallRequest("call-1", "tool", "{}"))],
            continuation: [
                new IOException("stream interrupted"),
                AgentModelTurn.Final("done")
            ]);
        RetryingToolCallingModel model = new(provider, delay: new RecordingDelay());

        AgentModelTurn first = model.Start(Request());
        AgentToolCallResult toolResult = new(
            Assert.Single(first.ToolCalls),
            ToolExecutionResult.Success("applied"));
        AgentModelTurn final = model.Continue(Request(), [toolResult]);

        Assert.Equal("done", final.FinalText);
        Assert.Equal(1, completedToolExecutions);
        Assert.Equal(1, provider.StartCalls);
        Assert.Equal(2, provider.ContinueCalls);
    }

    private static AgentRunRequest Request() => new(
        "hello",
        new WorkspaceContext("workspace", "workspace/.caicli/config.json", WorkspaceStatus.Ready));

    private sealed class RecordingObserver : IProviderAttemptObserver
    {
        public List<ProviderAttemptEvent> Events { get; } = [];
        public void OnProviderAttempt(ProviderAttemptEvent attemptEvent) => Events.Add(attemptEvent);
    }

    private sealed class RecordingDelay(Action? beforeThrow = null) : IProviderRetryDelay
    {
        public List<TimeSpan> Delays { get; } = [];
        public void Delay(TimeSpan delay, CancellationToken cancellationToken)
        {
            Delays.Add(delay);
            beforeThrow?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private sealed class ScriptedModel : IToolCallingModel
    {
        private readonly Queue<object> start;
        private readonly Queue<object> continuation;

        public ScriptedModel(IReadOnlyList<object> start)
            : this(start, [])
        {
        }

        public ScriptedModel(
            IReadOnlyList<object> start,
            IReadOnlyList<object> continuation)
        {
            this.start = new Queue<object>(start);
            this.continuation = new Queue<object>(continuation);
        }

        public int StartCalls { get; private set; }
        public int ContinueCalls { get; private set; }

        public AgentModelTurn Start(
            AgentRunRequest request,
            CancellationToken cancellationToken = default)
        {
            StartCalls++;
            return Next(start, cancellationToken);
        }

        public AgentModelTurn Continue(
            AgentRunRequest request,
            IReadOnlyList<AgentToolCallResult> toolResults,
            CancellationToken cancellationToken = default)
        {
            ContinueCalls++;
            return Next(continuation, cancellationToken);
        }

        private static AgentModelTurn Next(
            Queue<object> script,
            CancellationToken cancellationToken)
        {
            object value = script.Dequeue();
            if (value is Exception exception)
            {
                throw exception;
            }
            if (value is Func<CancellationToken, AgentModelTurn> action)
            {
                return action(cancellationToken);
            }
            return (AgentModelTurn)value;
        }
    }
}
