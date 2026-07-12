using System.Diagnostics;
using System.CommandLine;
using System.Text.Json.Nodes;
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
    public void Doctor_trace_writes_command_start_and_complete_without_polluting_stdout()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        RootCommand command = CliCommandFactory.Create(
            output,
            workspacePath => CreateSnapshot(workspacePath),
            (_, _) => { },
            _ => throw new InvalidOperationException("doctor must not create a model client"),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => new FakeConversationStore(),
            () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"));

        int exitCode = CliCommandFactory.Invoke(command, ["doctor", "--trace", "--workspace", temp.Path], output);

        string text = output.ToString();
        string tracePath = Path.Combine(temp.Path, ".caicli", "logs", "2024-01-01.trace.log");
        string[] traceLines = File.ReadAllLines(tracePath);
        JsonObject start = Assert.IsType<JsonObject>(JsonNode.Parse(traceLines[0]));
        JsonObject complete = Assert.IsType<JsonObject>(JsonNode.Parse(traceLines[1]));

        Assert.Equal(0, exitCode);
        Assert.Contains("C# AI CLI doctor", text, StringComparison.Ordinal);
        Assert.DoesNotContain("command.start", text, StringComparison.Ordinal);
        Assert.DoesNotContain("commandId", text, StringComparison.Ordinal);
        Assert.Equal(2, traceLines.Length);
        Assert.Equal("doctor", start["command"]?.GetValue<string>());
        Assert.Equal("command.start", start["type"]?.GetValue<string>());
        Assert.Equal("started", start["status"]?.GetValue<string>());
        Assert.Equal("command.complete", complete["type"]?.GetValue<string>());
        Assert.Equal("success", complete["status"]?.GetValue<string>());
    }

    [Fact]
    public void Status_command_writes_status_report_for_non_git_workspace()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();

        int exitCode = CliCommandFactory
            .Create(output, workspacePath => CreateSnapshot(workspacePath))
            .Parse(["status", "--workspace", temp.Path])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("C# AI CLI status", text);
        Assert.Contains($"workspace: {temp.Path}", text);
        Assert.Contains("workspaceStatus: ready", text);
        Assert.Contains("gitStatus: not a git repository", text);
        Assert.Contains("configurationStatus: incomplete", text);
        Assert.Contains("model: not configured (default)", text);
        Assert.Contains("baseUrl: https://api.openai.com/v1 (default)", text);
        Assert.Contains("apiKey: missing (missing)", text);
        Assert.Contains("approvalMode: on-request (default)", text);
    }

    [Fact]
    public void Diff_command_writes_current_diff_for_git_workspace()
    {
        using TempDirectory temp = TempDirectory.Create();
        string filePath = InitializeGitRepository(temp.Path);
        File.AppendAllText(filePath, "changed\n");
        using StringWriter output = new();
        List<string> loggedCommands = [];

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(workspacePath),
                (commandName, _) => loggedCommands.Add(commandName))
            .Parse(["diff", "--workspace", temp.Path])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Equal(["diff"], loggedCommands);
        Assert.Contains("diff --git", text, StringComparison.Ordinal);
        Assert.Contains("+changed", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Diff_command_with_default_logger_writes_no_diff_for_clean_git_workspace()
    {
        using TempDirectory temp = TempDirectory.Create();
        InitializeGitRepository(temp.Path);
        using StringWriter output = new();

        int exitCode = CliCommandFactory
            .Create(output)
            .Parse(["diff", "--workspace", temp.Path])
            .Invoke();

        string text = output.ToString().TrimEnd();
        Assert.Equal(0, exitCode);
        Assert.Equal("no diff", text);
        Assert.True(Directory.Exists(Path.Combine(temp.Path, ".caicli", "logs")));
    }

    [Fact]
    public void Diff_command_with_default_logger_stays_no_diff_when_rerun()
    {
        using TempDirectory temp = TempDirectory.Create();
        InitializeGitRepository(temp.Path);
        using StringWriter firstOutput = new();
        using StringWriter secondOutput = new();

        int firstExitCode = CliCommandFactory
            .Create(firstOutput)
            .Parse(["diff", "--workspace", temp.Path])
            .Invoke();
        int secondExitCode = CliCommandFactory
            .Create(secondOutput)
            .Parse(["diff", "--workspace", temp.Path])
            .Invoke();

        Assert.Equal(0, firstExitCode);
        Assert.Equal("no diff", firstOutput.ToString().TrimEnd());
        Assert.Equal(0, secondExitCode);
        Assert.Equal("no diff", secondOutput.ToString().TrimEnd());
    }

    [Fact]
    public void Diff_command_stat_with_default_logger_writes_no_diff_for_clean_git_workspace()
    {
        using TempDirectory temp = TempDirectory.Create();
        InitializeGitRepository(temp.Path);
        using StringWriter output = new();

        int exitCode = CliCommandFactory
            .Create(output)
            .Parse(["diff", "--stat", "--workspace", temp.Path])
            .Invoke();

        string text = output.ToString().TrimEnd();
        Assert.Equal(0, exitCode);
        Assert.Equal("no diff", text);
        Assert.True(Directory.Exists(Path.Combine(temp.Path, ".caicli", "logs")));
    }

    [Fact]
    public void Diff_command_with_default_logger_writes_no_diff_when_cli_log_is_tracked()
    {
        using TempDirectory temp = TempDirectory.Create();
        InitializeGitRepository(temp.Path);
        TrackCliCommandLogs(temp.Path);
        using StringWriter output = new();

        int exitCode = CliCommandFactory
            .Create(output)
            .Parse(["diff", "--workspace", temp.Path])
            .Invoke();

        string text = output.ToString().TrimEnd();
        Assert.Equal(0, exitCode);
        Assert.Equal("no diff", text);
    }

    [Fact]
    public void Diff_command_stat_with_default_logger_writes_no_diff_when_cli_log_is_tracked()
    {
        using TempDirectory temp = TempDirectory.Create();
        InitializeGitRepository(temp.Path);
        TrackCliCommandLogs(temp.Path);
        using StringWriter output = new();

        int exitCode = CliCommandFactory
            .Create(output)
            .Parse(["diff", "--stat", "--workspace", temp.Path])
            .Invoke();

        string text = output.ToString().TrimEnd();
        Assert.Equal(0, exitCode);
        Assert.Equal("no diff", text);
    }

    [Fact]
    public void Diff_command_writes_staged_and_untracked_changes_for_git_workspace()
    {
        using TempDirectory temp = TempDirectory.Create();
        string filePath = InitializeGitRepository(temp.Path);
        File.AppendAllText(filePath, "staged change\n");
        RunGit(temp.Path, "add", "tracked.txt");
        File.WriteAllText(Path.Combine(temp.Path, "new file.txt"), "fresh\n");
        using StringWriter output = new();

        int exitCode = CliCommandFactory
            .Create(output, workspacePath => CreateSnapshot(workspacePath))
            .Parse(["diff", "--workspace", temp.Path])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("+staged change", text, StringComparison.Ordinal);
        Assert.Contains("new file.txt", text, StringComparison.Ordinal);
        Assert.Contains("+fresh", text, StringComparison.Ordinal);
        Assert.DoesNotContain("no diff", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Diff_command_writes_canceling_staged_and_unstaged_tracked_changes()
    {
        using TempDirectory temp = TempDirectory.Create();
        string filePath = InitializeGitRepository(temp.Path);
        File.WriteAllText(filePath, "staged\n");
        RunGit(temp.Path, "add", "tracked.txt");
        File.WriteAllText(filePath, "original\n");
        using StringWriter output = new();

        int exitCode = CliCommandFactory
            .Create(output, workspacePath => CreateSnapshot(workspacePath))
            .Parse(["diff", "--workspace", temp.Path])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("+staged", text, StringComparison.Ordinal);
        Assert.Contains("-staged", text, StringComparison.Ordinal);
        Assert.Contains("+original", text, StringComparison.Ordinal);
        Assert.DoesNotContain("no diff", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Diff_command_writes_staged_and_untracked_changes_for_no_head_git_workspace()
    {
        using TempDirectory temp = TempDirectory.Create();
        InitializeNoHeadGitRepository(temp.Path);
        File.WriteAllText(Path.Combine(temp.Path, "staged.txt"), "staged\n");
        RunGit(temp.Path, "add", "staged.txt");
        File.WriteAllText(Path.Combine(temp.Path, "untracked.txt"), "loose\n");
        using StringWriter output = new();

        int exitCode = CliCommandFactory
            .Create(output, workspacePath => CreateSnapshot(workspacePath))
            .Parse(["diff", "--workspace", temp.Path])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("staged.txt", text, StringComparison.Ordinal);
        Assert.Contains("+staged", text, StringComparison.Ordinal);
        Assert.Contains("untracked.txt", text, StringComparison.Ordinal);
        Assert.Contains("+loose", text, StringComparison.Ordinal);
        Assert.DoesNotContain("bad revision", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no diff", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Diff_command_stat_writes_git_diff_stat()
    {
        using TempDirectory temp = TempDirectory.Create();
        string filePath = InitializeGitRepository(temp.Path);
        File.AppendAllText(filePath, "changed\n");
        using StringWriter output = new();

        int exitCode = CliCommandFactory
            .Create(output, workspacePath => CreateSnapshot(workspacePath))
            .Parse(["diff", "--stat", "--workspace", temp.Path])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("tracked.txt", text, StringComparison.Ordinal);
        Assert.Contains("1 file changed", text, StringComparison.Ordinal);
        Assert.DoesNotContain("diff --git", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Diff_command_stat_writes_canceling_staged_and_unstaged_tracked_changes()
    {
        using TempDirectory temp = TempDirectory.Create();
        string filePath = InitializeGitRepository(temp.Path);
        File.WriteAllText(filePath, "staged\n");
        RunGit(temp.Path, "add", "tracked.txt");
        File.WriteAllText(filePath, "original\n");
        using StringWriter output = new();

        int exitCode = CliCommandFactory
            .Create(output, workspacePath => CreateSnapshot(workspacePath))
            .Parse(["diff", "--stat", "--workspace", temp.Path])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("tracked.txt", text, StringComparison.Ordinal);
        Assert.Contains("1 file changed", text, StringComparison.Ordinal);
        Assert.DoesNotContain("diff --git", text, StringComparison.Ordinal);
        Assert.DoesNotContain("no diff", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Diff_command_writes_no_diff_for_clean_git_workspace()
    {
        using TempDirectory temp = TempDirectory.Create();
        InitializeGitRepository(temp.Path);
        using StringWriter output = new();

        int exitCode = CliCommandFactory
            .Create(output, workspacePath => CreateSnapshot(workspacePath))
            .Parse(["diff", "--workspace", temp.Path])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal("no diff", output.ToString().TrimEnd());
    }

    [Fact]
    public void Models_command_writes_current_configuration_and_examples_without_api_key_or_model_client()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        List<string> loggedCommands = [];
        string? receivedWorkspace = null;

        int exitCode = CliCommandFactory.Invoke(
            CliCommandFactory.Create(
                output,
                workspacePath =>
                {
                    receivedWorkspace = workspacePath;
                    return CreateSnapshot(
                        workspacePath,
                        apiKey: null,
                        apiKeySource: "missing",
                        model: "gpt-cli",
                        baseUrl: "https://gateway.example.test/v1",
                        baseUrlSource: "workspace config");
                },
                (commandName, _) => loggedCommands.Add(commandName),
                _ => throw new InvalidOperationException("models must not create a chat model client")),
            ["models", "--workspace", temp.Path],
            output);

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Equal(temp.Path, receivedWorkspace);
        Assert.Equal(["models"], loggedCommands);
        Assert.Contains("C# AI CLI models", text, StringComparison.Ordinal);
        Assert.Contains("currentModel: gpt-cli", text, StringComparison.Ordinal);
        Assert.Contains("currentModelSource: workspace config", text, StringComparison.Ordinal);
        Assert.Contains("baseUrl: https://gateway.example.test/v1", text, StringComparison.Ordinal);
        Assert.Contains("baseUrlSource: workspace config", text, StringComparison.Ordinal);
        Assert.Contains("apiKey: missing", text, StringComparison.Ordinal);
        Assert.Contains("modelListApi: not called", text, StringComparison.Ordinal);
        Assert.Contains("recommendedModels:", text, StringComparison.Ordinal);
        Assert.Contains("caicli config set model gpt-4.1-mini", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Review_command_sends_current_diff_to_non_streaming_model_without_logging()
    {
        using TempDirectory temp = TempDirectory.Create();
        string filePath = InitializeGitRepository(temp.Path);
        File.AppendAllText(filePath, "changed\n");
        using StringWriter output = new();
        bool loggerInvoked = false;
        string? receivedWorkspace = null;
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "review report")));
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            temp.Path,
            apiKey: "sk-test",
            apiKeySource: "OPENAI_API_KEY",
            model: "gpt-test")
            with
            {
                Instructions = InstructionLoadResult.Loaded("Use the project review style.", Path.Combine(temp.Path, "AICLI.md"))
            };

        int exitCode = CliCommandFactory.Invoke(
            CliCommandFactory.Create(
                output,
                workspacePath =>
                {
                    receivedWorkspace = workspacePath;
                    return snapshot;
                },
                (_, _) =>
                {
                    loggerInvoked = true;
                    throw new InvalidOperationException("review must not write command logs");
                },
                _ => chatClient),
            ["review", "--workspace", temp.Path],
            output);

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Equal(temp.Path, receivedWorkspace);
        Assert.False(loggerInvoked);
        Assert.Null(chatClient.LastStreamingPrompt);
        Assert.NotNull(chatClient.LastNonStreamingPrompt);
        Assert.Contains("code review", chatClient.LastNonStreamingPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("diff --git", chatClient.LastNonStreamingPrompt, StringComparison.Ordinal);
        Assert.Contains("+changed", chatClient.LastNonStreamingPrompt, StringComparison.Ordinal);
        Assert.Equal("Use the project review style.", chatClient.LastRequest?.Instructions);
        Assert.Contains("review report", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Review_command_sends_staged_diff_to_non_streaming_model()
    {
        using TempDirectory temp = TempDirectory.Create();
        string filePath = InitializeGitRepository(temp.Path);
        File.AppendAllText(filePath, "staged change\n");
        RunGit(temp.Path, "add", "tracked.txt");
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "review report")));

        int exitCode = CliCommandFactory.Invoke(
            CliCommandFactory.Create(
                output,
                workspacePath => CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
                (_, _) => throw new InvalidOperationException("review must not write command logs"),
                _ => chatClient),
            ["review", "--workspace", temp.Path],
            output);

        Assert.Equal(0, exitCode);
        Assert.NotNull(chatClient.LastNonStreamingPrompt);
        Assert.Contains("diff --git", chatClient.LastNonStreamingPrompt, StringComparison.Ordinal);
        Assert.Contains("+staged change", chatClient.LastNonStreamingPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("no diff", chatClient.LastNonStreamingPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Review_command_sends_canceling_staged_and_unstaged_tracked_diff_to_non_streaming_model()
    {
        using TempDirectory temp = TempDirectory.Create();
        string filePath = InitializeGitRepository(temp.Path);
        File.WriteAllText(filePath, "staged\n");
        RunGit(temp.Path, "add", "tracked.txt");
        File.WriteAllText(filePath, "original\n");
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "review report")));

        int exitCode = CliCommandFactory.Invoke(
            CliCommandFactory.Create(
                output,
                workspacePath => CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
                (_, _) => throw new InvalidOperationException("review must not write command logs"),
                _ => chatClient),
            ["review", "--workspace", temp.Path],
            output);

        Assert.Equal(0, exitCode);
        Assert.NotNull(chatClient.LastNonStreamingPrompt);
        Assert.Contains("+staged", chatClient.LastNonStreamingPrompt, StringComparison.Ordinal);
        Assert.Contains("-staged", chatClient.LastNonStreamingPrompt, StringComparison.Ordinal);
        Assert.Contains("+original", chatClient.LastNonStreamingPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("no diff", chatClient.LastNonStreamingPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Review_command_sends_untracked_diff_to_non_streaming_model()
    {
        using TempDirectory temp = TempDirectory.Create();
        InitializeGitRepository(temp.Path);
        File.WriteAllText(Path.Combine(temp.Path, "new file.txt"), "fresh\n");
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "review report")));

        int exitCode = CliCommandFactory.Invoke(
            CliCommandFactory.Create(
                output,
                workspacePath => CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
                (_, _) => throw new InvalidOperationException("review must not write command logs"),
                _ => chatClient),
            ["review", "--workspace", temp.Path],
            output);

        Assert.Equal(0, exitCode);
        Assert.NotNull(chatClient.LastNonStreamingPrompt);
        Assert.Contains("diff --git", chatClient.LastNonStreamingPrompt, StringComparison.Ordinal);
        Assert.Contains("new file.txt", chatClient.LastNonStreamingPrompt, StringComparison.Ordinal);
        Assert.Contains("+fresh", chatClient.LastNonStreamingPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("no diff", chatClient.LastNonStreamingPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Review_command_sends_no_diff_when_only_untracked_cli_logs_exist()
    {
        using TempDirectory temp = TempDirectory.Create();
        InitializeGitRepository(temp.Path);
        WriteCliCommandLog(temp.Path);
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "review report")));

        int exitCode = CliCommandFactory.Invoke(
            CliCommandFactory.Create(
                output,
                workspacePath => CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
                (_, _) => throw new InvalidOperationException("review must not write command logs"),
                _ => chatClient),
            ["review", "--workspace", temp.Path],
            output);

        Assert.Equal(0, exitCode);
        Assert.NotNull(chatClient.LastNonStreamingPrompt);
        Assert.Contains("no diff", chatClient.LastNonStreamingPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain(".caicli/logs", chatClient.LastNonStreamingPrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Review_command_sends_no_diff_when_only_tracked_cli_log_changes_exist()
    {
        using TempDirectory temp = TempDirectory.Create();
        InitializeGitRepository(temp.Path);
        string logPath = TrackCliCommandLogs(temp.Path);
        File.AppendAllText(logPath, "command=diff\n");
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "review report")));

        int exitCode = CliCommandFactory.Invoke(
            CliCommandFactory.Create(
                output,
                workspacePath => CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
                (_, _) => throw new InvalidOperationException("review must not write command logs"),
                _ => chatClient),
            ["review", "--workspace", temp.Path],
            output);

        Assert.Equal(0, exitCode);
        Assert.NotNull(chatClient.LastNonStreamingPrompt);
        Assert.Contains("no diff", chatClient.LastNonStreamingPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain(".caicli/logs", chatClient.LastNonStreamingPrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Review_command_ignores_staged_tracked_cli_logs_without_hiding_staged_user_files()
    {
        using TempDirectory temp = TempDirectory.Create();
        string filePath = InitializeGitRepository(temp.Path);
        string logPath = TrackCliCommandLogs(temp.Path);
        File.AppendAllText(logPath, "command=review\n");
        File.AppendAllText(filePath, "visible staged user change\n");
        RunGit(temp.Path, "add", ".caicli/logs", "tracked.txt");
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "review report")));

        int exitCode = CliCommandFactory.Invoke(
            CliCommandFactory.Create(
                output,
                workspacePath => CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
                (_, _) => throw new InvalidOperationException("review must not write command logs"),
                _ => chatClient),
            ["review", "--workspace", temp.Path],
            output);

        Assert.Equal(0, exitCode);
        Assert.NotNull(chatClient.LastNonStreamingPrompt);
        Assert.Contains("tracked.txt", chatClient.LastNonStreamingPrompt, StringComparison.Ordinal);
        Assert.Contains("+visible staged user change", chatClient.LastNonStreamingPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain(".caicli/logs", chatClient.LastNonStreamingPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no diff", chatClient.LastNonStreamingPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Review_command_sends_no_head_staged_and_untracked_diff_to_non_streaming_model()
    {
        using TempDirectory temp = TempDirectory.Create();
        InitializeNoHeadGitRepository(temp.Path);
        File.WriteAllText(Path.Combine(temp.Path, "staged.txt"), "staged\n");
        RunGit(temp.Path, "add", "staged.txt");
        File.WriteAllText(Path.Combine(temp.Path, "untracked.txt"), "loose\n");
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "review report")));

        int exitCode = CliCommandFactory.Invoke(
            CliCommandFactory.Create(
                output,
                workspacePath => CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
                (_, _) => throw new InvalidOperationException("review must not write command logs"),
                _ => chatClient),
            ["review", "--workspace", temp.Path],
            output);

        Assert.Equal(0, exitCode);
        Assert.NotNull(chatClient.LastNonStreamingPrompt);
        Assert.Contains("staged.txt", chatClient.LastNonStreamingPrompt, StringComparison.Ordinal);
        Assert.Contains("+staged", chatClient.LastNonStreamingPrompt, StringComparison.Ordinal);
        Assert.Contains("untracked.txt", chatClient.LastNonStreamingPrompt, StringComparison.Ordinal);
        Assert.Contains("+loose", chatClient.LastNonStreamingPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("bad revision", chatClient.LastNonStreamingPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no diff", chatClient.LastNonStreamingPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Review_command_output_warns_when_diff_is_truncated()
    {
        using TempDirectory temp = TempDirectory.Create();
        string filePath = InitializeGitRepository(temp.Path);
        File.AppendAllText(filePath, new string('x', 70 * 1024) + "\n");
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "review report")));

        int exitCode = CliCommandFactory.Invoke(
            CliCommandFactory.Create(
                output,
                workspacePath => CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
                (_, _) => throw new InvalidOperationException("review must not write command logs"),
                _ => chatClient),
            ["review", "--workspace", temp.Path],
            output);

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("WARNING: git output was truncated; diff is incomplete.", chatClient.LastNonStreamingPrompt, StringComparison.Ordinal);
        Assert.Contains("WARNING: git output was truncated; diff is incomplete.", text, StringComparison.Ordinal);
        Assert.Contains("review report", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Review_command_sends_no_diff_to_non_streaming_model_for_clean_git_workspace()
    {
        using TempDirectory temp = TempDirectory.Create();
        InitializeGitRepository(temp.Path);
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "review report")));

        int exitCode = CliCommandFactory.Invoke(
            CliCommandFactory.Create(
                output,
                workspacePath => CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
                (_, _) => throw new InvalidOperationException("review must not write command logs"),
                _ => chatClient),
            ["review", "--workspace", temp.Path],
            output);

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Null(chatClient.LastStreamingPrompt);
        Assert.NotNull(chatClient.LastNonStreamingPrompt);
        Assert.Contains("Current git diff:", chatClient.LastNonStreamingPrompt, StringComparison.Ordinal);
        Assert.Contains("no diff", chatClient.LastNonStreamingPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("diff --git", chatClient.LastNonStreamingPrompt, StringComparison.Ordinal);
        Assert.Contains("review report", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Review_command_text_success_writes_findings_first_with_metadata()
    {
        using TempDirectory temp = TempDirectory.Create();
        string filePath = InitializeGitRepository(temp.Path);
        File.AppendAllText(filePath, "changed\n");
        using StringWriter output = new();
        string findingsText = """
        Findings:
        - src/Example.cs:10: Important issue.
        """;
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: findingsText)));

        int exitCode = CliCommandFactory.Invoke(
            CliCommandFactory.Create(
                output,
                workspacePath => CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
                (_, _) => throw new InvalidOperationException("review must not write command logs"),
                _ => chatClient),
            ["review", "--workspace", temp.Path],
            output);

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.StartsWith(findingsText, text, StringComparison.Ordinal);
        Assert.True(
            text.IndexOf("status: completed", StringComparison.Ordinal) > text.IndexOf("Important issue.", StringComparison.Ordinal),
            text);
        Assert.Contains("provider: openai", text, StringComparison.Ordinal);
        Assert.Contains("model: gpt-test", text, StringComparison.Ordinal);
        Assert.Contains("responseId: resp_test", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Review_command_json_success_writes_single_result_object()
    {
        using TempDirectory temp = TempDirectory.Create();
        string filePath = InitializeGitRepository(temp.Path);
        File.AppendAllText(filePath, "changed\n");
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "review report")));

        int exitCode = CliCommandFactory.Invoke(
            CliCommandFactory.Create(
                output,
                workspacePath => CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
                (_, _) => throw new InvalidOperationException("review must not write command logs"),
                _ => chatClient),
            ["review", "--json", "--workspace", temp.Path],
            output);

        JsonObject json = AssertSingleReviewJsonResult(output);
        Assert.Equal(0, exitCode);
        Assert.Equal("completed", json["status"]?.GetValue<string>());
        Assert.Equal("openai", json["provider"]?.GetValue<string>());
        Assert.Equal("gpt-test", json["model"]?.GetValue<string>());
        Assert.Equal("resp_test", json["responseId"]?.GetValue<string>());
        Assert.Equal("review report", json["findingsText"]?.GetValue<string>());
    }

    [Fact]
    public void Review_json_with_verbose_remains_single_json_object_without_verbose_text()
    {
        using TempDirectory temp = TempDirectory.Create();
        string filePath = InitializeGitRepository(temp.Path);
        File.AppendAllText(filePath, "changed\n");
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "review report")));

        int exitCode = CliCommandFactory.Invoke(
            CliCommandFactory.Create(
                output,
                workspacePath => CreateSnapshot(workspacePath, apiKey: "sk-test-secret", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
                (_, _) => throw new InvalidOperationException("review must not write command logs"),
                _ => chatClient),
            ["review", "--json", "--verbose", "--workspace", temp.Path],
            output);

        JsonObject json = AssertSingleReviewJsonResult(output);
        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Equal("completed", json["status"]?.GetValue<string>());
        Assert.DoesNotContain("C# AI CLI verbose diagnostics", text, StringComparison.Ordinal);
        Assert.DoesNotContain("commandId:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-test-secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Review_command_json_success_includes_empty_findings_text()
    {
        using TempDirectory temp = TempDirectory.Create();
        string filePath = InitializeGitRepository(temp.Path);
        File.AppendAllText(filePath, "changed\n");
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "")));

        int exitCode = CliCommandFactory.Invoke(
            CliCommandFactory.Create(
                output,
                workspacePath => CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
                (_, _) => throw new InvalidOperationException("review must not write command logs"),
                _ => chatClient),
            ["review", "--json", "--workspace", temp.Path],
            output);

        JsonObject json = AssertSingleReviewJsonResult(output);
        Assert.Equal(0, exitCode);
        Assert.True(json.ContainsKey("findingsText"));
        Assert.Equal(string.Empty, json["findingsText"]?.GetValue<string>());
    }

    [Fact]
    public void Review_command_output_json_success_writes_single_result_object()
    {
        using TempDirectory temp = TempDirectory.Create();
        string filePath = InitializeGitRepository(temp.Path);
        File.AppendAllText(filePath, "changed\n");
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "review report")));

        int exitCode = CliCommandFactory.Invoke(
            CliCommandFactory.Create(
                output,
                workspacePath => CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
                (_, _) => throw new InvalidOperationException("review must not write command logs"),
                _ => chatClient),
            ["review", "--output", "json", "--workspace", temp.Path],
            output);

        JsonObject json = AssertSingleReviewJsonResult(output);
        Assert.Equal(0, exitCode);
        Assert.Equal("completed", json["status"]?.GetValue<string>());
        Assert.Equal("review report", json["findingsText"]?.GetValue<string>());
    }

    [Fact]
    public void Review_output_rejects_unknown_value_before_calling_model_or_logger()
    {
        using TempDirectory temp = TempDirectory.Create();
        string filePath = InitializeGitRepository(temp.Path);
        File.AppendAllText(filePath, "changed\n");
        using StringWriter output = new();
        bool loggerInvoked = false;
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "review report")));

        int exitCode = CliCommandFactory.Invoke(
            CliCommandFactory.Create(
                output,
                workspacePath => CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
                (_, _) => loggerInvoked = true,
                _ => chatClient),
            ["review", "--output", "banana", "--workspace", temp.Path],
            output);

        Assert.Equal(2, exitCode);
        Assert.False(loggerInvoked);
        Assert.Null(chatClient.LastNonStreamingPrompt);
        Assert.Null(chatClient.LastStreamingPrompt);
        Assert.Contains("Invalid value for --output. Allowed values are text and json.", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Review_command_returns_model_failure_without_streaming()
    {
        using TempDirectory temp = TempDirectory.Create();
        string filePath = InitializeGitRepository(temp.Path);
        File.AppendAllText(filePath, "changed\n");
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Failure(new ModelError(
            Provider: "openai",
            Operation: "responses.create",
            StatusCode: null,
            LocalErrorCode: "missing-openai-api-key",
            SafeMessage: "OpenAI API key is missing.",
            Retryable: false)));

        int exitCode = CliCommandFactory.Invoke(
            CliCommandFactory.Create(
                output,
                workspacePath => CreateSnapshot(workspacePath, apiKey: null, apiKeySource: "missing", model: "gpt-test"),
                (_, _) => { },
                _ => chatClient),
            ["review", "--workspace", temp.Path],
            output);

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.NotNull(chatClient.LastNonStreamingPrompt);
        Assert.Null(chatClient.LastStreamingPrompt);
        Assert.Contains("status: failed", text, StringComparison.Ordinal);
        Assert.Contains("missing-openai-api-key", text, StringComparison.Ordinal);
        Assert.Contains("OpenAI API key is missing.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Review_command_json_model_failure_writes_safe_failure_without_streaming_or_logging()
    {
        using TempDirectory temp = TempDirectory.Create();
        string filePath = InitializeGitRepository(temp.Path);
        File.AppendAllText(filePath, "changed\n");
        using StringWriter output = new();
        bool loggerInvoked = false;
        FakeChatModelClient chatClient = new(ChatModelResult.Failure(new ModelError(
            Provider: "openai",
            Operation: "responses.create",
            StatusCode: 429,
            LocalErrorCode: "rate-limited",
            SafeMessage: "OpenAI request was rate limited.",
            Retryable: true)));

        int exitCode = CliCommandFactory.Invoke(
            CliCommandFactory.Create(
                output,
                workspacePath => CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
                (_, _) => loggerInvoked = true,
                _ => chatClient),
            ["review", "--json", "--workspace", temp.Path],
            output);

        JsonObject json = AssertSingleReviewJsonResult(output);
        Assert.Equal(1, exitCode);
        Assert.False(loggerInvoked);
        Assert.NotNull(chatClient.LastNonStreamingPrompt);
        Assert.Null(chatClient.LastStreamingPrompt);
        Assert.Equal("failed", json["status"]?.GetValue<string>());
        Assert.Equal("rate-limited", json["errorCode"]?.GetValue<string>());
        Assert.Equal("OpenAI request was rate limited.", json["safeMessage"]?.GetValue<string>());
        Assert.Equal("openai", json["provider"]?.GetValue<string>());
        Assert.Equal("responses.create", json["operation"]?.GetValue<string>());
        Assert.Equal(429, json["statusCode"]?.GetValue<int>());
        Assert.True(json["retryable"]?.GetValue<bool>());
    }

    [Fact]
    public void Review_command_returns_git_diff_failure_without_calling_model()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "review report")));

        int exitCode = CliCommandFactory.Invoke(
            CliCommandFactory.Create(
                output,
                workspacePath => CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
                (_, _) => { },
                _ => chatClient),
            ["review", "--workspace", temp.Path],
            output);

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Null(chatClient.LastNonStreamingPrompt);
        Assert.Null(chatClient.LastStreamingPrompt);
        Assert.Contains("status: failed", text, StringComparison.Ordinal);
        Assert.Contains("errorCode: git-not-repository", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Review_command_json_git_diff_failure_writes_single_result_object_without_calling_model()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "review report")));

        int exitCode = CliCommandFactory.Invoke(
            CliCommandFactory.Create(
                output,
                workspacePath => CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
                (_, _) => throw new InvalidOperationException("review must not write command logs"),
                _ => chatClient),
            ["review", "--output", "json", "--workspace", temp.Path],
            output);

        JsonObject json = AssertSingleReviewJsonResult(output);
        Assert.Equal(1, exitCode);
        Assert.Null(chatClient.LastNonStreamingPrompt);
        Assert.Null(chatClient.LastStreamingPrompt);
        Assert.Equal("failed", json["status"]?.GetValue<string>());
        Assert.Equal("git-not-repository", json["errorCode"]?.GetValue<string>());
        Assert.False(string.IsNullOrWhiteSpace(json["safeMessage"]?.GetValue<string>()));
    }

    [Fact]
    public void Diff_temp_repo_commit_ignores_configured_prepare_commit_msg_hook()
    {
        using TempDirectory temp = TempDirectory.Create();
        string hooksPath = Path.Combine(temp.Path, "failing-hooks");
        WriteFailingHook(hooksPath, "prepare-commit-msg");

        string filePath = InitializeGitRepository(temp.Path, hooksPath);

        Assert.True(File.Exists(filePath));
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
        Assert.Contains("baseUrl: https://api.openai.com/v1", output.ToString());
    }

    [Fact]
    public void Config_list_command_writes_non_secret_config_report()
    {
        using StringWriter output = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: "sk-test-secret",
            apiKeySource: "OPENAI_API_KEY",
            model: "gpt-workspace",
            baseUrl: "https://gateway.example.test/v1",
            baseUrlSource: "workspace config",
            disabledTools: new HashSet<string>(StringComparer.Ordinal)
            {
                "workspace.run_shell"
            });

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["config", "list"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Contains("C# AI CLI effective configuration", output.ToString());
        Assert.Contains("model: gpt-workspace", output.ToString());
        Assert.Contains("modelSource: workspace config", output.ToString());
        Assert.Contains("baseUrl: https://gateway.example.test/v1", output.ToString());
        Assert.Contains("baseUrlSource: workspace config", output.ToString());
        Assert.Contains("agentBackend: direct", output.ToString());
        Assert.Contains("agentBackendSource: default", output.ToString());
        Assert.Contains("disabledTools: workspace.run_shell", output.ToString());
        Assert.Contains("apiKey: present", output.ToString());
        Assert.Contains("apiKeySource: OPENAI_API_KEY", output.ToString());
        Assert.DoesNotContain("sk-test-secret", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Config_list_command_accepts_verbose_and_writes_safe_diagnostics()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: temp.Path,
            apiKey: "sk-test-secret",
            apiKeySource: "OPENAI_API_KEY",
            model: "gpt-workspace",
            baseUrl: "https://gateway.example.test/v1",
            baseUrlSource: "workspace config");

        int exitCode = CliCommandFactory
            .Create(
                output,
                _ => snapshot,
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => new FakeConversationStore(),
                () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                (_, _, _) => throw new InvalidOperationException("config list must not create an exec runner"))
            .Parse(["config", "list", "--verbose"])
            .Invoke();

        string text = output.ToString();
        string[] lines = text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(0, exitCode);
        Assert.Contains("C# AI CLI verbose diagnostics", text);
        Assert.Contains("commandName: config list", text);
        Assert.Contains(lines, line => line.StartsWith("commandId: ", StringComparison.Ordinal) && line.Length > "commandId: ".Length);
        Assert.Contains(lines, line => line.StartsWith("sessionId: ", StringComparison.Ordinal) && line.Length > "sessionId: ".Length);
        Assert.Contains("timestampUtc: 2024-01-01T00:00:00.0000000Z", text);
        Assert.Contains($"workspace: {temp.Path}", text);
        Assert.Contains("workspaceStatus: ready", text);
        Assert.Contains("model: gpt-workspace", text);
        Assert.Contains("modelSource: workspace config", text);
        Assert.Contains("baseUrl: https://gateway.example.test/v1", text);
        Assert.Contains("baseUrlSource: workspace config", text);
        Assert.Contains("apiKey: present", text);
        Assert.Contains("apiKeySource: OPENAI_API_KEY", text);
        Assert.Contains("C# AI CLI effective configuration", text);
        Assert.DoesNotContain("sk-test-secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Tools_call_verbose_redacts_secret_like_values_from_diagnostics()
    {
        using StringWriter output = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "gpt-workspace",
            baseUrl: "https://gateway.example.test/v1\napiKey: sk-command-secret",
            baseUrlSource: "workspace config");

        int exitCode = CliCommandFactory
            .Create(
                output,
                _ => snapshot,
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => new FakeConversationStore(),
                () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                (_, _, _) => throw new InvalidOperationException("tools call must not create an exec runner"))
            .Parse(["tools", "call", "missing.tool", "{}", "--verbose"])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("C# AI CLI verbose diagnostics", text);
        Assert.DoesNotContain("sk-command-secret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("apiKey: sk-command-secret", text, StringComparison.Ordinal);
        Assert.Contains("apiKey: missing", text);
    }

    [Fact]
    public void Config_set_model_creates_user_config_json()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: userConfigPath);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["config", "set", "model", "gpt-test"])
            .Invoke();

        Assert.Equal(0, exitCode);
        JsonObject json = ReadJsonObject(userConfigPath);
        Assert.Equal("gpt-test", json["model"]?.GetValue<string>());
        Assert.Contains("status: updated", output.ToString());
        Assert.Contains("key: model", output.ToString());
        Assert.Contains("scope: user", output.ToString());
        Assert.Contains($"path: {userConfigPath}", output.ToString());
    }

    [Fact]
    public void Config_set_preserves_existing_user_config_fields()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(userConfigPath)!);
        File.WriteAllText(userConfigPath, """
        {
          "baseUrl": "https://gateway.example.test/v1",
          "customSetting": 42,
          "disabledTools": [
            "workspace.run_shell"
          ]
        }
        """);
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: userConfigPath);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["config", "set", "model", "gpt-test"])
            .Invoke();

        Assert.Equal(0, exitCode);
        JsonObject json = ReadJsonObject(userConfigPath);
        Assert.Equal("gpt-test", json["model"]?.GetValue<string>());
        Assert.Equal("https://gateway.example.test/v1", json["baseUrl"]?.GetValue<string>());
        Assert.Equal(42, json["customSetting"]?.GetValue<int>());
        Assert.Equal("workspace.run_shell", json["disabledTools"]?[0]?.GetValue<string>());
    }

    [Fact]
    public void Config_set_base_url_validates_and_writes_normalized_value()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: userConfigPath);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["config", "set", "baseUrl", "  https://gateway.example.test/v1  "])
            .Invoke();

        Assert.Equal(0, exitCode);
        JsonObject json = ReadJsonObject(userConfigPath);
        Assert.Equal("https://gateway.example.test/v1", json["baseUrl"]?.GetValue<string>());
        Assert.Contains("status: updated", output.ToString());
        Assert.Contains("key: baseUrl", output.ToString());
        Assert.Contains("scope: user", output.ToString());
        Assert.Contains($"path: {userConfigPath}", output.ToString());
    }

    [Fact]
    public void Config_set_base_url_rejects_invalid_value_without_writing()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: userConfigPath);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["config", "set", "baseUrl", "file:///tmp/gateway"])
            .Invoke();

        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(userConfigPath));
        Assert.Contains("status: failed", output.ToString());
        Assert.Contains("errorCode: invalid-base-url", output.ToString());
    }

    [Fact]
    public void Config_set_base_url_rejects_secret_like_invalid_value_without_printing_value()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        string secretLikeInvalidBaseUrl = "https://gateway.example.test/v1?api-key=sk-command-secret";
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: userConfigPath);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["config", "set", "baseUrl", secretLikeInvalidBaseUrl])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(userConfigPath));
        Assert.Contains("status: failed", text);
        Assert.Contains("errorCode: invalid-base-url", text);
        Assert.DoesNotContain(secretLikeInvalidBaseUrl, text, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-command-secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Config_set_base_url_rejects_newline_secret_like_value_without_printing_value()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        string secretLikeInvalidBaseUrl = "https://gateway.example.test/v1\napiKey: sk-command-secret";
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: userConfigPath);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["config", "set", "baseUrl", secretLikeInvalidBaseUrl])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(userConfigPath));
        Assert.Contains("status: failed", text);
        Assert.Contains("errorCode: invalid-base-url", text);
        Assert.DoesNotContain(secretLikeInvalidBaseUrl, text, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-command-secret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("apiKey:", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Config_set_api_key_does_not_print_secret_value()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: userConfigPath);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["config", "set", "ApiKey", "sk-test-secret"])
            .Invoke();

        Assert.Equal(0, exitCode);
        JsonObject json = ReadJsonObject(userConfigPath);
        Assert.Equal("sk-test-secret", json["apiKey"]?.GetValue<string>());
        Assert.DoesNotContain("sk-test-secret", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Config_set_api_key_removes_case_insensitive_duplicate_key_without_printing_secrets()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(userConfigPath)!);
        File.WriteAllText(userConfigPath, """
        {
          "ApiKey": "old-secret",
          "model": "gpt-existing"
        }
        """);
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: userConfigPath);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["config", "set", "apiKey", "new-secret"])
            .Invoke();

        Assert.Equal(0, exitCode);
        JsonObject json = ReadJsonObject(userConfigPath);
        Assert.Equal("new-secret", json["apiKey"]?.GetValue<string>());
        Assert.False(json.ContainsKey("ApiKey"));
        Assert.Equal("gpt-existing", json["model"]?.GetValue<string>());
        Assert.DoesNotContain("old-secret", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("new-secret", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Config_set_agent_backend_writes_normalized_framework_alias()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: userConfigPath);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["config", "set", "agentBackend", "maf"])
            .Invoke();

        Assert.Equal(0, exitCode);
        JsonObject json = ReadJsonObject(userConfigPath);
        Assert.Equal("framework", json["agentBackend"]?.GetValue<string>());
    }

    [Fact]
    public void Config_set_agent_backend_rejects_invalid_value_without_writing_or_echoing_value()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: userConfigPath);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["config", "set", "agentBackend", "nope"])
            .Invoke();

        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(userConfigPath));
        Assert.Contains("status: failed", output.ToString());
        Assert.Contains("errorCode: invalid-agent-backend", output.ToString());
        Assert.DoesNotContain("nope", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Config_set_unknown_key_returns_nonzero_without_creating_file()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: userConfigPath);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["config", "set", "temperature", "0.2"])
            .Invoke();

        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(userConfigPath));
        Assert.Contains("status: failed", output.ToString());
        Assert.Contains("errorCode: unknown-config-key", output.ToString());
    }

    [Fact]
    public void Config_set_unknown_secret_like_key_returns_nonzero_without_printing_key()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        string secretLikeKey = "apiKey: sk-command-secret";
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: userConfigPath);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["config", "set", secretLikeKey, "0.2"])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(userConfigPath));
        Assert.Contains("status: failed", text);
        Assert.Contains("errorCode: unknown-config-key", text);
        Assert.Contains("Supported scalar keys are: model, baseUrl, agentBackend, apiKey.", text);
        Assert.DoesNotContain(secretLikeKey, text, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-command-secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Config_set_invalid_existing_json_returns_nonzero_without_overwriting()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(userConfigPath)!);
        File.WriteAllText(userConfigPath, "{not json");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: userConfigPath);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["config", "set", "model", "gpt-test"])
            .Invoke();

        Assert.Equal(1, exitCode);
        Assert.Equal("{not json", File.ReadAllText(userConfigPath));
        Assert.Contains("status: failed", output.ToString());
        Assert.Contains("errorCode: invalid-config-file", output.ToString());
    }

    [Fact]
    public void Config_set_json_array_returns_invalid_config_file_without_overwriting()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(userConfigPath)!);
        File.WriteAllText(userConfigPath, "[]");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: userConfigPath);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["config", "set", "model", "gpt-test"])
            .Invoke();

        Assert.Equal(1, exitCode);
        Assert.Equal("[]", File.ReadAllText(userConfigPath));
        Assert.Contains("status: failed", output.ToString());
        Assert.Contains("errorCode: invalid-config-file", output.ToString());
    }

    [Fact]
    public void Config_set_parent_path_conflict_returns_write_failure_without_throwing()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string userConfigDirectory = Path.Combine(temp.Path, ".caicli");
        string userConfigPath = Path.Combine(userConfigDirectory, "config.json");
        File.WriteAllText(userConfigDirectory, "not a directory");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: userConfigPath);

        int exitCode = -1;
        Exception? exception = Record.Exception(() =>
        {
            exitCode = CliCommandFactory
                .Create(output, _ => snapshot)
                .Parse(["config", "set", "model", "gpt-test"])
                .Invoke();
        });

        Assert.Null(exception);
        Assert.Equal(1, exitCode);
        Assert.Equal("not a directory", File.ReadAllText(userConfigDirectory));
        Assert.Contains("status: failed", output.ToString());
        Assert.Contains("errorCode: config-write-failed", output.ToString());
    }

    [Fact]
    public void Config_unset_api_key_removes_case_insensitive_matches_without_printing_secrets()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(userConfigPath)!);
        File.WriteAllText(userConfigPath, """
        {
          "apiKey": "old-secret",
          "ApiKey": "older-secret",
          "model": "gpt-existing"
        }
        """);
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: userConfigPath);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["config", "unset", "APIKEY"])
            .Invoke();

        Assert.Equal(0, exitCode);
        JsonObject json = ReadJsonObject(userConfigPath);
        Assert.False(json.ContainsKey("apiKey"));
        Assert.False(json.ContainsKey("ApiKey"));
        Assert.Equal("gpt-existing", json["model"]?.GetValue<string>());
        Assert.Contains("status: updated", output.ToString());
        Assert.Contains("key: apiKey", output.ToString());
        Assert.Contains("scope: user", output.ToString());
        Assert.Contains($"path: {userConfigPath}", output.ToString());
        Assert.DoesNotContain("old-secret", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("older-secret", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Config_unset_base_url_removes_user_config_value_without_printing_secret_like_value()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        string secretLikeBaseUrl = "https://gateway.example.test/v1?api-key=sk-existing-secret";
        Directory.CreateDirectory(Path.GetDirectoryName(userConfigPath)!);
        File.WriteAllText(userConfigPath, $$"""
        {
          "baseUrl": "{{secretLikeBaseUrl}}",
          "model": "gpt-existing"
        }
        """);
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: userConfigPath);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["config", "unset", "BaseURL"])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        JsonObject json = ReadJsonObject(userConfigPath);
        Assert.False(json.ContainsKey("baseUrl"));
        Assert.Equal("gpt-existing", json["model"]?.GetValue<string>());
        Assert.Contains("status: updated", text);
        Assert.Contains("key: baseUrl", text);
        Assert.Contains("scope: user", text);
        Assert.Contains($"path: {userConfigPath}", text);
        Assert.DoesNotContain(secretLikeBaseUrl, text, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-existing-secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Config_unset_missing_config_file_succeeds_unchanged_without_creating_file()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: userConfigPath);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["config", "unset", "model"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.False(File.Exists(userConfigPath));
        Assert.Contains("status: unchanged", output.ToString());
        Assert.Contains("key: model", output.ToString());
        Assert.Contains("scope: user", output.ToString());
    }

    [Fact]
    public void Config_unset_unknown_key_returns_nonzero_without_creating_file()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: userConfigPath);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["config", "unset", "temperature"])
            .Invoke();

        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(userConfigPath));
        Assert.Contains("status: failed", output.ToString());
        Assert.Contains("errorCode: unknown-config-key", output.ToString());
    }

    [Fact]
    public void Config_unset_unknown_secret_like_key_returns_nonzero_without_printing_key()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        string secretLikeKey = "apiKey: sk-command-secret";
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: userConfigPath);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["config", "unset", secretLikeKey])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(userConfigPath));
        Assert.Contains("status: failed", text);
        Assert.Contains("errorCode: unknown-config-key", text);
        Assert.Contains("Supported scalar keys are: model, baseUrl, agentBackend, apiKey.", text);
        Assert.DoesNotContain(secretLikeKey, text, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-command-secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Version_command_writes_version_metadata()
    {
        using StringWriter output = new();

        int exitCode = CliCommandFactory
            .Create(output, CreateSnapshot)
            .Parse(["version"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Contains("caicli ", output.ToString());
        Assert.Contains("target framework: net9.0", output.ToString());
        Assert.Contains("release runtime: win-x64", output.ToString());
    }

    [Fact]
    public void Version_command_without_verbose_does_not_create_snapshot()
    {
        using StringWriter output = new();

        int exitCode = CliCommandFactory
            .Create(output, _ => throw new InvalidOperationException("plain version must not create a snapshot"))
            .Parse(["version"])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("caicli ", text);
        Assert.Contains("target framework: net9.0", text);
        Assert.Contains("release runtime: win-x64", text);
        Assert.DoesNotContain("C# AI CLI verbose diagnostics", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Version_command_accepts_verbose_and_writes_safe_diagnostics()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string? receivedWorkspace = null;
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: temp.Path,
            apiKey: "sk-version-secret",
            apiKeySource: "OPENAI_API_KEY",
            model: "gpt-version",
            baseUrl: "https://gateway.example.test/v1",
            baseUrlSource: "workspace config");

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath =>
                {
                    receivedWorkspace = workspacePath;
                    return snapshot;
                },
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => new FakeConversationStore(),
                () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                (_, _, _) => throw new InvalidOperationException("version must not create an exec runner"))
            .Parse(["version", "--workspace", temp.Path, "--verbose"])
            .Invoke();

        string text = output.ToString();
        string[] lines = text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(0, exitCode);
        Assert.Equal(temp.Path, receivedWorkspace);
        Assert.Contains("C# AI CLI verbose diagnostics", text);
        Assert.Contains("commandName: version", text);
        Assert.Contains(lines, line => line.StartsWith("commandId: ", StringComparison.Ordinal) && line.Length > "commandId: ".Length);
        Assert.Contains(lines, line => line.StartsWith("sessionId: ", StringComparison.Ordinal) && line.Length > "sessionId: ".Length);
        Assert.Contains("timestampUtc: 2024-01-01T00:00:00.0000000Z", text);
        Assert.Contains($"workspace: {temp.Path}", text);
        Assert.Contains("workspaceStatus: ready", text);
        Assert.Contains("logDirectory: ", text);
        Assert.Contains("userConfigPath: ", text);
        Assert.Contains("workspaceConfigPath: ", text);
        Assert.Contains("apiKey: present", text);
        Assert.Contains("apiKeySource: OPENAI_API_KEY", text);
        Assert.Contains("caicli ", text);
        Assert.Contains("target framework: net9.0", text);
        Assert.Contains("release runtime: win-x64", text);
        Assert.DoesNotContain("sk-version-secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Mcp_list_command_writes_mcp_server_report()
    {
        using StringWriter output = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            configSources:
            [
                new CliConfigFileSource(
                    "workspace config",
                    "workspace-config.json",
                    new CliConfigFile
                    {
                        McpServers = new Dictionary<string, McpServerConfig>
                        {
                            ["disabled"] = new()
                            {
                                Enabled = false,
                                Transport = "stdio",
                                Command = "mcp-disabled"
                            }
                        }
                    })
            ]);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["mcp", "list"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Contains("C# AI CLI MCP servers", output.ToString());
        Assert.Contains("server: disabled", output.ToString());
        Assert.Contains("status: inactive", output.ToString());
    }

    [Fact]
    public void Mcp_doctor_command_writes_mcp_diagnostic_report()
    {
        using StringWriter output = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            configSources:
            [
                new CliConfigFileSource(
                    "workspace config",
                    "workspace-config.json",
                    new CliConfigFile
                    {
                        McpServers = new Dictionary<string, McpServerConfig>
                        {
                            ["disabled"] = new()
                            {
                                Enabled = false,
                                Transport = "stdio",
                                Command = "mcp-disabled"
                            }
                        }
                    })
            ]);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["mcp", "doctor"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Contains("C# AI CLI MCP doctor", output.ToString());
        Assert.Contains("server: disabled", output.ToString());
        Assert.Contains("connectionStatus: inactive", output.ToString());
    }

    [Fact]
    public void Workflow_list_command_writes_workflow_profiles()
    {
        using StringWriter output = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            configSources: [CreateWorkflowSource()]);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["workflow", "list"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Contains("C# AI CLI workflows", output.ToString());
        Assert.Contains("profile: cpp", output.ToString());
    }

    [Fact]
    public void Workflow_validate_command_suggests_validation_command_without_running()
    {
        using StringWriter output = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: "cli-root",
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            configSources: [CreateWorkflowSource()]);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["workflow", "validate", "cpp"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Contains("profile: cpp", output.ToString());
        Assert.Contains("validationCommand: dotnet test", output.ToString());
        Assert.Contains("execution: not run", output.ToString());
    }

    [Fact]
    public void Tools_list_json_writes_stable_parseable_tool_metadata()
    {
        using StringWriter output = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            disabledTools: new HashSet<string>(StringComparer.Ordinal)
            {
                "workspace.search_text",
                "workspace.run_shell"
            });

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["tools", "list", "--json"])
            .Invoke();

        Assert.Equal(0, exitCode);
        JsonObject json = Assert.IsType<JsonObject>(JsonNode.Parse(output.ToString()));
        Assert.Equal("tools.list", json["type"]?.GetValue<string>());

        JsonArray tools = Assert.IsType<JsonArray>(json["tools"]);
        List<JsonObject> toolObjects = tools.Select(tool => Assert.IsType<JsonObject>(tool)).ToList();
        string[] toolNames = toolObjects
            .Select(tool => tool["name"]?.GetValue<string>() ?? string.Empty)
            .ToArray();
        Assert.Equal(toolNames.OrderBy(name => name, StringComparer.Ordinal), toolNames);
        Assert.DoesNotContain("workspace.search_text", toolNames);
        Assert.DoesNotContain("workspace.run_shell", toolNames);

        JsonObject readTool = Assert.Single(
            toolObjects,
            tool => tool["name"]?.GetValue<string>() == "workspace.read_text");
        Assert.Equal("Read a UTF-8 text file from the current workspace.", readTool["description"]?.GetValue<string>());
        Assert.Equal("read", readTool["riskLevel"]?.GetValue<string>());
        JsonObject parameters = Assert.IsType<JsonObject>(readTool["parameters"]);
        Assert.Equal("object", parameters["type"]?.GetValue<string>());
        JsonObject properties = Assert.IsType<JsonObject>(parameters["properties"]);
        Assert.True(properties.ContainsKey("path"));

        JsonArray disabledTools = Assert.IsType<JsonArray>(json["disabledTools"]);
        string[] disabledToolNames = disabledTools
            .Select(tool => tool?.GetValue<string>() ?? string.Empty)
            .ToArray();
        Assert.Equal(["workspace.run_shell", "workspace.search_text"], disabledToolNames);
    }

    [Fact]
    public void Tools_list_json_with_verbose_remains_parseable_without_verbose_text()
    {
        using StringWriter output = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: "sk-tools-secret",
            apiKeySource: "OPENAI_API_KEY",
            model: "gpt-test");

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["tools", "list", "--json", "--verbose"])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        JsonObject json = Assert.IsType<JsonObject>(JsonNode.Parse(text));
        Assert.Equal("tools.list", json["type"]?.GetValue<string>());
        Assert.DoesNotContain("C# AI CLI verbose diagnostics", text, StringComparison.Ordinal);
        Assert.DoesNotContain("commandId:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-tools-secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Tools_list_json_omits_disabled_mcp_tools_and_reports_disabled_names()
    {
        using StringWriter output = new();
        using TempDirectory temp = TempDirectory.Create();
        string scriptPath = WriteMcpEchoServerScript(temp.Path);
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: temp.Path,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            configSources:
            [
                new CliConfigFileSource(
                    "user config",
                    "user-config.json",
                    new CliConfigFile
                    {
                        McpServers = new Dictionary<string, McpServerConfig>
                        {
                            ["active"] = CreateMcpEchoServerConfig(scriptPath),
                            ["disabled"] = CreateMcpEchoServerConfig(scriptPath)
                        }
                    })
            ],
            disabledTools: new HashSet<string>(StringComparer.Ordinal)
            {
                "mcp.disabled.echo"
            });

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["tools", "list", "--json"])
            .Invoke();

        Assert.Equal(0, exitCode);
        JsonObject json = Assert.IsType<JsonObject>(JsonNode.Parse(output.ToString()));
        JsonArray tools = Assert.IsType<JsonArray>(json["tools"]);
        List<JsonObject> toolObjects = tools.Select(tool => Assert.IsType<JsonObject>(tool)).ToList();
        string[] toolNames = toolObjects
            .Select(tool => tool["name"]?.GetValue<string>() ?? string.Empty)
            .ToArray();
        Assert.Contains("mcp.active.echo", toolNames);
        Assert.DoesNotContain("mcp.disabled.echo", toolNames);

        JsonObject activeMcpTool = Assert.Single(
            toolObjects,
            tool => tool["name"]?.GetValue<string>() == "mcp.active.echo");
        Assert.Equal("Echo from MCP. (MCP server 'active', tool 'echo'.)", activeMcpTool["description"]?.GetValue<string>());
        Assert.Equal("shell", activeMcpTool["riskLevel"]?.GetValue<string>());
        JsonObject parameters = Assert.IsType<JsonObject>(activeMcpTool["parameters"]);
        Assert.Equal("object", parameters["type"]?.GetValue<string>());
        JsonObject properties = Assert.IsType<JsonObject>(parameters["properties"]);
        Assert.True(properties.ContainsKey("text"));

        JsonArray disabledTools = Assert.IsType<JsonArray>(json["disabledTools"]);
        string disabledToolName = Assert.Single(disabledTools.Select(tool => tool?.GetValue<string>() ?? string.Empty));
        Assert.Equal("mcp.disabled.echo", disabledToolName);
    }

    [Fact]
    public void Tools_list_json_does_not_start_workspace_configured_mcp_stdio_server()
    {
        using StringWriter output = new();
        using FakeMcpStdioServer server = FakeMcpStdioServer.CreateSuccessful();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: server.WorkspacePath,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            configSources:
            [
                new CliConfigFileSource(
                    "workspace config",
                    "workspace-config.json",
                    new CliConfigFile
                    {
                        McpServers = new Dictionary<string, McpServerConfig>
                        {
                            ["workspace"] = server.CreateConfig(timeoutMilliseconds: 10_000)
                        }
                    })
            ]);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["tools", "list", "--json"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.False(server.HasStarted);
        JsonObject json = Assert.IsType<JsonObject>(JsonNode.Parse(output.ToString()));
        JsonArray tools = Assert.IsType<JsonArray>(json["tools"]);
        string[] toolNames = tools
            .Select(tool => Assert.IsType<JsonObject>(tool)["name"]?.GetValue<string>() ?? string.Empty)
            .ToArray();
        Assert.DoesNotContain("mcp.workspace.echo", toolNames);
    }

    [Fact]
    public void Tools_list_prints_enabled_tools_and_disabled_tools()
    {
        using StringWriter output = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            disabledTools: new HashSet<string>(StringComparer.Ordinal)
            {
                "workspace.run_shell"
            });

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["tools", "list"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Contains("workspace.read_text", output.ToString());
        Assert.DoesNotContain("workspace.run_shell: Run", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("disabledTools: workspace.run_shell", output.ToString());
    }

    [Fact]
    public void Tools_call_returns_tool_disabled_when_tool_is_disabled()
    {
        using StringWriter output = new();
        using TempDirectory temp = TempDirectory.Create();
        string argumentsPath = Path.Combine(temp.Path, "arguments.json");
        File.WriteAllText(argumentsPath, """{"command":"dotnet --version"}""");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            disabledTools: new HashSet<string>(StringComparer.Ordinal)
            {
                "workspace.run_shell"
            });

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["tools", "call", "workspace.run_shell", "--arguments-file", argumentsPath])
            .Invoke();

        Assert.Equal(1, exitCode);
        Assert.Contains("errorCode: tool-disabled", output.ToString());
    }

    [Fact]
    public void Tools_call_returns_tool_disabled_when_mcp_tool_is_disabled()
    {
        using StringWriter output = new();
        using TempDirectory temp = TempDirectory.Create();
        string scriptPath = WriteMcpEchoServerScript(temp.Path);
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: temp.Path,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            configSources:
            [
                new CliConfigFileSource(
                    "user config",
                    "user-config.json",
                    new CliConfigFile
                    {
                        McpServers = new Dictionary<string, McpServerConfig>
                        {
                            ["active"] = CreateMcpEchoServerConfig(scriptPath)
                        }
                    })
            ],
            disabledTools: new HashSet<string>(StringComparer.Ordinal)
            {
                "mcp.active.echo"
            });

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["tools", "call", "mcp.active.echo", """{"text":"hello"}"""])
            .Invoke();

        Assert.Equal(1, exitCode);
        Assert.Contains("errorCode: tool-disabled", output.ToString());
    }

    [Fact]
    public void Tools_call_invokes_user_configured_discovered_mcp_tool()
    {
        using StringWriter output = new();
        using TempDirectory temp = TempDirectory.Create();
        string scriptPath = WriteMcpEchoServerScript(temp.Path);
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: temp.Path,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            configSources:
            [
                new CliConfigFileSource(
                    "user config",
                    "user-config.json",
                    new CliConfigFile
                    {
                        McpServers = new Dictionary<string, McpServerConfig>
                        {
                            ["active"] = CreateMcpEchoServerConfig(scriptPath)
                        }
                    })
            ]);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["tools", "call", "--approve", "mcp.active.echo", """{"text":"hello"}"""])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("status: succeeded", text, StringComparison.Ordinal);
        Assert.Contains("approvalStatus: approved", text, StringComparison.Ordinal);
        Assert.Contains("echo: hello", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Tools_call_mcp_startup_enforces_configured_shell_policy_from_snapshot()
    {
        using StringWriter output = new();
        using TempDirectory temp = TempDirectory.Create();
        string markerPath = Path.Combine(temp.Path, "mcp-policy-startup-ran.txt");
        string scriptPath = WriteMcpEchoServerScript(temp.Path, markerPath);
        CliEnvironmentSnapshot baseSnapshot = CreateSnapshot(
            workspacePath: temp.Path,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            configSources:
            [
                new CliConfigFileSource(
                    "user config",
                    "user-config.json",
                    new CliConfigFile
                    {
                        McpServers = new Dictionary<string, McpServerConfig>
                        {
                            ["active"] = CreateMcpEchoServerConfig(scriptPath)
                        }
                    })
            ]);
        CliEnvironmentSnapshot snapshot = baseSnapshot with
        {
            Configuration = baseSnapshot.Configuration with
            {
                ShellPolicy = new ShellPolicyConfiguration(
                    AllowedCommands: [],
                    AllowedCommandsConfigured: false,
                    AllowedCommandsSource: "default",
                    DeniedCommands: [PowerShellPolicyCommandName],
                    MaxTimeoutMilliseconds: null,
                    MaxTimeoutMillisecondsSource: "default")
            }
        };

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["tools", "call", "--approve", "mcp.active.echo", """{"text":"hello"}"""])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("errorCode: mcp-start-failed", text, StringComparison.Ordinal);
        Assert.Contains("blocked by shell policy", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("errorCode: unknown-tool", text, StringComparison.Ordinal);
        Assert.DoesNotContain("echo: hello", text, StringComparison.Ordinal);
        Assert.False(File.Exists(markerPath));
    }

    [Fact]
    public void Tools_call_stdin_reads_arguments_from_injected_reader()
    {
        using TempDirectory temp = TempDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "note.txt"), "hello from stdin");
        using StringWriter output = new();
        using StringReader input = new("""{"path":"note.txt"}""");

        int exitCode = CliCommandFactory
            .Create(output, workspacePath => CreateSnapshot(workspacePath), input)
            .Parse(["tools", "call", "--workspace", temp.Path, "workspace.read_text", "--stdin"])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("status: succeeded", text);
        Assert.Contains("hello from stdin", text);
    }

    [Fact]
    public void Tools_call_stdin_and_arguments_file_conflict_before_logging()
    {
        using TempDirectory temp = TempDirectory.Create();
        string argumentsPath = Path.Combine(temp.Path, "arguments.json");
        File.WriteAllText(argumentsPath, """{"path":"note.txt"}""");
        using StringWriter output = new();
        List<string> loggedCommands = [];
        RootCommand command = CliCommandFactory.Create(
            output,
            workspacePath => CreateSnapshot(workspacePath),
            (commandName, _) => loggedCommands.Add(commandName));

        int exitCode = CliCommandFactory.Invoke(
            command,
            ["tools", "call", "--workspace", temp.Path, "workspace.read_text", "--stdin", "--arguments-file", argumentsPath],
            output);

        string text = output.ToString();
        Assert.Equal(2, exitCode);
        Assert.Empty(loggedCommands);
        Assert.Contains("--stdin cannot be used with --arguments-file.", text);
        Assert.DoesNotContain("status:", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Tools_call_stdin_and_positional_arguments_conflict_before_logging()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        List<string> loggedCommands = [];
        RootCommand command = CliCommandFactory.Create(
            output,
            workspacePath => CreateSnapshot(workspacePath),
            (commandName, _) => loggedCommands.Add(commandName));

        int exitCode = CliCommandFactory.Invoke(
            command,
            ["tools", "call", "--workspace", temp.Path, "workspace.read_text", """{"path":"note.txt"}""", "--stdin"],
            output);

        string text = output.ToString();
        Assert.Equal(2, exitCode);
        Assert.Empty(loggedCommands);
        Assert.Contains("--stdin cannot be used with positional arguments.", text);
        Assert.DoesNotContain("status:", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Tools_call_invalid_stdin_json_reports_invalid_tool_arguments()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        using StringReader input = new("{");

        int exitCode = CliCommandFactory
            .Create(output, workspacePath => CreateSnapshot(workspacePath), input)
            .Parse(["tools", "call", "--workspace", temp.Path, "workspace.read_text", "--stdin"])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("status: failed", text);
        Assert.Contains("errorCode: invalid-tool-arguments", text);
        Assert.Contains("Tool arguments must be valid JSON.", text);
    }

    [Fact]
    public void Tools_call_arguments_file_still_reads_arguments()
    {
        using TempDirectory temp = TempDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "note.txt"), "hello from arguments file");
        string argumentsPath = Path.Combine(temp.Path, "arguments.json");
        File.WriteAllText(argumentsPath, """{"path":"note.txt"}""");
        using StringWriter output = new();

        int exitCode = CliCommandFactory
            .Create(output, workspacePath => CreateSnapshot(workspacePath))
            .Parse(["tools", "call", "--workspace", temp.Path, "workspace.read_text", "--arguments-file", argumentsPath])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("status: succeeded", text);
        Assert.Contains("hello from arguments file", text);
    }

    [Fact]
    public void Tools_call_inline_arguments_still_work()
    {
        using TempDirectory temp = TempDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "note.txt"), "hello from inline arguments");
        using StringWriter output = new();

        int exitCode = CliCommandFactory
            .Create(output, workspacePath => CreateSnapshot(workspacePath))
            .Parse(["tools", "call", "--workspace", temp.Path, "workspace.read_text", """{"path":"note.txt"}"""])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("status: succeeded", text);
        Assert.Contains("hello from inline arguments", text);
    }

    [Fact]
    public void Tools_call_refuses_patch_without_approval()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();

        int exitCode = InvokeToolsCallPatch(temp, output, []);

        Assert.Equal(1, exitCode);
        Assert.Contains("errorCode: approval-denied", output.ToString());
        Assert.Contains("approvalStatus: approval-required", output.ToString());
        Assert.Equal("before", File.ReadAllText(Path.Combine(temp.Path, "note.txt")));
    }

    [Theory]
    [InlineData("on-request", "Approval is required, but this CLI cannot request interactive approval.")]
    [InlineData("on-failure", "Approval after failure is not available because sandbox retry escalation is not implemented.")]
    public void Tools_call_interactive_approval_modes_report_approval_required_for_write_tool(
        string approvalMode,
        string expectedSummary)
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();

        int exitCode = InvokeToolsCallPatch(temp, output, ["--approval", approvalMode]);

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("errorCode: approval-denied", text);
        Assert.Contains("approvalStatus: approval-required", text);
        Assert.Contains(expectedSummary, text);
        Assert.Equal("before", File.ReadAllText(Path.Combine(temp.Path, "note.txt")));
    }

    [Fact]
    public void Tools_call_approval_always_applies_patch()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();

        int exitCode = InvokeToolsCallPatch(temp, output, ["--approval", "always"]);

        Assert.Equal(0, exitCode);
        Assert.Contains("approvalStatus: approved", output.ToString());
        Assert.Equal("after", File.ReadAllText(Path.Combine(temp.Path, "note.txt")));
    }

    [Fact]
    public void Tools_call_approval_never_denies_patch()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();

        int exitCode = InvokeToolsCallPatch(temp, output, ["--approval", "never"]);

        Assert.Equal(1, exitCode);
        Assert.Contains("errorCode: approval-denied", output.ToString());
        Assert.Contains("approvalStatus: denied", output.ToString());
        Assert.Equal("before", File.ReadAllText(Path.Combine(temp.Path, "note.txt")));
    }

    [Fact]
    public void Tools_call_without_approval_refuses_shell_without_executing_command()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string argumentsPath = Path.Combine(temp.Path, "arguments.json");
        string markerPath = Path.Combine(temp.Path, "shell-marker.txt");
        File.WriteAllText(argumentsPath, """{"command":"dotnet --version > shell-marker.txt"}""");

        int exitCode = CliCommandFactory
            .Create(output, workspacePath => CreateSnapshot(workspacePath))
            .Parse(["tools", "call", "--workspace", temp.Path, "workspace.run_shell", "--arguments-file", argumentsPath])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("errorCode: approval-denied", text);
        Assert.Contains("approvalStatus: approval-required", text);
        Assert.False(File.Exists(markerPath));
    }

    [Fact]
    public void Tools_call_approval_always_runs_safe_shell_command()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string argumentsPath = Path.Combine(temp.Path, "arguments.json");
        File.WriteAllText(argumentsPath, """{"command":"dotnet --version","timeoutMilliseconds":10000}""");

        int exitCode = CliCommandFactory
            .Create(output, workspacePath => CreateSnapshot(workspacePath))
            .Parse([
                "tools",
                "call",
                "--workspace",
                temp.Path,
                "--approval",
                "always",
                "workspace.run_shell",
                "--arguments-file",
                argumentsPath
            ])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("status: succeeded", text);
        Assert.Contains("approvalStatus: approved", text);
        Assert.Contains("stdout:", text);
    }

    [Fact]
    public void Tools_call_approval_always_reports_dangerous_shell_denial()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string argumentsPath = Path.Combine(temp.Path, "arguments.json");
        File.WriteAllText(argumentsPath, """{"command":"rm -rf ."}""");

        int exitCode = CliCommandFactory
            .Create(output, workspacePath => CreateSnapshot(workspacePath))
            .Parse([
                "tools",
                "call",
                "--workspace",
                temp.Path,
                "--approval",
                "always",
                "workspace.run_shell",
                "--arguments-file",
                argumentsPath
            ])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("errorCode: approval-denied", text);
        Assert.Contains("approvalStatus: dangerous-shell-denied", text);
        Assert.DoesNotContain("approvalStatus: approved", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Tools_call_shell_enforces_configured_allowlist_from_snapshot()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string argumentsPath = Path.Combine(temp.Path, "arguments.json");
        string markerPath = Path.Combine(temp.Path, "blocked-by-allowlist.txt");
        File.WriteAllText(argumentsPath, """{"command":"dotnet --version > blocked-by-allowlist.txt","timeoutMilliseconds":10000}""");
        ShellPolicyConfiguration shellPolicy = new(
            AllowedCommands: ["dotnet test"],
            AllowedCommandsConfigured: true,
            AllowedCommandsSource: "workspace config",
            DeniedCommands: [],
            MaxTimeoutMilliseconds: null,
            MaxTimeoutMillisecondsSource: "default");
        CliEnvironmentSnapshot snapshot = CreateSnapshotWithShellPolicy(temp.Path, shellPolicy);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse([
                "tools",
                "call",
                "--workspace",
                temp.Path,
                "--approval",
                "always",
                "workspace.run_shell",
                "--arguments-file",
                argumentsPath
            ])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("errorCode: shell-policy-denied", text, StringComparison.Ordinal);
        Assert.Contains("approvalStatus: shell-policy-denied", text, StringComparison.Ordinal);
        Assert.Contains("workspace config", text, StringComparison.Ordinal);
        Assert.False(File.Exists(markerPath));
    }

    [Fact]
    public void Tools_call_shell_enforces_configured_denylist_from_snapshot()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string argumentsPath = Path.Combine(temp.Path, "arguments.json");
        string markerPath = Path.Combine(temp.Path, "blocked-by-denylist.txt");
        File.WriteAllText(argumentsPath, """{"command":"dotnet --version > blocked-by-denylist.txt","timeoutMilliseconds":10000}""");
        ShellPolicyConfiguration shellPolicy = new(
            AllowedCommands: ["dotnet"],
            AllowedCommandsConfigured: true,
            AllowedCommandsSource: "workspace config",
            DeniedCommands: ["--version"],
            MaxTimeoutMilliseconds: null,
            MaxTimeoutMillisecondsSource: "default");
        CliEnvironmentSnapshot snapshot = CreateSnapshotWithShellPolicy(temp.Path, shellPolicy);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse([
                "tools",
                "call",
                "--workspace",
                temp.Path,
                "--approval",
                "always",
                "workspace.run_shell",
                "--arguments-file",
                argumentsPath
            ])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("errorCode: shell-policy-denied", text, StringComparison.Ordinal);
        Assert.Contains("approvalStatus: shell-policy-denied", text, StringComparison.Ordinal);
        Assert.Contains("--version", text, StringComparison.Ordinal);
        Assert.False(File.Exists(markerPath));
    }

    [Fact]
    public void Tools_call_shell_enforces_configured_timeout_max_from_snapshot()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string argumentsPath = Path.Combine(temp.Path, "arguments.json");
        File.WriteAllText(argumentsPath, """{"command":"dotnet --version","timeoutMilliseconds":5000}""");
        ShellPolicyConfiguration shellPolicy = new(
            AllowedCommands: [],
            AllowedCommandsConfigured: false,
            AllowedCommandsSource: "default",
            DeniedCommands: [],
            MaxTimeoutMilliseconds: 1000,
            MaxTimeoutMillisecondsSource: "user config");
        CliEnvironmentSnapshot snapshot = CreateSnapshotWithShellPolicy(temp.Path, shellPolicy);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse([
                "tools",
                "call",
                "--workspace",
                temp.Path,
                "--approval",
                "always",
                "workspace.run_shell",
                "--arguments-file",
                argumentsPath
            ])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("errorCode: shell-policy-denied", text, StringComparison.Ordinal);
        Assert.Contains("timeout", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("5000", text, StringComparison.Ordinal);
        Assert.Contains("1000", text, StringComparison.Ordinal);
        Assert.Contains("user config", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Tools_call_approve_still_applies_patch()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();

        int exitCode = InvokeToolsCallPatch(temp, output, ["--approve"]);

        Assert.Equal(0, exitCode);
        Assert.Contains("approvalStatus: approved", output.ToString());
        Assert.Equal("after", File.ReadAllText(Path.Combine(temp.Path, "note.txt")));
    }

    [Fact]
    public void Tools_call_approval_option_takes_priority_over_approve()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();

        int exitCode = InvokeToolsCallPatch(temp, output, ["--approval", "never", "--approve"]);

        Assert.Equal(1, exitCode);
        Assert.Contains("errorCode: approval-denied", output.ToString());
        Assert.Contains("approvalStatus: denied", output.ToString());
        Assert.Equal("before", File.ReadAllText(Path.Combine(temp.Path, "note.txt")));
    }

    [Fact]
    public void Tools_call_invalid_approval_rejects_before_invoking_tool()
    {
        using TempDirectory temp = TempDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "note.txt"), "before");
        string argumentsPath = Path.Combine(temp.Path, "arguments.json");
        File.WriteAllText(argumentsPath, """{"path":"note.txt","find":"before","replace":"after"}""");
        using StringWriter output = new();
        List<string> loggedCommands = [];
        RootCommand command = CliCommandFactory.Create(
            output,
            workspacePath => CreateSnapshot(workspacePath),
            (commandName, _) => loggedCommands.Add(commandName));

        int exitCode = CliCommandFactory.Invoke(
            command,
            ["tools", "call", "--workspace", temp.Path, "--approval", "maybe", "workspace.apply_patch", "--arguments-file", argumentsPath],
            output);

        string text = output.ToString();
        Assert.Equal(2, exitCode);
        Assert.Empty(loggedCommands);
        Assert.Contains("Invalid value for --approval. Allowed values are never, on-request, on-failure, and always.", text);
        Assert.Equal("before", File.ReadAllText(Path.Combine(temp.Path, "note.txt")));
    }

    [Fact]
    public void Run_create_smoke_note_applies_patch_when_approved()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();

        int exitCode = CliCommandFactory
            .Create(output, workspacePath => CreateSnapshot(workspacePath))
            .Parse(["run", "--workspace", temp.Path, "--approve", "create smoke note"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Contains("status: succeeded", output.ToString());
        Assert.Contains("status: completed", File.ReadAllText(Path.Combine(temp.Path, "caicli-smoke.txt")));
    }

    [Fact]
    public void Run_create_smoke_note_without_approval_returns_failure_without_creating_smoke_note()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();

        int exitCode = CliCommandFactory
            .Create(output, workspacePath => CreateSnapshot(workspacePath))
            .Parse(["run", "--workspace", temp.Path, "create smoke note"])
            .Invoke();

        string smokeNotePath = Path.Combine(temp.Path, "caicli-smoke.txt");
        Assert.Equal(1, exitCode);
        Assert.Contains("errorCode: approval-denied", output.ToString());
        Assert.False(File.Exists(smokeNotePath));
    }

    [Fact]
    public void Run_approve_dangerous_shell_reports_dangerous_approval_status()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();

        int exitCode = CliCommandFactory
            .Create(output, workspacePath => CreateSnapshot(workspacePath))
            .Parse(["run", "--workspace", temp.Path, "--approve", "shell rm -rf ."])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("status: failed", text);
        Assert.Contains("approvalStatus: dangerous-shell-denied", text);
        Assert.DoesNotContain("approvalStatus: approved", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_create_smoke_note_respects_patch_tool_disable()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: temp.Path,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            disabledTools: new HashSet<string>(StringComparer.Ordinal)
            {
                "workspace.apply_patch"
            });

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["run", "--approve", "create smoke note"])
            .Invoke();

        Assert.Equal(1, exitCode);
        Assert.Contains("errorCode: tool-disabled", output.ToString());
        Assert.False(File.Exists(Path.Combine(temp.Path, "caicli-smoke.txt")));
    }

    [Fact]
    public void Run_read_note_text_output_returns_old_shape()
    {
        using TempDirectory temp = TempDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "note.txt"), "hello run");
        using StringWriter output = new();

        int exitCode = CliCommandFactory
            .Create(output, workspacePath => CreateSnapshot(workspacePath))
            .Parse(["run", "--workspace", temp.Path, "read note.txt"])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("status: succeeded", text);
        Assert.Contains("approvalStatus: not-required", text);
        Assert.Contains("summary:", text);
        Assert.Contains("hello run", text);
        Assert.DoesNotContain("event:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("exec.result", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Exec_uses_injected_agent_runner_and_renders_success_text_output()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        FakeAgentRunner agentRunner = new(AgentRunResult.Success(
            "agent completed task",
            [],
            [
                new AgentRunEvent(
                    Type: "final.response",
                    Sequence: 0,
                    Timestamp: DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                    Summary: "agent completed task",
                    Payload: new Dictionary<string, string>
                    {
                        ["kind"] = "final"
                    })
            ]));
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: temp.Path,
            apiKey: "sk-test-secret",
            apiKeySource: "OPENAI_API_KEY",
            model: "gpt-test")
            with
            {
                Instructions = InstructionLoadResult.Loaded("Prefer concise answers.", Path.Combine(temp.Path, "AICLI.md"))
            };

        int exitCode = CliCommandFactory
            .Create(
                output,
                _ => snapshot,
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => new FakeConversationStore(),
                () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                (_, _, _) => agentRunner)
            .Parse([
                "exec",
                "--workspace",
                temp.Path,
                "--max-turns",
                "3",
                "--max-tool-calls",
                "5",
                "--timeout-seconds",
                "7",
                "summarize workspace"
            ])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Equal("summarize workspace", agentRunner.LastRequest?.Prompt);
        Assert.Same(snapshot.Workspace, agentRunner.LastRequest?.Workspace);
        Assert.Equal("Prefer concise answers.", agentRunner.LastRequest?.Instructions);
        Assert.NotNull(agentRunner.LastRequest?.TaskContext);
        Assert.Equal(temp.Path, agentRunner.LastRequest?.TaskContext?.WorkspaceRoot);
        Assert.Equal("Prefer concise answers.", agentRunner.LastRequest?.TaskContext?.Instructions);
        Assert.Single(agentRunner.LastRequest?.TaskContext?.InstructionSources ?? []);
        Assert.False(agentRunner.LastRequest?.TaskContext?.Git.StatusSucceeded);
        Assert.False(agentRunner.LastRequest?.TaskContext?.Git.DiffSucceeded);
        Assert.Equal(3, agentRunner.LastRequest?.Limits?.MaxSteps);
        Assert.Equal(3, agentRunner.LastRequest?.Limits?.MaxTurns);
        Assert.Equal(5, agentRunner.LastRequest?.Limits?.MaxToolCalls);
        Assert.Equal(TimeSpan.FromSeconds(7), agentRunner.LastRequest?.Limits?.OverallTimeout);
        Assert.Equal(TimeSpan.FromSeconds(7), agentRunner.LastRequest?.Limits?.ModelCallTimeout);
        Assert.Contains("event: final.response", text);
        Assert.Contains("result: success", text);
        Assert.Contains("agent completed task", text);
        Assert.Contains("payload.kind=final", text);
        Assert.DoesNotContain("sk-test-secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Exec_command_passes_cwd_to_snapshot_provider_and_merged_instructions_to_agent_request()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string cwd = Path.Combine("src", "app");
        string? receivedWorkspace = null;
        string? receivedCwd = null;
        FakeAgentRunner agentRunner = new(AgentRunResult.Success("agent completed task", []));
        RootCommand command = CliCommandFactory.Create(
            output,
            (workspacePath, instructionTargetPath) =>
            {
                receivedWorkspace = workspacePath;
                receivedCwd = instructionTargetPath;
                string workspaceRoot = workspacePath ?? temp.Path;
                return CreateSnapshot(
                    workspacePath,
                    apiKey: "sk-test-secret",
                    apiKeySource: "OPENAI_API_KEY",
                    model: "gpt-test")
                    with
                    {
                        Instructions = InstructionLoadResult.Loaded(
                            "Root rules\n\nApp rules",
                            [
                                new InstructionSource(Path.Combine(workspaceRoot, "AICLI.md"), 0),
                                new InstructionSource(Path.Combine(workspaceRoot, "src", "app", "AICLI.md"), 1)
                            ])
                    };
            },
            (_, _) => { },
            _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => new FakeConversationStore(),
            () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
            (_, _, _) => agentRunner);

        int exitCode = CliCommandFactory.Invoke(
            command,
            ["exec", "--workspace", temp.Path, "--cwd", cwd, "summarize workspace"],
            output);

        Assert.Equal(0, exitCode);
        Assert.Equal(temp.Path, receivedWorkspace);
        Assert.Equal(cwd, receivedCwd);
        Assert.Equal(temp.Path, agentRunner.LastRequest?.Workspace.RootPath);
        Assert.Equal("Root rules\n\nApp rules", agentRunner.LastRequest?.Instructions);
        Assert.NotNull(agentRunner.LastRequest?.TaskContext);
        Assert.Equal("Root rules\n\nApp rules", agentRunner.LastRequest?.TaskContext?.Instructions);
        Assert.Equal(2, agentRunner.LastRequest?.TaskContext?.InstructionSources.Count);
        Assert.EndsWith(Path.Combine("src", "app"), agentRunner.LastRequest?.TaskContext?.CurrentDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Root rules", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("sk-test-secret", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Exec_command_without_cwd_keeps_legacy_snapshot_provider_compatibility()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string? receivedWorkspace = null;
        FakeAgentRunner agentRunner = new(AgentRunResult.Success("agent completed task", []));

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath =>
                {
                    receivedWorkspace = workspacePath;
                    return CreateSnapshot(
                        workspacePath,
                        apiKey: "sk-test-secret",
                        apiKeySource: "OPENAI_API_KEY",
                        model: "gpt-test");
                },
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => new FakeConversationStore(),
                () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                (_, _, _) => agentRunner)
            .Parse(["exec", "--workspace", temp.Path, "summarize workspace"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal(temp.Path, receivedWorkspace);
        Assert.Null(agentRunner.LastRequest?.Instructions);
        Assert.NotNull(agentRunner.LastRequest?.TaskContext);
        Assert.Null(agentRunner.LastRequest?.TaskContext?.Instructions);
    }

    [Fact]
    public void Exec_uses_injected_agent_runner_and_renders_success_json_output()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        FakeAgentRunner agentRunner = new(AgentRunResult.Success(
            "json agent summary",
            [],
            [
                new AgentRunEvent(
                    Type: "model.turn",
                    Sequence: 0,
                    Timestamp: DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                    Summary: "model planned",
                    Payload: new Dictionary<string, string>
                    {
                        ["toolCallCount"] = "0"
                    })
            ]));

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(
                    workspacePath,
                    apiKey: "sk-test-secret",
                    apiKeySource: "OPENAI_API_KEY",
                    model: "gpt-test"),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => new FakeConversationStore(),
                () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                (_, _, _) => agentRunner)
            .Parse(["exec", "--json", "--workspace", temp.Path, "summarize workspace"])
            .Invoke();

        string[] lines = output.ToString()
            .TrimEnd()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(0, exitCode);
        Assert.Equal(4, lines.Length);
        foreach (string line in lines)
        {
            JsonNode? node = JsonNode.Parse(line);
            Assert.NotNull(node);
        }

        JsonObject modelTurn = Assert.IsType<JsonObject>(JsonNode.Parse(lines[0]));
        JsonObject reviewGate = Assert.IsType<JsonObject>(JsonNode.Parse(lines[1]));
        JsonObject taskReport = Assert.IsType<JsonObject>(JsonNode.Parse(lines[2]));
        JsonObject result = Assert.IsType<JsonObject>(JsonNode.Parse(lines[^1]));
        Assert.Equal("model.turn", modelTurn["type"]?.GetValue<string>());
        Assert.Equal("0", modelTurn["payload"]?["toolCallCount"]?.GetValue<string>());
        Assert.Equal("review.gate", reviewGate["type"]?.GetValue<string>());
        Assert.Equal("taskReport", taskReport["type"]?.GetValue<string>());
        Assert.Equal("exec.result", result["type"]?.GetValue<string>());
        Assert.Equal("json agent summary", result["summary"]?.GetValue<string>());
        Assert.Equal("success", result["payload"]?["status"]?.GetValue<string>());
        Assert.Equal(0, result["payload"]?["exitCode"]?.GetValue<int>());
        Assert.Equal("success", result["payload"]?["taskReport"]?["status"]?.GetValue<string>());
        Assert.DoesNotContain("sk-test-secret", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Exec_trace_session_json_writes_task_report_and_read_only_review_gate()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        FakeConversationStore store = new()
        {
            Transcript = ConversationTranscript.Create(
                "smoke",
                DateTimeOffset.Parse("2024-01-01T00:00:00Z"))
        };
        AgentRunResult agentResult = AgentRunResult.Success(
            "agent completed task",
            [],
            [
                new AgentRunEvent(
                    Type: "final.response",
                    Sequence: 0,
                    Timestamp: DateTimeOffset.Parse("2024-01-01T00:00:01Z"),
                    Summary: "agent completed task",
                    Status: "success")
            ],
            changedFiles:
            [
                new ChangedFileSummary(
                    Path: "src/App.cs",
                    Status: "modified",
                    SourceToolCallId: "call_patch")
            ],
            verificationResults:
            [
                new VerificationResultSummary(
                    Status: "success",
                    Source: "project-instructions",
                    Command: "dotnet test",
                    WorkingDirectory: ".",
                    Succeeded: true,
                    ApprovalStatus: "approved",
                    ErrorCode: null,
                    ExitCode: 0,
                    TimedOut: false,
                    StdoutTruncated: false,
                    StderrTruncated: false,
                    Stdout: "passed",
                    Stderr: "",
                    Summary: "Shell command completed.")
            ]);
        FakeAgentRunner agentRunner = new(agentResult);
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: temp.Path,
            apiKey: "sk-test-secret",
            apiKeySource: "OPENAI_API_KEY",
            model: "gpt-test");

        int exitCode = CliCommandFactory
            .Create(
                output,
                _ => snapshot,
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => store,
                () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                (_, _, _) => agentRunner)
            .Parse(["exec", "--json", "--trace", "--session", "smoke", "--workspace", temp.Path, "summarize workspace"])
            .Invoke();

        string[] outputLines = output.ToString()
            .TrimEnd()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        JsonObject reviewGate = Assert.IsType<JsonObject>(JsonNode.Parse(outputLines[1]));
        JsonObject taskReportEvent = Assert.IsType<JsonObject>(JsonNode.Parse(outputLines[2]));
        JsonObject result = Assert.IsType<JsonObject>(JsonNode.Parse(outputLines[^1]));
        JsonObject resultTaskReport = Assert.IsType<JsonObject>(result["payload"]?["taskReport"]);

        Assert.Equal(0, exitCode);
        Assert.Equal("review.gate", reviewGate["type"]?.GetValue<string>());
        Assert.Equal("true", reviewGate["payload"]?["readOnly"]?.GetValue<string>());
        Assert.Equal("git.diff", reviewGate["payload"]?["toolName"]?.GetValue<string>());
        Assert.Equal("taskReport", taskReportEvent["type"]?.GetValue<string>());
        Assert.Equal("success", resultTaskReport["status"]?.GetValue<string>());
        JsonArray resultChangedFiles = Assert.IsType<JsonArray>(resultTaskReport["changedFiles"]);
        JsonArray resultCommands = Assert.IsType<JsonArray>(resultTaskReport["commands"]);
        JsonArray resultVerification = Assert.IsType<JsonArray>(resultTaskReport["verification"]);
        Assert.Equal("src/App.cs", resultChangedFiles[0]?["path"]?.GetValue<string>());
        Assert.Equal("dotnet test", resultCommands[0]?["command"]?.GetValue<string>());
        Assert.Equal("success", resultVerification[0]?["status"]?.GetValue<string>());

        ConversationAgentRun savedRun = Assert.Single(store.SavedTranscript?.AgentRuns ?? []);
        Assert.NotNull(savedRun.TaskReport);
        Assert.Equal("src/App.cs", Assert.Single(savedRun.TaskReport!.ChangedFiles).Path);
        Assert.Equal("dotnet test", Assert.Single(savedRun.TaskReport.Commands).Command);

        string tracePath = Path.Combine(temp.Path, ".caicli", "logs", "2024-01-01.trace.log");
        string[] traceLines = File.ReadAllLines(tracePath);
        JsonObject traceResult = Assert.IsType<JsonObject>(JsonNode.Parse(traceLines[^1]));
        JsonObject traceTaskReport = Assert.IsType<JsonObject>(traceResult["payload"]?["taskReport"]);
        Assert.Equal("success", traceTaskReport["status"]?.GetValue<string>());
        JsonArray traceChangedFiles = Assert.IsType<JsonArray>(traceTaskReport["changedFiles"]);
        Assert.Equal("src/App.cs", traceChangedFiles[0]?["path"]?.GetValue<string>());
        Assert.DoesNotContain("sk-test-secret", File.ReadAllText(tracePath), StringComparison.Ordinal);
    }

    [Fact]
    public void Exec_json_output_redacts_secret_bearing_stdout_events()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        FakeAgentRunner agentRunner = new(CreateSecretBearingExecAgentResult("json"));

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(
                    workspacePath,
                    apiKey: "sk-test-secret",
                    apiKeySource: "OPENAI_API_KEY",
                    model: "gpt-test"),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => new FakeConversationStore(),
                () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                (_, _, _) => agentRunner)
            .Parse(["exec", "--json", "--workspace", temp.Path, "summarize workspace"])
            .Invoke();

        string text = output.ToString();
        string[] lines = text.TrimEnd().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(0, exitCode);
        Assert.Equal(4, lines.Length);
        foreach (string line in lines)
        {
            JsonNode? node = JsonNode.Parse(line);
            Assert.NotNull(node);
        }

        JsonObject toolCall = Assert.IsType<JsonObject>(JsonNode.Parse(lines[0]));
        JsonObject payload = Assert.IsType<JsonObject>(toolCall["payload"]);
        JsonObject taskReport = Assert.IsType<JsonObject>(JsonNode.Parse(lines[2]));
        JsonObject result = Assert.IsType<JsonObject>(JsonNode.Parse(lines[^1]));
        Assert.Equal("tool.call", toolCall["type"]?.GetValue<string>());
        Assert.Contains("[redacted]", toolCall["message"]?.GetValue<string>(), StringComparison.Ordinal);
        Assert.Contains("[redacted]", toolCall["summary"]?.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal("workspace.search", payload["toolName"]?.GetValue<string>());
        Assert.Equal("note.txt", payload["path"]?.GetValue<string>());
        Assert.Contains("[redacted]", payload["argumentsJson"]?.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal("[redacted]", payload["apiKey"]?.GetValue<string>());
        Assert.Equal("[redacted]", payload["password"]?.GetValue<string>());
        Assert.Equal("[redacted]", payload["authorization"]?.GetValue<string>());
        Assert.Equal("[redacted]", payload["secretKey"]?.GetValue<string>());
        Assert.Equal("[redacted]", payload["privateKey"]?.GetValue<string>());
        Assert.Equal("taskReport", taskReport["type"]?.GetValue<string>());
        Assert.Equal("exec.result", result["type"]?.GetValue<string>());
        Assert.Contains("[redacted]", result["summary"]?.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal("success", result["payload"]?["status"]?.GetValue<string>());
        AssertDoesNotContainSecrets(text, CreateExecStdoutRawSecrets("json"));
    }

    [Fact]
    public void Exec_text_output_redacts_secret_bearing_stdout_events()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        FakeAgentRunner agentRunner = new(CreateSecretBearingExecAgentResult("text"));

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(
                    workspacePath,
                    apiKey: "sk-test-secret",
                    apiKeySource: "OPENAI_API_KEY",
                    model: "gpt-test"),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => new FakeConversationStore(),
                () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                (_, _, _) => agentRunner)
            .Parse(["exec", "--workspace", temp.Path, "summarize workspace"])
            .Invoke();

        string text = output.ToString();

        Assert.Equal(0, exitCode);
        Assert.Contains("event: tool.call", text, StringComparison.Ordinal);
        Assert.Contains("message=message password=[redacted] authorization=Bearer [redacted]", text, StringComparison.Ordinal);
        Assert.Contains("summary=summary apiKey=[redacted]", text, StringComparison.Ordinal);
        Assert.Contains("payload.toolName=workspace.search", text, StringComparison.Ordinal);
        Assert.Contains("payload.path=note.txt", text, StringComparison.Ordinal);
        Assert.Contains("payload.argumentsJson=", text, StringComparison.Ordinal);
        Assert.Contains("[redacted]", text, StringComparison.Ordinal);
        Assert.Contains("result: success exitCode=0 summary=result apiKey=[redacted]", text, StringComparison.Ordinal);
        AssertDoesNotContainSecrets(text, CreateExecStdoutRawSecrets("text"));
    }

    [Fact]
    public void Exec_output_json_with_verbose_remains_valid_ndjson_without_verbose_text()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        FakeAgentRunner agentRunner = new(AgentRunResult.Success(
            "json agent summary",
            [],
            [
                new AgentRunEvent(
                    Type: "model.turn",
                    Sequence: 0,
                    Timestamp: DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                    Summary: "model planned")
            ]));

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(
                    workspacePath,
                    apiKey: "sk-test-secret",
                    apiKeySource: "OPENAI_API_KEY",
                    model: "gpt-test"),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => new FakeConversationStore(),
                () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                (_, _, _) => agentRunner)
            .Parse(["exec", "--output", "json", "--verbose", "--workspace", temp.Path, "summarize workspace"])
            .Invoke();

        string text = output.ToString();
        string[] lines = text
            .TrimEnd()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(0, exitCode);
        Assert.Equal(4, lines.Length);
        foreach (string line in lines)
        {
            JsonNode? node = JsonNode.Parse(line);
            Assert.NotNull(node);
        }

        JsonObject result = Assert.IsType<JsonObject>(JsonNode.Parse(lines[^1]));
        Assert.Equal("exec.result", result["type"]?.GetValue<string>());
        Assert.Equal("json agent summary", result["summary"]?.GetValue<string>());
        Assert.DoesNotContain("C# AI CLI verbose diagnostics", text, StringComparison.Ordinal);
        Assert.DoesNotContain("commandId:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-test-secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Exec_trace_output_json_remains_valid_ndjson_and_writes_ordered_trace_events()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        FakeAgentRunner agentRunner = new(AgentRunResult.Success(
            "json trace summary",
            [],
            [
                new AgentRunEvent(
                    Type: "model.turn",
                    Sequence: 0,
                    Timestamp: DateTimeOffset.Parse("2024-01-01T00:00:01Z"),
                    Summary: "model planned",
                    Payload: new Dictionary<string, string>
                    {
                        ["toolCallCount"] = "1"
                    },
                    Status: "success",
                    DurationMs: 25),
                new AgentRunEvent(
                    Type: "tool.completed",
                    Sequence: 1,
                    Timestamp: DateTimeOffset.Parse("2024-01-01T00:00:02Z"),
                    Summary: "tool completed",
                    Payload: new Dictionary<string, string>
                    {
                        ["toolName"] = "workspace.search"
                    },
                    ApprovalStatus: "approved",
                    ApprovalDurationMs: 7,
                    Status: "success",
                    DurationMs: 11)
            ]));

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(
                    workspacePath,
                    apiKey: "sk-test-secret",
                    apiKeySource: "OPENAI_API_KEY",
                    model: "gpt-test"),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => new FakeConversationStore(),
                () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                (_, _, _) => agentRunner)
            .Parse(["exec", "--trace", "--output", "json", "--workspace", temp.Path, "summarize workspace"])
            .Invoke();

        string[] outputLines = output.ToString()
            .TrimEnd()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(0, exitCode);
        Assert.Equal(5, outputLines.Length);
        foreach (string line in outputLines)
        {
            JsonNode? node = JsonNode.Parse(line);
            Assert.NotNull(node);
        }

        string tracePath = Path.Combine(temp.Path, ".caicli", "logs", "2024-01-01.trace.log");
        string[] traceLines = File.ReadAllLines(tracePath);
        JsonObject modelTurn = Assert.IsType<JsonObject>(JsonNode.Parse(traceLines[0]));
        JsonObject toolCompleted = Assert.IsType<JsonObject>(JsonNode.Parse(traceLines[1]));
        JsonObject reviewGate = Assert.IsType<JsonObject>(JsonNode.Parse(traceLines[2]));
        JsonObject taskReport = Assert.IsType<JsonObject>(JsonNode.Parse(traceLines[3]));
        JsonObject result = Assert.IsType<JsonObject>(JsonNode.Parse(traceLines[^1]));

        Assert.Equal("model.turn", modelTurn["type"]?.GetValue<string>());
        Assert.Equal("tool.completed", toolCompleted["type"]?.GetValue<string>());
        Assert.Equal("review.gate", reviewGate["type"]?.GetValue<string>());
        Assert.Equal("taskReport", taskReport["type"]?.GetValue<string>());
        Assert.Equal("exec.result", result["type"]?.GetValue<string>());
        Assert.Equal("exec", modelTurn["command"]?.GetValue<string>());
        Assert.False(string.IsNullOrWhiteSpace(modelTurn["commandId"]?.GetValue<string>()));
        Assert.False(string.IsNullOrWhiteSpace(modelTurn["sessionId"]?.GetValue<string>()));
        Assert.Equal(temp.Path, modelTurn["workspace"]?.GetValue<string>());
        Assert.Equal("success", modelTurn["status"]?.GetValue<string>());
        Assert.Equal(25, modelTurn["durationMs"]?.GetValue<long>());
        Assert.Equal("approved", toolCompleted["approvalStatus"]?.GetValue<string>());
        Assert.Equal(7, toolCompleted["approvalDurationMs"]?.GetValue<long>());
        Assert.Equal("json trace summary", result["summary"]?.GetValue<string>());
        Assert.Equal("success", result["status"]?.GetValue<string>());
        Assert.Equal("success", result["payload"]?["taskReport"]?["status"]?.GetValue<string>());
        Assert.DoesNotContain("sk-test-secret", File.ReadAllText(tracePath), StringComparison.Ordinal);
    }

    [Fact]
    public void Exec_trace_can_be_enabled_with_environment_variable()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        FakeAgentRunner agentRunner = new(AgentRunResult.Success("environment trace summary", []));

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(
                    workspacePath,
                    apiKey: "sk-test-secret",
                    apiKeySource: "OPENAI_API_KEY",
                    model: "gpt-test"),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => new FakeConversationStore(),
                () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                (_, _, _) => agentRunner,
                name => string.Equals(name, "CAICLI_TRACE", StringComparison.Ordinal) ? "1" : null)
            .Parse(["exec", "--output", "json", "--workspace", temp.Path, "summarize workspace"])
            .Invoke();

        string tracePath = Path.Combine(temp.Path, ".caicli", "logs", "2024-01-01.trace.log");
        string[] outputLines = output.ToString()
            .TrimEnd()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        JsonObject result = Assert.IsType<JsonObject>(JsonNode.Parse(outputLines[^1]));

        Assert.Equal(0, exitCode);
        Assert.Equal("exec.result", result["type"]?.GetValue<string>());
        Assert.True(File.Exists(tracePath));
        Assert.Contains("environment trace summary", File.ReadAllText(tracePath), StringComparison.Ordinal);
    }

    [Fact]
    public void Exec_without_trace_does_not_create_trace_file()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        FakeAgentRunner agentRunner = new(AgentRunResult.Success("no trace summary", []));

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(
                    workspacePath,
                    apiKey: "sk-test-secret",
                    apiKeySource: "OPENAI_API_KEY",
                    model: "gpt-test"),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => new FakeConversationStore(),
                () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                (_, _, _) => agentRunner,
                _ => null)
            .Parse(["exec", "--output", "json", "--workspace", temp.Path, "summarize workspace"])
            .Invoke();

        string logDirectory = Path.Combine(temp.Path, ".caicli", "logs");

        Assert.Equal(0, exitCode);
        Assert.False(Directory.Exists(logDirectory) &&
            Directory.EnumerateFiles(logDirectory, "*.trace.log").Any());
    }

    [Fact]
    public void Exec_trace_session_and_resume_conflict_writes_trace_failure_without_polluting_json_output()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        FakeAgentRunner agentRunner = new(AgentRunResult.Success("agent completed task", [], []));

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(
                    workspacePath,
                    apiKey: "sk-test-secret",
                    apiKeySource: "OPENAI_API_KEY",
                    model: "gpt-test"),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => new FakeConversationStore(),
                () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                (_, _, _) => agentRunner)
            .Parse(["exec", "--trace", "--output", "json", "--workspace", temp.Path, "--session", "smoke", "--resume", "smoke", "summarize workspace"])
            .Invoke();

        JsonObject outputResult = AssertSingleExecJsonResult(output);
        string[] traceLines = File.ReadAllLines(Path.Combine(temp.Path, ".caicli", "logs", "2024-01-01.trace.log"));
        JsonObject traceResult = Assert.IsType<JsonObject>(JsonNode.Parse(Assert.Single(traceLines)));

        Assert.Equal(1, exitCode);
        Assert.Equal("session-option-conflict", outputResult["errorCode"]?.GetValue<string>());
        Assert.DoesNotContain("commandId", output.ToString(), StringComparison.Ordinal);
        Assert.Equal("exec.result", traceResult["type"]?.GetValue<string>());
        Assert.Equal("failure", traceResult["status"]?.GetValue<string>());
        Assert.Equal("session-option-conflict", traceResult["errorCode"]?.GetValue<string>());
        Assert.Equal(0, traceResult["payload"]?["eventCount"]?.GetValue<int>());
        Assert.Null(agentRunner.LastRequest);
    }

    [Fact]
    public void Exec_trace_resume_missing_writes_trace_failure_without_running_agent()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        FakeAgentRunner agentRunner = new(AgentRunResult.Success("agent completed task", [], []));
        FakeConversationStore store = new()
        {
            TryLoadResult = false
        };

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(
                    workspacePath,
                    apiKey: "sk-test-secret",
                    apiKeySource: "OPENAI_API_KEY",
                    model: "gpt-test"),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => store,
                () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                (_, _, _) => agentRunner)
            .Parse(["exec", "--trace", "--output", "json", "--workspace", temp.Path, "--resume", "missing", "summarize workspace"])
            .Invoke();

        JsonObject outputResult = AssertSingleExecJsonResult(output);
        string[] traceLines = File.ReadAllLines(Path.Combine(temp.Path, ".caicli", "logs", "2024-01-01.trace.log"));
        JsonObject traceResult = Assert.IsType<JsonObject>(JsonNode.Parse(Assert.Single(traceLines)));

        Assert.Equal(1, exitCode);
        Assert.Equal("session-not-found", outputResult["errorCode"]?.GetValue<string>());
        Assert.Equal("missing", store.TryLoadedSessionName?.Value);
        Assert.Null(agentRunner.LastRequest);
        Assert.Equal("exec.result", traceResult["type"]?.GetValue<string>());
        Assert.Equal("failure", traceResult["status"]?.GetValue<string>());
        Assert.Equal("session-not-found", traceResult["errorCode"]?.GetValue<string>());
        Assert.Equal("Session transcript was not found.", traceResult["summary"]?.GetValue<string>());
    }

    [Fact]
    public void Exec_trace_session_save_exception_preserves_agent_events_in_output_and_trace()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        FakeAgentRunner agentRunner = new(AgentRunResult.Success(
            "agent completed task",
            [],
            [
                new AgentRunEvent(
                    Type: "model.turn",
                    Sequence: 0,
                    Timestamp: DateTimeOffset.Parse("2024-01-01T00:00:01Z"),
                    Summary: "model planned",
                    Status: "success",
                    DurationMs: 13)
            ]));
        FakeConversationStore store = new()
        {
            SaveException = new IOException("cannot write C:\\secret\\smoke.transcript.json")
        };

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(
                    workspacePath,
                    apiKey: "sk-test-secret",
                    apiKeySource: "OPENAI_API_KEY",
                    model: "gpt-test"),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => store,
                () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                (_, _, _) => agentRunner)
            .Parse(["exec", "--trace", "--output", "json", "--workspace", temp.Path, "--session", "smoke", "summarize workspace"])
            .Invoke();

        string[] outputLines = output.ToString().TrimEnd().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        JsonObject outputEvent = Assert.IsType<JsonObject>(JsonNode.Parse(outputLines[0]));
        JsonObject outputReviewGate = Assert.IsType<JsonObject>(JsonNode.Parse(outputLines[1]));
        JsonObject outputTaskReport = Assert.IsType<JsonObject>(JsonNode.Parse(outputLines[2]));
        JsonObject outputResult = Assert.IsType<JsonObject>(JsonNode.Parse(outputLines[^1]));
        string[] traceLines = File.ReadAllLines(Path.Combine(temp.Path, ".caicli", "logs", "2024-01-01.trace.log"));
        JsonObject traceEvent = Assert.IsType<JsonObject>(JsonNode.Parse(traceLines[0]));
        JsonObject traceReviewGate = Assert.IsType<JsonObject>(JsonNode.Parse(traceLines[1]));
        JsonObject traceTaskReport = Assert.IsType<JsonObject>(JsonNode.Parse(traceLines[2]));
        JsonObject traceResult = Assert.IsType<JsonObject>(JsonNode.Parse(traceLines[^1]));

        Assert.Equal(1, exitCode);
        Assert.Equal(4, outputLines.Length);
        Assert.Equal("model.turn", outputEvent["type"]?.GetValue<string>());
        Assert.Equal("review.gate", outputReviewGate["type"]?.GetValue<string>());
        Assert.Equal("taskReport", outputTaskReport["type"]?.GetValue<string>());
        Assert.Equal("exec.result", outputResult["type"]?.GetValue<string>());
        Assert.Equal("session-store-error", outputResult["errorCode"]?.GetValue<string>());
        Assert.Equal("failure", outputResult["payload"]?["status"]?.GetValue<string>());
        Assert.Equal(3, outputResult["payload"]?["eventCount"]?.GetValue<int>());
        Assert.Equal(4, traceLines.Length);
        Assert.Equal("model.turn", traceEvent["type"]?.GetValue<string>());
        Assert.Equal("review.gate", traceReviewGate["type"]?.GetValue<string>());
        Assert.Equal("taskReport", traceTaskReport["type"]?.GetValue<string>());
        Assert.Equal("exec.result", traceResult["type"]?.GetValue<string>());
        Assert.Equal("failure", traceResult["status"]?.GetValue<string>());
        Assert.Equal("session-store-error", traceResult["errorCode"]?.GetValue<string>());
        Assert.Equal(3, traceResult["payload"]?["eventCount"]?.GetValue<int>());
        Assert.Equal("smoke", store.SavedSessionName?.Value);
        Assert.NotNull(agentRunner.LastRequest);
        Assert.DoesNotContain("C:\\secret", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\secret", File.ReadAllText(Path.Combine(temp.Path, ".caicli", "logs", "2024-01-01.trace.log")), StringComparison.Ordinal);
    }

    [Fact]
    public void Exec_json_output_includes_approval_status_for_tool_event_and_result()
    {
        using TempDirectory temp = TempDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "note.txt"), "before");
        using StringWriter output = new();
        ExecutorToolCallAgentRunner agentRunner = new(
            "workspace.apply_patch",
            """{"path":"note.txt","find":"before","replace":"after"}""");

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(workspacePath),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => new FakeConversationStore(),
                () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                (_, _, executor) =>
                {
                    agentRunner.Executor = executor;
                    return agentRunner;
                })
            .Parse(["exec", "--json", "--workspace", temp.Path, "--approval", "always", "update note"])
            .Invoke();

        string[] lines = output.ToString()
            .TrimEnd()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(0, exitCode);
        Assert.Equal(4, lines.Length);
        JsonObject toolEvent = Assert.IsType<JsonObject>(JsonNode.Parse(lines[0]));
        JsonObject reviewGate = Assert.IsType<JsonObject>(JsonNode.Parse(lines[1]));
        JsonObject taskReport = Assert.IsType<JsonObject>(JsonNode.Parse(lines[2]));
        JsonObject result = Assert.IsType<JsonObject>(JsonNode.Parse(lines[^1]));
        Assert.Equal("tool.completed", toolEvent["type"]?.GetValue<string>());
        Assert.Equal("approved", toolEvent["approvalStatus"]?.GetValue<string>());
        Assert.Equal("review.gate", reviewGate["type"]?.GetValue<string>());
        Assert.Equal("taskReport", taskReport["type"]?.GetValue<string>());
        Assert.Equal("exec.result", result["type"]?.GetValue<string>());
        Assert.Equal("approved", result["approvalStatus"]?.GetValue<string>());
        Assert.Equal("success", result["payload"]?["status"]?.GetValue<string>());
        Assert.Equal("after", File.ReadAllText(Path.Combine(temp.Path, "note.txt")));
    }

    [Fact]
    public void Exec_approval_always_allows_injected_runner_tool_call()
    {
        using TempDirectory temp = TempDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "note.txt"), "before");
        using StringWriter output = new();
        ExecutorToolCallAgentRunner agentRunner = new(
            "workspace.apply_patch",
            """{"path":"note.txt","find":"before","replace":"after"}""");

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(workspacePath),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => new FakeConversationStore(),
                () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                (_, _, executor) =>
                {
                    agentRunner.Executor = executor;
                    return agentRunner;
                })
            .Parse(["exec", "--workspace", temp.Path, "--approval", "always", "update note"])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("approvalStatus=approved", text);
        Assert.Contains("result: success", text);
        Assert.Equal("after", File.ReadAllText(Path.Combine(temp.Path, "note.txt")));
    }

    [Fact]
    public void Exec_approval_never_takes_priority_over_approve()
    {
        using TempDirectory temp = TempDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "note.txt"), "before");
        using StringWriter output = new();
        ExecutorToolCallAgentRunner agentRunner = new(
            "workspace.apply_patch",
            """{"path":"note.txt","find":"before","replace":"after"}""");

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(workspacePath),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => new FakeConversationStore(),
                () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                (_, _, executor) =>
                {
                    agentRunner.Executor = executor;
                    return agentRunner;
                })
            .Parse(["exec", "--workspace", temp.Path, "--approval", "never", "--approve", "update note"])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("errorCode=approval-denied", text);
        Assert.Contains("approvalStatus=denied", text);
        Assert.Contains("result: failure", text);
        Assert.Equal("before", File.ReadAllText(Path.Combine(temp.Path, "note.txt")));
    }

    [Fact]
    public void Exec_invalid_approval_rejects_before_logging_or_running_agent()
    {
        using TempDirectory temp = TempDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "note.txt"), "before");
        using StringWriter output = new();
        List<string> loggedCommands = [];
        FakeAgentRunner agentRunner = new(AgentRunResult.Success("should not run", [], []));
        RootCommand command = CliCommandFactory.Create(
            output,
            workspacePath => CreateSnapshot(workspacePath),
            (commandName, _) => loggedCommands.Add(commandName),
            _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => new FakeConversationStore(),
            () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
            (_, _, _) => agentRunner);

        int exitCode = CliCommandFactory.Invoke(
            command,
            ["exec", "--workspace", temp.Path, "--approval", "maybe", "update note"],
            output);

        string text = output.ToString();
        Assert.Equal(2, exitCode);
        Assert.Empty(loggedCommands);
        Assert.Null(agentRunner.LastRequest);
        Assert.Contains("Invalid value for --approval. Allowed values are never, on-request, on-failure, and always.", text);
        Assert.Equal("before", File.ReadAllText(Path.Combine(temp.Path, "note.txt")));
    }

    [Fact]
    public void Exec_approve_legacy_still_allows_injected_runner_tool_call()
    {
        using TempDirectory temp = TempDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "note.txt"), "before");
        using StringWriter output = new();
        ExecutorToolCallAgentRunner agentRunner = new(
            "workspace.apply_patch",
            """{"path":"note.txt","find":"before","replace":"after"}""");

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(workspacePath),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => new FakeConversationStore(),
                () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                (_, _, executor) =>
                {
                    agentRunner.Executor = executor;
                    return agentRunner;
                })
            .Parse(["exec", "--workspace", temp.Path, "--approve", "update note"])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("approvalStatus=approved", text);
        Assert.Contains("result: success", text);
        Assert.Equal("after", File.ReadAllText(Path.Combine(temp.Path, "note.txt")));
    }

    [Fact]
    public void Exec_uses_configured_approval_mode_without_cli_override()
    {
        using TempDirectory temp = TempDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "note.txt"), "before");
        using StringWriter output = new();
        ExecutorToolCallAgentRunner agentRunner = new(
            "workspace.apply_patch",
            """{"path":"note.txt","find":"before","replace":"after"}""");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(temp.Path)
            with
            {
                Configuration = CreateSnapshot(temp.Path).Configuration with
                {
                    ApprovalMode = ApprovalMode.Always,
                    ApprovalModeSource = "workspace config"
                }
            };

        int exitCode = CliCommandFactory
            .Create(
                output,
                _ => snapshot,
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => new FakeConversationStore(),
                () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                (_, _, executor) =>
                {
                    agentRunner.Executor = executor;
                    return agentRunner;
                })
            .Parse(["exec", "--workspace", temp.Path, "update note"])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("approvalStatus=approved", text);
        Assert.Equal("after", File.ReadAllText(Path.Combine(temp.Path, "note.txt")));
    }

    [Fact]
    public void Exec_session_loads_transcript_passes_it_to_runner_and_saves()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        FakeAgentRunner agentRunner = new(AgentRunResult.Success("agent completed task", [], []));
        ConversationTranscript existing = ConversationTranscript.Create(
            "smoke",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        FakeConversationStore store = new()
        {
            Transcript = existing
        };

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(
                    workspacePath,
                    apiKey: "sk-test-secret",
                    apiKeySource: "OPENAI_API_KEY",
                    model: "gpt-test"),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => store,
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
                (_, _, _) => agentRunner)
            .Parse(["exec", "--workspace", temp.Path, "--session", "smoke", "summarize workspace"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal("smoke", store.LoadedSessionName?.Value);
        Assert.Equal("smoke", store.SavedSessionName?.Value);
        Assert.Equal("smoke", agentRunner.LastRequest?.SessionName);
        Assert.Equal("smoke", agentRunner.LastRequest?.TaskContext?.SessionName);
        Assert.False(agentRunner.LastRequest?.TaskContext?.HasTranscriptContext);
        Assert.Same(existing, agentRunner.LastTranscript);
        Assert.Same(existing, store.SavedTranscript);
        ConversationAgentRun run = Assert.Single(existing.AgentRuns);
        Assert.Equal("success", run.Status);
        Assert.Equal("completed", run.StopReason);
        Assert.Equal("agent completed task", run.Summary);
        Assert.Equal(0, run.EventCount);
    }

    [Fact]
    public void Exec_resume_requires_existing_transcript_passes_it_to_runner_and_saves()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        FakeAgentRunner agentRunner = new(AgentRunResult.Success("agent completed task", [], []));
        ConversationTranscript existing = ConversationTranscript.Create(
            "smoke",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        FakeConversationStore store = new()
        {
            Transcript = existing
        };

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(
                    workspacePath,
                    apiKey: "sk-test-secret",
                    apiKeySource: "OPENAI_API_KEY",
                    model: "gpt-test"),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => store,
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
                (_, _, _) => agentRunner)
            .Parse(["exec", "--workspace", temp.Path, "--resume", "smoke", "summarize workspace"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal("smoke", store.TryLoadedSessionName?.Value);
        Assert.Null(store.LoadedSessionName);
        Assert.Equal("smoke", store.SavedSessionName?.Value);
        Assert.Equal("smoke", agentRunner.LastRequest?.SessionName);
        Assert.Equal("smoke", agentRunner.LastRequest?.TaskContext?.SessionName);
        Assert.True(agentRunner.LastRequest?.TaskContext?.HasTranscriptContext);
        Assert.Same(existing, agentRunner.LastRequest?.TranscriptContext);
        Assert.Same(existing, agentRunner.LastTranscript);
        Assert.Same(existing, store.SavedTranscript);
    }

    [Fact]
    public void Exec_resume_missing_returns_session_not_found_without_running_agent()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        FakeAgentRunner agentRunner = new(AgentRunResult.Success("agent completed task", [], []));
        FakeConversationStore store = new()
        {
            TryLoadResult = false
        };

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(
                    workspacePath,
                    apiKey: "sk-test-secret",
                    apiKeySource: "OPENAI_API_KEY",
                    model: "gpt-test"),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => store,
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
                (_, _, _) => agentRunner)
            .Parse(["exec", "--workspace", temp.Path, "--resume", "missing", "summarize workspace"])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("status: failed", text);
        Assert.Contains("errorCode: session-not-found", text);
        Assert.Contains("summary:", text);
        Assert.Contains("Session transcript was not found.", text);
        Assert.Equal("missing", store.TryLoadedSessionName?.Value);
        Assert.Null(store.ExistsSessionName);
        Assert.Null(store.LoadedSessionName);
        Assert.Null(store.SavedSessionName);
        Assert.Null(agentRunner.LastRequest);
    }

    [Fact]
    public void Exec_json_resume_missing_renders_json_failure_without_running_agent()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        FakeAgentRunner agentRunner = new(AgentRunResult.Success("agent completed task", [], []));
        FakeConversationStore store = new()
        {
            TryLoadResult = false
        };

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(
                    workspacePath,
                    apiKey: "sk-test-secret",
                    apiKeySource: "OPENAI_API_KEY",
                    model: "gpt-test"),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => store,
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
                (_, _, _) => agentRunner)
            .Parse(["exec", "--json", "--workspace", temp.Path, "--resume", "missing", "summarize workspace"])
            .Invoke();

        string[] lines = output.ToString().TrimEnd().Split(Environment.NewLine);
        JsonObject result = Assert.IsType<JsonObject>(JsonNode.Parse(Assert.Single(lines)));
        Assert.Equal(1, exitCode);
        Assert.Equal("exec.result", result["type"]?.GetValue<string>());
        Assert.Equal("session-not-found", result["errorCode"]?.GetValue<string>());
        Assert.Equal("Session transcript was not found.", result["summary"]?.GetValue<string>());
        JsonObject payload = Assert.IsType<JsonObject>(result["payload"]);
        Assert.Equal("failure", payload["status"]?.GetValue<string>());
        Assert.Equal(1, payload["exitCode"]?.GetValue<int>());
        Assert.Equal("missing", store.TryLoadedSessionName?.Value);
        Assert.Null(agentRunner.LastRequest);
    }

    [Fact]
    public void Exec_json_resume_invalid_session_name_renders_json_failure_without_running_agent_or_store()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        bool storeFactoryInvoked = false;
        bool runnerFactoryInvoked = false;

        RootCommand command = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(
                    workspacePath,
                    apiKey: "sk-test-secret",
                    apiKeySource: "OPENAI_API_KEY",
                    model: "gpt-test"),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ =>
                {
                    storeFactoryInvoked = true;
                    return new FakeConversationStore();
                },
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
                (_, _, _) =>
                {
                    runnerFactoryInvoked = true;
                    return new FakeAgentRunner(AgentRunResult.Success("agent completed task", [], []));
                });
        int exitCode = CliCommandFactory.Invoke(
            command,
            ["exec", "--json", "--workspace", temp.Path, "--resume", "../secret", "summarize workspace"],
            output);

        string[] lines = output.ToString().TrimEnd().Split(Environment.NewLine);
        JsonObject result = Assert.IsType<JsonObject>(JsonNode.Parse(Assert.Single(lines)));
        Assert.Equal(1, exitCode);
        Assert.Equal("exec.result", result["type"]?.GetValue<string>());
        Assert.Equal("invalid-session-name", result["errorCode"]?.GetValue<string>());
        Assert.Equal("Session name contains invalid path characters.", result["summary"]?.GetValue<string>());
        JsonObject payload = Assert.IsType<JsonObject>(result["payload"]);
        Assert.Equal("failure", payload["status"]?.GetValue<string>());
        Assert.Equal(1, payload["exitCode"]?.GetValue<int>());
        Assert.False(storeFactoryInvoked);
        Assert.False(runnerFactoryInvoked);
    }

    [Fact]
    public void Exec_json_resume_empty_session_name_renders_json_failure_without_running_agent_or_store()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        bool storeFactoryInvoked = false;
        bool runnerFactoryInvoked = false;

        RootCommand command = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(
                    workspacePath,
                    apiKey: "sk-test-secret",
                    apiKeySource: "OPENAI_API_KEY",
                    model: "gpt-test"),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ =>
                {
                    storeFactoryInvoked = true;
                    return new FakeConversationStore();
                },
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
                (_, _, _) =>
                {
                    runnerFactoryInvoked = true;
                    return new FakeAgentRunner(AgentRunResult.Success("agent completed task", [], []));
                });
        int exitCode = CliCommandFactory.Invoke(
            command,
            ["exec", "--json", "--workspace", temp.Path, "--resume", "", "summarize workspace"],
            output);

        JsonObject result = AssertLastExecJsonResult(output);
        Assert.Equal(1, exitCode);
        Assert.Equal("invalid-session-name", result["errorCode"]?.GetValue<string>());
        Assert.Equal("Session name must not be empty.", result["summary"]?.GetValue<string>());
        JsonObject payload = Assert.IsType<JsonObject>(result["payload"]);
        Assert.Equal("failure", payload["status"]?.GetValue<string>());
        Assert.Equal(1, payload["exitCode"]?.GetValue<int>());
        Assert.False(storeFactoryInvoked);
        Assert.False(runnerFactoryInvoked);
    }

    [Fact]
    public void Exec_output_json_session_invalid_session_name_renders_json_failure_without_running_agent_or_store()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        bool storeFactoryInvoked = false;
        bool runnerFactoryInvoked = false;

        RootCommand command = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(
                    workspacePath,
                    apiKey: "sk-test-secret",
                    apiKeySource: "OPENAI_API_KEY",
                    model: "gpt-test"),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ =>
                {
                    storeFactoryInvoked = true;
                    return new FakeConversationStore();
                },
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
                (_, _, _) =>
                {
                    runnerFactoryInvoked = true;
                    return new FakeAgentRunner(AgentRunResult.Success("agent completed task", [], []));
                });
        int exitCode = CliCommandFactory.Invoke(
            command,
            ["exec", "--workspace", temp.Path, "--output", "json", "--session", "../secret", "summarize workspace"],
            output);

        string[] lines = output.ToString().TrimEnd().Split(Environment.NewLine);
        JsonObject result = Assert.IsType<JsonObject>(JsonNode.Parse(Assert.Single(lines)));
        Assert.Equal(1, exitCode);
        Assert.Equal("exec.result", result["type"]?.GetValue<string>());
        Assert.Equal("invalid-session-name", result["errorCode"]?.GetValue<string>());
        Assert.Equal("Session name contains invalid path characters.", result["summary"]?.GetValue<string>());
        JsonObject payload = Assert.IsType<JsonObject>(result["payload"]);
        Assert.Equal("failure", payload["status"]?.GetValue<string>());
        Assert.Equal(1, payload["exitCode"]?.GetValue<int>());
        Assert.False(storeFactoryInvoked);
        Assert.False(runnerFactoryInvoked);
    }

    [Fact]
    public void Exec_output_json_session_whitespace_session_name_renders_json_failure_without_running_agent_or_store()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        bool storeFactoryInvoked = false;
        bool runnerFactoryInvoked = false;

        RootCommand command = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(
                    workspacePath,
                    apiKey: "sk-test-secret",
                    apiKeySource: "OPENAI_API_KEY",
                    model: "gpt-test"),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ =>
                {
                    storeFactoryInvoked = true;
                    return new FakeConversationStore();
                },
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
                (_, _, _) =>
                {
                    runnerFactoryInvoked = true;
                    return new FakeAgentRunner(AgentRunResult.Success("agent completed task", [], []));
                });
        int exitCode = CliCommandFactory.Invoke(
            command,
            ["exec", "--workspace", temp.Path, "--output", "json", "--session", " ", "summarize workspace"],
            output);

        JsonObject result = AssertLastExecJsonResult(output);
        Assert.Equal(1, exitCode);
        Assert.Equal("invalid-session-name", result["errorCode"]?.GetValue<string>());
        Assert.Equal("Session name must not be empty.", result["summary"]?.GetValue<string>());
        JsonObject payload = Assert.IsType<JsonObject>(result["payload"]);
        Assert.Equal("failure", payload["status"]?.GetValue<string>());
        Assert.Equal(1, payload["exitCode"]?.GetValue<int>());
        Assert.False(storeFactoryInvoked);
        Assert.False(runnerFactoryInvoked);
    }

    [Fact]
    public void Exec_session_and_resume_conflict_returns_failure_without_running_agent()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        FakeAgentRunner agentRunner = new(AgentRunResult.Success("agent completed task", [], []));
        FakeConversationStore store = new();

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(
                    workspacePath,
                    apiKey: "sk-test-secret",
                    apiKeySource: "OPENAI_API_KEY",
                    model: "gpt-test"),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => store,
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
                (_, _, _) => agentRunner)
            .Parse(["exec", "--workspace", temp.Path, "--session", "smoke", "--resume", "smoke", "summarize workspace"])
            .Invoke();

        Assert.Equal(1, exitCode);
        Assert.Contains("session-option-conflict", output.ToString());
        Assert.Null(store.ExistsSessionName);
        Assert.Null(store.LoadedSessionName);
        Assert.Null(agentRunner.LastRequest);
    }

    [Fact]
    public void Exec_output_json_session_and_resume_conflict_renders_json_failure_without_running_agent()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        FakeAgentRunner agentRunner = new(AgentRunResult.Success("agent completed task", [], []));
        FakeConversationStore store = new();

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(
                    workspacePath,
                    apiKey: "sk-test-secret",
                    apiKeySource: "OPENAI_API_KEY",
                    model: "gpt-test"),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => store,
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
                (_, _, _) => agentRunner)
            .Parse(["exec", "--workspace", temp.Path, "--output", "json", "--session", "smoke", "--resume", "smoke", "summarize workspace"])
            .Invoke();

        string[] lines = output.ToString().TrimEnd().Split(Environment.NewLine);
        JsonObject result = Assert.IsType<JsonObject>(JsonNode.Parse(Assert.Single(lines)));
        Assert.Equal(1, exitCode);
        Assert.Equal("exec.result", result["type"]?.GetValue<string>());
        Assert.Equal("session-option-conflict", result["errorCode"]?.GetValue<string>());
        Assert.Equal("Use either --session or --resume, not both.", result["summary"]?.GetValue<string>());
        JsonObject payload = Assert.IsType<JsonObject>(result["payload"]);
        Assert.Equal("failure", payload["status"]?.GetValue<string>());
        Assert.Equal(1, payload["exitCode"]?.GetValue<int>());
        Assert.Null(store.TryLoadedSessionName);
        Assert.Null(agentRunner.LastRequest);
    }

    [Fact]
    public void Exec_output_json_empty_session_and_resume_conflict_renders_json_failure_without_running_agent_or_store()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        bool storeFactoryInvoked = false;
        bool runnerFactoryInvoked = false;

        RootCommand command = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(
                    workspacePath,
                    apiKey: "sk-test-secret",
                    apiKeySource: "OPENAI_API_KEY",
                    model: "gpt-test"),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ =>
                {
                    storeFactoryInvoked = true;
                    return new FakeConversationStore();
                },
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
                (_, _, _) =>
                {
                    runnerFactoryInvoked = true;
                    return new FakeAgentRunner(AgentRunResult.Success("agent completed task", [], []));
                });
        int exitCode = CliCommandFactory.Invoke(
            command,
            ["exec", "--workspace", temp.Path, "--output", "json", "--session", "", "--resume", "smoke", "summarize workspace"],
            output);

        JsonObject result = AssertSingleExecJsonResult(output);
        Assert.Equal(1, exitCode);
        Assert.Equal("session-option-conflict", result["errorCode"]?.GetValue<string>());
        Assert.Equal("Use either --session or --resume, not both.", result["summary"]?.GetValue<string>());
        JsonObject payload = Assert.IsType<JsonObject>(result["payload"]);
        Assert.Equal("failure", payload["status"]?.GetValue<string>());
        Assert.Equal(1, payload["exitCode"]?.GetValue<int>());
        Assert.False(storeFactoryInvoked);
        Assert.False(runnerFactoryInvoked);
    }

    [Fact]
    public void Exec_json_resume_malformed_file_transcript_renders_json_failure_without_running_agent()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        bool runnerFactoryInvoked = false;
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        string sessionDirectory = Path.Combine(temp.Path, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        File.WriteAllText(Path.Combine(sessionDirectory, "smoke.transcript.json"), """{"schemaVersion":""");

        RootCommand command = CliCommandFactory.Create(
            output,
            workspacePath => CreateSnapshot(
                workspacePath,
                apiKey: "sk-test-secret",
                apiKeySource: "OPENAI_API_KEY",
                model: "gpt-test",
                userConfigPath: userConfigPath),
            (_, _) => { },
            _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
            writer => new TerminalChatStreamingRenderer(writer),
            snapshot => FileConversationStore.Create(snapshot),
            () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
            (_, _, _) =>
            {
                runnerFactoryInvoked = true;
                return new FakeAgentRunner(AgentRunResult.Success("agent completed task", [], []));
            });

        int exitCode = CliCommandFactory.Invoke(
            command,
            ["exec", "--json", "--workspace", temp.Path, "--resume", "smoke", "summarize workspace"],
            output);

        JsonObject result = AssertSingleExecJsonResult(output);
        Assert.Equal(1, exitCode);
        Assert.Equal("session-transcript-invalid", result["errorCode"]?.GetValue<string>());
        Assert.Equal("Conversation transcript is missing or uses an unsupported schema version.", result["summary"]?.GetValue<string>());
        JsonObject payload = Assert.IsType<JsonObject>(result["payload"]);
        Assert.Equal("failure", payload["status"]?.GetValue<string>());
        Assert.Equal(1, payload["exitCode"]?.GetValue<int>());
        Assert.False(runnerFactoryInvoked);
    }

    [Fact]
    public void Exec_json_session_malformed_file_transcript_renders_json_failure_without_running_agent()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        bool runnerFactoryInvoked = false;
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        string sessionDirectory = Path.Combine(temp.Path, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        File.WriteAllText(Path.Combine(sessionDirectory, "smoke.transcript.json"), """{"schemaVersion":""");

        RootCommand command = CliCommandFactory.Create(
            output,
            workspacePath => CreateSnapshot(
                workspacePath,
                apiKey: "sk-test-secret",
                apiKeySource: "OPENAI_API_KEY",
                model: "gpt-test",
                userConfigPath: userConfigPath),
            (_, _) => { },
            _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
            writer => new TerminalChatStreamingRenderer(writer),
            snapshot => FileConversationStore.Create(snapshot),
            () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
            (_, _, _) =>
            {
                runnerFactoryInvoked = true;
                return new FakeAgentRunner(AgentRunResult.Success("agent completed task", [], []));
            });

        int exitCode = CliCommandFactory.Invoke(
            command,
            ["exec", "--json", "--workspace", temp.Path, "--session", "smoke", "summarize workspace"],
            output);

        JsonObject result = AssertSingleExecJsonResult(output);
        Assert.Equal(1, exitCode);
        Assert.Equal("session-transcript-invalid", result["errorCode"]?.GetValue<string>());
        Assert.Equal("Conversation transcript is missing or uses an unsupported schema version.", result["summary"]?.GetValue<string>());
        JsonObject payload = Assert.IsType<JsonObject>(result["payload"]);
        Assert.Equal("failure", payload["status"]?.GetValue<string>());
        Assert.Equal(1, payload["exitCode"]?.GetValue<int>());
        Assert.False(runnerFactoryInvoked);
    }

    [Fact]
    public void Exec_json_session_save_exception_renders_json_failure_without_leaking_exception_detail()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        FakeAgentRunner agentRunner = new(AgentRunResult.Success("agent completed task", [], []));
        FakeConversationStore store = new()
        {
            SaveException = new IOException("cannot write C:\\secret\\smoke.transcript.json")
        };

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(
                    workspacePath,
                    apiKey: "sk-test-secret",
                    apiKeySource: "OPENAI_API_KEY",
                    model: "gpt-test"),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => store,
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
                (_, _, _) => agentRunner)
            .Parse(["exec", "--json", "--workspace", temp.Path, "--session", "smoke", "summarize workspace"])
            .Invoke();

        JsonObject result = AssertLastExecJsonResult(output);
        Assert.Equal(1, exitCode);
        Assert.Equal("session-store-error", result["errorCode"]?.GetValue<string>());
        Assert.Equal("Conversation session store operation failed.", result["summary"]?.GetValue<string>());
        JsonObject payload = Assert.IsType<JsonObject>(result["payload"]);
        Assert.Equal("failure", payload["status"]?.GetValue<string>());
        Assert.Equal(1, payload["exitCode"]?.GetValue<int>());
        Assert.Equal("smoke", store.SavedSessionName?.Value);
        Assert.NotNull(agentRunner.LastRequest);
        Assert.DoesNotContain("C:\\secret", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Exec_session_persists_tool_call_recorded_by_runner_into_passed_transcript()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        FakeConversationStore store = new();
        TranscriptRecordingAgentRunner agentRunner = new(
            callId: "call_tool_1",
            toolName: "workspace.search",
            argumentsJson: """{"query":"workspace"}""",
            result: ToolExecutionResult.Success("3 matching files", "approved"));

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(
                    workspacePath,
                    apiKey: "sk-test-secret",
                    apiKeySource: "OPENAI_API_KEY",
                    model: "gpt-test"),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => store,
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
                (_, _, _) => agentRunner)
            .Parse(["exec", "--workspace", temp.Path, "--session", "smoke", "summarize workspace"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal("smoke", store.LoadedSessionName?.Value);
        Assert.Equal("smoke", store.SavedSessionName?.Value);
        Assert.NotNull(store.SavedTranscript);
        Assert.Same(agentRunner.LastTranscript, store.SavedTranscript);
        ConversationToolCall toolCall = Assert.Single(store.SavedTranscript.ToolCalls);
        Assert.Equal("call_tool_1", toolCall.CallId);
        Assert.Equal("workspace.search", toolCall.ToolName);
        Assert.Equal("""{"query":"workspace"}""", toolCall.ArgumentsJson);
        Assert.Equal("approved", toolCall.ApprovalStatus);
        Assert.True(toolCall.Succeeded);
        Assert.Equal("3 matching files", toolCall.OutputSummary);
        Assert.Null(toolCall.FailureReason);
        Assert.Null(toolCall.ErrorCode);
        Assert.False(toolCall.Retryable);
    }

    [Fact]
    public void Exec_without_session_does_not_create_transcript()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        FakeAgentRunner agentRunner = new(AgentRunResult.Success("agent completed task", [], []));
        FakeConversationStore store = new();

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(
                    workspacePath,
                    apiKey: "sk-test-secret",
                    apiKeySource: "OPENAI_API_KEY",
                    model: "gpt-test"),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => store,
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
                (_, _, _) => agentRunner)
            .Parse(["exec", "--workspace", temp.Path, "summarize workspace"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Null(store.LoadedSessionName);
        Assert.Null(store.SavedSessionName);
        Assert.Null(store.SavedTranscript);
        Assert.Null(agentRunner.LastTranscript);
    }

    [Fact]
    public void Exec_output_rejects_unknown_value_before_running_task()
    {
        using TempDirectory temp = TempDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "note.txt"), "hello exec");
        using StringWriter output = new();
        List<string> loggedCommands = [];
        RootCommand command = CliCommandFactory.Create(
            output,
            CreateSnapshot,
            (commandName, _) => loggedCommands.Add(commandName));

        int exitCode = CliCommandFactory.Invoke(command, ["exec", "--workspace", temp.Path, "--output", "banana", "read note.txt"], output);

        Assert.Equal(2, exitCode);
        Assert.Empty(loggedCommands);
        Assert.DoesNotContain("hello exec", output.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--max-steps", "0")]
    [InlineData("--max-turns", "0")]
    [InlineData("--max-tool-calls", "0")]
    [InlineData("--timeout-seconds", "0")]
    public void Exec_limit_options_reject_non_positive_values_before_running_task(string option, string value)
    {
        using TempDirectory temp = TempDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "note.txt"), "hello exec");
        using StringWriter output = new();
        List<string> loggedCommands = [];
        RootCommand command = CliCommandFactory.Create(
            output,
            CreateSnapshot,
            (commandName, _) => loggedCommands.Add(commandName));

        int exitCode = CliCommandFactory.Invoke(
            command,
            ["exec", "--workspace", temp.Path, option, value, "read note.txt"],
            output);

        Assert.Equal(2, exitCode);
        Assert.Empty(loggedCommands);
        Assert.DoesNotContain("hello exec", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Exec_max_retries_rejects_negative_values_before_running_task()
    {
        using TempDirectory temp = TempDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "note.txt"), "hello exec");
        using StringWriter output = new();
        List<string> loggedCommands = [];
        RootCommand command = CliCommandFactory.Create(
            output,
            CreateSnapshot,
            (commandName, _) => loggedCommands.Add(commandName));

        int exitCode = CliCommandFactory.Invoke(
            command,
            ["exec", "--workspace", temp.Path, "--max-retries", "-1", "read note.txt"],
            output);

        Assert.Equal(2, exitCode);
        Assert.Empty(loggedCommands);
        Assert.DoesNotContain("hello exec", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Exec_rejects_conflicting_max_steps_and_max_turns_before_running_task()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        List<string> loggedCommands = [];
        FakeAgentRunner agentRunner = new(AgentRunResult.Success("should not run", [], []));
        RootCommand command = CliCommandFactory.Create(
            output,
            workspacePath => CreateSnapshot(
                workspacePath,
                apiKey: "sk-test-secret",
                apiKeySource: "OPENAI_API_KEY",
                model: "gpt-test"),
            (commandName, _) => loggedCommands.Add(commandName),
            _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => new FakeConversationStore(),
            () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
            (_, _, _) => agentRunner);

        int exitCode = CliCommandFactory.Invoke(
            command,
            ["exec", "--workspace", temp.Path, "--max-steps", "2", "--max-turns", "3", "read note.txt"],
            output);

        Assert.Equal(1, exitCode);
        Assert.Contains("invalid-agent-limits", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(["exec"], loggedCommands);
        Assert.Null(agentRunner.LastRequest);
    }

    [Fact]
    public void Exec_agent_failure_maps_to_nonzero_exit_code_and_error_code()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        FakeAgentRunner agentRunner = new(AgentRunResult.Failure(
            new AgentError(
                "agent-test-failure",
                "Agent test failure.",
                Retryable: false),
            [],
            [
                new AgentRunEvent(
                    Type: "agent.error",
                    Sequence: 0,
                    Timestamp: DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                    Message: "Agent test failure.",
                    ErrorCode: "agent-test-failure")
            ]));
        RootCommand command = CliCommandFactory.Create(
            output,
            workspacePath => CreateSnapshot(
                workspacePath,
                apiKey: "sk-test-secret",
                apiKeySource: "OPENAI_API_KEY",
                model: "gpt-test"),
            (_, _) => { },
            _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => new FakeConversationStore(),
            () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
            (_, _, _) => agentRunner);

        int exitCode = CliCommandFactory.Invoke(command, ["exec", "--workspace", temp.Path, "paint the moon"], output);

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("event: agent.error", text);
        Assert.Contains("result: failure exitCode=1", text);
        Assert.Contains("errorCode=agent-test-failure", text);
        Assert.DoesNotContain("sk-test-secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Exec_runner_not_supported_exception_surfaces_from_injected_runner()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        RootCommand command = CliCommandFactory.Create(
            output,
            workspacePath => CreateSnapshot(
                workspacePath,
                apiKey: "sk-test-secret",
                apiKeySource: "OPENAI_API_KEY",
                model: "gpt-test"),
            (_, _) => { },
            _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => new FakeConversationStore(),
            () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
            (_, _, _) => new ThrowingAgentRunner(new NotSupportedException("runner detail")));

        NotSupportedException exception = Assert.Throws<NotSupportedException>(
            () => CliCommandFactory.Invoke(command, ["exec", "--workspace", temp.Path, "paint the moon"], output));

        Assert.Contains("runner detail", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("agent-backend-unavailable", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("sk-test-secret", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Exec_default_with_missing_model_reports_missing_model()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        RootCommand command = CliCommandFactory.Create(
            output,
            workspacePath => CreateSnapshot(
                workspacePath,
                apiKey: "sk-test-secret",
                apiKeySource: "OPENAI_API_KEY",
                model: "not configured"));

        int exitCode = CliCommandFactory.Invoke(command, ["exec", "--workspace", temp.Path, "summarize workspace"], output);

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("event: agent.error", text);
        Assert.Contains("result: failure exitCode=1", text);
        Assert.Contains("errorCode=missing-model", text);
        Assert.DoesNotContain("sk-test-secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Exec_default_with_configured_model_and_missing_api_key_reports_missing_openai_api_key()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        RootCommand command = CliCommandFactory.Create(
            output,
            workspacePath => CreateSnapshot(
                workspacePath,
                apiKey: null,
                apiKeySource: "missing",
                model: "gpt-test"));

        int exitCode = CliCommandFactory.Invoke(command, ["exec", "--workspace", temp.Path, "summarize workspace"], output);

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("event: agent.error", text);
        Assert.Contains("result: failure exitCode=1", text);
        Assert.Contains("errorCode=missing-openai-api-key", text);
    }

    [Fact]
    public void Exec_default_with_unsupported_api_key_source_reports_unsupported_api_key_source_without_secret()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        RootCommand command = CliCommandFactory.Create(
            output,
            workspacePath => CreateSnapshot(
                workspacePath,
                apiKey: "sk-workspace-secret",
                apiKeySource: "workspace config",
                model: "gpt-test"));

        int exitCode = CliCommandFactory.Invoke(command, ["exec", "--workspace", temp.Path, "summarize workspace"], output);

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("event: agent.error", text);
        Assert.Contains("result: failure exitCode=1", text);
        Assert.Contains("errorCode=unsupported-api-key-source", text);
        Assert.DoesNotContain("sk-workspace-secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Exec_direct_backend_failure_output_does_not_leak_api_key()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        FakeAgentRunner agentRunner = new(AgentRunResult.Failure(
            new AgentError(
                "openai-client-error",
                "OpenAI agent model call failed before a response was completed.",
                Retryable: true),
            [],
            [
                new AgentRunEvent(
                    Type: "agent.error",
                    Sequence: 0,
                    Timestamp: DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                    Message: "OpenAI agent model call failed before a response was completed.",
                    ErrorCode: "openai-client-error",
                    Status: DiagnosticEventStatus.Failure)
            ]));
        RootCommand command = CliCommandFactory.Create(
            output,
            workspacePath => CreateSnapshot(
                workspacePath,
                apiKey: "sk-test-secret",
                apiKeySource: "OPENAI_API_KEY",
                model: "gpt-test"),
            (_, _) => { },
            _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => new FakeConversationStore(),
            () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
            (_, _, _) => agentRunner);

        int exitCode = CliCommandFactory.Invoke(command, ["exec", "--workspace", temp.Path, "summarize workspace"], output);

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("event: agent.error", text);
        Assert.Contains("result: failure exitCode=1", text);
        Assert.Contains("errorCode=openai-client-error", text);
        Assert.Contains("OpenAI agent model call failed before a response was completed.", text);
        Assert.DoesNotContain("sk-test-secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Exec_uses_configured_agent_limits_when_cli_limits_are_missing()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        FakeAgentRunner agentRunner = new(AgentRunResult.Success("agent completed task", []));
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: temp.Path,
            apiKey: "sk-test-secret",
            apiKeySource: "OPENAI_API_KEY",
            model: "gpt-test",
            agentRunLimits: new AgentRunLimits(
                MaxSteps: 4,
                MaxToolCalls: 9,
                MaxRetries: 2,
                ModelCallTimeout: TimeSpan.FromSeconds(11),
                OverallTimeout: TimeSpan.FromSeconds(11)));

        int exitCode = CliCommandFactory
            .Create(
                output,
                _ => snapshot,
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => new FakeConversationStore(),
                () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                (_, _, _) => agentRunner)
            .Parse(["exec", "--workspace", temp.Path, "summarize workspace"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal(4, agentRunner.LastRequest?.Limits?.MaxSteps);
        Assert.Equal(9, agentRunner.LastRequest?.Limits?.MaxToolCalls);
        Assert.Equal(2, agentRunner.LastRequest?.Limits?.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(11), agentRunner.LastRequest?.Limits?.OverallTimeout);
        Assert.Equal(TimeSpan.FromSeconds(11), agentRunner.LastRequest?.Limits?.ModelCallTimeout);
    }

    [Fact]
    public void Exec_cli_agent_limits_override_configured_agent_limits()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        FakeAgentRunner agentRunner = new(AgentRunResult.Success("agent completed task", []));
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: temp.Path,
            apiKey: "sk-test-secret",
            apiKeySource: "OPENAI_API_KEY",
            model: "gpt-test",
            agentRunLimits: new AgentRunLimits(
                MaxSteps: 4,
                MaxToolCalls: 9,
                MaxRetries: 2,
                ModelCallTimeout: TimeSpan.FromSeconds(11),
                OverallTimeout: TimeSpan.FromSeconds(11)));

        int exitCode = CliCommandFactory
            .Create(
                output,
                _ => snapshot,
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => new FakeConversationStore(),
                () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                (_, _, _) => agentRunner)
            .Parse([
                "exec",
                "--workspace",
                temp.Path,
                "--max-steps",
                "2",
                "--max-tool-calls",
                "3",
                "--max-retries",
                "0",
                "--timeout-seconds",
                "5",
                "summarize workspace"
            ])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal(2, agentRunner.LastRequest?.Limits?.MaxSteps);
        Assert.Equal(3, agentRunner.LastRequest?.Limits?.MaxToolCalls);
        Assert.Equal(0, agentRunner.LastRequest?.Limits?.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(5), agentRunner.LastRequest?.Limits?.OverallTimeout);
        Assert.Equal(TimeSpan.FromSeconds(5), agentRunner.LastRequest?.Limits?.ModelCallTimeout);
    }

    [Fact]
    public void Exec_command_writes_command_log_through_delegate()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        List<string> loggedCommands = [];
        FakeAgentRunner agentRunner = new(AgentRunResult.Success("logged", []));
        RootCommand command = CliCommandFactory.Create(
            output,
            CreateSnapshot,
            (commandName, _) => loggedCommands.Add(commandName),
            _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => new FakeConversationStore(),
            () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
            (_, _, _) => agentRunner);

        int exitCode = CliCommandFactory.Invoke(command, ["exec", "--workspace", temp.Path, "read note.txt"], output);

        Assert.Equal(0, exitCode);
        Assert.Equal(["exec"], loggedCommands);
    }

    [Fact]
    public void Run_unsupported_task_returns_task_failure_exit_code()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        RootCommand command = CliCommandFactory.Create(output, workspacePath => CreateSnapshot(workspacePath));

        int exitCode = CliCommandFactory.Invoke(command, ["run", "--workspace", temp.Path, "paint the moon"], output);

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("status: failed", text);
        Assert.Contains("errorCode: unsupported-run-task", text);
    }

    [Fact]
    public void Session_export_defaults_to_json_transcript_file()
    {
        using TempDirectory temp = TempDirectory.Create();
        string userHome = Path.Combine(temp.Path, "user-home");
        string workspace = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(workspace);
        string sessionDirectory = Path.Combine(userHome, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        File.WriteAllText(Path.Combine(sessionDirectory, "smoke.transcript.json"), """
        {
          "schemaVersion": 1,
          "sessionName": "smoke",
          "createdAtUtc": "2024-01-01T00:00:00+00:00",
          "updatedAtUtc": "2024-01-01T00:00:00+00:00",
          "messages": [],
          "toolCalls": [],
          "errors": []
        }
        """);
        using StringWriter exportOutput = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: workspace,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: Path.Combine(userHome, ".caicli", "config.json"));

        int exportExitCode = CliCommandFactory
            .Create(exportOutput, _ => snapshot)
            .Parse(["session", "export", "smoke"])
            .Invoke();

        Assert.Equal(0, exportExitCode);
        Assert.Contains("\"sessionName\": \"smoke\"", exportOutput.ToString());
    }

    [Fact]
    public void Session_export_format_json_prints_json_transcript_file()
    {
        using TempDirectory temp = TempDirectory.Create();
        string userHome = Path.Combine(temp.Path, "user-home");
        string workspace = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(workspace);
        string sessionDirectory = Path.Combine(userHome, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        File.WriteAllText(Path.Combine(sessionDirectory, "smoke.transcript.json"), """
        {
          "schemaVersion": 1,
          "sessionName": "smoke",
          "createdAtUtc": "2024-01-01T00:00:00+00:00",
          "updatedAtUtc": "2024-01-01T00:00:00+00:00",
          "messages": [],
          "toolCalls": [],
          "errors": []
        }
        """);
        using StringWriter exportOutput = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: workspace,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: Path.Combine(userHome, ".caicli", "config.json"));

        int exportExitCode = CliCommandFactory
            .Create(exportOutput, _ => snapshot)
            .Parse(["session", "export", "--format", "json", "smoke"])
            .Invoke();

        Assert.Equal(0, exportExitCode);
        Assert.Contains("\"sessionName\": \"smoke\"", exportOutput.ToString());
    }

    [Fact]
    public void Session_export_format_json_with_verbose_remains_parseable_without_verbose_text()
    {
        using TempDirectory temp = TempDirectory.Create();
        string userHome = Path.Combine(temp.Path, "user-home");
        string workspace = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(workspace);
        string sessionDirectory = Path.Combine(userHome, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        File.WriteAllText(Path.Combine(sessionDirectory, "smoke.transcript.json"), """
        {
          "schemaVersion": 1,
          "sessionName": "smoke",
          "createdAtUtc": "2024-01-01T00:00:00+00:00",
          "updatedAtUtc": "2024-01-01T00:00:00+00:00",
          "messages": [],
          "toolCalls": [],
          "errors": []
        }
        """);
        using StringWriter exportOutput = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: workspace,
            apiKey: "sk-session-secret",
            apiKeySource: "OPENAI_API_KEY",
            model: "gpt-test",
            userConfigPath: Path.Combine(userHome, ".caicli", "config.json"));

        int exportExitCode = CliCommandFactory
            .Create(exportOutput, _ => snapshot)
            .Parse(["session", "export", "--format", "json", "--verbose", "smoke"])
            .Invoke();

        string text = exportOutput.ToString();
        JsonObject json = Assert.IsType<JsonObject>(JsonNode.Parse(text));
        Assert.Equal(0, exportExitCode);
        Assert.Equal("smoke", json["sessionName"]?.GetValue<string>());
        Assert.DoesNotContain("C# AI CLI verbose diagnostics", text, StringComparison.Ordinal);
        Assert.DoesNotContain("commandId:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-session-secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Session_export_format_json_does_not_create_conversation_store()
    {
        using TempDirectory temp = TempDirectory.Create();
        string userHome = Path.Combine(temp.Path, "user-home");
        string workspace = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(workspace);
        string sessionDirectory = Path.Combine(userHome, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        File.WriteAllText(Path.Combine(sessionDirectory, "smoke.transcript.json"), """
        {
          "schemaVersion": 1,
          "sessionName": "smoke",
          "createdAtUtc": "2024-01-01T00:00:00+00:00",
          "updatedAtUtc": "2024-01-01T00:00:00+00:00",
          "messages": [],
          "toolCalls": [],
          "errors": []
        }
        """);
        using StringWriter exportOutput = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: workspace,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: Path.Combine(userHome, ".caicli", "config.json"));
        int storeFactoryCalls = 0;

        int exportExitCode = CliCommandFactory
            .Create(
                exportOutput,
                _ => snapshot,
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ =>
                {
                    storeFactoryCalls++;
                    return new FakeConversationStore();
                },
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"))
            .Parse(["session", "export", "--format", "json", "smoke"])
            .Invoke();

        Assert.Equal(0, exportExitCode);
        Assert.Equal(0, storeFactoryCalls);
        Assert.Contains("\"sessionName\": \"smoke\"", exportOutput.ToString());
    }

    [Fact]
    public void Session_export_format_markdown_with_file_store_prints_safe_readable_transcript_from_disk()
    {
        using TempDirectory temp = TempDirectory.Create();
        string userHome = Path.Combine(temp.Path, "user-home");
        string workspace = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(workspace);
        string sessionDirectory = Path.Combine(userHome, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        File.WriteAllText(Path.Combine(sessionDirectory, "smoke.transcript.json"), """
        {
          "schemaVersion": 1,
          "sessionName": "smoke",
          "createdAtUtc": "2024-01-01T00:00:00+00:00",
          "updatedAtUtc": "2024-01-01T00:00:04+00:00",
          "messages": [
            {
              "role": "user",
              "createdAtUtc": "2024-01-01T00:00:01+00:00",
              "content": "hello from disk",
              "provider": null,
              "model": null,
              "responseId": null
            }
          ],
          "toolCalls": [
            {
              "createdAtUtc": "2024-01-01T00:00:02+00:00",
              "callId": "call_test",
              "toolName": "workspace.read_text",
              "argumentsJson": "{\"apiKey\":\"sk-tool-secret\",\"path\":\"note.txt\"}",
              "approvalStatus": "approved",
              "completedAtUtc": "2024-01-01T00:00:03+00:00",
              "succeeded": true,
              "outputSummary": "read note from disk",
              "failureReason": null,
              "errorCode": null,
              "retryable": false
            }
          ],
          "errors": []
        }
        """);
        using StringWriter exportOutput = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: workspace,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: Path.Combine(userHome, ".caicli", "config.json"));

        int exportExitCode = CliCommandFactory
            .Create(exportOutput, _ => snapshot)
            .Parse(["session", "export", "--format", "markdown", "smoke"])
            .Invoke();

        string text = exportOutput.ToString();
        Assert.Equal(0, exportExitCode);
        Assert.Contains("# Session: smoke", text);
        Assert.Contains("hello from disk", text);
        Assert.Contains("workspace.read_text succeeded", text);
        Assert.Contains("read note from disk", text);
        Assert.DoesNotContain("ArgumentsJson", text, StringComparison.Ordinal);
        Assert.DoesNotContain("argumentsJson", text, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-tool-secret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("apiKey", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Session_export_format_json_with_malformed_file_transcript_returns_safe_failure()
    {
        using TempDirectory temp = TempDirectory.Create();
        string userHome = Path.Combine(temp.Path, "user-home");
        string workspace = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(workspace);
        string sessionDirectory = Path.Combine(userHome, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        File.WriteAllText(Path.Combine(sessionDirectory, "smoke.transcript.json"), """{"schemaVersion":""");
        using StringWriter exportOutput = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: workspace,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: Path.Combine(userHome, ".caicli", "config.json"));

        int exportExitCode = CliCommandFactory.Invoke(
            CliCommandFactory.Create(exportOutput, _ => snapshot),
            ["session", "export", "--format", "json", "smoke"],
            exportOutput);

        string text = exportOutput.ToString();
        Assert.Equal(1, exportExitCode);
        Assert.Contains("status: failed", text);
        Assert.Contains("errorCode: session-transcript-invalid", text);
        Assert.Contains("Conversation transcript is missing or uses an unsupported schema version.", text);
        Assert.DoesNotContain("JsonReaderException", text, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Text.Json", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Session_export_format_markdown_with_malformed_file_transcript_returns_safe_failure()
    {
        using TempDirectory temp = TempDirectory.Create();
        string userHome = Path.Combine(temp.Path, "user-home");
        string workspace = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(workspace);
        string sessionDirectory = Path.Combine(userHome, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        File.WriteAllText(Path.Combine(sessionDirectory, "smoke.transcript.json"), """{"schemaVersion":""");
        using StringWriter exportOutput = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: workspace,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: Path.Combine(userHome, ".caicli", "config.json"));

        int exportExitCode = CliCommandFactory.Invoke(
            CliCommandFactory.Create(exportOutput, _ => snapshot),
            ["session", "export", "--format", "markdown", "smoke"],
            exportOutput);

        string text = exportOutput.ToString();
        Assert.Equal(1, exportExitCode);
        Assert.Contains("status: failed", text);
        Assert.Contains("errorCode: session-transcript-invalid", text);
        Assert.Contains("Conversation transcript is missing or uses an unsupported schema version.", text);
        Assert.DoesNotContain("JsonReaderException", text, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Text.Json", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("toolCalls")]
    [InlineData("errors")]
    public void Session_export_format_markdown_with_null_tool_call_or_error_returns_safe_failure(
        string nullEntryCollectionName)
    {
        using TempDirectory temp = TempDirectory.Create();
        string userHome = Path.Combine(temp.Path, "user-home");
        string workspace = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(workspace);
        string sessionDirectory = Path.Combine(userHome, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        string toolCallsJson = nullEntryCollectionName == "toolCalls" ? "[null]" : "[]";
        string errorsJson = nullEntryCollectionName == "errors" ? "[null]" : "[]";
        File.WriteAllText(Path.Combine(sessionDirectory, "smoke.transcript.json"), $$"""
        {
          "schemaVersion": 1,
          "sessionName": "smoke",
          "createdAtUtc": "2024-01-01T00:00:00+00:00",
          "updatedAtUtc": "2024-01-01T00:00:00+00:00",
          "messages": [],
          "toolCalls": {{toolCallsJson}},
          "errors": {{errorsJson}}
        }
        """);
        using StringWriter exportOutput = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: workspace,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: Path.Combine(userHome, ".caicli", "config.json"));

        int exportExitCode = CliCommandFactory.Invoke(
            CliCommandFactory.Create(exportOutput, _ => snapshot),
            ["session", "export", "--format", "markdown", "smoke"],
            exportOutput);

        string text = exportOutput.ToString();
        Assert.Equal(1, exportExitCode);
        Assert.Contains("status: failed", text);
        Assert.Contains("errorCode: session-transcript-invalid", text);
        Assert.Contains("summary:", text);
        Assert.Contains("Conversation transcript is missing or uses an unsupported schema version.", text);
        Assert.DoesNotContain("NullReferenceException", text, StringComparison.Ordinal);
        Assert.DoesNotContain("System.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Session_export_format_markdown_prints_safe_readable_transcript()
    {
        using StringWriter output = new();
        ConversationTranscript transcript = ConversationTranscript.Create(
            "smoke",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        transcript.AddUserMessage("hello assistant", DateTimeOffset.Parse("2024-01-01T00:00:01Z"));
        transcript.AddAssistantMessage(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "hello user"), DateTimeOffset.Parse("2024-01-01T00:00:02Z"));
        transcript.AddError(new ModelError(
            Provider: "openai",
            Operation: "responses.create",
            StatusCode: null,
            LocalErrorCode: "missing-openai-api-key",
            SafeMessage: "safe message without secret",
            Retryable: false), DateTimeOffset.Parse("2024-01-01T00:00:03Z"));
        transcript.AddToolCall(ConversationToolCall.FromExecution(
            "call_test",
            "workspace.read_text",
            """{"apiKey":"sk-tool-secret","path":"note.txt"}""",
            ToolExecutionResult.Success("read safe summary"),
            DateTimeOffset.Parse("2024-01-01T00:00:04Z")));
        FakeConversationStore store = new()
        {
            Transcript = transcript
        };

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(workspacePath),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => store,
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"))
            .Parse(["session", "export", "--format", "markdown", "smoke"])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Equal("smoke", store.TryLoadedSessionName?.Value);
        Assert.Contains("# Session: smoke", text);
        Assert.Contains("- Created: 2024-01-01T00:00:00.0000000+00:00", text);
        Assert.Contains("- Updated: 2024-01-01T00:00:04.0000000+00:00", text);
        Assert.Contains("- Turns: 1", text);
        Assert.Contains("- Tool calls: 1", text);
        Assert.Contains("### User - 2024-01-01T00:00:01.0000000+00:00", text);
        Assert.Contains("hello assistant", text);
        Assert.Contains("### Assistant - 2024-01-01T00:00:02.0000000+00:00", text);
        Assert.Contains("hello user", text);
        Assert.Contains("## Errors", text);
        Assert.Contains("- 2024-01-01T00:00:03.0000000+00:00 missing-openai-api-key", text);
        Assert.Contains("safe message without secret", text);
        Assert.Contains("## Tool Calls", text);
        Assert.Contains("- 2024-01-01T00:00:04.0000000+00:00 workspace.read_text succeeded", text);
        Assert.Contains("read safe summary", text);
        Assert.DoesNotContain("sk-", text, StringComparison.Ordinal);
        Assert.DoesNotContain("apiKey", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ArgumentsJson", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Session_export_format_markdown_accepts_case_insensitive_value()
    {
        using StringWriter output = new();
        ConversationTranscript transcript = ConversationTranscript.Create(
            "smoke",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        transcript.AddUserMessage("hello assistant", DateTimeOffset.Parse("2024-01-01T00:00:01Z"));
        FakeConversationStore store = new()
        {
            Transcript = transcript
        };

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(workspacePath),
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp", ""))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => store,
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"))
            .Parse(["session", "export", "--format", "Markdown", "smoke"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal("smoke", store.TryLoadedSessionName?.Value);
        Assert.Contains("# Session: smoke", output.ToString());
    }

    [Fact]
    public void Session_export_format_rejects_invalid_value()
    {
        using StringWriter output = new();
        RootCommand command = CliCommandFactory.Create(output, workspacePath => CreateSnapshot(workspacePath));

        int exitCode = CliCommandFactory.Invoke(command, ["session", "export", "--format", "xml", "smoke"], output);

        Assert.Equal(2, exitCode);
        Assert.Contains("Invalid value for --format. Allowed values are json and markdown.", output.ToString());
    }

    [Fact]
    public void Session_clear_deletes_transcript_file()
    {
        using TempDirectory temp = TempDirectory.Create();
        string userHome = Path.Combine(temp.Path, "user-home");
        string workspace = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(workspace);
        string sessionDirectory = Path.Combine(userHome, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        File.WriteAllText(Path.Combine(sessionDirectory, "smoke.transcript.json"), """
        {
          "schemaVersion": 1,
          "sessionName": "smoke",
          "createdAtUtc": "2024-01-01T00:00:00+00:00",
          "updatedAtUtc": "2024-01-01T00:00:00+00:00",
          "messages": [],
          "toolCalls": [],
          "errors": []
        }
        """);
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: workspace,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: Path.Combine(userHome, ".caicli", "config.json"));

        using StringWriter clearOutput = new();
        int clearExitCode = CliCommandFactory
            .Create(clearOutput, _ => snapshot)
            .Parse(["session", "clear", "smoke"])
            .Invoke();

        Assert.Equal(0, clearExitCode);
        Assert.Contains("status: cleared", clearOutput.ToString());
        Assert.False(File.Exists(Path.Combine(sessionDirectory, "smoke.transcript.json")));
    }

    [Fact]
    public void Session_list_writes_session_summaries()
    {
        using StringWriter output = new();
        FakeConversationStore store = new()
        {
            Summaries =
            [
                new ConversationTranscriptSummary(
                    "beta",
                    DateTimeOffset.Parse("2024-01-02T00:00:00Z"),
                    DateTimeOffset.Parse("2024-01-02T00:03:00Z"),
                    TurnCount: 4,
                    ToolCallCount: 3),
                new ConversationTranscriptSummary(
                    "alpha",
                    DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                    DateTimeOffset.Parse("2024-01-01T00:01:00Z"),
                    TurnCount: 2,
                    ToolCallCount: 1)
            ]
        };

        RootCommand command = CliCommandFactory.Create(
            output,
            CreateSnapshot,
            (_, _) => { },
            _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp_test", "ok"))),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => store,
            () => DateTimeOffset.Parse("2024-01-03T00:00:00Z"));

        int exitCode = CliCommandFactory.Invoke(command, ["session", "list"], output);

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Equal(
            [
                "C# AI CLI sessions",
                "- alpha created=2024-01-01T00:00:00.0000000+00:00 updated=2024-01-01T00:01:00.0000000+00:00 turns=2 toolCalls=1",
                "- beta created=2024-01-02T00:00:00.0000000+00:00 updated=2024-01-02T00:03:00.0000000+00:00 turns=4 toolCalls=3"
            ],
            text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void Session_list_with_malformed_file_transcript_returns_safe_failure()
    {
        using TempDirectory temp = TempDirectory.Create();
        string userHome = Path.Combine(temp.Path, "user-home");
        string workspace = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(workspace);
        string sessionDirectory = Path.Combine(userHome, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        File.WriteAllText(Path.Combine(sessionDirectory, "smoke.transcript.json"), """{"schemaVersion":""");
        using StringWriter output = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: workspace,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: Path.Combine(userHome, ".caicli", "config.json"));

        int exitCode = CliCommandFactory.Invoke(
            CliCommandFactory.Create(output, _ => snapshot),
            ["session", "list"],
            output);

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("status: failed", text);
        Assert.Contains("errorCode: session-transcript-invalid", text);
        Assert.Contains("Conversation transcript is missing or uses an unsupported schema version.", text);
        Assert.DoesNotContain("JsonReaderException", text, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Text.Json", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Session_list_writes_empty_status_for_empty_store()
    {
        using StringWriter output = new();
        FakeConversationStore store = new();

        RootCommand command = CliCommandFactory.Create(
            output,
            CreateSnapshot,
            (_, _) => { },
            _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp_test", "ok"))),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => store,
            () => DateTimeOffset.Parse("2024-01-03T00:00:00Z"));

        int exitCode = CliCommandFactory.Invoke(command, ["session", "list"], output);

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("C# AI CLI sessions", text);
        Assert.Contains("status: empty", text);
    }

    [Fact]
    public void Session_list_writes_command_log_through_delegate()
    {
        using StringWriter output = new();
        List<string> loggedCommands = [];
        FakeConversationStore store = new();

        RootCommand command = CliCommandFactory.Create(
            output,
            CreateSnapshot,
            (commandName, _) => loggedCommands.Add(commandName),
            _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp_test", "ok"))),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => store,
            () => DateTimeOffset.Parse("2024-01-03T00:00:00Z"));

        int exitCode = CliCommandFactory.Invoke(command, ["session", "list"], output);

        Assert.Equal(0, exitCode);
        Assert.Equal(["session list"], loggedCommands);
    }

    [Fact]
    public void Session_show_writes_session_summary()
    {
        using StringWriter output = new();
        FakeConversationStore store = new()
        {
            Summaries =
            [
                new ConversationTranscriptSummary(
                    "smoke",
                    DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                    DateTimeOffset.Parse("2024-01-01T00:05:00Z"),
                    TurnCount: 2,
                    ToolCallCount: 1)
            ]
        };

        RootCommand command = CliCommandFactory.Create(
            output,
            CreateSnapshot,
            (_, _) => { },
            _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp_test", "ok"))),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => store,
            () => DateTimeOffset.Parse("2024-01-03T00:00:00Z"));

        int exitCode = CliCommandFactory.Invoke(command, ["session", "show", "smoke"], output);

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Equal(
            [
                "C# AI CLI session",
                "name: smoke",
                "createdAtUtc: 2024-01-01T00:00:00.0000000+00:00",
                "updatedAtUtc: 2024-01-01T00:05:00.0000000+00:00",
                "turnCount: 2",
                "toolCallCount: 1"
            ],
            text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void Session_show_missing_session_returns_session_not_found()
    {
        using StringWriter output = new();
        FakeConversationStore store = new();

        RootCommand command = CliCommandFactory.Create(
            output,
            CreateSnapshot,
            (_, _) => { },
            _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp_test", "ok"))),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => store,
            () => DateTimeOffset.Parse("2024-01-03T00:00:00Z"));

        int exitCode = CliCommandFactory.Invoke(command, ["session", "show", "missing"], output);

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Equal(
            [
                "status: failed",
                "errorCode: session-not-found",
                "summary:",
                "Session transcript was not found."
            ],
            text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void Session_show_writes_command_log_through_delegate()
    {
        using StringWriter output = new();
        List<string> loggedCommands = [];
        FakeConversationStore store = new()
        {
            Summaries =
            [
                new ConversationTranscriptSummary(
                    "smoke",
                    DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                    DateTimeOffset.Parse("2024-01-01T00:05:00Z"),
                    TurnCount: 2,
                    ToolCallCount: 1)
            ]
        };

        RootCommand command = CliCommandFactory.Create(
            output,
            CreateSnapshot,
            (commandName, _) => loggedCommands.Add(commandName),
            _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp_test", "ok"))),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => store,
            () => DateTimeOffset.Parse("2024-01-03T00:00:00Z"));

        int exitCode = CliCommandFactory.Invoke(command, ["session", "show", "smoke"], output);

        Assert.Equal(0, exitCode);
        Assert.Equal(["session show"], loggedCommands);
    }

    [Fact]
    public void Session_commands_wrap_expected_store_failures_without_leaking_details()
    {
        static RootCommand CreateCommand(StringWriter output, FakeConversationStore store)
        {
            return CliCommandFactory.Create(
                output,
                CreateSnapshot,
                (_, _) => { },
                _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp_test", "ok"))),
                writer => new TerminalChatStreamingRenderer(writer),
                _ => store,
                () => DateTimeOffset.Parse("2024-01-03T00:00:00Z"));
        }

        foreach ((string[] args, FakeConversationStore store) in new[]
        {
            (new[] { "session", "list" }, new FakeConversationStore { ListSummariesException = new IOException("cannot read C:\\secret\\smoke.transcript.json") }),
            (new[] { "session", "show", "smoke" }, new FakeConversationStore { TryGetSummaryException = new IOException("cannot read C:\\secret\\smoke.transcript.json") }),
            (new[] { "session", "export", "--format", "markdown", "smoke" }, new FakeConversationStore { TryLoadException = new IOException("cannot read C:\\secret\\smoke.transcript.json") }),
            (new[] { "session", "clear", "smoke" }, new FakeConversationStore { DeleteException = new IOException("cannot delete C:\\secret\\smoke.transcript.json") }),
            (new[] { "session", "delete", "smoke" }, new FakeConversationStore { DeleteException = new IOException("cannot delete C:\\secret\\smoke.transcript.json") }),
            (new[] { "session", "rename", "smoke", "archive" }, new FakeConversationStore { RenameException = new IOException("cannot move C:\\secret\\smoke.transcript.json") }),
        })
        {
            using StringWriter output = new();
            int exitCode = CliCommandFactory.Invoke(CreateCommand(output, store), args, output);

            string text = output.ToString();
            Assert.Equal(1, exitCode);
            Assert.Contains("status: failed", text);
            Assert.Contains("errorCode: session-store-error", text);
            Assert.Contains("Conversation session store operation failed.", text);
            Assert.DoesNotContain("C:\\secret", text, StringComparison.Ordinal);
            Assert.DoesNotContain("System.", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Session_show_does_not_report_store_factory_argument_exception_as_invalid_session_name()
    {
        using StringWriter output = new();
        RootCommand command = CliCommandFactory.Create(
            output,
            CreateSnapshot,
            (_, _) => { },
            _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp_test", "ok"))),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => throw new ArgumentException("factory failed"),
            () => DateTimeOffset.Parse("2024-01-03T00:00:00Z"));

        int exitCode = CliCommandFactory.Invoke(command, ["session", "show", "smoke"], output);

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.DoesNotContain("errorCode: invalid-session-name", text, StringComparison.Ordinal);
        Assert.Contains("factory failed", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Session_rename_calls_store_and_writes_ordered_success_output()
    {
        using StringWriter output = new();
        FakeConversationStore store = new()
        {
            RenameResult = true
        };

        RootCommand command = CliCommandFactory.Create(
            output,
            CreateSnapshot,
            (_, _) => { },
            _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp_test", "ok"))),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => store,
            () => DateTimeOffset.Parse("2024-01-03T00:00:00Z"));

        int exitCode = CliCommandFactory.Invoke(command, ["session", "rename", "smoke", "archive"], output);

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Equal("smoke", store.RenamedSourceSessionName?.Value);
        Assert.Equal("archive", store.RenamedDestinationSessionName?.Value);
        Assert.Equal(
            [
                "status: renamed",
                "from: smoke",
                "to: archive"
            ],
            text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void Session_rename_with_file_store_moves_transcript_file_and_updates_session_name()
    {
        using TempDirectory temp = TempDirectory.Create();
        string userHome = Path.Combine(temp.Path, "user-home");
        string workspace = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(workspace);
        string sessionDirectory = Path.Combine(userHome, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        string sourcePath = Path.Combine(sessionDirectory, "smoke.transcript.json");
        string destinationPath = Path.Combine(sessionDirectory, "archive.transcript.json");
        File.WriteAllText(sourcePath, """
        {
          "schemaVersion": 1,
          "sessionName": "smoke",
          "createdAtUtc": "2024-01-01T00:00:00+00:00",
          "updatedAtUtc": "2024-01-01T00:00:01+00:00",
          "messages": [
            {
              "role": "user",
              "createdAtUtc": "2024-01-01T00:00:01+00:00",
              "content": "keep me",
              "provider": null,
              "model": null,
              "responseId": null
            }
          ],
          "toolCalls": [],
          "errors": []
        }
        """);
        using StringWriter output = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: workspace,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: Path.Combine(userHome, ".caicli", "config.json"));

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["session", "rename", "smoke", "archive"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.False(File.Exists(sourcePath));
        Assert.True(File.Exists(destinationPath));
        JsonObject json = ReadJsonObject(destinationPath);
        Assert.Equal("archive", json["sessionName"]?.GetValue<string>());
        Assert.Equal("keep me", json["messages"]?[0]?["content"]?.GetValue<string>());
        Assert.Contains("status: renamed", output.ToString());
    }

    [Fact]
    public void Session_rename_with_file_store_preserves_files_when_destination_exists()
    {
        using TempDirectory temp = TempDirectory.Create();
        string userHome = Path.Combine(temp.Path, "user-home");
        string workspace = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(workspace);
        string sessionDirectory = Path.Combine(userHome, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        string sourcePath = Path.Combine(sessionDirectory, "smoke.transcript.json");
        string destinationPath = Path.Combine(sessionDirectory, "archive.transcript.json");
        File.WriteAllText(sourcePath, """
        {
          "schemaVersion": 1,
          "sessionName": "smoke",
          "createdAtUtc": "2024-01-01T00:00:00+00:00",
          "updatedAtUtc": "2024-01-01T00:00:00+00:00",
          "messages": [],
          "toolCalls": [],
          "errors": []
        }
        """);
        File.WriteAllText(destinationPath, """
        {
          "schemaVersion": 1,
          "sessionName": "archive",
          "createdAtUtc": "2024-01-02T00:00:00+00:00",
          "updatedAtUtc": "2024-01-02T00:00:00+00:00",
          "messages": [],
          "toolCalls": [],
          "errors": []
        }
        """);
        string originalSourceJson = File.ReadAllText(sourcePath);
        string originalDestinationJson = File.ReadAllText(destinationPath);
        using StringWriter output = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: workspace,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: Path.Combine(userHome, ".caicli", "config.json"));

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["session", "rename", "smoke", "archive"])
            .Invoke();

        Assert.Equal(1, exitCode);
        Assert.Equal(originalSourceJson, File.ReadAllText(sourcePath));
        Assert.Equal(originalDestinationJson, File.ReadAllText(destinationPath));
        Assert.Contains("status: failed", output.ToString());
        Assert.Contains("errorCode: session-rename-failed", output.ToString());
    }

    [Fact]
    public void Session_rename_failure_returns_generic_safe_failure_output()
    {
        using StringWriter output = new();
        FakeConversationStore store = new()
        {
            RenameResult = false
        };

        RootCommand command = CliCommandFactory.Create(
            output,
            CreateSnapshot,
            (_, _) => { },
            _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp_test", "ok"))),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => store,
            () => DateTimeOffset.Parse("2024-01-03T00:00:00Z"));

        int exitCode = CliCommandFactory.Invoke(command, ["session", "rename", "missing", "archive"], output);

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Equal("missing", store.RenamedSourceSessionName?.Value);
        Assert.Equal("archive", store.RenamedDestinationSessionName?.Value);
        Assert.Equal(
            [
                "status: failed",
                "errorCode: session-rename-failed",
                "summary:",
                "Session could not be renamed because the source is missing or the destination already exists."
            ],
            text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void Session_rename_writes_command_log_through_delegate()
    {
        using StringWriter output = new();
        List<string> loggedCommands = [];
        FakeConversationStore store = new()
        {
            RenameResult = true
        };

        RootCommand command = CliCommandFactory.Create(
            output,
            CreateSnapshot,
            (commandName, _) => loggedCommands.Add(commandName),
            _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp_test", "ok"))),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => store,
            () => DateTimeOffset.Parse("2024-01-03T00:00:00Z"));

        int exitCode = CliCommandFactory.Invoke(command, ["session", "rename", "smoke", "archive"], output);

        Assert.Equal(0, exitCode);
        Assert.Equal(["session rename"], loggedCommands);
    }

    [Fact]
    public void Session_delete_calls_store_and_writes_ordered_success_output()
    {
        using StringWriter output = new();
        FakeConversationStore store = new()
        {
            DeleteResult = true
        };

        RootCommand command = CliCommandFactory.Create(
            output,
            CreateSnapshot,
            (_, _) => { },
            _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp_test", "ok"))),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => store,
            () => DateTimeOffset.Parse("2024-01-03T00:00:00Z"));

        int exitCode = CliCommandFactory.Invoke(command, ["session", "delete", "smoke"], output);

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Equal("smoke", store.DeletedSessionName?.Value);
        Assert.Equal(
            [
                "status: deleted",
                "session: smoke"
            ],
            text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void Session_delete_with_file_store_removes_transcript_file()
    {
        using TempDirectory temp = TempDirectory.Create();
        string userHome = Path.Combine(temp.Path, "user-home");
        string workspace = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(workspace);
        string sessionDirectory = Path.Combine(userHome, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        string sessionPath = Path.Combine(sessionDirectory, "smoke.transcript.json");
        File.WriteAllText(sessionPath, """
        {
          "schemaVersion": 1,
          "sessionName": "smoke",
          "createdAtUtc": "2024-01-01T00:00:00+00:00",
          "updatedAtUtc": "2024-01-01T00:00:00+00:00",
          "messages": [],
          "toolCalls": [],
          "errors": []
        }
        """);
        using StringWriter output = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: workspace,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: Path.Combine(userHome, ".caicli", "config.json"));

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["session", "delete", "smoke"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.False(File.Exists(sessionPath));
        Assert.Contains("status: deleted", output.ToString());
    }

    [Fact]
    public void Session_delete_missing_session_returns_session_not_found()
    {
        using StringWriter output = new();
        FakeConversationStore store = new()
        {
            DeleteResult = false
        };

        RootCommand command = CliCommandFactory.Create(
            output,
            CreateSnapshot,
            (_, _) => { },
            _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp_test", "ok"))),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => store,
            () => DateTimeOffset.Parse("2024-01-03T00:00:00Z"));

        int exitCode = CliCommandFactory.Invoke(command, ["session", "delete", "missing"], output);

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Equal("missing", store.DeletedSessionName?.Value);
        Assert.Equal(
            [
                "status: not-found",
                "errorCode: session-not-found"
            ],
            text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void Session_clear_calls_store_delete_and_preserves_success_output()
    {
        using StringWriter output = new();
        FakeConversationStore store = new()
        {
            DeleteResult = true
        };

        RootCommand command = CliCommandFactory.Create(
            output,
            CreateSnapshot,
            (_, _) => { },
            _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp_test", "ok"))),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => store,
            () => DateTimeOffset.Parse("2024-01-03T00:00:00Z"));

        int exitCode = CliCommandFactory.Invoke(command, ["session", "clear", "smoke"], output);

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Equal("smoke", store.DeletedSessionName?.Value);
        Assert.Equal(
            [
                "status: cleared",
                "session: smoke"
            ],
            text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void Session_clear_missing_session_preserves_not_found_exit_zero_output()
    {
        using StringWriter output = new();
        FakeConversationStore store = new()
        {
            DeleteResult = false
        };

        RootCommand command = CliCommandFactory.Create(
            output,
            CreateSnapshot,
            (_, _) => { },
            _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp_test", "ok"))),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => store,
            () => DateTimeOffset.Parse("2024-01-03T00:00:00Z"));

        int exitCode = CliCommandFactory.Invoke(command, ["session", "clear", "missing"], output);

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Equal("missing", store.DeletedSessionName?.Value);
        Assert.Equal(
            [
                "status: not-found"
            ],
            text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void Session_delete_writes_command_log_through_delegate()
    {
        using StringWriter output = new();
        List<string> loggedCommands = [];
        FakeConversationStore store = new()
        {
            DeleteResult = true
        };

        RootCommand command = CliCommandFactory.Create(
            output,
            CreateSnapshot,
            (commandName, _) => loggedCommands.Add(commandName),
            _ => new FakeChatModelClient(ChatModelResult.Success(new ChatResponse("openai", "gpt-test", "resp_test", "ok"))),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => store,
            () => DateTimeOffset.Parse("2024-01-03T00:00:00Z"));

        int exitCode = CliCommandFactory.Invoke(command, ["session", "delete", "smoke"], output);

        Assert.Equal(0, exitCode);
        Assert.Equal(["session delete"], loggedCommands);
    }

    [Fact]
    public void Session_export_invalid_session_name_returns_safe_failure()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: temp.Path,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: Path.Combine(temp.Path, ".caicli", "config.json"));
        RootCommand command = CliCommandFactory.Create(output, _ => snapshot);

        int exitCode = CliCommandFactory.Invoke(command, ["session", "export", "../secret"], output);

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("status: failed", text);
        Assert.Contains("errorCode: invalid-session-name", text);
        Assert.Contains("Session name contains invalid path characters.", text);
        Assert.DoesNotContain("Unhandled exception", text, StringComparison.Ordinal);
        Assert.DoesNotContain("System.", text, StringComparison.Ordinal);
        Assert.DoesNotContain(temp.Path, text, StringComparison.OrdinalIgnoreCase);
    }

    public static TheoryData<string[]> InvalidSessionNameCommandCases => new()
    {
        new[] { "session", "show", "../secret" },
        new[] { "session", "export", "../secret" },
        new[] { "session", "export", "--format", "markdown", "../secret" },
        new[] { "session", "clear", "../secret" },
        new[] { "session", "delete", "../secret" },
        new[] { "session", "rename", "../secret", "archive" },
        new[] { "session", "rename", "smoke", "../secret" },
    };

    [Theory]
    [MemberData(nameof(InvalidSessionNameCommandCases))]
    public void Session_commands_reject_invalid_session_names_without_creating_files(string[] args)
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string userHome = Path.Combine(temp.Path, "user-home");
        string workspace = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(workspace);
        string sessionDirectory = Path.Combine(userHome, ".caicli", "sessions");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: workspace,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: Path.Combine(userHome, ".caicli", "config.json"));

        RootCommand command = CliCommandFactory.Create(output, _ => snapshot);

        int exitCode = CliCommandFactory.Invoke(command, args, output);

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("status: failed", text);
        Assert.Contains("errorCode: invalid-session-name", text);
        Assert.Contains("summary:", text);
        Assert.Contains("Session name contains invalid path characters.", text);
        Assert.DoesNotContain("Unhandled exception", text, StringComparison.Ordinal);
        Assert.DoesNotContain("System.", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Directory.GetCurrentDirectory(), text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(temp.Path, text, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(sessionDirectory));
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
    public void Workspace_option_is_passed_to_config_list_command()
    {
        using StringWriter output = new();
        string? receivedWorkspace = null;

        int exitCode = CliCommandFactory
            .Create(output, workspacePath =>
            {
                receivedWorkspace = workspacePath;
                return CreateSnapshot(workspacePath);
            })
            .Parse(["config", "list", "--workspace", "custom-root"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal("custom-root", receivedWorkspace);
        Assert.Contains("workspace: custom-root", output.ToString());
    }

    [Fact]
    public void Logs_path_prints_resolved_cli_log_directory_without_creating_it()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string expectedPath = Path.Combine(temp.Path, ".caicli", "logs");

        int exitCode = CliCommandFactory
            .Create(output, _ => CreateSnapshot(temp.Path))
            .Parse(["logs", "path"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal(expectedPath + Environment.NewLine, output.ToString());
        Assert.False(Directory.Exists(expectedPath));
    }

    [Fact]
    public void Logs_path_with_real_command_logger_prints_path_without_creating_log_directory()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string expectedPath = Path.Combine(temp.Path, ".caicli", "logs");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(temp.Path);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot, (commandName, commandSnapshot) => CommandLogger.Append(commandName, commandSnapshot))
            .Parse(["logs", "path"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal(expectedPath + Environment.NewLine, output.ToString());
        Assert.False(Directory.Exists(expectedPath));
    }

    [Fact]
    public void Logs_path_honors_workspace_option()
    {
        using StringWriter output = new();
        string? receivedWorkspace = null;

        int exitCode = CliCommandFactory
            .Create(output, workspacePath =>
            {
                receivedWorkspace = workspacePath;
                return CreateSnapshot(workspacePath);
            })
            .Parse(["logs", "path", "--workspace", "custom-root"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal("custom-root", receivedWorkspace);
        Assert.Equal(Path.Combine("custom-root", ".caicli", "logs") + Environment.NewLine, output.ToString());
    }

    [Fact]
    public void Logs_path_writes_command_log_through_delegate_when_log_directory_exists()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        List<string> loggedCommands = [];
        CliEnvironmentSnapshot snapshot = CreateSnapshot(temp.Path);
        Directory.CreateDirectory(LogPathResolver.ResolveLogDirectory(snapshot));

        int exitCode = CliCommandFactory
            .Create(
                output,
                _ => snapshot,
                (commandName, _) => loggedCommands.Add(commandName))
            .Parse(["logs", "path"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal(["logs path"], loggedCommands);
    }

    [Fact]
    public void Logs_path_verbose_writes_diagnostics_before_path()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string expectedPath = Path.Combine(temp.Path, ".caicli", "logs");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: temp.Path,
            apiKey: "sk-secret",
            apiKeySource: "OPENAI_API_KEY",
            model: "gpt-test");

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["logs", "path", "--verbose"])
            .Invoke();

        string text = output.ToString();
        int diagnosticsIndex = text.IndexOf("C# AI CLI verbose diagnostics", StringComparison.Ordinal);
        int pathIndex = text.LastIndexOf(expectedPath, StringComparison.Ordinal);
        Assert.Equal(0, exitCode);
        Assert.True(diagnosticsIndex >= 0, text);
        Assert.True(pathIndex > diagnosticsIndex, text);
        Assert.True(text.EndsWith(expectedPath + Environment.NewLine, StringComparison.Ordinal), text);
        Assert.DoesNotContain("sk-secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Logs_show_tail_reads_command_and_trace_logs_in_filename_order()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        List<string> loggedCommands = [];
        CliEnvironmentSnapshot snapshot = CreateSnapshot(temp.Path);
        string logDirectory = LogPathResolver.ResolveLogDirectory(snapshot);
        Directory.CreateDirectory(logDirectory);
        File.WriteAllLines(
            Path.Combine(logDirectory, "2026-07-08.log"),
            ["old command 1", "old command 2"]);
        File.WriteAllLines(
            Path.Combine(logDirectory, "2026-07-08.trace.log"),
            ["old trace 1"]);
        File.WriteAllLines(
            Path.Combine(logDirectory, "2026-07-09.log"),
            ["new command 1", "new command 2"]);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot, (commandName, _) => loggedCommands.Add(commandName))
            .Parse(["logs", "show", "--tail", "3"])
            .Invoke();

        string expected = string.Join(
            Environment.NewLine,
            ["old trace 1", "new command 1", "new command 2"]) + Environment.NewLine;
        Assert.Equal(0, exitCode);
        Assert.Equal(expected, output.ToString());
        Assert.Empty(loggedCommands);
    }

    [Fact]
    public void Logs_show_skips_locked_log_files_and_returns_readable_lines()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(temp.Path);
        string logDirectory = LogPathResolver.ResolveLogDirectory(snapshot);
        Directory.CreateDirectory(logDirectory);
        File.WriteAllLines(
            Path.Combine(logDirectory, "2026-07-08.log"),
            ["readable 1", "readable 2"]);
        string lockedLogPath = Path.Combine(logDirectory, "2026-07-09.log");
        File.WriteAllLines(lockedLogPath, ["locked line"]);
        using FileStream lockedLog = File.Open(lockedLogPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["logs", "show", "--tail", "10"])
            .Invoke();

        string expected = string.Join(
            Environment.NewLine,
            ["readable 1", "readable 2"]) + Environment.NewLine;
        Assert.Equal(0, exitCode);
        Assert.Equal(expected, output.ToString());
    }

    [Fact]
    public void Logs_show_defaults_to_tail_20()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(temp.Path);
        string logDirectory = LogPathResolver.ResolveLogDirectory(snapshot);
        Directory.CreateDirectory(logDirectory);
        File.WriteAllLines(
            Path.Combine(logDirectory, "2026-07-09.log"),
            Enumerable.Range(1, 25).Select(index => $"line-{index:00}"));

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["logs", "show"])
            .Invoke();

        string expected = string.Join(
            Environment.NewLine,
            Enumerable.Range(6, 20).Select(index => $"line-{index:00}")) + Environment.NewLine;
        Assert.Equal(0, exitCode);
        Assert.Equal(expected, output.ToString());
    }

    [Fact]
    public void Logs_show_missing_log_directory_succeeds_without_output_or_creating_it()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(temp.Path);
        string logDirectory = LogPathResolver.ResolveLogDirectory(snapshot);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot, (commandName, commandSnapshot) => CommandLogger.Append(commandName, commandSnapshot))
            .Parse(["logs", "show"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.False(Directory.Exists(logDirectory));
    }

    [Fact]
    public void Logs_show_honors_workspace_option()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string workspaceRoot = Path.Combine(temp.Path, "custom-root");
        string? receivedWorkspace = null;
        CliEnvironmentSnapshot snapshot = CreateSnapshot(workspaceRoot);
        string logDirectory = LogPathResolver.ResolveLogDirectory(snapshot);
        Directory.CreateDirectory(logDirectory);
        File.WriteAllLines(Path.Combine(logDirectory, "2026-07-09.log"), ["workspace log"]);

        int exitCode = CliCommandFactory
            .Create(output, workspacePath =>
            {
                receivedWorkspace = workspacePath;
                return CreateSnapshot(workspacePath);
            })
            .Parse(["logs", "show", "--workspace", workspaceRoot])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal(workspaceRoot, receivedWorkspace);
        Assert.Equal("workspace log" + Environment.NewLine, output.ToString());
    }

    [Fact]
    public void Logs_show_tail_rejects_nonpositive_values_before_reading_logs()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        List<string> loggedCommands = [];
        CliEnvironmentSnapshot snapshot = CreateSnapshot(temp.Path);
        string logDirectory = LogPathResolver.ResolveLogDirectory(snapshot);
        Directory.CreateDirectory(logDirectory);
        File.WriteAllLines(Path.Combine(logDirectory, "2026-07-09.log"), ["line"]);
        RootCommand command = CliCommandFactory.Create(
            output,
            _ => snapshot,
            (commandName, _) => loggedCommands.Add(commandName));

        int exitCode = CliCommandFactory.Invoke(command, ["logs", "show", "--tail", "0"], output);

        Assert.Equal(2, exitCode);
        Assert.Contains("greater than zero", output.ToString(), StringComparison.Ordinal);
        Assert.Empty(loggedCommands);
    }

    [Fact]
    public void Logs_show_verbose_writes_diagnostics_before_log_lines()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: temp.Path,
            apiKey: "sk-secret",
            apiKeySource: "OPENAI_API_KEY",
            model: "gpt-test");
        string logDirectory = LogPathResolver.ResolveLogDirectory(snapshot);
        Directory.CreateDirectory(logDirectory);
        File.WriteAllLines(Path.Combine(logDirectory, "2026-07-09.log"), ["visible log"]);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["logs", "show", "--verbose"])
            .Invoke();

        string text = output.ToString();
        int diagnosticsIndex = text.IndexOf("C# AI CLI verbose diagnostics", StringComparison.Ordinal);
        int logLineIndex = text.LastIndexOf("visible log", StringComparison.Ordinal);
        Assert.Equal(0, exitCode);
        Assert.True(diagnosticsIndex >= 0, text);
        Assert.True(logLineIndex > diagnosticsIndex, text);
        Assert.True(text.EndsWith("visible log" + Environment.NewLine, StringComparison.Ordinal), text);
        Assert.DoesNotContain("sk-secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Logs_clear_deletes_direct_log_files_and_reports_count()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(temp.Path);
        string logDirectory = LogPathResolver.ResolveLogDirectory(snapshot);
        Directory.CreateDirectory(logDirectory);
        string commandLogPath = Path.Combine(logDirectory, "2026-07-09.log");
        string traceLogPath = Path.Combine(logDirectory, "2026-07-09.trace.log");
        File.WriteAllText(commandLogPath, "command");
        File.WriteAllText(traceLogPath, "trace");

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["logs", "clear"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal("Cleared 2 log file(s)." + Environment.NewLine, output.ToString());
        Assert.False(File.Exists(commandLogPath));
        Assert.False(File.Exists(traceLogPath));
    }

    [Fact]
    public void Logs_clear_keeps_non_log_files_subdirectories_workspace_files_and_other_caicli_files()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(temp.Path);
        string caicliDirectory = Path.Combine(temp.Path, ".caicli");
        string logDirectory = LogPathResolver.ResolveLogDirectory(snapshot);
        Directory.CreateDirectory(logDirectory);
        string directLogPath = Path.Combine(logDirectory, "2026-07-09.log");
        string textPath = Path.Combine(logDirectory, "notes.txt");
        string backupPath = Path.Combine(logDirectory, "2026-07-09.log.bak");
        string archiveDirectory = Path.Combine(logDirectory, "archive");
        Directory.CreateDirectory(archiveDirectory);
        string nestedLogPath = Path.Combine(archiveDirectory, "2026-07-08.log");
        string configPath = Path.Combine(caicliDirectory, "config.json");
        string workspaceLogPath = Path.Combine(temp.Path, "workspace.log");
        File.WriteAllText(directLogPath, "delete");
        File.WriteAllText(textPath, "keep");
        File.WriteAllText(backupPath, "keep");
        File.WriteAllText(nestedLogPath, "keep");
        File.WriteAllText(configPath, "{}");
        File.WriteAllText(workspaceLogPath, "keep");

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["logs", "clear"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal("Cleared 1 log file(s)." + Environment.NewLine, output.ToString());
        Assert.False(File.Exists(directLogPath));
        Assert.True(File.Exists(textPath));
        Assert.True(File.Exists(backupPath));
        Assert.True(Directory.Exists(archiveDirectory));
        Assert.True(File.Exists(nestedLogPath));
        Assert.True(File.Exists(configPath));
        Assert.True(File.Exists(workspaceLogPath));
    }

    [Fact]
    public void Logs_clear_missing_log_directory_succeeds_without_output_or_creating_it()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(temp.Path);
        string logDirectory = LogPathResolver.ResolveLogDirectory(snapshot);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["logs", "clear"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.False(Directory.Exists(logDirectory));
    }

    [Fact]
    public void Logs_clear_honors_workspace_option()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string workspaceRoot = Path.Combine(temp.Path, "custom-root");
        string? receivedWorkspace = null;
        CliEnvironmentSnapshot snapshot = CreateSnapshot(workspaceRoot);
        string logDirectory = LogPathResolver.ResolveLogDirectory(snapshot);
        Directory.CreateDirectory(logDirectory);
        string logPath = Path.Combine(logDirectory, "2026-07-09.log");
        File.WriteAllText(logPath, "workspace log");

        int exitCode = CliCommandFactory
            .Create(output, workspacePath =>
            {
                receivedWorkspace = workspacePath;
                return CreateSnapshot(workspacePath);
            })
            .Parse(["logs", "clear", "--workspace", workspaceRoot])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal(workspaceRoot, receivedWorkspace);
        Assert.Equal("Cleared 1 log file(s)." + Environment.NewLine, output.ToString());
        Assert.False(File.Exists(logPath));
    }

    [Fact]
    public void Logs_clear_skips_locked_log_files_without_failing()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(temp.Path);
        string logDirectory = LogPathResolver.ResolveLogDirectory(snapshot);
        Directory.CreateDirectory(logDirectory);
        string readableLogPath = Path.Combine(logDirectory, "2026-07-08.log");
        string lockedLogPath = Path.Combine(logDirectory, "2026-07-09.log");
        File.WriteAllText(readableLogPath, "readable");
        File.WriteAllText(lockedLogPath, "locked");
        using FileStream lockedLog = File.Open(lockedLogPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["logs", "clear"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal("Cleared 1 log file(s)." + Environment.NewLine, output.ToString());
        Assert.False(File.Exists(readableLogPath));
        Assert.True(File.Exists(lockedLogPath));
    }

    [Fact]
    public void Logs_clear_skips_log_directory_symlink_or_junction()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string workspaceRoot = Path.Combine(temp.Path, "workspace");
        string outsideTarget = Path.Combine(temp.Path, "outside-target");
        Directory.CreateDirectory(outsideTarget);
        string outsideLogPath = Path.Combine(outsideTarget, "outside.log");
        File.WriteAllText(outsideLogPath, "outside");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(workspaceRoot);
        string logDirectory = LogPathResolver.ResolveLogDirectory(snapshot);
        Directory.CreateDirectory(Path.GetDirectoryName(logDirectory)!);
        if (!TryCreateDirectoryLink(logDirectory, outsideTarget))
        {
            return;
        }

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["logs", "clear"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(outsideLogPath));
    }

    [Fact]
    public void Logs_clear_skips_when_caicli_directory_is_symlink_or_junction()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string workspaceRoot = Path.Combine(temp.Path, "workspace");
        string outsideTarget = Path.Combine(temp.Path, "outside-target");
        string outsideLogsDirectory = Path.Combine(outsideTarget, "logs");
        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(outsideLogsDirectory);
        string outsideLogPath = Path.Combine(outsideLogsDirectory, "outside.log");
        File.WriteAllText(outsideLogPath, "outside");
        string caicliDirectory = Path.Combine(workspaceRoot, ".caicli");
        if (!TryCreateDirectoryLink(caicliDirectory, outsideTarget))
        {
            return;
        }

        CliEnvironmentSnapshot snapshot = CreateSnapshot(workspaceRoot);

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["logs", "clear"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(outsideLogPath));
    }

    [Fact]
    public void Logs_clear_does_not_call_command_logger_or_create_new_log_entry()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        List<string> loggedCommands = [];
        CliEnvironmentSnapshot snapshot = CreateSnapshot(temp.Path);
        string logDirectory = LogPathResolver.ResolveLogDirectory(snapshot);
        Directory.CreateDirectory(logDirectory);

        int exitCode = CliCommandFactory
            .Create(
                output,
                _ => snapshot,
                (commandName, commandSnapshot) =>
                {
                    loggedCommands.Add(commandName);
                    CommandLogger.Append(commandName, commandSnapshot);
                })
            .Parse(["logs", "clear"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal("Cleared 0 log file(s)." + Environment.NewLine, output.ToString());
        Assert.Empty(loggedCommands);
        Assert.Empty(Directory.GetFiles(logDirectory, "*.log"));
    }

    [Fact]
    public void Logs_clear_verbose_writes_diagnostics_before_summary()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: temp.Path,
            apiKey: "sk-secret",
            apiKeySource: "OPENAI_API_KEY",
            model: "gpt-test");
        string logDirectory = LogPathResolver.ResolveLogDirectory(snapshot);
        Directory.CreateDirectory(logDirectory);
        File.WriteAllText(Path.Combine(logDirectory, "2026-07-09.log"), "visible log");

        int exitCode = CliCommandFactory
            .Create(output, _ => snapshot)
            .Parse(["logs", "clear", "--verbose"])
            .Invoke();

        string text = output.ToString();
        int diagnosticsIndex = text.IndexOf("C# AI CLI verbose diagnostics", StringComparison.Ordinal);
        int summaryIndex = text.LastIndexOf("Cleared 1 log file(s).", StringComparison.Ordinal);
        Assert.Equal(0, exitCode);
        Assert.True(diagnosticsIndex >= 0, text);
        Assert.True(summaryIndex > diagnosticsIndex, text);
        Assert.True(text.EndsWith("Cleared 1 log file(s)." + Environment.NewLine, StringComparison.Ordinal), text);
        Assert.DoesNotContain("commandName: logs show", text, StringComparison.Ordinal);
        Assert.Contains("commandName: logs clear", text, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-secret", text, StringComparison.Ordinal);
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
    public void Status_command_writes_command_log_through_delegate()
    {
        using StringWriter output = new();
        List<string> loggedCommands = [];

        int exitCode = CliCommandFactory
            .Create(
                output,
                CreateSnapshot,
                (commandName, _) => loggedCommands.Add(commandName))
            .Parse(["status"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal(["status"], loggedCommands);
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
    public void Config_list_command_writes_command_log_through_delegate()
    {
        using StringWriter output = new();
        List<string> loggedCommands = [];

        int exitCode = CliCommandFactory
            .Create(
                output,
                CreateSnapshot,
                (commandName, _) => loggedCommands.Add(commandName))
            .Parse(["config", "list"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal(["config list"], loggedCommands);
    }

    [Fact]
    public void Config_set_command_writes_command_log_through_delegate()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        List<string> loggedCommands = [];
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: userConfigPath);

        int exitCode = CliCommandFactory
            .Create(
                output,
                _ => snapshot,
                (commandName, _) => loggedCommands.Add(commandName))
            .Parse(["config", "set", "model", "gpt-test"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal(["config set"], loggedCommands);
    }

    [Fact]
    public void Config_unset_command_writes_command_log_through_delegate()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        List<string> loggedCommands = [];
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured",
            userConfigPath: userConfigPath);

        int exitCode = CliCommandFactory
            .Create(
                output,
                _ => snapshot,
                (commandName, _) => loggedCommands.Add(commandName))
            .Parse(["config", "unset", "model"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal(["config unset"], loggedCommands);
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
        Assert.Null(chatClient.LastRequest?.Instructions);
        Assert.Null(chatClient.LastNonStreamingPrompt);
        Assert.Contains("status: streaming", output.ToString());
        Assert.Contains("fake model output", output.ToString());
        Assert.Contains("status: completed", output.ToString());
    }

    [Fact]
    public void Chat_command_without_cwd_keeps_legacy_snapshot_provider_compatibility()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string? receivedWorkspace = null;
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
                (_, _) => { },
                _ => chatClient,
                writer => new TerminalChatStreamingRenderer(writer))
            .Parse(["chat", "--workspace", temp.Path, "hello model"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal(temp.Path, receivedWorkspace);
        Assert.Equal("hello model", chatClient.LastRequest?.Prompt);
        Assert.Null(chatClient.LastRequest?.Instructions);
    }

    [Fact]
    public void Chat_command_passes_workspace_instructions_to_model_request()
    {
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "fake model output")));
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath: null,
            apiKey: "sk-test",
            apiKeySource: "OPENAI_API_KEY",
            model: "gpt-test")
            with
            {
                Instructions = InstructionLoadResult.Loaded("Be concise.", Path.Combine("workspace-root", "AICLI.md"))
            };

        int exitCode = CliCommandFactory
            .Create(
                output,
                _ => snapshot,
                (_, _) => { },
                _ => chatClient,
                writer => new TerminalChatStreamingRenderer(writer))
            .Parse(["chat", "hello model"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal("hello model", chatClient.LastRequest?.Prompt);
        Assert.Equal("Be concise.", chatClient.LastRequest?.Instructions);
    }

    [Fact]
    public void Chat_command_passes_cwd_to_snapshot_provider_and_merged_instructions_to_model_request()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string cwd = Path.Combine("src", "app");
        string? receivedWorkspace = null;
        string? receivedCwd = null;
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "fake model output")));
        RootCommand command = CliCommandFactory.Create(
            output,
            (workspacePath, instructionTargetPath) =>
            {
                receivedWorkspace = workspacePath;
                receivedCwd = instructionTargetPath;
                string workspaceRoot = workspacePath ?? temp.Path;
                return CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test")
                    with
                    {
                        Instructions = InstructionLoadResult.Loaded(
                            "Root rules\n\nApp rules",
                            [
                                new InstructionSource(Path.Combine(workspaceRoot, "AICLI.md"), 0),
                                new InstructionSource(Path.Combine(workspaceRoot, "src", "app", "AICLI.md"), 1)
                            ])
                    };
            },
            (_, _) => { },
            _ => chatClient,
            writer => new TerminalChatStreamingRenderer(writer),
            _ => new FakeConversationStore(),
            () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
            (_, _, _) => new FakeAgentRunner(AgentRunResult.Success("agent completed task", [])));

        int exitCode = CliCommandFactory.Invoke(command, ["chat", "--workspace", temp.Path, "--cwd", cwd, "hello model"], output);

        Assert.Equal(0, exitCode);
        Assert.Equal(temp.Path, receivedWorkspace);
        Assert.Equal(cwd, receivedCwd);
        Assert.Equal("hello model", chatClient.LastRequest?.Prompt);
        Assert.Equal("Root rules\n\nApp rules", chatClient.LastRequest?.Instructions);
        Assert.DoesNotContain("Root rules", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Chat_command_with_real_snapshot_loads_merged_cwd_instructions_without_printing_them()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        string workspaceRoot = Path.Combine(temp.Path, "workspace");
        string homeDirectory = Path.Combine(temp.Path, "home");
        string appDirectory = Path.Combine(workspaceRoot, "src", "app");
        Directory.CreateDirectory(homeDirectory);
        Directory.CreateDirectory(appDirectory);

        string rootInstruction = "root-secret instructions.";
        string appInstruction = "app-secret instructions.";
        File.WriteAllText(Path.Combine(workspaceRoot, "AICLI.md"), rootInstruction);
        File.WriteAllText(Path.Combine(appDirectory, "AGENTS.md"), appInstruction);

        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "fake model output")));
        RootCommand command = CliCommandFactory.Create(
            output,
            (workspacePath, instructionTargetPath) => CliEnvironmentSnapshot.Create(
                workspacePath: workspacePath,
                currentDirectory: temp.Path,
                userProfile: homeDirectory,
                dotnetSdkVersion: "9.0.308",
                dotnetRuntime: ".NET 9.0.0",
                openAiApiKey: "sk-test-secret",
                openAiModel: "gpt-test",
                hasGlobalJson: false,
                instructionTargetPath: instructionTargetPath),
            (_, _) => { },
            _ => chatClient,
            writer => new TerminalChatStreamingRenderer(writer),
            _ => new FakeConversationStore(),
            () => DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
            (_, _, _) => new FakeAgentRunner(AgentRunResult.Success("agent completed task", [])));

        int exitCode = CliCommandFactory.Invoke(
            command,
            ["chat", "--workspace", workspaceRoot, "--cwd", Path.Combine("src", "app"), "hello model"],
            output);

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Equal("hello model", chatClient.LastRequest?.Prompt);
        Assert.Equal(
            string.Join(
                $"{Environment.NewLine}{Environment.NewLine}",
                rootInstruction,
                appInstruction),
            chatClient.LastRequest?.Instructions);
        Assert.Contains("fake model output", text);
        Assert.DoesNotContain(rootInstruction, text, StringComparison.Ordinal);
        Assert.DoesNotContain(appInstruction, text, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-test-secret", text, StringComparison.Ordinal);
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
        Assert.Null(chatClient.LastRequest?.Instructions);
        Assert.Equal("smoke", store.LoadedSessionName?.Value);
        Assert.Equal("smoke", store.SavedSessionName?.Value);
        Assert.NotNull(store.SavedTranscript);
        Assert.Equal(2, store.SavedTranscript.Messages.Count);
        Assert.Equal("hello model", store.SavedTranscript.Messages[0].Content);
        Assert.Equal("fake model output", store.SavedTranscript.Messages[1].Content);
    }

    [Fact]
    public void Chat_resume_requires_existing_transcript_records_success_and_saves()
    {
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "fake model output")));
        ConversationTranscript existing = ConversationTranscript.Create(
            "smoke",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        FakeConversationStore store = new()
        {
            Transcript = existing
        };

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
                (_, _) => { },
                _ => chatClient,
                writer => new TerminalChatStreamingRenderer(writer),
                _ => store,
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"))
            .Parse(["chat", "--resume", "smoke", "hello model"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal("smoke", chatClient.LastRequest?.SessionName);
        Assert.Same(existing, chatClient.LastRequest?.TranscriptContext);
        Assert.Equal("smoke", store.TryLoadedSessionName?.Value);
        Assert.Null(store.LoadedSessionName);
        Assert.Equal("smoke", store.SavedSessionName?.Value);
        Assert.Same(existing, store.SavedTranscript);
        ConversationTranscript savedTranscript = Assert.IsType<ConversationTranscript>(store.SavedTranscript);
        Assert.Equal(2, savedTranscript.Messages.Count);
        Assert.Equal("hello model", savedTranscript.Messages[0].Content);
        Assert.Equal("fake model output", savedTranscript.Messages[1].Content);
    }

    [Fact]
    public void Chat_resume_missing_returns_session_not_found_without_calling_model()
    {
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
        Text: "fake model output")));
        FakeConversationStore store = new()
        {
            TryLoadResult = false
        };

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
                (_, _) => { },
                _ => chatClient,
                writer => new TerminalChatStreamingRenderer(writer),
                _ => store,
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"))
            .Parse(["chat", "--resume", "missing", "hello model"])
            .Invoke();

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("status: failed", text);
        Assert.Contains("errorCode: session-not-found", text);
        Assert.Contains("summary:", text);
        Assert.Contains("Session transcript was not found.", text);
        Assert.Equal("missing", store.TryLoadedSessionName?.Value);
        Assert.Null(store.ExistsSessionName);
        Assert.Null(store.LoadedSessionName);
        Assert.Null(store.SavedSessionName);
        Assert.Null(chatClient.LastRequest);
    }

    [Fact]
    public void Chat_resume_malformed_file_transcript_returns_safe_failure_without_calling_model()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "fake model output")));
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        string sessionDirectory = Path.Combine(temp.Path, ".caicli", "sessions");
        Directory.CreateDirectory(sessionDirectory);
        File.WriteAllText(Path.Combine(sessionDirectory, "smoke.transcript.json"), """{"schemaVersion":""");
        RootCommand command = CliCommandFactory.Create(
            output,
            workspacePath => CreateSnapshot(
                workspacePath,
                apiKey: "sk-test",
                apiKeySource: "OPENAI_API_KEY",
                model: "gpt-test",
                userConfigPath: userConfigPath),
            (_, _) => { },
            _ => chatClient,
            writer => new TerminalChatStreamingRenderer(writer),
            snapshot => FileConversationStore.Create(snapshot),
            () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"));

        int exitCode = CliCommandFactory.Invoke(command, ["chat", "--resume", "smoke", "hello model"], output);

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("status: failed", text);
        Assert.Contains("errorCode: session-transcript-invalid", text);
        Assert.Contains("Conversation transcript is missing or uses an unsupported schema version.", text);
        Assert.DoesNotContain("System.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonReaderException", text, StringComparison.Ordinal);
        Assert.Null(chatClient.LastRequest);
    }

    [Fact]
    public void Chat_session_load_or_create_exception_returns_safe_failure_without_calling_model()
    {
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "fake model output")));
        FakeConversationStore store = new()
        {
            LoadOrCreateException = new IOException("cannot read C:\\secret\\smoke.transcript.json")
        };
        RootCommand command = CliCommandFactory.Create(
            output,
            workspacePath => CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
            (_, _) => { },
            _ => chatClient,
            writer => new TerminalChatStreamingRenderer(writer),
            _ => store,
            () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"));

        int exitCode = CliCommandFactory.Invoke(command, ["chat", "--session", "smoke", "hello model"], output);

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("status: failed", text);
        Assert.Contains("errorCode: session-store-error", text);
        Assert.Contains("Conversation session store operation failed.", text);
        Assert.DoesNotContain("C:\\secret", text, StringComparison.Ordinal);
        Assert.Equal("smoke", store.LoadedSessionName?.Value);
        Assert.Null(store.SavedSessionName);
        Assert.Null(chatClient.LastRequest);
    }

    [Fact]
    public void Chat_resume_empty_session_name_rejects_without_calling_model_or_store()
    {
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "fake model output")));
        FakeConversationStore store = new();
        bool storeFactoryInvoked = false;
        RootCommand command = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
                (_, _) => { },
                _ => chatClient,
                writer => new TerminalChatStreamingRenderer(writer),
                _ =>
                {
                    storeFactoryInvoked = true;
                    return store;
                },
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"));

        int exitCode = CliCommandFactory.Invoke(command, ["chat", "--resume", "", "hello model"], output);

        Assert.Equal(1, exitCode);
        Assert.Contains("Session name must not be empty.", output.ToString());
        Assert.False(storeFactoryInvoked);
        Assert.Null(store.ExistsSessionName);
        Assert.Null(store.TryLoadedSessionName);
        Assert.Null(store.LoadedSessionName);
        Assert.Null(store.SavedSessionName);
        Assert.Null(chatClient.LastRequest);
    }

    [Fact]
    public void Chat_session_whitespace_session_name_rejects_without_calling_model_or_store()
    {
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "fake model output")));
        FakeConversationStore store = new();
        bool storeFactoryInvoked = false;
        RootCommand command = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
                (_, _) => { },
                _ => chatClient,
                writer => new TerminalChatStreamingRenderer(writer),
                _ =>
                {
                    storeFactoryInvoked = true;
                    return store;
                },
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"));

        int exitCode = CliCommandFactory.Invoke(command, ["chat", "--session", " ", "hello model"], output);

        Assert.Equal(1, exitCode);
        Assert.Contains("Session name must not be empty.", output.ToString());
        Assert.False(storeFactoryInvoked);
        Assert.Null(store.ExistsSessionName);
        Assert.Null(store.TryLoadedSessionName);
        Assert.Null(store.LoadedSessionName);
        Assert.Null(store.SavedSessionName);
        Assert.Null(chatClient.LastRequest);
    }

    [Fact]
    public void Chat_empty_session_and_resume_conflict_returns_failure_without_calling_model_or_store()
    {
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "fake model output")));
        FakeConversationStore store = new();
        bool storeFactoryInvoked = false;
        RootCommand command = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
                (_, _) => { },
                _ => chatClient,
                writer => new TerminalChatStreamingRenderer(writer),
                _ =>
                {
                    storeFactoryInvoked = true;
                    return store;
                },
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"));

        int exitCode = CliCommandFactory.Invoke(command, ["chat", "--session", "", "--resume", "smoke", "hello model"], output);

        Assert.Equal(1, exitCode);
        Assert.Contains("session-option-conflict", output.ToString());
        Assert.False(storeFactoryInvoked);
        Assert.Null(store.ExistsSessionName);
        Assert.Null(store.TryLoadedSessionName);
        Assert.Null(store.LoadedSessionName);
        Assert.Null(store.SavedSessionName);
        Assert.Null(chatClient.LastRequest);
    }

    [Fact]
    public void Chat_session_and_resume_conflict_returns_failure_without_calling_model()
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
            .Parse(["chat", "--session", "smoke", "--resume", "smoke", "hello model"])
            .Invoke();

        Assert.Equal(1, exitCode);
        Assert.Contains("session-option-conflict", output.ToString());
        Assert.Null(store.ExistsSessionName);
        Assert.Null(store.LoadedSessionName);
        Assert.Null(chatClient.LastRequest);
    }

    [Fact]
    public void Chat_session_appends_to_existing_transcript()
    {
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_second",
            Text: "second response")));
        ConversationTranscript existing = ConversationTranscript.Create(
            "smoke",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        existing.AddUserMessage("first prompt", DateTimeOffset.Parse("2024-01-01T00:00:01Z"));
        existing.AddAssistantMessage(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_first",
            Text: "first response"), DateTimeOffset.Parse("2024-01-01T00:00:02Z"));
        FakeConversationStore store = new()
        {
            Transcript = existing
        };

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
                (_, _) => { },
                _ => chatClient,
                writer => new TerminalChatStreamingRenderer(writer),
                _ => store,
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"))
            .Parse(["chat", "--session", "smoke", "second prompt"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal("second prompt", chatClient.LastStreamingPrompt);
        Assert.NotNull(store.SavedTranscript);
        Assert.Equal(
            ["first prompt", "first response", "second prompt", "second response"],
            store.SavedTranscript.Messages.Select(message => message.Content).ToArray());
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
    public void Chat_session_save_exception_returns_safe_failure_without_leaking_exception_detail()
    {
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "fake model output")));
        FakeConversationStore store = new()
        {
            SaveException = new IOException("cannot write C:\\secret\\smoke.transcript.json")
        };
        RootCommand command = CliCommandFactory.Create(
            output,
            workspacePath => CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
            (_, _) => { },
            _ => chatClient,
            writer => new TerminalChatStreamingRenderer(writer),
            _ => store,
            () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"));

        int exitCode = CliCommandFactory.Invoke(command, ["chat", "--session", "smoke", "hello model"], output);

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("status: failed", text);
        Assert.Contains("errorCode: session-store-error", text);
        Assert.Contains("Conversation session store operation failed.", text);
        Assert.DoesNotContain("C:\\secret", text, StringComparison.Ordinal);
        Assert.Equal("smoke", chatClient.LastRequest?.SessionName);
        Assert.Equal("smoke", store.SavedSessionName?.Value);
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
        string model,
        IReadOnlyList<CliConfigFileSource>? configSources = null,
        IReadOnlySet<string>? disabledTools = null,
        string? userConfigPath = null,
        string baseUrl = "https://api.openai.com/v1",
        string baseUrlSource = "default",
        AgentRunLimits? agentRunLimits = null)
    {
        string workspaceRoot = string.IsNullOrWhiteSpace(workspacePath) ? "workspace-root" : workspacePath;

        WorkspaceContext workspace = new(
            RootPath: workspaceRoot,
            ConfigPath: Path.Combine(workspaceRoot, ".caicli", "config.json"),
            Status: WorkspaceStatus.Ready);

        EffectiveConfiguration configuration = new(
            WorkspaceRoot: workspaceRoot,
            UserConfigPath: userConfigPath ?? Path.Combine("user-home", ".caicli", "config.json"),
            WorkspaceConfigPath: Path.Combine(workspaceRoot, ".caicli", "config.json"),
            Model: model,
            ModelSource: model == "not configured" ? "default" : "workspace config",
            AgentBackend: "direct",
            AgentBackendSource: "default",
            DisabledTools: disabledTools ?? new HashSet<string>(StringComparer.Ordinal),
            ApiKey: SecretValue.From(apiKey),
            ApiKeySource: apiKeySource,
            LoadedConfigPaths: [],
            Warnings: [],
            ConfigSources: configSources ?? [])
        {
            BaseUrl = baseUrl,
            BaseUrlSource = baseUrlSource,
            AgentRunLimits = agentRunLimits ?? AgentRunLimits.Default
        };

        return new CliEnvironmentSnapshot(
            Workspace: workspace,
            Configuration: configuration,
            DotnetSdkVersion: "9.0.308",
            DotnetRuntime: ".NET 9.0.0",
            TargetFramework: "net9.0",
            HasGlobalJson: false);
    }

    private static AgentRunResult CreateSecretBearingExecAgentResult(string prefix)
    {
        AgentRunEvent secretEvent = new(
            Type: "tool.call",
            Sequence: 0,
            Timestamp: DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
            Message: $"message password={prefix}-message-password authorization=Bearer {prefix}-message-auth-secret",
            Summary: $"summary apiKey={prefix}-summary-secret",
            Payload: new Dictionary<string, string>
            {
                ["argumentsJson"] = $$"""
                {"apiKey":"sk-{{prefix}}-argument-api-secret","nested":{"password":"{{prefix}}-argument-password-secret","authorization":"Bearer {{prefix}}-argument-bearer-secret","Authorization":"Basic {{prefix}}-argument-basic-secret","secretKey":"{{prefix}}-argument-secret-key-secret","privateKey":"{{prefix}}-argument-private-key-secret","github":"ghp_{{prefix}}argumentsecret1234567890","githubPat":"github_pat_{{prefix}}_argument_secret_1234567890","escaped":"{\"secretKey\":\"{{prefix}}-escaped-secret-key-secret\",\"authorization\":\"Bearer {{prefix}}-escaped-auth-secret\"}"},"path":"note.txt"}
                """,
                ["toolName"] = "workspace.search",
                ["path"] = "note.txt",
                ["apiKey"] = $"{prefix}-payload-api-key-secret",
                ["password"] = $"{prefix}-payload-password-secret",
                ["authorization"] = $"Bearer {prefix}-payload-authorization-secret",
                ["secretKey"] = $"{prefix}-payload-secret-key-secret",
                ["privateKey"] = $"{prefix}-payload-private-key-secret",
                [$"sk-{prefix}-payload-key-one"] = "first key should keep its value",
                [$"sk-{prefix}-payload-key-two"] = "second key should keep its value"
            },
            ApprovalStatus: "approved",
            Status: "started",
            DurationMs: 42,
            ApprovalDurationMs: 5);

        return AgentRunResult.Success($"result apiKey={prefix}-result-secret", [], [secretEvent]);
    }

    private static string[] CreateExecStdoutRawSecrets(string prefix)
    {
        return
        [
            "sk-test-secret",
            $"{prefix}-message-password",
            $"{prefix}-message-auth-secret",
            $"{prefix}-summary-secret",
            $"{prefix}-result-secret",
            $"sk-{prefix}-argument-api-secret",
            $"{prefix}-argument-password-secret",
            $"{prefix}-argument-bearer-secret",
            $"{prefix}-argument-basic-secret",
            $"{prefix}-argument-secret-key-secret",
            $"{prefix}-argument-private-key-secret",
            $"{prefix}-escaped-secret-key-secret",
            $"{prefix}-escaped-auth-secret",
            $"ghp_{prefix}argumentsecret1234567890",
            $"github_pat_{prefix}_argument_secret_1234567890",
            $"{prefix}-payload-api-key-secret",
            $"{prefix}-payload-password-secret",
            $"{prefix}-payload-authorization-secret",
            $"{prefix}-payload-secret-key-secret",
            $"{prefix}-payload-private-key-secret",
            $"sk-{prefix}-payload-key-one",
            $"sk-{prefix}-payload-key-two"
        ];
    }

    private static void AssertDoesNotContainSecrets(string text, IEnumerable<string> rawSecrets)
    {
        foreach (string rawSecret in rawSecrets)
        {
            Assert.DoesNotContain(rawSecret, text, StringComparison.Ordinal);
        }
    }

    private static CliEnvironmentSnapshot CreateSnapshotWithShellPolicy(
        string workspacePath,
        ShellPolicyConfiguration shellPolicy)
    {
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            workspacePath,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured");

        return snapshot with
        {
            Configuration = snapshot.Configuration with
            {
                ShellPolicy = shellPolicy
            }
        };
    }

    private static JsonObject ReadJsonObject(string path)
    {
        JsonNode? node = JsonNode.Parse(File.ReadAllText(path));
        return Assert.IsType<JsonObject>(node);
    }

    private static JsonObject AssertSingleExecJsonResult(StringWriter output)
    {
        string[] lines = output.ToString().TrimEnd().Split(Environment.NewLine);
        JsonObject result = Assert.IsType<JsonObject>(JsonNode.Parse(Assert.Single(lines)));
        Assert.Equal("exec.result", result["type"]?.GetValue<string>());
        return result;
    }

    private static JsonObject AssertLastExecJsonResult(StringWriter output)
    {
        string[] lines = output.ToString()
            .TrimEnd()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        JsonObject result = Assert.IsType<JsonObject>(JsonNode.Parse(lines[^1]));
        Assert.Equal("exec.result", result["type"]?.GetValue<string>());
        return result;
    }

    private static JsonObject AssertSingleReviewJsonResult(StringWriter output)
    {
        string[] lines = output.ToString().TrimEnd().Split(Environment.NewLine);
        JsonObject result = Assert.IsType<JsonObject>(JsonNode.Parse(Assert.Single(lines)));
        Assert.Equal("review.result", result["type"]?.GetValue<string>());
        return result;
    }

    private static int InvokeToolsCallPatch(TempDirectory temp, StringWriter output, string[] approvalArgs)
    {
        File.WriteAllText(Path.Combine(temp.Path, "note.txt"), "before");
        string argumentsPath = Path.Combine(temp.Path, "arguments.json");
        File.WriteAllText(argumentsPath, """{"path":"note.txt","find":"before","replace":"after"}""");

        List<string> args =
        [
            "tools",
            "call",
            "--workspace",
            temp.Path
        ];
        args.AddRange(approvalArgs);
        args.Add("workspace.apply_patch");
        args.Add("--arguments-file");
        args.Add(argumentsPath);

        return CliCommandFactory
            .Create(output, workspacePath => CreateSnapshot(workspacePath))
            .Parse([.. args])
            .Invoke();
    }

    private static McpServerConfig CreateMcpEchoServerConfig(string scriptPath)
    {
        return new McpServerConfig
        {
            Enabled = true,
            Transport = "stdio",
            Command = PowerShellExecutable,
            Args =
            [
                "-NoLogo",
                "-NoProfile",
                "-NonInteractive",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                scriptPath
            ],
            TimeoutMilliseconds = 10_000
        };
    }

    private static string WriteMcpEchoServerScript(string directory, string? startupMarkerPath = null)
    {
        string scriptPath = Path.Combine(directory, "mcp-echo-fixture-" + Guid.NewGuid().ToString("N") + ".ps1");
        string startupMarkerScript = string.IsNullOrWhiteSpace(startupMarkerPath)
            ? string.Empty
            : $"[System.IO.File]::WriteAllText('{startupMarkerPath.Replace("'", "''", StringComparison.Ordinal)}', 'started'){Environment.NewLine}";
        File.WriteAllText(
            scriptPath,
            startupMarkerScript +
            """
            while (($line = [Console]::In.ReadLine()) -ne $null) {
                $request = $line | ConvertFrom-Json
                if ($null -eq $request.id) {
                    continue
                }

                if ($request.method -eq 'initialize') {
                    $result = [ordered]@{
                        protocolVersion = '2025-03-26'
                        capabilities = [ordered]@{}
                        serverInfo = [ordered]@{
                            name = 'cli-test-mcp'
                            version = '1.0.0'
                        }
                    }
                    $response = [ordered]@{
                        jsonrpc = '2.0'
                        id = $request.id
                        result = $result
                    } | ConvertTo-Json -Compress -Depth 10
                    [Console]::Out.WriteLine($response)
                    continue
                }

                if ($request.method -eq 'tools/list') {
                    $result = [ordered]@{
                        tools = @(
                            [ordered]@{
                                name = 'echo'
                                description = 'Echo from MCP.'
                                inputSchema = [ordered]@{
                                    type = 'object'
                                    properties = [ordered]@{
                                        text = [ordered]@{
                                            type = 'string'
                                        }
                                    }
                                }
                            }
                        )
                    }
                    $response = [ordered]@{
                        jsonrpc = '2.0'
                        id = $request.id
                        result = $result
                    } | ConvertTo-Json -Compress -Depth 10
                    [Console]::Out.WriteLine($response)
                    continue
                }

                if ($request.method -eq 'tools/call') {
                    $result = [ordered]@{
                        content = @(
                            [ordered]@{
                                type = 'text'
                                text = ('echo: ' + $request.params.arguments.text)
                            }
                        )
                    }
                    $response = [ordered]@{
                        jsonrpc = '2.0'
                        id = $request.id
                        result = $result
                    } | ConvertTo-Json -Compress -Depth 10
                    [Console]::Out.WriteLine($response)
                    continue
                }

                $response = [ordered]@{
                    jsonrpc = '2.0'
                    id = $request.id
                    error = [ordered]@{
                        code = -32601
                        message = 'Method not found'
                    }
                } | ConvertTo-Json -Compress -Depth 10
                [Console]::Out.WriteLine($response)
            }
            """);
        return scriptPath;
    }

    private static string PowerShellExecutable => OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh";

    private static string PowerShellPolicyCommandName => OperatingSystem.IsWindows() ? "powershell" : "pwsh";

    private static void InitializeNoHeadGitRepository(string root)
    {
        RunGit(root, "init");
        RunGit(root, "config", "user.email", "test@example.invalid");
        RunGit(root, "config", "user.name", "Test User");
    }

    private static string InitializeGitRepository(string root, string? configuredHooksPath = null)
    {
        RunGit(root, "init");
        RunGit(root, "config", "user.email", "test@example.invalid");
        RunGit(root, "config", "user.name", "Test User");
        if (!string.IsNullOrWhiteSpace(configuredHooksPath))
        {
            RunGit(root, "config", "core.hooksPath", configuredHooksPath);
        }

        string filePath = Path.Combine(root, "tracked.txt");
        File.WriteAllText(filePath, "original\n");
        RunGit(root, "add", "tracked.txt");
        string emptyHooksPath = Path.Combine(root, ".caicli-empty-hooks");
        Directory.CreateDirectory(emptyHooksPath);
        RunGit(
            root,
            "-c",
            "commit.gpgSign=false",
            "-c",
            "core.hooksPath=" + emptyHooksPath,
            "commit",
            "--no-gpg-sign",
            "--no-verify",
            "-m",
            "initial");
        return filePath;
    }

    private static void WriteFailingHook(string hooksPath, string hookName)
    {
        Directory.CreateDirectory(hooksPath);
        File.WriteAllText(
            Path.Combine(hooksPath, hookName),
            "#!/bin/sh\necho configured hook failed >&2\nexit 1\n");
    }

    private static void WriteCliCommandLog(string root)
    {
        string logsPath = Path.Combine(root, ".caicli", "logs");
        Directory.CreateDirectory(logsPath);
        File.WriteAllText(Path.Combine(logsPath, "2026-07-09.log"), "command=diff\n");
    }

    private static string TrackCliCommandLogs(string root)
    {
        string logsPath = Path.Combine(root, ".caicli", "logs");
        Directory.CreateDirectory(logsPath);
        DateTime utcToday = DateTime.UtcNow.Date;
        string currentLogPath = Path.Combine(logsPath, utcToday.ToString("yyyy-MM-dd") + ".log");
        foreach (DateTime date in new[] { utcToday.AddDays(-1), utcToday, utcToday.AddDays(1) })
        {
            File.WriteAllText(Path.Combine(logsPath, date.ToString("yyyy-MM-dd") + ".log"), "initial\n");
        }

        RunGit(root, "add", ".caicli/logs");
        CommitAll(root, "track cli logs");
        return currentLogPath;
    }

    private static void RunGit(string workingDirectory, params string[] arguments)
    {
        ProcessStartInfo startInfo = new("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new()
        {
            StartInfo = startInfo
        };

        process.Start();
        string commandText = string.Join(" ", arguments);
        Assert.True(process.WaitForExit(10_000), "git command timed out: " + commandText);
        string stderr = process.StandardError.ReadToEnd();
        Assert.True(process.ExitCode == 0, $"git {commandText} failed: {stderr}");
    }

    private static void CommitAll(string workingDirectory, string message)
    {
        string emptyHooksPath = Path.Combine(workingDirectory, ".caicli-empty-hooks");
        Directory.CreateDirectory(emptyHooksPath);
        RunGit(
            workingDirectory,
            "-c",
            "commit.gpgSign=false",
            "-c",
            "core.hooksPath=" + emptyHooksPath,
            "commit",
            "--no-gpg-sign",
            "--no-verify",
            "-m",
            message);
    }

    private static bool TryCreateDirectoryLink(string linkPath, string targetPath)
    {
        try
        {
            new DirectoryInfo(linkPath).CreateAsSymbolicLink(targetPath);
            return true;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or PlatformNotSupportedException
            or NotSupportedException)
        {
            return OperatingSystem.IsWindows() && TryCreateWindowsJunction(linkPath, targetPath);
        }
    }

    private static bool TryCreateWindowsJunction(string linkPath, string targetPath)
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo("cmd.exe")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            }
        };

        process.StartInfo.ArgumentList.Add("/c");
        process.StartInfo.ArgumentList.Add("mklink");
        process.StartInfo.ArgumentList.Add("/J");
        process.StartInfo.ArgumentList.Add(linkPath);
        process.StartInfo.ArgumentList.Add(targetPath);

        try
        {
            process.Start();
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempDirectory Create()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "caicli-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                ClearReadOnlyAttributes(Path);
                Directory.Delete(Path, recursive: true);
            }
        }

        private static void ClearReadOnlyAttributes(string path)
        {
            foreach (string filePath in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(filePath, FileAttributes.Normal);
            }

            foreach (string directoryPath in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(directoryPath, FileAttributes.Normal);
            }
        }
    }

    private static CliConfigFileSource CreateWorkflowSource()
    {
        return new CliConfigFileSource(
            "workspace config",
            "workspace-config.json",
            new CliConfigFile
            {
                WorkflowProfiles = new Dictionary<string, WorkflowProfileConfig>
                {
                    ["cpp"] = new()
                    {
                        WorkspacePath = null,
                        ValidationCommand = "dotnet test"
                    }
                }
            });
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

    private sealed class FakeAgentRunner(AgentRunResult result) : IAgentRunner
    {
        public AgentRunRequest? LastRequest { get; private set; }
        public ConversationTranscript? LastTranscript { get; private set; }

        public AgentRunResult Run(
            AgentRunRequest request,
            ConversationTranscript? transcript = null,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            LastTranscript = transcript;
            return result;
        }
    }

    private sealed class TranscriptRecordingAgentRunner(
        string callId,
        string toolName,
        string argumentsJson,
        ToolExecutionResult result) : IAgentRunner
    {
        public ConversationTranscript? LastTranscript { get; private set; }

        public AgentRunResult Run(
            AgentRunRequest request,
            ConversationTranscript? transcript = null,
            CancellationToken cancellationToken = default)
        {
            LastTranscript = transcript;
            ConversationToolCall toolCall = ConversationToolCall.FromExecution(
                callId,
                toolName,
                argumentsJson,
                result,
                DateTimeOffset.Parse("2024-01-01T00:00:06Z"));
            transcript?.AddToolCall(toolCall);
            return AgentRunResult.Success("agent completed task", [toolCall], []);
        }
    }

    private sealed class ExecutorToolCallAgentRunner(
        string toolName,
        string argumentsJson) : IAgentRunner
    {
        public IToolExecutor? Executor { get; set; }

        public AgentRunRequest? LastRequest { get; private set; }

        public AgentRunResult Run(
            AgentRunRequest request,
            ConversationTranscript? transcript = null,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            IToolExecutor executor = Executor ?? throw new InvalidOperationException("Executor was not injected.");
            ToolExecutionResult result = executor.Execute(
                toolName,
                new ToolExecutionContext("call_tool_1", request.Workspace, argumentsJson),
                cancellationToken);

            AgentRunEvent toolEvent = new(
                Type: result.Succeeded ? "tool.completed" : "tool.failed",
                Sequence: 0,
                Timestamp: DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                Summary: result.Summary,
                Payload: new Dictionary<string, string>
                {
                    ["toolName"] = toolName
                },
                ErrorCode: result.ErrorCode,
                ApprovalStatus: result.ApprovalStatus);

            ConversationToolCall toolCall = ConversationToolCall.FromExecution(
                "call_tool_1",
                toolName,
                argumentsJson,
                result,
                DateTimeOffset.Parse("2024-01-01T00:00:00Z"));

            return result.Succeeded
                ? AgentRunResult.Success(result.Summary, [toolCall], [toolEvent])
                : AgentRunResult.Failure(
                    new AgentError(
                        result.ErrorCode ?? "tool-call-failed",
                        result.Summary,
                        result.Retryable),
                    [toolCall],
                    [toolEvent]);
        }
    }

    private sealed class ThrowingAgentRunner(Exception exception) : IAgentRunner
    {
        public AgentRunResult Run(
            AgentRunRequest request,
            ConversationTranscript? transcript = null,
            CancellationToken cancellationToken = default)
        {
            throw exception;
        }
    }

    private sealed class FakeConversationStore : IConversationStore
    {
        public ConversationSessionName? ExistsSessionName { get; private set; }
        public ConversationSessionName? TryLoadedSessionName { get; private set; }
        public ConversationSessionName? LoadedSessionName { get; private set; }
        public ConversationSessionName? SavedSessionName { get; private set; }
        public ConversationTranscript? SavedTranscript { get; private set; }
        public ConversationSessionName? RenamedSourceSessionName { get; private set; }
        public ConversationSessionName? RenamedDestinationSessionName { get; private set; }
        public ConversationSessionName? DeletedSessionName { get; private set; }
        public bool ExistsResult { get; init; } = true;
        public bool TryLoadResult { get; init; } = true;
        public bool RenameResult { get; init; }
        public bool DeleteResult { get; init; }
        public Exception? LoadOrCreateException { get; init; }
        public Exception? SaveException { get; init; }
        public Exception? TryLoadException { get; init; }
        public Exception? ListSummariesException { get; init; }
        public Exception? TryGetSummaryException { get; init; }
        public Exception? RenameException { get; init; }
        public Exception? DeleteException { get; init; }
        public ConversationTranscript Transcript { get; init; } = ConversationTranscript.Create(
            "smoke",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        public IReadOnlyList<ConversationTranscriptSummary> Summaries { get; init; } = [];

        public bool Exists(ConversationSessionName sessionName)
        {
            ExistsSessionName = sessionName;
            return ExistsResult;
        }

        public bool TryLoad(ConversationSessionName sessionName, out ConversationTranscript? transcript)
        {
            TryLoadedSessionName = sessionName;
            if (TryLoadException is not null)
            {
                throw TryLoadException;
            }

            transcript = TryLoadResult ? Transcript : null;
            return TryLoadResult;
        }

        public ConversationTranscript LoadOrCreate(ConversationSessionName sessionName, DateTimeOffset nowUtc)
        {
            LoadedSessionName = sessionName;
            if (LoadOrCreateException is not null)
            {
                throw LoadOrCreateException;
            }

            return Transcript;
        }

        public string Save(ConversationSessionName sessionName, ConversationTranscript transcript)
        {
            SavedSessionName = sessionName;
            SavedTranscript = transcript;
            if (SaveException is not null)
            {
                throw SaveException;
            }

            return Path.Combine("user-home", ".caicli", "sessions", $"{sessionName.FileSafeName}.transcript.json");
        }

        public IReadOnlyList<ConversationTranscriptSummary> ListSummaries()
        {
            if (ListSummariesException is not null)
            {
                throw ListSummariesException;
            }

            return Summaries;
        }

        public bool TryGetSummary(ConversationSessionName sessionName, out ConversationTranscriptSummary? summary)
        {
            if (TryGetSummaryException is not null)
            {
                throw TryGetSummaryException;
            }

            summary = Summaries.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, sessionName.Value, StringComparison.Ordinal));
            return summary is not null;
        }

        public bool Rename(ConversationSessionName sourceSessionName, ConversationSessionName destinationSessionName)
        {
            RenamedSourceSessionName = sourceSessionName;
            RenamedDestinationSessionName = destinationSessionName;
            if (RenameException is not null)
            {
                throw RenameException;
            }

            return RenameResult;
        }

        public bool Delete(ConversationSessionName sessionName)
        {
            DeletedSessionName = sessionName;
            if (DeleteException is not null)
            {
                throw DeleteException;
            }

            return DeleteResult;
        }
    }
}
