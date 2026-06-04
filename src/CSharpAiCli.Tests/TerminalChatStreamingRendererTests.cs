using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class TerminalChatStreamingRendererTests
{
    [Fact]
    public void Start_delta_and_complete_write_streaming_output_without_secret()
    {
        using StringWriter writer = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            apiKey: "sk-stream-secret",
            apiKeySource: "OPENAI_API_KEY");
        TerminalChatStreamingRenderer renderer = new(writer);

        renderer.Start(snapshot, provider: "openai", model: "gpt-test");
        renderer.WriteDelta("Hel");
        renderer.WriteDelta("lo");
        renderer.Complete(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_stream",
            Text: "Hello"));

        string output = writer.ToString();
        Assert.Contains("C# AI CLI chat", output);
        Assert.Contains("workspace: workspace-root", output);
        Assert.Contains("workspace status: ready", output);
        Assert.Contains("api key: present", output);
        Assert.Contains("api key source: OPENAI_API_KEY", output);
        Assert.Contains("status: streaming", output);
        Assert.Contains("provider: openai", output);
        Assert.Contains("model: gpt-test", output);
        Assert.Contains("Hello", output);
        Assert.Contains("status: completed", output);
        Assert.Contains("responseId: resp_stream", output);
        Assert.DoesNotContain("sk-stream-secret", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "gpt-test")]
    [InlineData("", "gpt-test")]
    [InlineData("   ", "gpt-test")]
    [InlineData("openai", null)]
    [InlineData("openai", "")]
    [InlineData("openai", "   ")]
    public void Start_rejects_null_or_empty_provider_and_model(string? provider, string? model)
    {
        using StringWriter writer = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            apiKey: "sk-stream-secret",
            apiKeySource: "OPENAI_API_KEY");
        TerminalChatStreamingRenderer renderer = new(writer);

        Assert.ThrowsAny<ArgumentException>(() => renderer.Start(snapshot, provider!, model!));
        Assert.Equal(string.Empty, writer.ToString());
    }

    [Fact]
    public void Fail_before_start_writes_safe_error_report()
    {
        using StringWriter writer = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            apiKey: "sk-stream-secret",
            apiKeySource: "OPENAI_API_KEY");
        TerminalChatStreamingRenderer renderer = new(writer);

        renderer.Fail(snapshot, new ModelError(
            Provider: "openai",
            Operation: "responses.create",
            StatusCode: null,
            LocalErrorCode: "missing-model",
            SafeMessage: "Model is not configured.",
            Retryable: false));

        string output = writer.ToString();
        Assert.Contains("C# AI CLI chat", output);
        Assert.Contains("status: failed", output);
        Assert.Contains("localErrorCode: missing-model", output);
        Assert.Contains("safeMessage: Model is not configured.", output);
        Assert.DoesNotContain("sk-stream-secret", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Fail_after_start_appends_safe_error_without_repeating_header()
    {
        using StringWriter writer = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            apiKey: "sk-stream-secret",
            apiKeySource: "OPENAI_API_KEY");
        TerminalChatStreamingRenderer renderer = new(writer);

        renderer.Start(snapshot, provider: "openai", model: "gpt-test");
        renderer.WriteDelta("partial");
        renderer.Fail(snapshot, new ModelError(
            Provider: "openai",
            Operation: "responses.create",
            StatusCode: null,
            LocalErrorCode: "model-call-canceled",
            SafeMessage: "Model call was canceled before it completed.",
            Retryable: true));

        string output = writer.ToString();
        Assert.Equal(1, CountOccurrences(output, "C# AI CLI chat"));
        Assert.Contains("partial", output);
        Assert.Contains("status: failed", output);
        Assert.Contains("localErrorCode: model-call-canceled", output);
        Assert.Contains("retryable: true", output);
        Assert.DoesNotContain("sk-stream-secret", output, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int startIndex = 0;

        while (true)
        {
            int index = text.IndexOf(value, startIndex, StringComparison.Ordinal);
            if (index < 0)
            {
                return count;
            }

            count++;
            startIndex = index + value.Length;
        }
    }

    private static CliEnvironmentSnapshot CreateSnapshot(string? apiKey, string apiKeySource)
    {
        WorkspaceContext workspace = new(
            RootPath: "workspace-root",
            ConfigPath: Path.Combine("workspace-root", ".caicli", "config.json"),
            Status: WorkspaceStatus.Ready);

        EffectiveConfiguration configuration = new(
            WorkspaceRoot: "workspace-root",
            UserConfigPath: Path.Combine("user-home", ".caicli", "config.json"),
            WorkspaceConfigPath: Path.Combine("workspace-root", ".caicli", "config.json"),
            Model: "gpt-test",
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
}
