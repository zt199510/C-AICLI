using System.CommandLine;
using CSharpAiCli.Cli;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class CliCommandFactoryTests
{
    [Fact]
    public void Doctor_command_writes_doctor_report()
    {
        using StringWriter output = new();

        int exitCode = CliCommandFactory
            .Create(output, CreateSnapshot)
            .Parse(["doctor"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Contains("C# AI CLI doctor", output.ToString());
        Assert.Contains("api key: missing", output.ToString());
    }

    [Fact]
    public void Config_get_command_writes_config_report()
    {
        using StringWriter output = new();

        int exitCode = CliCommandFactory
            .Create(output, CreateSnapshot)
            .Parse(["config", "get"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Contains("C# AI CLI effective configuration", output.ToString());
        Assert.Contains("model: not configured", output.ToString());
    }

    [Fact]
    public void Workspace_option_is_passed_to_doctor_command()
    {
        using StringWriter output = new();
        string? receivedWorkspace = null;

        int exitCode = CliCommandFactory
            .Create(output, workspacePath =>
            {
                receivedWorkspace = workspacePath;
                return CreateSnapshot(workspacePath);
            })
            .Parse(["doctor", "--workspace", "custom-root"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal("custom-root", receivedWorkspace);
        Assert.Contains("workspace: custom-root", output.ToString());
    }

    [Fact]
    public void Workspace_option_is_passed_to_config_get_command()
    {
        using StringWriter output = new();
        string? receivedWorkspace = null;

        int exitCode = CliCommandFactory
            .Create(output, workspacePath =>
            {
                receivedWorkspace = workspacePath;
                return CreateSnapshot(workspacePath);
            })
            .Parse(["config", "get", "--workspace", "custom-root"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal("custom-root", receivedWorkspace);
        Assert.Contains("workspace: custom-root", output.ToString());
    }

    [Fact]
    public void Doctor_command_writes_command_log_through_delegate()
    {
        using StringWriter output = new();
        List<string> loggedCommands = [];

        int exitCode = CliCommandFactory
            .Create(
                output,
                CreateSnapshot,
                (commandName, _) => loggedCommands.Add(commandName))
            .Parse(["doctor"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal(["doctor"], loggedCommands);
    }

    [Fact]
    public void Config_get_command_writes_command_log_through_delegate()
    {
        using StringWriter output = new();
        List<string> loggedCommands = [];

        int exitCode = CliCommandFactory
            .Create(
                output,
                CreateSnapshot,
                (commandName, _) => loggedCommands.Add(commandName))
            .Parse(["config", "get"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal(["config get"], loggedCommands);
    }

    [Fact]
    public void Command_logger_failure_does_not_block_doctor_report()
    {
        using StringWriter output = new();

        int exitCode = CliCommandFactory
            .Create(
                output,
                CreateSnapshot,
                (_, _) => throw new IOException("log directory unavailable"))
            .Parse(["doctor"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Contains("C# AI CLI doctor", output.ToString());
    }

    [Fact]
    public void Chat_command_streams_prompt_to_model_client_and_logs_command()
    {
        using StringWriter output = new();
        string? receivedWorkspace = null;
        List<string> loggedCommands = [];
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "fake model output")));

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath =>
                {
                    receivedWorkspace = workspacePath;
                    return CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test");
                },
                (commandName, _) => loggedCommands.Add(commandName),
                _ => chatClient,
                writer => new TerminalChatStreamingRenderer(writer))
            .Parse(["chat", "--workspace", "custom-root", "hello model"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal("custom-root", receivedWorkspace);
        Assert.Equal(["chat"], loggedCommands);
        Assert.Equal("hello model", chatClient.LastStreamingPrompt);
        Assert.Null(chatClient.LastNonStreamingPrompt);
        Assert.Contains("status: streaming", output.ToString());
        Assert.Contains("fake model output", output.ToString());
        Assert.Contains("status: completed", output.ToString());
    }

    private static CliEnvironmentSnapshot CreateSnapshot(string? workspacePath)
    {
        return CreateSnapshot(
            workspacePath,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured");
    }

    [Fact]
    public void Chat_command_returns_nonzero_for_streaming_model_error()
    {
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Failure(new ModelError(
            Provider: "openai",
            Operation: "responses.create",
            StatusCode: null,
            LocalErrorCode: "missing-openai-api-key",
            SafeMessage: "OpenAI API key is missing. Set OPENAI_API_KEY or user config apiKey.",
            Retryable: false)));

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(workspacePath, apiKey: null, apiKeySource: "missing", model: "gpt-test"),
                (_, _) => { },
                _ => chatClient,
                writer => new TerminalChatStreamingRenderer(writer))
            .Parse(["chat", "hello model"])
            .Invoke();

        Assert.Equal(1, exitCode);
        Assert.Contains("status: failed", output.ToString());
        Assert.Contains("localErrorCode: missing-openai-api-key", output.ToString());
    }

    [Fact]
    public void Chat_session_loads_transcript_records_success_and_saves()
    {
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "fake model output")));
        FakeConversationStore store = new();

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
                (_, _) => { },
                _ => chatClient,
                writer => new TerminalChatStreamingRenderer(writer),
                _ => store,
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"))
            .Parse(["chat", "--session", "smoke", "hello model"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal("smoke", chatClient.LastRequest?.SessionName);
        Assert.Equal("smoke", store.LoadedSessionName?.Value);
        Assert.Equal("smoke", store.SavedSessionName?.Value);
        Assert.NotNull(store.SavedTranscript);
        Assert.Equal(2, store.SavedTranscript.Messages.Count);
        Assert.Equal("hello model", store.SavedTranscript.Messages[0].Content);
        Assert.Equal("fake model output", store.SavedTranscript.Messages[1].Content);
    }

    [Fact]
    public void Chat_session_records_failure_and_returns_nonzero()
    {
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Failure(new ModelError(
            Provider: "openai",
            Operation: "responses.create",
            StatusCode: null,
            LocalErrorCode: "missing-openai-api-key",
            SafeMessage: "OpenAI API key is missing. Set OPENAI_API_KEY or user config apiKey.",
            Retryable: false)));
        FakeConversationStore store = new();

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(workspacePath, apiKey: null, apiKeySource: "missing", model: "gpt-test"),
                (_, _) => { },
                _ => chatClient,
                writer => new TerminalChatStreamingRenderer(writer),
                _ => store,
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"))
            .Parse(["chat", "--session", "smoke", "hello model"])
            .Invoke();

        Assert.Equal(1, exitCode);
        Assert.NotNull(store.SavedTranscript);
        Assert.Equal("hello model", Assert.Single(store.SavedTranscript.Messages).Content);
        Assert.Equal("missing-openai-api-key", Assert.Single(store.SavedTranscript.Errors).LocalErrorCode);
    }

    [Fact]
    public void Chat_without_session_does_not_create_transcript()
    {
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "fake model output")));
        FakeConversationStore store = new();

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
                (_, _) => { },
                _ => chatClient,
                writer => new TerminalChatStreamingRenderer(writer),
                _ => store,
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"))
            .Parse(["chat", "hello model"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Null(store.LoadedSessionName);
        Assert.Null(store.SavedSessionName);
        Assert.Null(store.SavedTranscript);
    }

    private static CliEnvironmentSnapshot CreateSnapshot(
        string? workspacePath,
        string? apiKey,
        string apiKeySource,
        string model)
    {
        string workspaceRoot = string.IsNullOrWhiteSpace(workspacePath) ? "workspace-root" : workspacePath;

        WorkspaceContext workspace = new(
            RootPath: workspaceRoot,
            ConfigPath: Path.Combine(workspaceRoot, ".caicli", "config.json"),
            Status: WorkspaceStatus.Ready);

        EffectiveConfiguration configuration = new(
            WorkspaceRoot: workspaceRoot,
            UserConfigPath: Path.Combine("user-home", ".caicli", "config.json"),
            WorkspaceConfigPath: Path.Combine(workspaceRoot, ".caicli", "config.json"),
            Model: model,
            ModelSource: model == "not configured" ? "default" : "workspace config",
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

    private sealed class FakeChatModelClient(ChatModelResult result) : IChatModelClient
    {
        public string? LastNonStreamingPrompt { get; private set; }
        public string? LastStreamingPrompt { get; private set; }
        public ChatRequest? LastRequest { get; private set; }

        public ChatModelResult Send(ChatRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            LastNonStreamingPrompt = request.Prompt;
            return result;
        }

        public ChatModelResult SendStreaming(
            ChatRequest request,
            IChatStreamingRenderer renderer,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            LastStreamingPrompt = request.Prompt;

            if (result.Response is not null)
            {
                renderer.Start(CreateSnapshot(
                    workspacePath: null,
                    apiKey: "sk-test",
                    apiKeySource: "OPENAI_API_KEY",
                    model: result.Response.Model), result.Response.Provider, result.Response.Model);
                renderer.WriteDelta(result.Response.Text);
                renderer.Complete(result.Response);
                return result;
            }

            renderer.Fail(CreateSnapshot(
                workspacePath: null,
                apiKey: null,
                apiKeySource: "missing",
                model: "gpt-test"), result.Error!);
            return result;
        }
    }

    private sealed class FakeConversationStore : IConversationStore
    {
        public ConversationSessionName? LoadedSessionName { get; private set; }
        public ConversationSessionName? SavedSessionName { get; private set; }
        public ConversationTranscript? SavedTranscript { get; private set; }
        public ConversationTranscript Transcript { get; init; } = ConversationTranscript.Create(
            "smoke",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));

        public ConversationTranscript LoadOrCreate(ConversationSessionName sessionName, DateTimeOffset nowUtc)
        {
            LoadedSessionName = sessionName;
            return Transcript;
        }

        public string Save(ConversationSessionName sessionName, ConversationTranscript transcript)
        {
            SavedSessionName = sessionName;
            SavedTranscript = transcript;
            return Path.Combine("user-home", ".caicli", "sessions", $"{sessionName.FileSafeName}.transcript.json");
        }
    }
}
