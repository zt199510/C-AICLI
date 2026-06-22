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

    private static AgentRunRequest CreateRequest()
    {
        WorkspaceContext workspace = new(
            RootPath: Path.GetTempPath(),
            ConfigPath: Path.Combine(Path.GetTempPath(), ".caicli", "config.json"),
            Status: WorkspaceStatus.Ready);

        return new AgentRunRequest(
            Prompt: "use a test tool",
            Workspace: workspace);
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
}
