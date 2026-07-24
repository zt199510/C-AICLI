using System.ClientModel;
using System.ClientModel.Primitives;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class OpenAiAgentRunnerTests
{
    [Fact]
    public void Run_returns_structured_unavailable_error_when_sdk_agent_gateway_is_not_enabled()
    {
        ToolRegistry registry = new();
        ToolExecutor executor = new(registry);
        OpenAiAgentRunner runner = new(
            "gpt-test",
            instructions: null,
            registry,
            new NotSupportedAgentGateway(),
            executor);

        AgentRunResult result = runner.Run(CreateRequest());

        Assert.False(result.IsSuccess);
        Assert.Equal("agent-backend-unavailable", result.Error?.LocalErrorCode);
        AgentRunEvent errorEvent = Assert.Single(result.Events);
        Assert.Equal("agent.error", errorEvent.Type);
        Assert.Equal("agent-backend-unavailable", errorEvent.ErrorCode);
        Assert.Equal(DiagnosticEventStatus.Failure, errorEvent.Status);
    }

    [Fact]
    public void Run_uses_openai_tool_calling_contract_when_gateway_returns_tool_calls()
    {
        ToolRegistry registry = new();
        registry.Register(new EchoTool());
        ToolExecutor executor = new(registry);
        FakeAgentGateway gateway = new()
        {
            Responses =
            [
                new OpenAiResponseEnvelope(
                    ResponseId: "resp_tool",
                    Model: "gpt-test",
                    Text: "",
                    ToolCalls:
                    [
                        new OpenAiToolCall("call_echo", "test.echo", """{"text":"hello"}""")
                    ]),
                new OpenAiResponseEnvelope(
                    ResponseId: "resp_final",
                    Model: "gpt-test",
                    Text: "finished")
            ]
        };
        OpenAiAgentRunner runner = new(
            "gpt-test",
            "Use tools.",
            registry,
            gateway,
            executor);

        AgentRunResult result = runner.Run(CreateRequest());

        Assert.True(result.IsSuccess);
        Assert.Equal("finished", result.Text);
        Assert.Collection(
            result.Events,
            agentEvent => Assert.Equal("model.turn", agentEvent.Type),
            agentEvent => Assert.Equal("tool.call", agentEvent.Type),
            agentEvent => Assert.Equal("tool.result", agentEvent.Type),
            agentEvent => Assert.Equal("model.turn", agentEvent.Type),
            agentEvent => Assert.Equal("final.response", agentEvent.Type));

        OpenAiAgentRequest firstRequest = gateway.AgentRequests[0];
        OpenAiAgentRequest secondRequest = gateway.AgentRequests[1];
        Assert.Equal("gpt-test", firstRequest.Model);
        Assert.Equal("Use tools.", firstRequest.Instructions);
        Assert.Equal("test.echo", Assert.Single(firstRequest.Tools).Name);
        Assert.Equal(firstRequest.Prompt, secondRequest.Prompt);
        Assert.Null(secondRequest.PreviousResponseId);
        OpenAiToolResultInput toolResult = Assert.Single(secondRequest.ToolResults);
        Assert.Equal("call_echo", toolResult.CallId);
        Assert.Equal("""{"text":"hello"}""", toolResult.ArgumentsJson);
        Assert.Equal("echo: hello", toolResult.Summary);
    }

    [Theory]
    [InlineData(401, false, "API key")]
    [InlineData(429, true, "rate limit")]
    public void Run_maps_sdk_http_errors_to_safe_agent_failure(
        int status,
        bool expectedRetryable,
        string expectedSafeMessageFragment)
    {
        ToolRegistry registry = new();
        ToolExecutor executor = new(registry);
        OpenAiAgentRunner runner = new(
            "gpt-test",
            instructions: null,
            registry,
            new ThrowingAgentGateway(CreateClientResultException(status, "raw http detail sk-secret")),
            executor);

        AgentRunResult result = runner.Run(CreateRequest());

        Assert.False(result.IsSuccess);
        Assert.Equal("openai-http-error", result.Error?.LocalErrorCode);
        Assert.Equal(expectedRetryable, result.Error?.Retryable);
        Assert.Contains(expectedSafeMessageFragment, result.Error?.SafeMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("raw http detail", result.Error?.SafeMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-secret", result.Error?.SafeMessage, StringComparison.Ordinal);
        AgentRunEvent errorEvent = Assert.Single(result.Events);
        Assert.Equal("agent.error", errorEvent.Type);
        Assert.Equal("openai-http-error", errorEvent.ErrorCode);
        Assert.Equal(status.ToString(System.Globalization.CultureInfo.InvariantCulture), errorEvent.Payload?["statusCode"]);
    }

    [Fact]
    public void Run_maps_gateway_exceptions_to_safe_agent_failure()
    {
        ToolRegistry registry = new();
        ToolExecutor executor = new(registry);
        OpenAiAgentRunner runner = new(
            "gpt-test",
            instructions: null,
            registry,
            new ThrowingAgentGateway(new InvalidOperationException("raw sdk detail sk-secret")),
            executor);

        AgentRunResult result = runner.Run(CreateRequest());

        Assert.False(result.IsSuccess);
        Assert.Equal("openai-client-error", result.Error?.LocalErrorCode);
        Assert.True(result.Error?.Retryable);
        Assert.Equal("OpenAI agent model call failed before a response was completed.", result.Error?.SafeMessage);
        Assert.DoesNotContain("raw sdk detail", result.Events[0].Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-secret", result.Events[0].Message, StringComparison.Ordinal);
    }

    private static ClientResultException CreateClientResultException(int status, string rawMessage)
    {
        return new ClientResultException(
            rawMessage,
            new FakePipelineResponse(status),
            innerException: null);
    }

    private static AgentRunRequest CreateRequest()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "caicli-openai-agent-runner-test"));
        WorkspaceContext workspace = new(
            root,
            Path.Combine(root, ".caicli", "config.json"),
            WorkspaceStatus.Ready);

        return new AgentRunRequest("inspect workspace", workspace);
    }

    private sealed class NotSupportedAgentGateway : IOpenAiResponsesGateway
    {
        public OpenAiResponseEnvelope CreateAgentResponse(
            OpenAiAgentRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException(
                "OpenAI agent tool continuation is not implemented for the SDK gateway yet.");
        }

        public OpenAiResponseEnvelope CreateResponse(
            string model,
            string prompt,
            string? instructions = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public IEnumerable<OpenAiStreamingResponseUpdate> CreateResponseStreaming(
            string model,
            string prompt,
            string? instructions = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeAgentGateway : IOpenAiResponsesGateway
    {
        private int responseIndex;

        public List<OpenAiAgentRequest> AgentRequests { get; } = [];

        public IReadOnlyList<OpenAiResponseEnvelope> Responses { get; init; } = [];

        public OpenAiResponseEnvelope CreateAgentResponse(
            OpenAiAgentRequest request,
            CancellationToken cancellationToken = default)
        {
            AgentRequests.Add(request);
            return Responses[responseIndex++];
        }

        public OpenAiResponseEnvelope CreateResponse(
            string model,
            string prompt,
            string? instructions = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public IEnumerable<OpenAiStreamingResponseUpdate> CreateResponseStreaming(
            string model,
            string prompt,
            string? instructions = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class ThrowingAgentGateway(Exception exception) : IOpenAiResponsesGateway
    {
        public OpenAiResponseEnvelope CreateAgentResponse(
            OpenAiAgentRequest request,
            CancellationToken cancellationToken = default)
        {
            throw exception;
        }

        public OpenAiResponseEnvelope CreateResponse(
            string model,
            string prompt,
            string? instructions = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public IEnumerable<OpenAiStreamingResponseUpdate> CreateResponseStreaming(
            string model,
            string prompt,
            string? instructions = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakePipelineResponse : PipelineResponse
    {
        private static readonly PipelineResponseHeaders EmptyHeaders = new FakePipelineResponseHeaders();

        public FakePipelineResponse(int status)
        {
            Status = status;
        }

        public override int Status { get; }
        public override string ReasonPhrase => string.Empty;
        protected override PipelineResponseHeaders HeadersCore => EmptyHeaders;
        public override Stream? ContentStream { get; set; }
        public override BinaryData Content => BinaryData.FromString(string.Empty);
        protected override bool IsErrorCore { get; set; }

        public override BinaryData BufferContent(CancellationToken cancellationToken = default)
        {
            return Content;
        }

        public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Content);
        }

        public override void Dispose()
        {
        }
    }

    private sealed class FakePipelineResponseHeaders : PipelineResponseHeaders
    {
        public override IEnumerator<KeyValuePair<string, string>> GetEnumerator()
        {
            return Enumerable.Empty<KeyValuePair<string, string>>().GetEnumerator();
        }

        public override bool TryGetValue(string name, out string? value)
        {
            value = null;
            return false;
        }

        public override bool TryGetValues(string name, out IEnumerable<string>? values)
        {
            values = null;
            return false;
        }
    }

    private sealed class EchoTool : ITool
    {
        public ToolDefinition Definition { get; } = new(
            "test.echo",
            "Echo text.",
            """
            {
              "type": "object",
              "properties": {
                "text": { "type": "string" }
              },
              "required": [ "text" ]
            }
            """);

        public ToolExecutionResult Execute(
            ToolExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            using System.Text.Json.JsonDocument document =
                System.Text.Json.JsonDocument.Parse(context.ArgumentsJson);
            string text = document.RootElement.GetProperty("text").GetString() ?? string.Empty;
            return ToolExecutionResult.Success($"echo: {text}");
        }
    }
}
