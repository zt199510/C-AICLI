using System.Text.Json;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class OfflineAgentRunnerTests
{
    [Fact]
    public void Run_executes_fake_model_tool_loop_and_records_transcript_tool_call()
    {
        ToolRegistry registry = new();
        registry.Register(new EchoTool());
        ToolExecutor executor = new(registry);
        FakeToolCallingModel model = new(
            startTurn: AgentModelTurn.RequestTools(new AgentToolCallRequest(
                CallId: "call_echo_1",
                ToolName: "test.echo",
                ArgumentsJson: """{"text":"hello"}""")),
            continueFactory: results =>
            {
                AgentToolCallResult result = Assert.Single(results);
                Assert.True(result.Result.Succeeded);
                return AgentModelTurn.Final("tool said: " + result.Result.Summary);
            });
        DateTimeOffset now = DateTimeOffset.Parse("2024-01-01T00:00:05Z");
        OfflineAgentRunner runner = new(model, executor, () => now);
        ConversationTranscript transcript = ConversationTranscript.Create(
            "smoke",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));

        AgentRunResult result = runner.Run(CreateRequest(), transcript);

        Assert.True(result.IsSuccess);
        Assert.Equal("tool said: hello", result.Text);
        ConversationToolCall toolCall = Assert.Single(result.ToolCalls);
        Assert.Equal("call_echo_1", toolCall.CallId);
        Assert.Equal("test.echo", toolCall.ToolName);
        Assert.Equal("""{"text":"hello"}""", toolCall.ArgumentsJson);
        Assert.Equal("not-required", toolCall.ApprovalStatus);
        Assert.True(toolCall.Succeeded);
        Assert.Equal("hello", toolCall.OutputSummary);
        Assert.Null(toolCall.FailureReason);
        Assert.Null(toolCall.ErrorCode);
        Assert.Same(toolCall, Assert.Single(transcript.ToolCalls));
        Assert.Equal(now, transcript.UpdatedAtUtc);

        Assert.Equal(
            new[] { "model.turn", "tool.call", "tool.result", "model.turn", "final.response" },
            result.Events.Select(agentEvent => agentEvent.Type).ToArray());
        Assert.Equal(new long[] { 0, 1, 2, 3, 4 }, result.Events.Select(agentEvent => agentEvent.Sequence).ToArray());
        Assert.All(result.Events, agentEvent => Assert.Equal(now, agentEvent.Timestamp));
        Assert.Equal("test.echo", result.Events[1].Payload?["toolName"]);
        Assert.Equal("call_echo_1", result.Events[1].Payload?["callId"]);
        Assert.Equal("not-required", result.Events[2].ApprovalStatus);
        Assert.Equal("hello", result.Events[2].Summary);
        Assert.Equal("tool said: hello", result.Events[4].Summary);
    }

    [Fact]
    public void Run_executes_multiple_tools_in_one_model_turn_and_records_transcript_tool_calls()
    {
        ToolRegistry registry = new();
        registry.Register(new EchoTool());
        ToolExecutor executor = new(registry);
        FakeToolCallingModel model = new(
            startTurn: AgentModelTurn.RequestTools(
                new AgentToolCallRequest(
                    CallId: "call_echo_1",
                    ToolName: "test.echo",
                    ArgumentsJson: """{"text":"first"}"""),
                new AgentToolCallRequest(
                    CallId: "call_echo_2",
                    ToolName: "test.echo",
                    ArgumentsJson: """{"text":"second"}""")),
            continueFactory: results =>
            {
                Assert.Equal(2, results.Count);
                Assert.Equal("call_echo_1", results[0].Request.CallId);
                Assert.True(results[0].Result.Succeeded);
                Assert.Equal("first", results[0].Result.Summary);
                Assert.Equal("call_echo_2", results[1].Request.CallId);
                Assert.True(results[1].Result.Succeeded);
                Assert.Equal("second", results[1].Result.Summary);
                return AgentModelTurn.Final("tool said: " + string.Join(", ", results.Select(result => result.Result.Summary)));
            });
        DateTimeOffset now = DateTimeOffset.Parse("2024-01-01T00:00:05Z");
        OfflineAgentRunner runner = new(model, executor, () => now);
        ConversationTranscript transcript = ConversationTranscript.Create(
            "smoke",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));

        AgentRunResult result = runner.Run(CreateRequest(), transcript);

        Assert.True(result.IsSuccess);
        Assert.Equal("tool said: first, second", result.Text);
        Assert.Equal(2, result.ToolCalls.Count);
        Assert.Equal("call_echo_1", result.ToolCalls[0].CallId);
        Assert.Equal("first", result.ToolCalls[0].OutputSummary);
        Assert.Equal("call_echo_2", result.ToolCalls[1].CallId);
        Assert.Equal("second", result.ToolCalls[1].OutputSummary);
        Assert.Equal(2, transcript.ToolCalls.Count);
        Assert.Equal("call_echo_1", transcript.ToolCalls[0].CallId);
        Assert.Equal("first", transcript.ToolCalls[0].OutputSummary);
        Assert.Equal("call_echo_2", transcript.ToolCalls[1].CallId);
        Assert.Equal("second", transcript.ToolCalls[1].OutputSummary);

        Assert.Equal(
            new[]
            {
                "model.turn",
                "tool.call",
                "tool.result",
                "tool.call",
                "tool.result",
                "model.turn",
                "final.response"
            },
            result.Events.Select(agentEvent => agentEvent.Type).ToArray());
        Assert.Equal(new long[] { 0, 1, 2, 3, 4, 5, 6 }, result.Events.Select(agentEvent => agentEvent.Sequence).ToArray());
        Assert.Equal("test.echo", result.Events[1].Payload?["toolName"]);
        Assert.Equal("call_echo_1", result.Events[1].Payload?["callId"]);
        Assert.Equal("first", result.Events[2].Summary);
        Assert.Equal("test.echo", result.Events[3].Payload?["toolName"]);
        Assert.Equal("call_echo_2", result.Events[3].Payload?["callId"]);
        Assert.Equal("second", result.Events[4].Summary);
        Assert.Equal("tool said: first, second", result.Events[6].Summary);
    }

    [Fact]
    public void Run_exposes_structured_tool_payload_to_model_continue()
    {
        ToolRegistry registry = new();
        registry.Register(new StructuredPayloadTool());
        ToolExecutor executor = new(registry);
        FakeToolCallingModel model = new(
            startTurn: AgentModelTurn.RequestTools(new AgentToolCallRequest(
                CallId: "call_structured_1",
                ToolName: "test.structured",
                ArgumentsJson: "{}")),
            continueFactory: results =>
            {
                AgentToolCallResult result = Assert.Single(results);
                Assert.True(result.Result.Succeeded);
                Assert.Equal("Read notes.txt.", result.Result.Summary);
                IReadOnlyDictionary<string, JsonElement> structuredPayload =
                    result.Result.StructuredPayload ?? throw new InvalidOperationException("Structured payload was not set.");
                Assert.Equal("notes.txt", structuredPayload["path"].GetString());
                Assert.Equal(3, structuredPayload["lineCount"].GetInt32());

                return AgentModelTurn.Final("read " + structuredPayload["path"].GetString());
            });
        OfflineAgentRunner runner = new(model, executor, () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"));

        AgentRunResult result = runner.Run(CreateRequest());

        Assert.True(result.IsSuccess);
        Assert.Equal("read notes.txt", result.Text);
        AgentRunEvent toolResultEvent = Assert.Single(result.Events, agentEvent => agentEvent.Type == "tool.result");
        Assert.Equal("Read notes.txt.", toolResultEvent.Summary);
        Assert.Equal("test.structured", toolResultEvent.Payload?["toolName"]);
        Assert.Equal("true", toolResultEvent.Payload?["succeeded"]);
    }

    [Fact]
    public void Run_records_tool_failure_and_continues_model_loop()
    {
        ToolExecutor executor = new(new ToolRegistry());
        FakeToolCallingModel model = new(
            startTurn: AgentModelTurn.RequestTools(new AgentToolCallRequest(
                CallId: "call_missing",
                ToolName: "missing.tool",
                ArgumentsJson: "{}")),
            continueFactory: results =>
            {
                AgentToolCallResult result = Assert.Single(results);
                Assert.False(result.Result.Succeeded);
                Assert.Equal("unknown-tool", result.Result.ErrorCode);
                return AgentModelTurn.Final("handled " + result.Result.ErrorCode);
            });
        DateTimeOffset now = DateTimeOffset.Parse("2024-01-01T00:00:05Z");
        OfflineAgentRunner runner = new(model, executor, () => now);
        ConversationTranscript transcript = ConversationTranscript.Create(
            "smoke",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));

        AgentRunResult result = runner.Run(CreateRequest(), transcript);

        Assert.True(result.IsSuccess);
        Assert.Equal("handled unknown-tool", result.Text);
        ConversationToolCall toolCall = Assert.Single(transcript.ToolCalls);
        Assert.False(toolCall.Succeeded);
        Assert.Equal("Tool 'missing.tool' is not registered.", toolCall.FailureReason);
        Assert.Equal("unknown-tool", toolCall.ErrorCode);
    }

    [Fact]
    public void Run_records_argument_error_without_throwing()
    {
        ToolRegistry registry = new();
        registry.Register(new EchoTool());
        ToolExecutor executor = new(registry);
        FakeToolCallingModel model = new(
            startTurn: AgentModelTurn.RequestTools(new AgentToolCallRequest(
                CallId: "call_bad_args",
                ToolName: "test.echo",
                ArgumentsJson: "[")),
            continueFactory: results =>
            {
                AgentToolCallResult result = Assert.Single(results);
                Assert.False(result.Result.Succeeded);
                Assert.Equal("invalid-tool-arguments", result.Result.ErrorCode);
                return AgentModelTurn.Final("argument failure recorded");
            });
        OfflineAgentRunner runner = new(model, executor, () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"));

        AgentRunResult result = runner.Run(CreateRequest());

        Assert.True(result.IsSuccess);
        ConversationToolCall toolCall = Assert.Single(result.ToolCalls);
        Assert.False(toolCall.Succeeded);
        Assert.Equal("invalid-tool-arguments", toolCall.ErrorCode);
    }

    [Fact]
    public void Run_returns_failure_when_model_never_finishes()
    {
        ToolRegistry registry = new();
        registry.Register(new EchoTool());
        ToolExecutor executor = new(registry);
        FakeToolCallingModel model = new(
            startTurn: AgentModelTurn.RequestTools(new AgentToolCallRequest(
                CallId: "call_1",
                ToolName: "test.echo",
                ArgumentsJson: """{"text":"hello"}""")),
            continueFactory: _ => AgentModelTurn.RequestTools(new AgentToolCallRequest(
                CallId: "call_loop",
                ToolName: "test.echo",
                ArgumentsJson: """{"text":"again"}""")));
        OfflineAgentRunner runner = new(
            model,
            executor,
            () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
            maxIterations: 2);

        AgentRunResult result = runner.Run(CreateRequest());

        Assert.False(result.IsSuccess);
        Assert.Equal("agent-loop-limit-reached", result.Error?.LocalErrorCode);
        Assert.Equal(2, result.ToolCalls.Count);
        AgentRunEvent limitEvent = Assert.Single(result.Events, agentEvent => agentEvent.Type == "agent.error");
        Assert.Equal("agent-loop-limit-reached", limitEvent.ErrorCode);
        Assert.Contains("maximum iteration limit", limitEvent.Message);
    }

    [Fact]
    public void Run_uses_request_max_turns_limit()
    {
        ToolRegistry registry = new();
        registry.Register(new EchoTool());
        ToolExecutor executor = new(registry);
        FakeToolCallingModel model = new(
            startTurn: AgentModelTurn.RequestTools(new AgentToolCallRequest(
                CallId: "call_1",
                ToolName: "test.echo",
                ArgumentsJson: """{"text":"hello"}""")),
            continueFactory: _ => AgentModelTurn.RequestTools(new AgentToolCallRequest(
                CallId: "call_loop",
                ToolName: "test.echo",
                ArgumentsJson: """{"text":"again"}""")));
        OfflineAgentRunner runner = new(
            model,
            executor,
            () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"));

        AgentRunResult result = runner.Run(CreateRequest(
            new AgentRunLimits(MaxTurns: 1)));

        Assert.False(result.IsSuccess);
        Assert.Equal("agent-loop-limit-reached", result.Error?.LocalErrorCode);
        Assert.Single(result.ToolCalls);
    }

    [Fact]
    public void Run_preserves_constructor_max_iterations_when_request_limits_do_not_override_turns()
    {
        ToolRegistry registry = new();
        registry.Register(new EchoTool());
        ToolExecutor executor = new(registry);
        int continueCalls = 0;
        FakeToolCallingModel model = new(
            startTurn: AgentModelTurn.RequestTools(new AgentToolCallRequest(
                CallId: "call_1",
                ToolName: "test.echo",
                ArgumentsJson: """{"text":"hello"}""")),
            continueFactory: _ =>
            {
                continueCalls++;
                return continueCalls == 1
                    ? AgentModelTurn.RequestTools(new AgentToolCallRequest(
                        CallId: "call_2",
                        ToolName: "test.echo",
                        ArgumentsJson: """{"text":"again"}"""))
                    : AgentModelTurn.Final("done");
            });
        OfflineAgentRunner runner = new(
            model,
            executor,
            () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
            maxIterations: 1);

        AgentRunResult result = runner.Run(CreateRequest(
            new AgentRunLimits(MaxToolCalls: 5)));

        Assert.False(result.IsSuccess);
        Assert.Equal("agent-loop-limit-reached", result.Error?.LocalErrorCode);
        Assert.Single(result.ToolCalls);
    }

    [Fact]
    public void Run_succeeds_when_continue_returns_final_on_last_allowed_turn()
    {
        ToolRegistry registry = new();
        registry.Register(new EchoTool());
        ToolExecutor executor = new(registry);
        FakeToolCallingModel model = new(
            startTurn: AgentModelTurn.RequestTools(new AgentToolCallRequest(
                CallId: "call_1",
                ToolName: "test.echo",
                ArgumentsJson: """{"text":"hello"}""")),
            continueFactory: _ => AgentModelTurn.Final("done"));
        OfflineAgentRunner runner = new(
            model,
            executor,
            () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"));

        AgentRunResult result = runner.Run(CreateRequest(
            new AgentRunLimits(MaxTurns: 1)));

        Assert.True(result.IsSuccess);
        Assert.Equal("done", result.Text);
        Assert.Single(result.ToolCalls);
        Assert.Equal("final.response", result.Events[^1].Type);
    }

    [Fact]
    public void Run_returns_failure_when_tool_call_limit_is_reached()
    {
        ToolRegistry registry = new();
        registry.Register(new EchoTool());
        ToolExecutor executor = new(registry);
        FakeToolCallingModel model = new(
            startTurn: AgentModelTurn.RequestTools(
                new AgentToolCallRequest(
                    CallId: "call_1",
                    ToolName: "test.echo",
                    ArgumentsJson: """{"text":"one"}"""),
                new AgentToolCallRequest(
                    CallId: "call_2",
                    ToolName: "test.echo",
                    ArgumentsJson: """{"text":"two"}""")),
            continueFactory: _ => AgentModelTurn.Final("done"));
        OfflineAgentRunner runner = new(
            model,
            executor,
            () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"));

        AgentRunResult result = runner.Run(CreateRequest(
            new AgentRunLimits(MaxToolCalls: 1)));

        Assert.False(result.IsSuccess);
        Assert.Equal("agent-tool-call-limit-reached", result.Error?.LocalErrorCode);
        Assert.Single(result.ToolCalls);
        AgentRunEvent limitEvent = Assert.Single(result.Events, agentEvent => agentEvent.Type == "agent.error");
        Assert.Equal("agent-tool-call-limit-reached", limitEvent.ErrorCode);
    }

    [Fact]
    public void Run_returns_failure_when_overall_timeout_deadline_has_passed()
    {
        ToolRegistry registry = new();
        registry.Register(new EchoTool());
        ToolExecutor executor = new(registry);
        DateTimeOffset start = DateTimeOffset.Parse("2024-01-01T00:00:00Z");
        int clockCalls = 0;
        FakeToolCallingModel model = new(
            startTurn: AgentModelTurn.RequestTools(new AgentToolCallRequest(
                CallId: "call_1",
                ToolName: "test.echo",
                ArgumentsJson: """{"text":"hello"}""")),
            continueFactory: _ => AgentModelTurn.Final("done"));
        OfflineAgentRunner runner = new(
            model,
            executor,
            () => clockCalls++ == 0 ? start : start.AddSeconds(2));

        AgentRunResult result = runner.Run(CreateRequest(
            new AgentRunLimits(OverallTimeout: TimeSpan.FromSeconds(1))));

        Assert.False(result.IsSuccess);
        Assert.Equal("agent-overall-timeout-reached", result.Error?.LocalErrorCode);
        Assert.Empty(result.ToolCalls);
        AgentRunEvent timeoutEvent = Assert.Single(result.Events, agentEvent => agentEvent.Type == "agent.error");
        Assert.Equal("agent-overall-timeout-reached", timeoutEvent.ErrorCode);
    }

    [Fact]
    public void Run_returns_overall_timeout_when_continue_final_crosses_deadline()
    {
        ToolRegistry registry = new();
        registry.Register(new EchoTool());
        ToolExecutor executor = new(registry);
        DateTimeOffset start = DateTimeOffset.Parse("2024-01-01T00:00:00Z");
        bool deadlineCrossed = false;
        FakeToolCallingModel model = new(
            startTurn: AgentModelTurn.RequestTools(new AgentToolCallRequest(
                CallId: "call_1",
                ToolName: "test.echo",
                ArgumentsJson: """{"text":"hello"}""")),
            continueFactory: _ =>
            {
                deadlineCrossed = true;
                return AgentModelTurn.Final("done");
            });
        OfflineAgentRunner runner = new(
            model,
            executor,
            () => deadlineCrossed ? start.AddSeconds(2) : start);

        AgentRunResult result = runner.Run(CreateRequest(
            new AgentRunLimits(OverallTimeout: TimeSpan.FromSeconds(1))));

        Assert.False(result.IsSuccess);
        Assert.Equal("agent-overall-timeout-reached", result.Error?.LocalErrorCode);
        Assert.Single(result.ToolCalls);
        AgentRunEvent timeoutEvent = Assert.Single(result.Events, agentEvent => agentEvent.Type == "agent.error");
        Assert.Equal("agent-overall-timeout-reached", timeoutEvent.ErrorCode);
    }

    [Fact]
    public void Run_returns_failure_when_model_start_observes_model_call_timeout()
    {
        OfflineAgentRunner runner = new(
            new TimeoutObservingModel(timeoutOnStart: true),
            new ToolExecutor(new ToolRegistry()),
            () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"));

        AgentRunResult result = runner.Run(CreateRequest(
            new AgentRunLimits(ModelCallTimeout: TimeSpan.FromMilliseconds(1))));

        Assert.False(result.IsSuccess);
        Assert.Equal("agent-model-call-timeout-reached", result.Error?.LocalErrorCode);
        Assert.Empty(result.ToolCalls);
        AgentRunEvent timeoutEvent = Assert.Single(result.Events, agentEvent => agentEvent.Type == "agent.error");
        Assert.Equal("agent-model-call-timeout-reached", timeoutEvent.ErrorCode);
    }

    [Fact]
    public void Run_returns_overall_timeout_when_model_start_observes_overall_timeout_before_model_call_timeout()
    {
        OfflineAgentRunner runner = new(
            new TimeoutObservingModel(timeoutOnStart: true),
            new ToolExecutor(new ToolRegistry()),
            () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"));

        AgentRunResult result = runner.Run(CreateRequest(
            new AgentRunLimits(
                OverallTimeout: TimeSpan.FromMilliseconds(1),
                ModelCallTimeout: TimeSpan.FromSeconds(30))));

        Assert.False(result.IsSuccess);
        Assert.Equal("agent-overall-timeout-reached", result.Error?.LocalErrorCode);
        Assert.Empty(result.ToolCalls);
        AgentRunEvent timeoutEvent = Assert.Single(result.Events, agentEvent => agentEvent.Type == "agent.error");
        Assert.Equal("agent-overall-timeout-reached", timeoutEvent.ErrorCode);
    }

    [Fact]
    public void Run_returns_failure_when_model_continue_observes_model_call_timeout()
    {
        ToolRegistry registry = new();
        registry.Register(new EchoTool());
        OfflineAgentRunner runner = new(
            new TimeoutObservingModel(timeoutOnStart: false),
            new ToolExecutor(registry),
            () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"));

        AgentRunResult result = runner.Run(CreateRequest(
            new AgentRunLimits(ModelCallTimeout: TimeSpan.FromMilliseconds(1))));

        Assert.False(result.IsSuccess);
        Assert.Equal("agent-model-call-timeout-reached", result.Error?.LocalErrorCode);
        Assert.Single(result.ToolCalls);
        AgentRunEvent timeoutEvent = Assert.Single(result.Events, agentEvent => agentEvent.Type == "agent.error");
        Assert.Equal("agent-model-call-timeout-reached", timeoutEvent.ErrorCode);
    }

    [Fact]
    public void Run_returns_overall_timeout_when_model_continue_observes_overall_timeout_before_model_call_timeout()
    {
        ToolRegistry registry = new();
        DateTimeOffset start = DateTimeOffset.Parse("2024-01-01T00:00:00Z");
        DateTimeOffset now = start;
        bool advanceClockAfterTool = false;
        registry.Register(new ClockAdvancingTool(() => advanceClockAfterTool = true));
        OfflineAgentRunner runner = new(
            new TimeoutObservingModel(
                timeoutOnStart: false,
                toolName: "test.advance-clock",
                argumentsJson: "{}"),
            new ToolExecutor(registry),
            () =>
            {
                if (advanceClockAfterTool)
                {
                    now = start.AddMilliseconds(900);
                }

                return now;
            });

        AgentRunResult result = runner.Run(CreateRequest(
            new AgentRunLimits(
                OverallTimeout: TimeSpan.FromSeconds(1),
                ModelCallTimeout: TimeSpan.FromSeconds(30))));

        Assert.False(result.IsSuccess);
        Assert.Equal("agent-overall-timeout-reached", result.Error?.LocalErrorCode);
        Assert.Single(result.ToolCalls);
        AgentRunEvent timeoutEvent = Assert.Single(result.Events, agentEvent => agentEvent.Type == "agent.error");
        Assert.Equal("agent-overall-timeout-reached", timeoutEvent.ErrorCode);
    }

    [Fact]
    public void Run_returns_model_call_timeout_when_model_timeout_was_earlier_but_overall_is_expired_by_classification()
    {
        ToolRegistry registry = new();
        registry.Register(new EchoTool());
        DateTimeOffset now = DateTimeOffset.Parse("2024-01-01T00:00:00Z");
        OfflineAgentRunner runner = new(
            new DelayedCancellationThrowModel(() => now = now.AddSeconds(5)),
            new ToolExecutor(registry),
            () => now);

        AgentRunResult result = runner.Run(CreateRequest(
            new AgentRunLimits(
                OverallTimeout: TimeSpan.FromSeconds(2),
                ModelCallTimeout: TimeSpan.FromMilliseconds(1))));

        Assert.False(result.IsSuccess);
        Assert.Equal("agent-model-call-timeout-reached", result.Error?.LocalErrorCode);
        Assert.Single(result.ToolCalls);
        AgentRunEvent timeoutEvent = Assert.Single(result.Events, agentEvent => agentEvent.Type == "agent.error");
        Assert.Equal("agent-model-call-timeout-reached", timeoutEvent.ErrorCode);
    }

    [Fact]
    public void Run_returns_overall_timeout_when_tool_execution_observes_overall_timeout()
    {
        ToolRegistry registry = new();
        registry.Register(new TimeoutObservingTool());
        ToolExecutor executor = new(registry);
        FakeToolCallingModel model = new(
            startTurn: AgentModelTurn.RequestTools(new AgentToolCallRequest(
                CallId: "call_wait_1",
                ToolName: "test.wait-for-cancellation",
                ArgumentsJson: "{}")),
            continueFactory: _ => AgentModelTurn.Final("unreachable"));
        OfflineAgentRunner runner = new(
            model,
            executor,
            () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"));

        AgentRunResult result = runner.Run(CreateRequest(
            new AgentRunLimits(
                OverallTimeout: TimeSpan.FromMilliseconds(1),
                ModelCallTimeout: TimeSpan.FromSeconds(30))));

        Assert.False(result.IsSuccess);
        Assert.Equal("agent-overall-timeout-reached", result.Error?.LocalErrorCode);
        Assert.Empty(result.ToolCalls);
        Assert.Equal(
            new[] { "model.turn", "tool.call", "agent.error" },
            result.Events.Select(agentEvent => agentEvent.Type).ToArray());
        Assert.Equal("agent-overall-timeout-reached", result.Events[^1].ErrorCode);
    }

    [Fact]
    public void Run_propagates_caller_cancellation_during_model_call()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        OfflineAgentRunner runner = new(
            new TimeoutObservingModel(timeoutOnStart: true),
            new ToolExecutor(new ToolRegistry()),
            () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"));

        Assert.Throws<OperationCanceledException>(() =>
            runner.Run(
                CreateRequest(new AgentRunLimits(ModelCallTimeout: TimeSpan.FromSeconds(30))),
                cancellationToken: cancellation.Token));
    }

    [Fact]
    public void Tool_call_schema_serializes_to_transcript_json()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2024-01-01T00:00:05Z");
        ConversationTranscript transcript = ConversationTranscript.Create(
            "smoke",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        transcript.AddToolCall(ConversationToolCall.FromExecution(
            "call_echo_1",
            "test.echo",
            """{"text":"hello"}""",
            ToolExecutionResult.Success("hello"),
            now,
            "not-required"));

        string json = JsonSerializer.Serialize(transcript, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement toolCall = document.RootElement.GetProperty("toolCalls")[0];

        Assert.Equal("call_echo_1", toolCall.GetProperty("callId").GetString());
        Assert.Equal("test.echo", toolCall.GetProperty("toolName").GetString());
        Assert.Equal("""{"text":"hello"}""", toolCall.GetProperty("argumentsJson").GetString());
        Assert.Equal("not-required", toolCall.GetProperty("approvalStatus").GetString());
        Assert.True(toolCall.GetProperty("succeeded").GetBoolean());
        Assert.Equal("hello", toolCall.GetProperty("outputSummary").GetString());
    }

    private static AgentRunRequest CreateRequest(AgentRunLimits? limits = null)
    {
        WorkspaceContext workspace = new(
            RootPath: Path.GetTempPath(),
            ConfigPath: Path.Combine(Path.GetTempPath(), ".caicli", "config.json"),
            Status: WorkspaceStatus.Ready);

        return new AgentRunRequest(
            Prompt: "use a test tool",
            Workspace: workspace,
            Limits: limits);
    }

    private sealed class EchoTool : ITool
    {
        public ToolDefinition Definition { get; } = new(
            "test.echo",
            "Echoes text.",
            """{"type":"object","properties":{"text":{"type":"string"}}}""");

        public ToolExecutionResult Execute(
            ToolExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            using JsonDocument document = JsonDocument.Parse(context.ArgumentsJson);
            string text = document.RootElement.GetProperty("text").GetString() ?? string.Empty;
            return ToolExecutionResult.Success(text);
        }
    }

    private sealed class StructuredPayloadTool : ITool
    {
        public ToolDefinition Definition { get; } = new(
            "test.structured",
            "Returns a structured payload.",
            """{"type":"object"}""");

        public ToolExecutionResult Execute(
            ToolExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            using JsonDocument document = JsonDocument.Parse("""
            {
              "path": "notes.txt",
              "lineCount": 3
            }
            """);
            Dictionary<string, JsonElement> payload = document.RootElement
                .EnumerateObject()
                .ToDictionary(property => property.Name, property => property.Value);

            return ToolExecutionResult.Success(
                "Read notes.txt.",
                structuredPayload: payload);
        }
    }

    private sealed class TimeoutObservingTool : ITool
    {
        public ToolDefinition Definition { get; } = new(
            "test.wait-for-cancellation",
            "Waits until the runner cancels the tool execution token.",
            """{"type":"object"}""");

        public ToolExecutionResult Execute(
            ToolExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(1));
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Expected overall timeout cancellation.");
        }
    }

    private sealed class ClockAdvancingTool(Action advanceClock) : ITool
    {
        public ToolDefinition Definition { get; } = new(
            "test.advance-clock",
            "Advances the test clock.",
            """{"type":"object"}""");

        public ToolExecutionResult Execute(
            ToolExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            advanceClock();
            return ToolExecutionResult.Success("advanced");
        }
    }

    private sealed class FakeToolCallingModel(
        AgentModelTurn startTurn,
        Func<IReadOnlyList<AgentToolCallResult>, AgentModelTurn> continueFactory) : IToolCallingModel
    {
        public AgentModelTurn Start(
            AgentRunRequest request,
            CancellationToken cancellationToken = default)
        {
            return startTurn;
        }

        public AgentModelTurn Continue(
            AgentRunRequest request,
            IReadOnlyList<AgentToolCallResult> toolResults,
            CancellationToken cancellationToken = default)
        {
            return continueFactory(toolResults);
        }
    }

    private sealed class TimeoutObservingModel(
        bool timeoutOnStart,
        string toolName = "test.echo",
        string argumentsJson = """{"text":"hello"}""") : IToolCallingModel
    {
        public AgentModelTurn Start(
            AgentRunRequest request,
            CancellationToken cancellationToken = default)
        {
            if (timeoutOnStart)
            {
                WaitForCancellation(cancellationToken);
            }

            return AgentModelTurn.RequestTools(new AgentToolCallRequest(
                CallId: "call_1",
                ToolName: toolName,
                ArgumentsJson: argumentsJson));
        }

        public AgentModelTurn Continue(
            AgentRunRequest request,
            IReadOnlyList<AgentToolCallResult> toolResults,
            CancellationToken cancellationToken = default)
        {
            WaitForCancellation(cancellationToken);
            return AgentModelTurn.Final("unreachable");
        }

        private static void WaitForCancellation(CancellationToken cancellationToken)
        {
            cancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(1));
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Expected model call timeout cancellation.");
        }
    }

    private sealed class DelayedCancellationThrowModel(Action afterCancellationObserved) : IToolCallingModel
    {
        public AgentModelTurn Start(
            AgentRunRequest request,
            CancellationToken cancellationToken = default)
        {
            return AgentModelTurn.RequestTools(new AgentToolCallRequest(
                CallId: "call_1",
                ToolName: "test.echo",
                ArgumentsJson: """{"text":"hello"}"""));
        }

        public AgentModelTurn Continue(
            AgentRunRequest request,
            IReadOnlyList<AgentToolCallResult> toolResults,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(1));
            afterCancellationObserved();
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Expected model call timeout cancellation.");
        }
    }
}
