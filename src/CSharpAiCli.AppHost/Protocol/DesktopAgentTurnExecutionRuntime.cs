using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using CSharpAiCli.Application;
using CSharpAiCli.Core;

namespace CSharpAiCli.AppHost.Protocol;

internal sealed class DesktopAgentTurnExecutionRuntime : ITurnExecutionRuntime
{
    private const int MaxRuntimeEvents =
        TurnExecutionLimits.MaxEventBatchItems *
        AgentRunLimits.DefaultMaxTurns *
        ProviderRequestRetryLimits.MaxAttempts;
    private static readonly Regex RootedPathPattern = new(
        @"(?<![A-Za-z0-9])(?:[A-Za-z]:[\\/][^\r\n""']+|\\\\[^\\\s]+\\[^\r\n""']+)",
        RegexOptions.CultureInvariant);

    private readonly OpenAiAgentRunnerFactory agentFactory;

    public DesktopAgentTurnExecutionRuntime()
        : this(new OpenAiAgentRunnerFactory())
    {
    }

    internal DesktopAgentTurnExecutionRuntime(OpenAiAgentRunnerFactory agentFactory)
    {
        this.agentFactory = agentFactory ?? throw new ArgumentNullException(nameof(agentFactory));
    }

    public async Task<TurnRuntimeResult> ExecuteAsync(
        TurnExecutionInput input,
        ITurnExecutionEventSink eventSink,
        IInteractiveApprovalGateway approvalGateway,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(eventSink);
        ArgumentNullException.ThrowIfNull(approvalGateway);

        using CancellationTokenSource executionCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ToolExecutionBoundary boundary = new(AllowMcpDiscovery: false);
        ToolRegistry registry = BuiltInToolRegistryFactory.Create(
            input.Snapshot,
            new DesktopApprovalPolicy(
                input,
                approvalGateway,
                executionCancellation.Token),
            boundary);
        ToolExecutor executor = new(
            registry,
            input.Snapshot.Configuration.DisabledTools,
            boundary);
        AgentRunRequest request = new(
            Prompt: input.Intent.Prompt,
            Workspace: input.Snapshot.Workspace,
            Instructions: input.Snapshot.Instructions.Instructions,
            Limits: input.Snapshot.Configuration.AgentRunLimits);
        Channel<PendingAgentEvent> channel = Channel.CreateBounded<PendingAgentEvent>(
            new BoundedChannelOptions(TurnExecutionLimits.MaxEventBatchItems)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            });
        ChannelEventObserver observer = new(channel.Writer, executionCancellation.Token);
        ProviderAgentEventObserver providerObserver = new(observer, input.TurnId);
        IAgentRunner runner = agentFactory.Create(
            input.Snapshot,
            registry,
            executor,
            observer,
            providerObserver);
        Task<AgentRunResult> producer = Task.Run(() =>
        {
            try
            {
                return runner.Run(request, cancellationToken: executionCancellation.Token);
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        }, executionCancellation.Token);

        try
        {
            long sequence = 1;
            await foreach (PendingAgentEvent pending in channel.Reader.ReadAllAsync(
                executionCancellation.Token).ConfigureAwait(false))
            {
                try
                {
                    if (sequence > MaxRuntimeEvents)
                    {
                        throw new InvalidOperationException("Desktop runtime event limit was exceeded.");
                    }
                    TurnRuntimeEvent runtimeEvent = Map(
                        pending.Event,
                        sequence++,
                        providerObserver);
                    await eventSink.EmitAsync(
                        runtimeEvent,
                        executionCancellation.Token).ConfigureAwait(false);
                    pending.Committed.TrySetResult();
                }
                catch (Exception exception)
                {
                    pending.Committed.TrySetException(exception);
                    throw;
                }
            }

            AgentRunResult result = await producer.ConfigureAwait(false);
            string finalSummary = Sanitize(
                result.IsSuccess ? result.Text : result.Error?.SafeMessage,
                TurnExecutionLimits.MaxFinalSummaryBytes,
                result.IsSuccess ? "Model execution completed." : "Model execution failed.");
            return new TurnRuntimeResult(
                Status: result.IsSuccess ? "completed" : "failed",
                StopReason: result.StopReason,
                ErrorCode: result.Error?.LocalErrorCode,
                FinalSummary: finalSummary);
        }
        catch
        {
            executionCancellation.Cancel();
            channel.Writer.TryComplete();
            try
            {
                await producer.ConfigureAwait(false);
            }
            catch
            {
            }
            throw;
        }
    }

    private static TurnRuntimeEvent Map(
        AgentRunEvent value,
        long sequence,
        ProviderAgentEventObserver providerObserver)
    {
        string? toolName = ReadPayload(value.Payload, "toolName");
        string kind = value.Type switch
        {
            "plan" => TurnRuntimeEventKind.Plan,
            "tool.call" when toolName == "workspace.run_shell" => TurnRuntimeEventKind.CommandStarted,
            "tool.result" when toolName == "workspace.run_shell" => TurnRuntimeEventKind.CommandCompleted,
            "tool.call" => TurnRuntimeEventKind.ToolStarted,
            "tool.result" => TurnRuntimeEventKind.ToolCompleted,
            "changed.files" => TurnRuntimeEventKind.Changes,
            "verification.result" => TurnRuntimeEventKind.Verification,
            "final.response" => TurnRuntimeEventKind.Final,
            "agent.error" or "retry.exhausted" => TurnRuntimeEventKind.Warning,
            "provider.attempt" => TurnRuntimeEventKind.ProviderProgress,
            "provider.stream" => TurnRuntimeEventKind.Assistant,
            "model.turn" => TurnRuntimeEventKind.Model,
            _ => TurnRuntimeEventKind.Assistant
        };
        int? changedFileCount = TryReadNonNegativeInt(value.Payload, "count");
        string summary = Sanitize(
            value.Summary ?? value.Message,
            TurnExecutionLimits.MaxAssistantPreviewBytes,
            SafeFallback(kind));
        bool? succeeded = value.Status switch
        {
            "success" => true,
            "failure" or "timeout" => false,
            _ => null
        };

        int? providerAttempt = TryReadNonNegativeInt(value.Payload, "attempt");
        if (providerAttempt is null &&
            kind is TurnRuntimeEventKind.Model or TurnRuntimeEventKind.Final)
        {
            providerAttempt = providerObserver.CurrentAttempt;
        }
        string? assistantMessageId = ReadPayload(value.Payload, "assistantMessageId");
        if (assistantMessageId is null &&
            kind is TurnRuntimeEventKind.Model or TurnRuntimeEventKind.Final)
        {
            assistantMessageId = providerObserver.AssistantMessageId;
        }

        return new TurnRuntimeEvent(
            EventSequence: sequence,
            CorrelationId: $"runtime-{sequence}-agent-{value.Sequence}",
            Kind: kind,
            Status: string.IsNullOrWhiteSpace(value.Status) ? "recorded" : value.Status,
            Summary: summary,
            TimestampUtc: value.Timestamp,
            Name: toolName,
            Succeeded: succeeded,
            ErrorCode: value.ErrorCode,
            ChangedFileCount: changedFileCount,
            ProviderAttempt: providerAttempt,
            MaxAdditionalRetries: TryReadNonNegativeInt(value.Payload, "maxAdditionalRetries"),
            ProviderPhase: ReadPayload(value.Payload, "phase"),
            AttemptHasStreamContent: TryReadBoolean(value.Payload, "hasStreamContent"),
            AssistantMessageId: assistantMessageId,
            ErrorCategory: ReadPayload(value.Payload, "errorCategory"),
            Retryable: TryReadBoolean(value.Payload, "retryable"),
            SafeErrorMessage: ReadPayload(value.Payload, "safeErrorMessage"),
            RetryExhausted: TryReadBoolean(value.Payload, "retryExhausted") == true);
    }

    private static string SafeFallback(string kind) => kind switch
    {
        TurnRuntimeEventKind.Plan => "Execution plan updated.",
        TurnRuntimeEventKind.ToolStarted => "Tool execution started.",
        TurnRuntimeEventKind.ToolCompleted => "Tool execution completed.",
        TurnRuntimeEventKind.CommandStarted => "Command execution started.",
        TurnRuntimeEventKind.CommandCompleted => "Command execution completed.",
        TurnRuntimeEventKind.Verification => "Verification completed.",
        TurnRuntimeEventKind.Changes => "Changed-file summary updated.",
        TurnRuntimeEventKind.Final => "Model execution completed.",
        TurnRuntimeEventKind.Warning => "Model execution reported a safe failure.",
        _ => "Model progress updated."
    };

    private static string? ReadPayload(
        IReadOnlyDictionary<string, string>? payload,
        string name) =>
        payload is not null && payload.TryGetValue(name, out string? value) &&
        !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    private static int? TryReadNonNegativeInt(
        IReadOnlyDictionary<string, string>? payload,
        string name)
    {
        string? value = ReadPayload(payload, name);
        return int.TryParse(
            value,
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out int parsed) && parsed >= 0
                ? parsed
                : null;
    }

    private static bool? TryReadBoolean(
        IReadOnlyDictionary<string, string>? payload,
        string name)
    {
        string? value = ReadPayload(payload, name);
        return bool.TryParse(value, out bool parsed) ? parsed : null;
    }

    private static string Sanitize(string? value, int maxBytes, string fallback)
    {
        string safe = DiagnosticSecretRedactor.Redact(value ?? string.Empty);
        safe = RootedPathPattern.Replace(safe, "[local path hidden]");
        if (string.IsNullOrWhiteSpace(safe))
        {
            safe = fallback;
        }
        if (Encoding.UTF8.GetByteCount(safe) <= maxBytes)
        {
            return safe;
        }

        int low = 0;
        int high = safe.Length;
        while (low < high)
        {
            int middle = low + ((high - low + 1) / 2);
            if (Encoding.UTF8.GetByteCount(safe.AsSpan(0, middle)) <= maxBytes)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }
        return safe[..low];
    }

    private sealed class ChannelEventObserver(
        ChannelWriter<PendingAgentEvent> writer,
        CancellationToken cancellationToken) : IAgentRunEventObserver
    {
        private readonly HashSet<string> observedIdentities = new(StringComparer.Ordinal);

        public void OnEvent(AgentRunEvent agentEvent)
        {
            string identity = string.Join(
                '\0',
                agentEvent.Type,
                agentEvent.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
                agentEvent.Timestamp.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (!observedIdentities.Add(identity))
            {
                throw new InvalidOperationException("Agent event identity was observed more than once.");
            }
            TaskCompletionSource committed = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            writer.WriteAsync(new PendingAgentEvent(agentEvent, committed), cancellationToken)
                .AsTask()
                .GetAwaiter()
                .GetResult();
            committed.Task
                .WaitAsync(cancellationToken)
                .GetAwaiter()
                .GetResult();
        }
    }

    private sealed record PendingAgentEvent(
        AgentRunEvent Event,
        TaskCompletionSource Committed);

    private sealed class ProviderAgentEventObserver(
        IAgentRunEventObserver eventObserver,
        string turnId) : IProviderAttemptObserver
    {
        private long sequence;

        public int CurrentAttempt { get; private set; }
        public string AssistantMessageId { get; } =
            "assistant_" + turnId["turn_".Length..];

        public void OnProviderAttempt(ProviderAttemptEvent attemptEvent)
        {
            CurrentAttempt = attemptEvent.Attempt;
            Dictionary<string, string> payload = new(StringComparer.Ordinal)
            {
                ["attempt"] = attemptEvent.Attempt.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["maxAdditionalRetries"] = attemptEvent.MaxAdditionalRetries.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["phase"] = attemptEvent.Phase,
                ["hasStreamContent"] = attemptEvent.HasStreamContent ? "true" : "false",
                ["assistantMessageId"] = AssistantMessageId,
                ["retryExhausted"] = attemptEvent.RetryExhausted ? "true" : "false"
            };
            if (attemptEvent.Failure is { } failure)
            {
                payload["errorCategory"] = failure.Category;
                payload["retryable"] = failure.Retryable ? "true" : "false";
                payload["safeErrorMessage"] = failure.SafeMessage;
            }

            eventObserver.OnEvent(new AgentRunEvent(
                Type: attemptEvent.Phase == ProviderAttemptPhase.Streaming
                    ? "provider.stream"
                    : "provider.attempt",
                Sequence: sequence++,
                Timestamp: DateTimeOffset.UtcNow,
                Summary: attemptEvent.Content ??
                    attemptEvent.Failure?.SafeMessage ??
                    attemptEvent.Phase,
                Payload: payload,
                ErrorCode: attemptEvent.Failure?.Code,
                Status: attemptEvent.Phase));
        }
    }
}
