using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class OpenAiToolCallingModelTests
{
    [Fact]
    public void Start_sends_prompt_instructions_and_mapped_tool_definitions()
    {
        ToolRegistry registry = new();
        registry.Register(new StubTool(
            "workspace.read_text",
            "Read a text file.",
            """
            {
              "type": "object",
              "properties": {
                "path": {
                  "type": "string"
                }
              }
            }
            """));
        FakeGateway gateway = new()
        {
            Responses =
            [
                new OpenAiResponseEnvelope(
                    ResponseId: "resp_tool",
                    Model: "gpt-test",
                    Text: "",
                    ToolCalls:
                    [
                        new OpenAiToolCall(
                            CallId: "call_1",
                            Name: "workspace.read_text",
                            ArgumentsJson: """{"path":"README.md"}""")
                    ])
            ]
        };
        OpenAiToolCallingModel model = new(
            model: "gpt-test",
            instructions: "Be concise.",
            registry,
            gateway);

        AgentModelTurn turn = model.Start(CreateRequest("inspect README"));

        OpenAiAgentRequest sentRequest = Assert.Single(gateway.AgentRequests);
        Assert.Equal("gpt-test", sentRequest.Model);
        Assert.Equal("inspect README", sentRequest.Prompt);
        Assert.Equal("Be concise.", sentRequest.Instructions);
        Assert.Empty(sentRequest.ToolResults);
        OpenAiToolDefinition definition = Assert.Single(sentRequest.Tools);
        Assert.Equal("function", definition.Type);
        Assert.Equal("workspace.read_text", definition.Name);
        Assert.Equal("""{"type":"object","properties":{"path":{"type":"string"}}}""", definition.ParametersSchema);

        AgentToolCallRequest toolCall = Assert.Single(turn.ToolCalls);
        Assert.Equal("call_1", toolCall.CallId);
        Assert.Equal("workspace.read_text", toolCall.ToolName);
        Assert.Equal("""{"path":"README.md"}""", toolCall.ArgumentsJson);
    }

    [Fact]
    public void Continue_sends_safe_tool_result_outputs_and_returns_parsed_final_turn()
    {
        ToolRegistry registry = new();
        registry.Register(new StubTool("workspace.git_status", "Show git status.", """{"type":"object"}"""));
        FakeGateway gateway = new()
        {
            Responses =
            [
                new OpenAiResponseEnvelope(
                    ResponseId: "resp_final",
                    Model: "gpt-test",
                    Text: "Working tree is clean.")
            ]
        };
        OpenAiToolCallingModel model = new(
            model: "gpt-test",
            instructions: "Summarize tool output.",
            registry,
            gateway);

        AgentModelTurn turn = model.Continue(
            CreateRequest("check status"),
            [
                new AgentToolCallResult(
                    new AgentToolCallRequest(
                        CallId: "call_status",
                        ToolName: "workspace.git_status",
                        ArgumentsJson: "{}"),
                    ToolExecutionResult.Success("On branch main. nothing to commit."))
            ]);

        OpenAiAgentRequest sentRequest = Assert.Single(gateway.AgentRequests);
        Assert.Null(sentRequest.Prompt);
        Assert.Equal("Summarize tool output.", sentRequest.Instructions);
        Assert.Empty(sentRequest.Tools);
        OpenAiToolResultInput resultInput = Assert.Single(sentRequest.ToolResults);
        Assert.Equal("call_status", resultInput.CallId);
        Assert.Equal("workspace.git_status", resultInput.ToolName);
        Assert.True(resultInput.Succeeded);
        Assert.Equal("On branch main. nothing to commit.", resultInput.Summary);
        Assert.Null(resultInput.ErrorCode);
        Assert.Equal("not-required", resultInput.ApprovalStatus);
        Assert.True(turn.IsFinal);
        Assert.Equal("Working tree is clean.", turn.FinalText);
    }

    [Fact]
    public void Continue_sends_previous_response_id_after_start_response()
    {
        ToolRegistry registry = new();
        registry.Register(new StubTool("workspace.git_status", "Show git status.", """{"type":"object"}"""));
        FakeGateway gateway = new()
        {
            Responses =
            [
                new OpenAiResponseEnvelope(
                    ResponseId: "resp_start",
                    Model: "gpt-test",
                    Text: "",
                    ToolCalls:
                    [
                        new OpenAiToolCall("call_status", "workspace.git_status", "{}")
                    ]),
                new OpenAiResponseEnvelope(
                    ResponseId: "resp_final",
                    Model: "gpt-test",
                    Text: "done")
            ]
        };
        OpenAiToolCallingModel model = new(
            model: "gpt-test",
            instructions: null,
            registry,
            gateway);

        AgentModelTurn firstTurn = model.Start(CreateRequest("check status"));
        model.Continue(
            CreateRequest("check status"),
            [
                new AgentToolCallResult(
                    firstTurn.ToolCalls.Single(),
                    ToolExecutionResult.Success("clean"))
            ]);

        Assert.Null(gateway.AgentRequests[0].PreviousResponseId);
        Assert.Equal("resp_start", gateway.AgentRequests[1].PreviousResponseId);
    }

    [Fact]
    public void Continue_represents_tool_failure_as_safe_output_without_throwing()
    {
        ToolRegistry registry = new();
        registry.Register(new StubTool("workspace.shell", "Run shell command.", """{"type":"object"}"""));
        FakeGateway gateway = new()
        {
            Responses =
            [
                new OpenAiResponseEnvelope(
                    ResponseId: "resp_retry",
                    Model: "gpt-test",
                    Text: "",
                    ToolCalls:
                    [
                        new OpenAiToolCall("call_retry", "workspace.shell", """{"command":"pwd"}""")
                    ])
            ]
        };
        OpenAiToolCallingModel model = new(
            model: "gpt-test",
            instructions: null,
            registry,
            gateway);
        ToolExecutionResult failure = ToolExecutionResult.Failure(
            errorCode: "shell-denied",
            safeMessage: "Shell command was denied.",
            retryable: false,
            approvalStatus: "denied");

        AgentModelTurn turn = model.Continue(
            CreateRequest("run shell"),
            [
                new AgentToolCallResult(
                    new AgentToolCallRequest("call_shell", "workspace.shell", "{}"),
                    failure)
            ]);

        OpenAiToolResultInput resultInput = Assert.Single(Assert.Single(gateway.AgentRequests).ToolResults);
        Assert.Equal("call_shell", resultInput.CallId);
        Assert.Equal("workspace.shell", resultInput.ToolName);
        Assert.False(resultInput.Succeeded);
        Assert.Equal("Shell command was denied.", resultInput.Summary);
        Assert.Equal("shell-denied", resultInput.ErrorCode);
        Assert.Equal("denied", resultInput.ApprovalStatus);
        Assert.DoesNotContain("Exception", resultInput.Summary, StringComparison.OrdinalIgnoreCase);

        AgentToolCallRequest retryCall = Assert.Single(turn.ToolCalls);
        Assert.Equal("call_retry", retryCall.CallId);
    }

    private static AgentRunRequest CreateRequest(string prompt)
    {
        WorkspaceContext workspace = new(
            RootPath: "workspace-root",
            ConfigPath: Path.Combine("workspace-root", ".caicli", "config.json"),
            Status: WorkspaceStatus.Ready);

        return new AgentRunRequest(prompt, workspace);
    }

    private sealed class FakeGateway : IOpenAiResponsesGateway
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

    private sealed class StubTool : ITool
    {
        public StubTool(string name, string description, string parametersSchema)
        {
            Definition = new ToolDefinition(name, description, parametersSchema);
        }

        public ToolDefinition Definition { get; }

        public ToolExecutionResult Execute(
            ToolExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            return ToolExecutionResult.Success("{}");
        }
    }
}
