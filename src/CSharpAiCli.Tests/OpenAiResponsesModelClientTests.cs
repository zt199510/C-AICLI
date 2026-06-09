using System.ClientModel;
using System.ClientModel.Primitives;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class OpenAiResponsesModelClientTests
{
    [Fact]
    public void Send_passes_effective_base_url_to_gateway_factory()
    {
        FakeGateway gateway = new();
        string? capturedBaseUrl = null;
        OpenAiResponsesModelClient client = CreateClientWithBaseUrlAwareFactory(
            CreateSnapshot(
                apiKey: "sk-test",
                apiKeySource: "OPENAI_API_KEY",
                model: "gpt-test",
                baseUrl: "https://gateway.example.test/v1"),
            (_, baseUrl) =>
            {
                capturedBaseUrl = baseUrl;
                return gateway;
            });

        ChatModelResult result = client.Send(new ChatRequest("hello"));

        Assert.True(result.IsSuccess);
        Assert.Equal("https://gateway.example.test/v1", capturedBaseUrl);
        Assert.Equal(1, gateway.CallCount);
    }

    [Fact]
    public void Send_passes_default_base_url_to_gateway_factory()
    {
        FakeGateway gateway = new();
        string? capturedBaseUrl = null;
        OpenAiResponsesModelClient client = CreateClientWithBaseUrlAwareFactory(
            CreateSnapshot(apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
            (_, baseUrl) =>
            {
                capturedBaseUrl = baseUrl;
                return gateway;
            });

        ChatModelResult result = client.Send(new ChatRequest("hello"));

        Assert.True(result.IsSuccess);
        Assert.Equal(ConfigLoader.DefaultOpenAiBaseUrl, capturedBaseUrl);
        Assert.Equal(1, gateway.CallCount);
    }

    [Fact]
    public void SendStreaming_passes_effective_base_url_to_gateway_factory()
    {
        FakeGateway gateway = new();
        string? capturedBaseUrl = null;
        FakeStreamingRenderer renderer = new();
        OpenAiResponsesModelClient client = CreateClientWithBaseUrlAwareFactory(
            CreateSnapshot(
                apiKey: "sk-test",
                apiKeySource: "OPENAI_API_KEY",
                model: "gpt-test",
                baseUrl: "https://streaming.example.test/v1"),
            (_, baseUrl) =>
            {
                capturedBaseUrl = baseUrl;
                return gateway;
            });

        ChatModelResult result = client.SendStreaming(new ChatRequest("hello"), renderer);

        Assert.True(result.IsSuccess);
        Assert.Equal("https://streaming.example.test/v1", capturedBaseUrl);
        Assert.Equal(1, gateway.StreamingCallCount);
    }

    [Fact]
    public void Sdk_gateway_exposes_configured_endpoint()
    {
        Uri expectedEndpoint = new("https://gateway.example.test/v1");
        SdkOpenAiResponsesGateway gateway = new("sk-test", expectedEndpoint.ToString());

        Assert.Equal(expectedEndpoint, gateway.Endpoint);
    }

    [Fact]
    public void Sdk_gateway_single_api_key_constructor_uses_default_endpoint()
    {
        SdkOpenAiResponsesGateway gateway = new("sk-test");

        Assert.Equal(new Uri(ConfigLoader.DefaultOpenAiBaseUrl), gateway.Endpoint);
    }

    [Theory]
    [InlineData("https://gateway.example.test/v1?api-key=sk-secret")]
    [InlineData("ftp://gateway.example.test/v1")]
    public void Sdk_gateway_rejects_unsafe_base_url(string baseUrl)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new SdkOpenAiResponsesGateway("sk-test", baseUrl));

        Assert.Equal("baseUrl", exception.ParamName);
    }

    [Fact]
    public void Constructor_rejects_null_legacy_gateway_factory()
    {
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            apiKey: "sk-test",
            apiKeySource: "OPENAI_API_KEY",
            model: "gpt-test");

        Assert.Throws<ArgumentNullException>(
            () => new OpenAiResponsesModelClient(
                snapshot,
                (Func<string, IOpenAiResponsesGateway>)null!));
    }

    [Fact]
    public void Send_returns_missing_key_error_without_calling_gateway()
    {
        FakeGateway gateway = new();
        OpenAiResponsesModelClient client = new(
            CreateSnapshot(apiKey: null, apiKeySource: "missing", model: "gpt-test"),
            _ => gateway);

        ChatModelResult result = client.Send(new ChatRequest("hello"));

        Assert.False(result.IsSuccess);
        Assert.Equal("missing-openai-api-key", result.Error?.LocalErrorCode);
        Assert.Equal(0, gateway.CallCount);
    }

    [Fact]
    public void Send_rejects_workspace_config_api_key_for_real_model_calls()
    {
        FakeGateway gateway = new();
        OpenAiResponsesModelClient client = new(
            CreateSnapshot(apiKey: "sk-workspace-secret", apiKeySource: "workspace config", model: "gpt-test"),
            _ => gateway);

        ChatModelResult result = client.Send(new ChatRequest("hello"));

        Assert.False(result.IsSuccess);
        Assert.Equal("unsupported-api-key-source", result.Error?.LocalErrorCode);
        Assert.Contains("OPENAI_API_KEY", result.Error?.SafeMessage);
        Assert.Equal(0, gateway.CallCount);
    }

    [Fact]
    public void Send_returns_missing_model_error_without_calling_gateway()
    {
        FakeGateway gateway = new();
        OpenAiResponsesModelClient client = new(
            CreateSnapshot(apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "not configured"),
            _ => gateway);

        ChatModelResult result = client.Send(new ChatRequest("hello"));

        Assert.False(result.IsSuccess);
        Assert.Equal("missing-model", result.Error?.LocalErrorCode);
        Assert.Equal(0, gateway.CallCount);
    }

    [Fact]
    public void Send_returns_empty_prompt_error_without_calling_gateway()
    {
        FakeGateway gateway = new();
        OpenAiResponsesModelClient client = new(
            CreateSnapshot(apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
            _ => gateway);

        ChatModelResult result = client.Send(new ChatRequest("   "));

        Assert.False(result.IsSuccess);
        Assert.Equal("empty-prompt", result.Error?.LocalErrorCode);
        Assert.Equal(0, gateway.CallCount);
    }

    [Fact]
    public void Send_calls_gateway_and_returns_response_text()
    {
        FakeGateway gateway = new()
        {
            Response = new OpenAiResponseEnvelope(
                ResponseId: "resp_123",
                Model: "gpt-test",
                Text: "hello from model")
        };

        OpenAiResponsesModelClient client = new(
            CreateSnapshot(apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
            _ => gateway);

        ChatModelResult result = client.Send(new ChatRequest("hello"));

        Assert.True(result.IsSuccess);
        Assert.Equal("openai", result.Response?.Provider);
        Assert.Equal("gpt-test", result.Response?.Model);
        Assert.Equal("resp_123", result.Response?.ResponseId);
        Assert.Equal("hello from model", result.Response?.Text);
        Assert.Equal(1, gateway.CallCount);
        Assert.Equal("gpt-test", gateway.LastModel);
        Assert.Equal("hello", gateway.LastPrompt);
    }

    [Fact]
    public void Send_ignores_session_name_for_week_seven_model_call_payload()
    {
        FakeGateway gateway = new()
        {
            Response = new OpenAiResponseEnvelope(
                ResponseId: "resp_session",
                Model: "gpt-test",
                Text: "hello from model")
        };
        OpenAiResponsesModelClient client = new(
            CreateSnapshot(apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
            _ => gateway);

        ChatModelResult result = client.Send(new ChatRequest("hello", SessionName: "smoke"));

        Assert.True(result.IsSuccess);
        Assert.Equal("hello", gateway.LastPrompt);
    }

    [Fact]
    public void Send_passes_instruction_text_to_gateway_without_merging_it_into_prompt()
    {
        FakeGateway gateway = new()
        {
            Response = new OpenAiResponseEnvelope(
                ResponseId: "resp_instruction",
                Model: "gpt-test",
                Text: "hello from model")
        };
        OpenAiResponsesModelClient client = new(
            CreateSnapshot(apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
            _ => gateway);

        ChatModelResult result = client.Send(new ChatRequest(
            Prompt: "hello",
            Instructions: "Be concise."));

        Assert.True(result.IsSuccess);
        Assert.Equal("hello", gateway.LastPrompt);
        Assert.Equal("Be concise.", gateway.LastInstructions);
    }

    [Fact]
    public void Send_allows_user_config_api_key_source_and_returns_response_text()
    {
        FakeGateway gateway = new()
        {
            Response = new OpenAiResponseEnvelope(
                ResponseId: "resp_user_config",
                Model: "gpt-test",
                Text: "hello from user config key")
        };

        OpenAiResponsesModelClient client = new(
            CreateSnapshot(apiKey: "sk-user-config", apiKeySource: "user config", model: "gpt-test"),
            _ => gateway);

        ChatModelResult result = client.Send(new ChatRequest("hello"));

        Assert.True(result.IsSuccess);
        Assert.Equal("hello from user config key", result.Response?.Text);
        Assert.Equal(1, gateway.CallCount);
        Assert.Equal("gpt-test", gateway.LastModel);
        Assert.Equal("hello", gateway.LastPrompt);
    }

    [Fact]
    public void Send_returns_retryable_error_when_response_text_is_empty()
    {
        FakeGateway gateway = new()
        {
            Response = new OpenAiResponseEnvelope(
                ResponseId: "resp_empty",
                Model: "gpt-test",
                Text: "   ")
        };

        OpenAiResponsesModelClient client = new(
            CreateSnapshot(apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
            _ => gateway);

        ChatModelResult result = client.Send(new ChatRequest("hello"));

        Assert.False(result.IsSuccess);
        Assert.Equal("empty-model-response", result.Error?.LocalErrorCode);
        Assert.True(result.Error?.Retryable);
        Assert.Equal(1, gateway.CallCount);
    }

    [Fact]
    public void Send_maps_operation_canceled_exception_to_safe_retryable_error()
    {
        FakeGateway gateway = new()
        {
            ExceptionToThrow = new OperationCanceledException("raw cancel detail")
        };

        OpenAiResponsesModelClient client = new(
            CreateSnapshot(apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
            _ => gateway);

        ChatModelResult result = client.Send(new ChatRequest("hello"));

        Assert.False(result.IsSuccess);
        Assert.Equal("model-call-canceled", result.Error?.LocalErrorCode);
        Assert.True(result.Error?.Retryable);
        Assert.DoesNotContain("raw cancel detail", result.Error?.SafeMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(429, true, "rate limit")]
    [InlineData(401, false, "API key")]
    public void Send_maps_client_result_exception_status_to_safe_http_error(
        int status,
        bool expectedRetryable,
        string expectedSafeMessageFragment)
    {
        FakeGateway gateway = new()
        {
            ExceptionToThrow = CreateClientResultException(status, "raw http detail")
        };

        OpenAiResponsesModelClient client = new(
            CreateSnapshot(apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
            _ => gateway);

        ChatModelResult result = client.Send(new ChatRequest("hello"));

        Assert.False(result.IsSuccess);
        Assert.Null(result.Error?.LocalErrorCode);
        Assert.Equal(status, result.Error?.StatusCode);
        Assert.Equal(expectedRetryable, result.Error?.Retryable);
        Assert.Contains(expectedSafeMessageFragment, result.Error?.SafeMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("raw http detail", result.Error?.SafeMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Send_maps_gateway_exception_to_safe_local_error()
    {
        FakeGateway gateway = new()
        {
            ExceptionToThrow = new InvalidOperationException("raw sdk detail")
        };

        OpenAiResponsesModelClient client = new(
            CreateSnapshot(apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
            _ => gateway);

        ChatModelResult result = client.Send(new ChatRequest("hello"));

        Assert.False(result.IsSuccess);
        Assert.Equal("openai-client-error", result.Error?.LocalErrorCode);
        Assert.DoesNotContain("raw sdk detail", result.Error?.SafeMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void SendStreaming_writes_deltas_and_returns_completed_response()
    {
        FakeGateway gateway = new()
        {
            StreamingUpdates =
            [
                OpenAiStreamingResponseUpdate.OutputTextDelta("Hel"),
                OpenAiStreamingResponseUpdate.OutputTextDelta("lo"),
                OpenAiStreamingResponseUpdate.Completed("resp_stream", "gpt-test")
            ]
        };
        FakeStreamingRenderer renderer = new();
        OpenAiResponsesModelClient client = new(
            CreateSnapshot(apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
            _ => gateway);

        ChatModelResult result = client.SendStreaming(new ChatRequest("hello"), renderer);

        Assert.True(result.IsSuccess);
        Assert.Equal("Hello", result.Response?.Text);
        Assert.Equal("resp_stream", result.Response?.ResponseId);
        Assert.Equal(1, renderer.StartCount);
        Assert.Equal(["Hel", "lo"], renderer.Deltas);
        Assert.Equal(1, renderer.CompleteCount);
        Assert.Null(renderer.Error);
        Assert.Equal(1, gateway.StreamingCallCount);
        Assert.Equal(0, gateway.CallCount);
    }

    [Fact]
    public void SendStreaming_passes_instruction_text_to_gateway()
    {
        FakeGateway gateway = new()
        {
            StreamingUpdates =
            [
                OpenAiStreamingResponseUpdate.OutputTextDelta("OK"),
                OpenAiStreamingResponseUpdate.Completed("resp_stream", "gpt-test")
            ]
        };
        FakeStreamingRenderer renderer = new();
        OpenAiResponsesModelClient client = new(
            CreateSnapshot(apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
            _ => gateway);

        ChatModelResult result = client.SendStreaming(
            new ChatRequest("hello", Instructions: "Be concise."),
            renderer);

        Assert.True(result.IsSuccess);
        Assert.Equal("hello", gateway.LastPrompt);
        Assert.Equal("Be concise.", gateway.LastInstructions);
    }

    [Fact]
    public void SendStreaming_validation_error_writes_failure_without_calling_gateway()
    {
        FakeGateway gateway = new();
        FakeStreamingRenderer renderer = new();
        OpenAiResponsesModelClient client = new(
            CreateSnapshot(apiKey: null, apiKeySource: "missing", model: "gpt-test"),
            _ => gateway);

        ChatModelResult result = client.SendStreaming(new ChatRequest("hello"), renderer);

        Assert.False(result.IsSuccess);
        Assert.Equal("missing-openai-api-key", result.Error?.LocalErrorCode);
        Assert.Equal("missing-openai-api-key", renderer.Error?.LocalErrorCode);
        Assert.Equal(0, renderer.StartCount);
        Assert.Equal(0, gateway.StreamingCallCount);
        Assert.Equal(0, gateway.CallCount);
    }

    [Fact]
    public void SendStreaming_returns_retryable_error_when_stream_has_no_text()
    {
        FakeGateway gateway = new()
        {
            StreamingUpdates =
            [
                OpenAiStreamingResponseUpdate.Completed("resp_empty", "gpt-test")
            ]
        };
        FakeStreamingRenderer renderer = new();
        OpenAiResponsesModelClient client = new(
            CreateSnapshot(apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
            _ => gateway);

        ChatModelResult result = client.SendStreaming(new ChatRequest("hello"), renderer);

        Assert.False(result.IsSuccess);
        Assert.Equal("empty-model-response", result.Error?.LocalErrorCode);
        Assert.True(result.Error?.Retryable);
        Assert.Equal("empty-model-response", renderer.Error?.LocalErrorCode);
        Assert.Equal(1, renderer.StartCount);
        Assert.Equal(0, renderer.CompleteCount);
    }

    [Fact]
    public void SendStreaming_returns_retryable_error_when_stream_text_is_whitespace_only()
    {
        FakeGateway gateway = new()
        {
            StreamingUpdates =
            [
                OpenAiStreamingResponseUpdate.OutputTextDelta("   "),
                OpenAiStreamingResponseUpdate.Completed("resp_blank", "gpt-test")
            ]
        };
        FakeStreamingRenderer renderer = new();
        OpenAiResponsesModelClient client = new(
            CreateSnapshot(apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
            _ => gateway);

        ChatModelResult result = client.SendStreaming(new ChatRequest("hello"), renderer);

        Assert.False(result.IsSuccess);
        Assert.Equal("empty-model-response", result.Error?.LocalErrorCode);
        Assert.True(result.Error?.Retryable);
        Assert.Equal("empty-model-response", renderer.Error?.LocalErrorCode);
        Assert.Equal(1, renderer.StartCount);
        Assert.Equal(0, renderer.CompleteCount);
        Assert.Equal(["   "], renderer.Deltas);
    }

    [Fact]
    public void SendStreaming_does_not_fail_renderer_after_complete_throws()
    {
        FakeGateway gateway = new()
        {
            StreamingUpdates =
            [
                OpenAiStreamingResponseUpdate.OutputTextDelta("Hello"),
                OpenAiStreamingResponseUpdate.Completed("resp_stream", "gpt-test")
            ]
        };
        InvalidOperationException exception = new("renderer complete failed");
        FakeStreamingRenderer renderer = new()
        {
            ThrowOnComplete = exception
        };
        OpenAiResponsesModelClient client = new(
            CreateSnapshot(apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
            _ => gateway);

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(
            () => client.SendStreaming(new ChatRequest("hello"), renderer));

        Assert.Same(exception, thrown);
        Assert.Equal(1, renderer.CompleteCount);
        Assert.Equal(0, renderer.FailCount);
    }

    [Fact]
    public void SendStreaming_does_not_retry_renderer_fail_when_fail_throws()
    {
        FakeGateway gateway = new()
        {
            StreamingUpdates =
            [
                OpenAiStreamingResponseUpdate.Completed("resp_empty", "gpt-test")
            ]
        };
        InvalidOperationException exception = new("renderer fail failed");
        FakeStreamingRenderer renderer = new()
        {
            ThrowOnFail = exception
        };
        OpenAiResponsesModelClient client = new(
            CreateSnapshot(apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
            _ => gateway);

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(
            () => client.SendStreaming(new ChatRequest("hello"), renderer));

        Assert.Same(exception, thrown);
        Assert.Equal(1, renderer.StartCount);
        Assert.Equal(0, renderer.CompleteCount);
        Assert.Equal(1, renderer.FailCount);
    }

    [Fact]
    public void SendStreaming_maps_gateway_exception_to_safe_error_and_renderer_failure()
    {
        FakeGateway gateway = new()
        {
            ExceptionToThrow = new InvalidOperationException("raw sdk detail")
        };
        FakeStreamingRenderer renderer = new();
        OpenAiResponsesModelClient client = new(
            CreateSnapshot(apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
            _ => gateway);

        ChatModelResult result = client.SendStreaming(new ChatRequest("hello"), renderer);

        Assert.False(result.IsSuccess);
        Assert.Equal("openai-client-error", result.Error?.LocalErrorCode);
        Assert.Equal("openai-client-error", renderer.Error?.LocalErrorCode);
        Assert.DoesNotContain("raw sdk detail", result.Error?.SafeMessage, StringComparison.Ordinal);
        Assert.Equal(1, renderer.StartCount);
        Assert.Equal(0, renderer.CompleteCount);
    }

    private static ClientResultException CreateClientResultException(int status, string rawMessage)
    {
        return new ClientResultException(
            rawMessage,
            new FakePipelineResponse(status),
            innerException: null);
    }

    private static OpenAiResponsesModelClient CreateClientWithBaseUrlAwareFactory(
        CliEnvironmentSnapshot snapshot,
        Func<string, string, IOpenAiResponsesGateway> gatewayFactory)
    {
        return new OpenAiResponsesModelClient(snapshot, gatewayFactory);
    }

    private static CliEnvironmentSnapshot CreateSnapshot(
        string? apiKey,
        string apiKeySource,
        string model,
        string? baseUrl = null)
    {
        WorkspaceContext workspace = new(
            RootPath: "workspace-root",
            ConfigPath: Path.Combine("workspace-root", ".caicli", "config.json"),
            Status: WorkspaceStatus.Ready);

        EffectiveConfiguration configuration = new(
            WorkspaceRoot: "workspace-root",
            UserConfigPath: Path.Combine("user-home", ".caicli", "config.json"),
            WorkspaceConfigPath: Path.Combine("workspace-root", ".caicli", "config.json"),
            Model: model,
            ModelSource: "workspace config",
            AgentBackend: "direct",
            AgentBackendSource: "default",
            DisabledTools: new HashSet<string>(StringComparer.Ordinal),
            ApiKey: SecretValue.From(apiKey),
            ApiKeySource: apiKeySource,
            LoadedConfigPaths: [],
            Warnings: [],
            ConfigSources: [])
        {
            BaseUrl = baseUrl ?? ConfigLoader.DefaultOpenAiBaseUrl
        };

        return new CliEnvironmentSnapshot(
            Workspace: workspace,
            Configuration: configuration,
            DotnetSdkVersion: "9.0.308",
            DotnetRuntime: ".NET 9.0.0",
            TargetFramework: "net9.0",
            HasGlobalJson: false);
    }

    private sealed class FakeGateway : IOpenAiResponsesGateway
    {
        public int CallCount { get; private set; }
        public int StreamingCallCount { get; private set; }
        public string? LastModel { get; private set; }
        public string? LastPrompt { get; private set; }
        public string? LastInstructions { get; private set; }
        public Exception? ExceptionToThrow { get; init; }
        public OpenAiResponseEnvelope Response { get; init; } = new(
            ResponseId: "resp_fake",
            Model: "gpt-test",
            Text: "fake response");
        public IReadOnlyList<OpenAiStreamingResponseUpdate> StreamingUpdates { get; init; } =
        [
            OpenAiStreamingResponseUpdate.OutputTextDelta("fake response"),
            OpenAiStreamingResponseUpdate.Completed("resp_fake", "gpt-test")
        ];

        public OpenAiResponseEnvelope CreateResponse(
            string model,
            string prompt,
            string? instructions = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastModel = model;
            LastPrompt = prompt;
            LastInstructions = instructions;

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return Response;
        }

        public IEnumerable<OpenAiStreamingResponseUpdate> CreateResponseStreaming(
            string model,
            string prompt,
            string? instructions = null,
            CancellationToken cancellationToken = default)
        {
            StreamingCallCount++;
            LastModel = model;
            LastPrompt = prompt;
            LastInstructions = instructions;

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return StreamingUpdates;
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

    private sealed class FakeStreamingRenderer : IChatStreamingRenderer
    {
        public int StartCount { get; private set; }
        public int CompleteCount { get; private set; }
        public int FailCount { get; private set; }
        public List<string> Deltas { get; } = [];
        public ModelError? Error { get; private set; }
        public ChatResponse? CompletedResponse { get; private set; }
        public Exception? ThrowOnComplete { get; init; }
        public Exception? ThrowOnFail { get; init; }

        public void Start(CliEnvironmentSnapshot snapshot, string provider, string model)
        {
            StartCount++;
        }

        public void WriteDelta(string textDelta)
        {
            Deltas.Add(textDelta);
        }

        public void Complete(ChatResponse response)
        {
            CompleteCount++;
            CompletedResponse = response;

            if (ThrowOnComplete is not null)
            {
                throw ThrowOnComplete;
            }
        }

        public void Fail(CliEnvironmentSnapshot snapshot, ModelError error)
        {
            FailCount++;
            Error = error;

            if (ThrowOnFail is not null)
            {
                throw ThrowOnFail;
            }
        }
    }
}
