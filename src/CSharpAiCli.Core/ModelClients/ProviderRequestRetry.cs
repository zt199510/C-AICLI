using System.ClientModel;
using System.Net;

namespace CSharpAiCli.Core;

public static class ProviderRequestRetryLimits
{
    public const int MaxAdditionalRetries = 5;
    public const int MaxAttempts = MaxAdditionalRetries + 1;
    public static readonly TimeSpan MaximumDelay = TimeSpan.FromSeconds(4);
}

public static class ProviderAttemptPhase
{
    public const string Connecting = "connecting";
    public const string Thinking = "thinking";
    public const string Streaming = "streaming";
    public const string RetryWait = "retry-wait";
    public const string Failed = "failed";

    public static bool IsKnown(string? value) =>
        value is Connecting or Thinking or Streaming or RetryWait or Failed;
}

public sealed record ProviderFailure(
    string Category,
    string Code,
    string SafeMessage,
    bool Retryable);

public sealed record ProviderAttemptEvent(
    int Attempt,
    int MaxAdditionalRetries,
    string Phase,
    bool HasStreamContent = false,
    string? Content = null,
    ProviderFailure? Failure = null,
    bool RetryExhausted = false);

public interface IProviderAttemptObserver
{
    void OnProviderAttempt(ProviderAttemptEvent attemptEvent);
}

public interface IProviderRetryDelay
{
    void Delay(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class ProviderRetryDelay : IProviderRetryDelay
{
    public void Delay(TimeSpan delay, CancellationToken cancellationToken)
    {
        Task.Delay(delay, cancellationToken).GetAwaiter().GetResult();
    }
}

public sealed class ProviderRequestException : Exception
{
    public ProviderRequestException(
        ProviderFailure failure,
        int attemptCount,
        bool retryExhausted,
        Exception innerException)
        : base(failure.SafeMessage, innerException)
    {
        Failure = failure;
        AttemptCount = attemptCount;
        RetryExhausted = retryExhausted;
    }

    public ProviderFailure Failure { get; }
    public int AttemptCount { get; }
    public bool RetryExhausted { get; }
}

public static class ProviderFailureClassifier
{
    public static ProviderFailure Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is ProviderRequestException provider)
        {
            return provider.Failure;
        }

        if (exception is ClientResultException client)
        {
            return FromStatus(client.Status);
        }

        if (exception is HttpRequestException request)
        {
            return request.StatusCode is HttpStatusCode status
                ? FromStatus((int)status)
                : new ProviderFailure(
                    "transport",
                    "provider-transport-error",
                    "The model connection could not be established.",
                    Retryable: true);
        }

        if (exception is TimeoutException or TaskCanceledException or OperationCanceledException)
        {
            return new ProviderFailure(
                "timeout",
                "provider-timeout",
                "The model request timed out before it completed.",
                Retryable: true);
        }

        if (exception is IOException)
        {
            return new ProviderFailure(
                "stream-interrupted",
                "provider-stream-interrupted",
                "The model response stream ended unexpectedly.",
                Retryable: true);
        }

        if (exception is ArgumentException)
        {
            return new ProviderFailure(
                "configuration",
                "provider-configuration-error",
                "The model request configuration is invalid.",
                Retryable: false);
        }

        if (exception is NotSupportedException)
        {
            return new ProviderFailure(
                "configuration",
                "agent-backend-unavailable",
                "Agent backend is unavailable.",
                Retryable: false);
        }

        return new ProviderFailure(
            "provider",
            "openai-client-error",
            "OpenAI agent model call failed before a response was completed.",
            Retryable: false);
    }

    public static ProviderFailure FromStatus(int statusCode) => statusCode switch
    {
        408 => new("timeout", "openai-http-error", "The model request timed out.", true),
        429 => new("rate-limit", "openai-http-error", "The model service is temporarily rate limited.", true),
        >= 500 and <= 599 => new("server", "openai-http-error", "The model service returned a temporary server error.", true),
        400 => new("invalid-request", "openai-http-error", "The model request was rejected as invalid.", false),
        401 => new("authentication", "openai-http-error", "The model service rejected the configured API key.", false),
        403 => new("authorization", "openai-http-error", "The model service denied this request.", false),
        404 => new("configuration", "openai-http-error", "The configured model or endpoint was not found.", false),
        _ => new("provider", "openai-http-error", "The model service rejected the request.", false)
    };
}

public interface IProviderRetryingToolCallingModel
{
}

public sealed class RetryingToolCallingModel : IToolCallingModel, IProviderRetryingToolCallingModel
{
    private readonly IToolCallingModel inner;
    private readonly IProviderAttemptObserver? observer;
    private readonly IProviderRetryDelay delay;
    private readonly int maxAdditionalRetries;

    public RetryingToolCallingModel(
        IToolCallingModel inner,
        IProviderAttemptObserver? observer = null,
        IProviderRetryDelay? delay = null,
        int maxAdditionalRetries = ProviderRequestRetryLimits.MaxAdditionalRetries)
    {
        ArgumentNullException.ThrowIfNull(inner);
        if (maxAdditionalRetries < 0 || maxAdditionalRetries > ProviderRequestRetryLimits.MaxAdditionalRetries)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAdditionalRetries));
        }

        this.inner = inner;
        this.observer = observer;
        this.delay = delay ?? new ProviderRetryDelay();
        this.maxAdditionalRetries = maxAdditionalRetries;
    }

    public AgentModelTurn Start(
        AgentRunRequest request,
        CancellationToken cancellationToken = default) =>
        Execute(
            (attempt, attemptToken) => inner.Start(request, attemptToken),
            request.Limits?.ModelCallTimeout ?? AgentRunLimits.Default.ModelCallTimeout,
            cancellationToken);

    public AgentModelTurn Continue(
        AgentRunRequest request,
        IReadOnlyList<AgentToolCallResult> toolResults,
        CancellationToken cancellationToken = default) =>
        Execute(
            (attempt, attemptToken) => inner.Continue(request, toolResults, attemptToken),
            request.Limits?.ModelCallTimeout ?? AgentRunLimits.Default.ModelCallTimeout,
            cancellationToken);

    private AgentModelTurn Execute(
        Func<int, CancellationToken, AgentModelTurn> request,
        TimeSpan attemptTimeout,
        CancellationToken cancellationToken)
    {
        int maxAttempts = maxAdditionalRetries + 1;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (inner is IProviderAttemptContextReceiver receiver)
            {
                receiver.BeginProviderAttempt(attempt, maxAdditionalRetries);
            }
            observer?.OnProviderAttempt(new ProviderAttemptEvent(
                attempt,
                maxAdditionalRetries,
                ProviderAttemptPhase.Connecting));

            try
            {
                using CancellationTokenSource attemptCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attemptCancellation.CancelAfter(attemptTimeout);
                return request(attempt, attemptCancellation.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                ProviderFailure failure = ProviderFailureClassifier.Classify(exception);
                bool retryExhausted = failure.Retryable && attempt == maxAttempts;
                if (!failure.Retryable || retryExhausted)
                {
                    observer?.OnProviderAttempt(new ProviderAttemptEvent(
                        attempt,
                        maxAdditionalRetries,
                        ProviderAttemptPhase.Failed,
                        Failure: failure,
                        RetryExhausted: retryExhausted));
                    throw new ProviderRequestException(
                        failure,
                        attempt,
                        retryExhausted,
                        exception);
                }

                observer?.OnProviderAttempt(new ProviderAttemptEvent(
                    attempt,
                    maxAdditionalRetries,
                    ProviderAttemptPhase.RetryWait,
                    Failure: failure));
                delay.Delay(Backoff(attempt), cancellationToken);
            }
        }

        throw new InvalidOperationException("Provider retry loop ended without a result.");
    }

    private static TimeSpan Backoff(int retryNumber)
    {
        double milliseconds = 250 * Math.Pow(2, Math.Max(0, retryNumber - 1));
        return TimeSpan.FromMilliseconds(Math.Min(
            milliseconds,
            ProviderRequestRetryLimits.MaximumDelay.TotalMilliseconds));
    }
}

public interface IProviderAttemptContextReceiver
{
    void BeginProviderAttempt(int attempt, int maxAdditionalRetries);
}
