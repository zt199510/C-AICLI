using System.Text.Json;
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
    public void Start_with_transcript_context_includes_prior_context_and_current_prompt()
    {
        ToolRegistry registry = new();
        registry.Register(new StubTool("workspace.read_text", "Read a text file.", """{"type":"object"}"""));
        FakeGateway gateway = new()
        {
            Responses =
            [
                new OpenAiResponseEnvelope(
                    ResponseId: "resp_context",
                    Model: "gpt-test",
                    Text: "done")
            ]
        };
        ConversationTranscript transcript = ConversationTranscript.Create(
            "smoke",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        transcript.AddUserMessage("previous exec task", DateTimeOffset.Parse("2024-01-01T00:00:01Z"));
        transcript.AddAssistantMessage(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_previous",
            Text: "previous exec answer"), DateTimeOffset.Parse("2024-01-01T00:00:02Z"));
        OpenAiToolCallingModel model = new(
            model: "gpt-test",
            instructions: null,
            registry,
            gateway);

        model.Start(CreateRequest("current exec task", transcript));

        OpenAiAgentRequest sentRequest = Assert.Single(gateway.AgentRequests);
        Assert.NotEqual("current exec task", sentRequest.Prompt);
        Assert.Contains("previous exec task", sentRequest.Prompt, StringComparison.Ordinal);
        Assert.Contains("previous exec answer", sentRequest.Prompt, StringComparison.Ordinal);
        Assert.Contains("current exec task", sentRequest.Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Start_emits_thinking_and_streaming_for_completed_response_text()
    {
        ToolRegistry registry = new();
        FakeGateway gateway = new()
        {
            Responses =
            [
                new OpenAiResponseEnvelope(
                    ResponseId: "resp_final",
                    Model: "gpt-test",
                    Text: "complete reply")
            ]
        };
        RecordingProviderObserver observer = new();
        OpenAiToolCallingModel model = new(
            model: "gpt-test",
            instructions: null,
            registry,
            gateway,
            observer);

        AgentModelTurn turn = model.Start(CreateRequest("answer"));

        Assert.Equal("complete reply", turn.FinalText);
        Assert.Collection(
            observer.Events,
            item => Assert.Equal(ProviderAttemptPhase.Thinking, item.Phase),
            item =>
            {
                Assert.Equal(ProviderAttemptPhase.Streaming, item.Phase);
                Assert.Equal("complete reply", item.Content);
                Assert.True(item.HasStreamContent);
            });
    }

    [Fact]
    public void Start_emits_bounded_cumulative_streaming_snapshots_before_completion()
    {
        ToolRegistry registry = new();
        string[] deltas = Enumerable.Range(0, 40)
            .Select(index => index.ToString("D2") + "abcdefgh")
            .ToArray();
        string finalText = string.Concat(deltas);
        FakeGateway gateway = new()
        {
            StreamingDeltas = deltas,
            Responses = [new OpenAiResponseEnvelope("resp_stream", "gpt-test", finalText)]
        };
        RecordingProviderObserver observer = new();
        OpenAiToolCallingModel model = new(
            model: "gpt-test",
            instructions: null,
            registry,
            gateway,
            observer);

        AgentModelTurn turn = model.Start(CreateRequest("stream the answer"));

        ProviderAttemptEvent[] streaming = observer.Events
            .Where(item => item.Phase == ProviderAttemptPhase.Streaming)
            .ToArray();
        Assert.True(streaming.Length >= 4);
        Assert.Equal(deltas[0], streaming[0].Content);
        Assert.Equal(finalText, streaming[^1].Content);
        Assert.All(streaming, item => Assert.True(item.HasStreamContent));
        Assert.Equal(
            streaming.Select(item => item.Content!.Length).Order(),
            streaming.Select(item => item.Content!.Length));
        Assert.Equal(finalText, turn.FinalText);
    }

    [Fact]
    public void Start_with_task_context_includes_bounded_context_and_startup_plan()
    {
        ToolRegistry registry = new();
        registry.Register(new StubTool("workspace.read_text", "Read a text file.", """{"type":"object"}"""));
        FakeGateway gateway = new()
        {
            Responses =
            [
                new OpenAiResponseEnvelope(
                    ResponseId: "resp_context",
                    Model: "gpt-test",
                    Text: "done")
            ]
        };
        OpenAiToolCallingModel model = new(
            model: "gpt-test",
            instructions: null,
            registry,
            gateway);

        model.Start(CreateRequest("fix src/App.cs") with
        {
            TaskContext = CreateTaskContext()
        });

        OpenAiAgentRequest sentRequest = Assert.Single(gateway.AgentRequests);
        Assert.Contains("Bounded startup context:", sentRequest.Prompt, StringComparison.Ordinal);
        Assert.Contains("Read-only startup plan:", sentRequest.Prompt, StringComparison.Ordinal);
        Assert.Contains("workspace-root", sentRequest.Prompt, StringComparison.Ordinal);
        Assert.Contains("AGENTS.md", sentRequest.Prompt, StringComparison.Ordinal);
        Assert.Contains("src/App.cs", sentRequest.Prompt, StringComparison.Ordinal);
        Assert.Contains("Current task:", sentRequest.Prompt, StringComparison.Ordinal);
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
                    ResponseId: "resp_tool",
                    Model: "gpt-test",
                    Text: "",
                    ToolCalls:
                    [
                        new OpenAiToolCall("call_status", "workspace.git_status", "{}")
                    ]),
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

        AgentModelTurn firstTurn = model.Start(CreateRequest("check status"));
        using JsonDocument payloadDocument = JsonDocument.Parse("""
        {
          "branch": "main",
          "clean": true
        }
        """);
        Dictionary<string, JsonElement> structuredPayload = payloadDocument.RootElement
            .EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value);
        AgentModelTurn turn = model.Continue(
            CreateRequest("check status"),
            [
                new AgentToolCallResult(
                    firstTurn.ToolCalls.Single(),
                    ToolExecutionResult.Success(
                        "On branch main. nothing to commit.",
                        structuredPayload: structuredPayload))
            ]);

        OpenAiAgentRequest sentRequest = gateway.AgentRequests[1];
        Assert.Equal("check status", sentRequest.Prompt);
        Assert.Null(sentRequest.PreviousResponseId);
        Assert.Equal("Summarize tool output.", sentRequest.Instructions);
        Assert.Equal("workspace.git_status", Assert.Single(sentRequest.Tools).Name);
        OpenAiToolResultInput resultInput = Assert.Single(sentRequest.ToolResults);
        Assert.Equal("call_status", resultInput.CallId);
        Assert.Equal("workspace.git_status", resultInput.ToolName);
        Assert.Equal("{}", resultInput.ArgumentsJson);
        Assert.True(resultInput.Succeeded);
        Assert.Equal("On branch main. nothing to commit.", resultInput.Summary);
        Assert.Null(resultInput.ErrorCode);
        Assert.Equal("not-required", resultInput.ApprovalStatus);
        IReadOnlyDictionary<string, JsonElement> resultPayload =
            resultInput.StructuredPayload ?? throw new InvalidOperationException("Structured payload was not set.");
        Assert.Equal("main", resultPayload["branch"].GetString());
        Assert.True(resultPayload["clean"].GetBoolean());
        Assert.True(turn.IsFinal);
        Assert.Equal("Working tree is clean.", turn.FinalText);
    }

    [Fact]
    public void Continue_replays_original_prompt_without_provider_response_state()
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

        Assert.Equal("check status", gateway.AgentRequests[0].Prompt);
        Assert.Equal("check status", gateway.AgentRequests[1].Prompt);
        Assert.Null(gateway.AgentRequests[0].PreviousResponseId);
        Assert.Null(gateway.AgentRequests[1].PreviousResponseId);
    }

    [Fact]
    public void Continue_before_start_throws_safe_lifecycle_error_without_calling_gateway()
    {
        ToolRegistry registry = new();
        registry.Register(new StubTool("workspace.git_status", "Show git status.", """{"type":"object"}"""));
        FakeGateway gateway = new();
        OpenAiToolCallingModel model = new(
            model: "gpt-test",
            instructions: null,
            registry,
            gateway);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => model.Continue(
                CreateRequest("check status"),
                [
                    new AgentToolCallResult(
                        new AgentToolCallRequest("call_status", "workspace.git_status", "{}"),
                        ToolExecutionResult.Success("clean"))
                ]));

        Assert.Equal(
            "OpenAI tool calling model must be started before continuing.",
            exception.Message);
        Assert.Empty(gateway.AgentRequests);
    }

    [Fact]
    public void Start_after_previous_run_resets_previous_response_id_before_sending_request()
    {
        ToolRegistry registry = new();
        registry.Register(new StubTool("workspace.git_status", "Show git status.", """{"type":"object"}"""));
        FakeGateway gateway = new()
        {
            Responses =
            [
                new OpenAiResponseEnvelope(
                    ResponseId: "resp_first_start",
                    Model: "gpt-test",
                    Text: "",
                    ToolCalls:
                    [
                        new OpenAiToolCall("call_status", "workspace.git_status", "{}")
                    ]),
                new OpenAiResponseEnvelope(
                    ResponseId: "resp_first_continue",
                    Model: "gpt-test",
                    Text: "done"),
                new OpenAiResponseEnvelope(
                    ResponseId: "resp_second_start",
                    Model: "gpt-test",
                    Text: "new run")
            ]
        };
        OpenAiToolCallingModel model = new(
            model: "gpt-test",
            instructions: null,
            registry,
            gateway);

        AgentModelTurn firstTurn = model.Start(CreateRequest("first run"));
        model.Continue(
            CreateRequest("first run"),
            [
                new AgentToolCallResult(
                    firstTurn.ToolCalls.Single(),
                    ToolExecutionResult.Success("clean"))
            ]);
        model.Start(CreateRequest("second run"));

        Assert.Single(gateway.AgentRequests[1].ToolResults);
        Assert.Empty(gateway.AgentRequests[2].ToolResults);
        Assert.Null(gateway.AgentRequests[1].PreviousResponseId);
        Assert.Null(gateway.AgentRequests[2].PreviousResponseId);
        Assert.Equal("second run", gateway.AgentRequests[2].Prompt);
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
                    ResponseId: "resp_shell",
                    Model: "gpt-test",
                    Text: "",
                    ToolCalls:
                    [
                        new OpenAiToolCall("call_shell", "workspace.shell", "{}")
                    ]),
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

        AgentModelTurn firstTurn = model.Start(CreateRequest("run shell"));
        AgentModelTurn turn = model.Continue(
            CreateRequest("run shell"),
            [
                new AgentToolCallResult(
                    firstTurn.ToolCalls.Single(),
                    failure)
            ]);

        OpenAiToolResultInput resultInput = Assert.Single(gateway.AgentRequests[1].ToolResults);
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

    private static AgentRunRequest CreateRequest(string prompt, ConversationTranscript? transcriptContext = null)
    {
        WorkspaceContext workspace = new(
            RootPath: "workspace-root",
            ConfigPath: Path.Combine("workspace-root", ".caicli", "config.json"),
            Status: WorkspaceStatus.Ready);

        return new AgentRunRequest(prompt, workspace, TranscriptContext: transcriptContext);
    }

    private static AgentTaskContext CreateTaskContext()
    {
        return new AgentTaskContext(
            CurrentDirectory: Path.Combine("workspace-root", "src"),
            WorkspaceRoot: "workspace-root",
            WorkspaceStatus: WorkspaceStatus.Ready.ToString(),
            CurrentDirectoryErrorCode: null,
            Instructions: "Use repo style.",
            InstructionSources: [new InstructionSource(Path.Combine("workspace-root", "AGENTS.md"), 0)],
            InstructionWarnings: [],
            SessionName: "smoke",
            HasTranscriptContext: false,
            Git: new AgentGitContextSummary(
                " M src/App.cs",
                StatusSucceeded: true,
                StatusErrorCode: null,
                IsDirty: true,
                StatusSummaryTruncated: false,
                "src/App.cs | 2 +-",
                DiffSucceeded: true,
                DiffErrorCode: null,
                DiffOutputTruncated: false,
                DiffSummaryTruncated: false));
    }

    private sealed class FakeGateway : IOpenAiResponsesGateway
    {
        private int responseIndex;

        public List<OpenAiAgentRequest> AgentRequests { get; } = [];
        public IReadOnlyList<OpenAiResponseEnvelope> Responses { get; init; } = [];
        public IReadOnlyList<string>? StreamingDeltas { get; init; }

        public OpenAiResponseEnvelope CreateAgentResponse(
            OpenAiAgentRequest request,
            CancellationToken cancellationToken = default)
        {
            if (responseIndex >= Responses.Count)
            {
                throw new InvalidOperationException("Fake gateway has no queued agent response.");
            }

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

        public IEnumerable<OpenAiStreamingResponseUpdate> CreateAgentResponseStreaming(
            OpenAiAgentRequest request,
            CancellationToken cancellationToken = default)
        {
            if (StreamingDeltas is null)
            {
                yield return OpenAiStreamingResponseUpdate.Completed(
                    CreateAgentResponse(request, cancellationToken));
                yield break;
            }
            if (responseIndex >= Responses.Count)
            {
                throw new InvalidOperationException("Fake gateway has no queued agent response.");
            }

            AgentRequests.Add(request);
            foreach (string delta in StreamingDeltas)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return OpenAiStreamingResponseUpdate.OutputTextDelta(delta);
            }
            yield return OpenAiStreamingResponseUpdate.Completed(Responses[responseIndex++]);
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

    private sealed class RecordingProviderObserver : IProviderAttemptObserver
    {
        public List<ProviderAttemptEvent> Events { get; } = [];

        public void OnProviderAttempt(ProviderAttemptEvent attemptEvent)
        {
            Events.Add(attemptEvent);
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
