using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class OpenAiResponsesModelClientTests
{
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

    private static CliEnvironmentSnapshot CreateSnapshot(string? apiKey, string apiKeySource, string model)
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
            ApiKey: SecretValue.From(apiKey),
            ApiKeySource: apiKeySource,
            LoadedConfigPaths: [],
            Warnings: []);

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
        public string? LastModel { get; private set; }
        public string? LastPrompt { get; private set; }
        public Exception? ExceptionToThrow { get; init; }
        public OpenAiResponseEnvelope Response { get; init; } = new(
            ResponseId: "resp_fake",
            Model: "gpt-test",
            Text: "fake response");

        public OpenAiResponseEnvelope CreateResponse(string model, string prompt, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastModel = model;
            LastPrompt = prompt;

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return Response;
        }
    }
}
