using System.Diagnostics;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class Phase03AcceptanceTests
{
    [Fact]
    public void Offline_agent_loop_can_read_search_patch_shell_and_report_git_status()
    {
        using TempDirectory temp = TempDirectory.Create();
        InitializeGitRepository(temp.Path);
        string filePath = Path.Combine(temp.Path, "README.md");
        File.WriteAllText(filePath, "needle before\n");
        RunGit(temp.Path, "add README.md");
        RunGit(temp.Path, "commit -m add-readme");

        WorkspaceGuard guard = new();
        ToolRegistry registry = new();
        registry.Register(new WorkspaceFileReadTool(guard));
        registry.Register(new WorkspaceSearchTool(guard));
        registry.Register(new WorkspacePatchTool(
            new SingleFilePatchApplier(
                guard,
                new StaticDirtyWorkspaceDetector(new DirtyWorkspaceStatus(false, "clean"))),
            new AlwaysApproveApprovalPolicy()));
        registry.Register(new WorkspaceShellTool(
            new RestrictedShellRunner(guard),
            new AlwaysApproveApprovalPolicy()));
        registry.Register(new GitStatusTool(guard));
        registry.Register(new GitDiffTool(guard));

        OfflineAgentRunner runner = new(
            new Phase03ToolCallingModel(),
            new ToolExecutor(registry),
            () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
            maxIterations: 12);
        ConversationTranscript transcript = ConversationTranscript.Create(
            "phase03",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));

        AgentRunResult result = runner.Run(new AgentRunRequest(
            "complete phase 03 workflow",
            WorkspaceContext.Detect(temp.Path, temp.Path)), transcript);

        Assert.True(result.IsSuccess);
        Assert.Equal("phase03 accepted", result.Text);
        Assert.Equal(6, transcript.ToolCalls.Count);
        Assert.All(transcript.ToolCalls, toolCall => Assert.True(toolCall.Succeeded));
        Assert.Equal("needle after\n", File.ReadAllText(filePath));
        Assert.Contains("M README.md", transcript.ToolCalls[4].OutputSummary, StringComparison.Ordinal);
        Assert.Contains("+needle after", transcript.ToolCalls[5].OutputSummary, StringComparison.Ordinal);
    }

    private static void InitializeGitRepository(string root)
    {
        RunGit(root, "init");
        RunGit(root, "config user.email test@example.invalid");
        RunGit(root, "config user.name Test User");
    }

    private static void RunGit(string workingDirectory, string arguments)
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };

        process.Start();
        Assert.True(process.WaitForExit(10_000), "git command timed out: " + arguments);
        string stderr = process.StandardError.ReadToEnd();
        Assert.True(process.ExitCode == 0, $"git {arguments} failed: {stderr}");
    }

    private sealed class StaticDirtyWorkspaceDetector(DirtyWorkspaceStatus status) : IDirtyWorkspaceDetector
    {
        public DirtyWorkspaceStatus Detect(WorkspaceContext workspace) => status;
    }

    private sealed class Phase03ToolCallingModel : IToolCallingModel
    {
        private int turn;

        public AgentModelTurn Start(AgentRunRequest request, CancellationToken cancellationToken = default)
        {
            return AgentModelTurn.RequestTools(new AgentToolCallRequest(
                "call_read",
                "workspace.read_text",
                """{"path":"README.md"}"""));
        }

        public AgentModelTurn Continue(
            AgentRunRequest request,
            IReadOnlyList<AgentToolCallResult> toolResults,
            CancellationToken cancellationToken = default)
        {
            Assert.True(Assert.Single(toolResults).Result.Succeeded);
            turn++;
            return turn switch
            {
                1 => AgentModelTurn.RequestTools(new AgentToolCallRequest(
                    "call_search",
                    "workspace.search_text",
                    """{"query":"needle"}""")),
                2 => AgentModelTurn.RequestTools(new AgentToolCallRequest(
                    "call_patch",
                    "workspace.apply_patch",
                    """{"path":"README.md","find":"needle before","replace":"needle after"}""")),
                3 => AgentModelTurn.RequestTools(new AgentToolCallRequest(
                    "call_shell",
                    "workspace.run_shell",
                    """{"command":"dotnet --version","timeoutMilliseconds":10000}""")),
                4 => AgentModelTurn.RequestTools(new AgentToolCallRequest(
                    "call_status",
                    "git.status",
                    "{}")),
                5 => AgentModelTurn.RequestTools(new AgentToolCallRequest(
                    "call_diff",
                    "git.diff",
                    "{}")),
                _ => AgentModelTurn.Final("phase03 accepted")
            };
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
}
