using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ChatModelReportTests
{
    [Fact]
    public void Create_formats_successful_chat_without_printing_api_key()
    {
        CliEnvironmentSnapshot snapshot = CreateSnapshot(apiKey: "sk-secret", apiKeySource: "OPENAI_API_KEY");
        ChatModelResult result = ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_123",
            Text: "model says hello"));

        string text = ChatModelReport.Create(snapshot, result).ToDisplayText();

        Assert.Contains("C# AI CLI chat", text);
        Assert.Contains("status: completed", text);
        Assert.Contains("provider: openai", text);
        Assert.Contains("model: gpt-test", text);
        Assert.Contains("responseId: resp_123", text);
        Assert.Contains("model says hello", text);
        Assert.Contains("api key: present", text);
        Assert.DoesNotContain("sk-secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_formats_failed_chat_with_safe_error()
    {
        CliEnvironmentSnapshot snapshot = CreateSnapshot(apiKey: null, apiKeySource: "missing");
        ChatModelResult result = ChatModelResult.Failure(new ModelError(
            Provider: "openai",
            Operation: "responses.create",
            StatusCode: null,
            LocalErrorCode: "missing-openai-api-key",
            SafeMessage: "OpenAI API key is missing. Set OPENAI_API_KEY or user config apiKey.",
            Retryable: false));

        string text = ChatModelReport.Create(snapshot, result).ToDisplayText();

        Assert.Contains("status: failed", text);
        Assert.Contains("provider: openai", text);
        Assert.Contains("operation: responses.create", text);
        Assert.Contains("localErrorCode: missing-openai-api-key", text);
        Assert.Contains("safeMessage: OpenAI API key is missing.", text);
        Assert.Contains("retryable: false", text);
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
            AgentBackend: "direct",
            AgentBackendSource: "default",
            DisabledTools: new HashSet<string>(StringComparer.Ordinal),
            ApiKey: SecretValue.From(apiKey),
            ApiKeySource: apiKeySource,
            LoadedConfigPaths: [],
            Warnings: [],
            ConfigSources: []);

        return new CliEnvironmentSnapshot(
            Workspace: workspace,
            Configuration: configuration,
            DotnetSdkVersion: "9.0.308",
            DotnetRuntime: ".NET 9.0.0",
            TargetFramework: "net9.0",
            HasGlobalJson: false);
    }
}
