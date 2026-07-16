using System.Diagnostics;
using System.Text.Json.Nodes;
using CSharpAiCli.Application;
using CSharpAiCli.Cli;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ChangesApplicationServiceTests
{
    [Fact]
    public void Query_projection_has_stable_text_golden_and_redacts_file_names()
    {
        using TempDirectory temp = TempDirectory.Create();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(temp.Path);
        ChangesApplicationService service = new(
            _ => new EmptyConversationStore(),
            (_, _) => ToolExecutionResult.Success(" M tracked.txt\n?? apiKey=plain-secret.txt"),
            (_, _) => ToolExecutionResult.Success("tracked.txt | 1 +"));

        ApplicationResult<ChangesViewReport> result = service.Query(new ChangesQueryRequest(snapshot));
        using StringWriter output = new();
        new ChangesTextRenderer(output).Write(result.Data!);
        string text = output.ToString();

        Assert.True(result.Succeeded);
        Assert.DoesNotContain("plain-secret", text, StringComparison.Ordinal);
        Assert.Equal(
            string.Join(Environment.NewLine,
            [
                "C# AI CLI changes",
                "status: dirty",
                $"workspace: {temp.Path}",
                "dirty: true",
                "gitStatusSucceeded: true",
                "gitStatus:",
                " M tracked.txt\n?? apiKey=[redacted]",
                "gitDiffSucceeded: true",
                "gitDiffTruncated: false",
                "diffStat:",
                "tracked.txt | 1 +",
                "changedFiles:",
                "- tracked.txt status=M",
                "- apiKey=[redacted] status=??",
                "taskReportSource: none",
                string.Empty
            ]),
            text);
    }

    [Fact]
    public void Real_cli_and_application_changes_queries_are_equivalent()
    {
        using TempDirectory temp = TempDirectory.Create();
        string tracked = InitializeGitRepository(temp.Path);
        File.AppendAllText(tracked, "changed\n");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(temp.Path);

        ApplicationResult<ChangesViewReport> application = new ChangesApplicationService().Query(
            new ChangesQueryRequest(snapshot));
        using StringWriter output = new();
        int exitCode = CliCommandFactory.Create(output, _ => snapshot)
            .Parse(["changes", "--output", "json", "--workspace", temp.Path])
            .Invoke();
        JsonObject cli = Assert.IsType<JsonObject>(JsonNode.Parse(output.ToString()));

        Assert.True(application.Succeeded);
        Assert.Equal(application.Data?.ExitCode, exitCode);
        Assert.Equal(application.Data?.Status, cli["status"]?.GetValue<string>());
        Assert.Equal(application.Data?.WorkspaceRoot, cli["workspace"]?.GetValue<string>());
        Assert.Equal(application.Data?.Dirty, cli["git"]?["dirty"]?.GetValue<bool>());
        Assert.Equal(
            application.Data?.ChangedFiles.Select(file => file.Path),
            cli["changedFiles"]!.AsArray().Select(item => item?["path"]?.GetValue<string>()));
    }

    [Fact]
    public void Query_stops_between_io_units_when_cancelled()
    {
        using TempDirectory temp = TempDirectory.Create();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(temp.Path);
        using CancellationTokenSource cancellation = new();
        bool diffCalled = false;
        ChangesApplicationService service = new(
            _ => new EmptyConversationStore(),
            (_, _) =>
            {
                cancellation.Cancel();
                return ToolExecutionResult.Success("working tree clean");
            },
            (_, _) =>
            {
                diffCalled = true;
                return ToolExecutionResult.Success("no diff");
            });

        Assert.Throws<OperationCanceledException>(() =>
            service.Query(new ChangesQueryRequest(snapshot), cancellation.Token));
        Assert.False(diffCalled);
    }

    [Fact]
    public void Query_redacts_nested_session_task_report_before_rendering()
    {
        using TempDirectory temp = TempDirectory.Create();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(temp.Path);
        ConversationTranscript transcript = ConversationTranscript.Create(
            "review",
            DateTimeOffset.Parse("2026-07-16T00:00:00Z"));
        transcript.AddAgentRun(new ConversationAgentRun(
            DateTimeOffset.Parse("2026-07-16T00:00:01Z"),
            "success",
            "completed",
            null,
            "done",
            1,
            0,
            TaskReport: new AgentTaskReport(
                "success",
                "completed",
                "apiKey=nested-secret",
                null,
                [],
                [],
                [new AgentTaskCommandReport("verification", "tool --token nested-token")],
                [],
                ["password=nested-password"],
                null)));
        ChangesApplicationService service = new(
            _ => new TranscriptConversationStore(transcript),
            (_, _) => ToolExecutionResult.Success("working tree clean"),
            (_, _) => ToolExecutionResult.Success("no diff"));

        ApplicationResult<ChangesViewReport> result = service.Query(
            new ChangesQueryRequest(snapshot, ConversationSessionName.Parse("review")));
        string json = System.Text.Json.JsonSerializer.Serialize(result.Data);

        Assert.DoesNotContain("nested-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("nested-token", json, StringComparison.Ordinal);
        Assert.DoesNotContain("nested-password", json, StringComparison.Ordinal);
        Assert.Contains("[redacted]", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Query_preserves_missing_task_report_warning_for_cli_renderer()
    {
        using TempDirectory temp = TempDirectory.Create();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(temp.Path);
        ConversationTranscript transcript = ConversationTranscript.Create(
            "review",
            DateTimeOffset.Parse("2026-07-16T00:00:00Z"));
        ChangesApplicationService service = new(
            _ => new TranscriptConversationStore(transcript),
            (_, _) => ToolExecutionResult.Success("working tree clean"),
            (_, _) => ToolExecutionResult.Success("no diff"));

        ApplicationResult<ChangesViewReport> result = service.Query(
            new ChangesQueryRequest(snapshot, ConversationSessionName.Parse("review")));

        Assert.Contains(
            "Session transcript does not contain an agent task report.",
            result.Data?.Warnings ?? []);
    }

    private static CliEnvironmentSnapshot CreateSnapshot(string workspace) => CliEnvironmentSnapshot.Create(
        workspace,
        currentDirectory: workspace,
        userProfile: Path.Combine(workspace, ".test-profile"),
        dotnetSdkVersion: "9.0.308",
        dotnetRuntime: ".NET 9",
        openAiApiKey: null,
        hasGlobalJson: false);

    private static string InitializeGitRepository(string root)
    {
        RunGit(root, "init");
        RunGit(root, "config", "user.email", "test@example.invalid");
        RunGit(root, "config", "user.name", "Test User");
        string filePath = Path.Combine(root, "tracked.txt");
        File.WriteAllText(filePath, "original\n");
        RunGit(root, "add", "tracked.txt");
        RunGit(root, "-c", "commit.gpgSign=false", "commit", "--no-gpg-sign", "--no-verify", "-m", "initial");
        return filePath;
    }

    private static void RunGit(string workingDirectory, params string[] arguments)
    {
        ProcessStartInfo startInfo = new("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new() { StartInfo = startInfo };
        process.Start();
        Assert.True(process.WaitForExit(10_000));
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
    }

    private sealed class EmptyConversationStore : IConversationStore
    {
        public ConversationTranscript LoadOrCreate(ConversationSessionName sessionName, DateTimeOffset nowUtc) =>
            ConversationTranscript.Create(sessionName.Value, nowUtc);

        public string Save(ConversationSessionName sessionName, ConversationTranscript transcript) => string.Empty;
    }

    private sealed class TranscriptConversationStore(ConversationTranscript transcript) : IConversationStore
    {
        public bool TryLoad(ConversationSessionName sessionName, out ConversationTranscript? loaded)
        {
            loaded = transcript;
            return true;
        }

        public ConversationTranscript LoadOrCreate(ConversationSessionName sessionName, DateTimeOffset nowUtc) => transcript;

        public string Save(ConversationSessionName sessionName, ConversationTranscript value) => string.Empty;
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;

        public string Path { get; }

        public static TempDirectory Create()
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "caicli-changes-application-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                foreach (string file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
