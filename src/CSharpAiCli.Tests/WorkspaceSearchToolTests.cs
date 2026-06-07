using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class WorkspaceSearchToolTests
{
    [Fact]
    public void Execute_finds_matches_inside_workspace()
    {
        using TempDirectory temp = TempDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "README.md"), "hello world\nanother line");
        Directory.CreateDirectory(Path.Combine(temp.Path, "docs"));
        File.WriteAllText(Path.Combine(temp.Path, "docs", "notes.txt"), "HELLO again");
        WorkspaceSearchTool tool = new(new WorkspaceGuard());

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path, """{"query":"hello"}"""));

        Assert.True(result.Succeeded);
        Assert.Contains("README.md:1: hello world", result.Summary, StringComparison.Ordinal);
        Assert.Contains(Path.Combine("docs", "notes.txt") + ":1: HELLO again", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_respects_max_results()
    {
        using TempDirectory temp = TempDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "a.txt"), "needle\nneedle\nneedle");
        WorkspaceSearchTool tool = new(new WorkspaceGuard());

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path, """{"query":"needle","maxResults":2}"""));

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Summary.Split(Environment.NewLine).Length);
    }

    [Fact]
    public void Execute_denies_workspace_outside_search_path()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspaceRoot = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(Path.Combine(temp.Path, "outside"));
        WorkspaceSearchTool tool = new(new WorkspaceGuard());

        ToolExecutionResult result = tool.Execute(CreateContext(workspaceRoot, """{"query":"needle","path":"../outside"}"""));

        Assert.False(result.Succeeded);
        Assert.Equal("workspace-boundary-denied", result.ErrorCode);
    }

    [Fact]
    public void Execute_skips_binary_and_large_files()
    {
        using TempDirectory temp = TempDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "small.txt"), "needle visible");
        File.WriteAllBytes(Path.Combine(temp.Path, "binary.bin"), [0x6e, 0x65, 0x65, 0x64, 0x6c, 0x65, 0x00]);
        File.WriteAllText(Path.Combine(temp.Path, "large.txt"), "needle hidden in a large file");
        WorkspaceSearchTool tool = new(new WorkspaceGuard(), maxFileBytes: 16);

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path, """{"query":"needle"}"""));

        Assert.True(result.Succeeded);
        Assert.Contains("small.txt:1: needle visible", result.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("binary.bin", result.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("large.txt", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_returns_no_matches_message()
    {
        using TempDirectory temp = TempDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "a.txt"), "hello");
        WorkspaceSearchTool tool = new(new WorkspaceGuard());

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path, """{"query":"needle"}"""));

        Assert.True(result.Succeeded);
        Assert.Equal("No matches found.", result.Summary);
    }

    [Fact]
    public void Execute_returns_argument_failure_for_missing_query()
    {
        using TempDirectory temp = TempDirectory.Create();
        WorkspaceSearchTool tool = new(new WorkspaceGuard());

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path, "{}"));

        Assert.False(result.Succeeded);
        Assert.Equal("invalid-tool-arguments", result.ErrorCode);
    }

    [Fact]
    public void Read_and_search_tools_work_inside_offline_agent_loop()
    {
        using TempDirectory temp = TempDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "README.md"), "needle in README");
        ToolRegistry registry = new();
        WorkspaceGuard guard = new();
        registry.Register(new WorkspaceFileReadTool(guard));
        registry.Register(new WorkspaceSearchTool(guard));
        ToolExecutor executor = new(registry);
        FakeToolCallingModel model = new();
        OfflineAgentRunner runner = new(model, executor, () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"));

        AgentRunResult result = runner.Run(new AgentRunRequest(
            Prompt: "read and search",
            Workspace: WorkspaceContext.Detect(temp.Path, temp.Path)));

        Assert.True(result.IsSuccess);
        Assert.Equal("done", result.Text);
        Assert.Equal(2, result.ToolCalls.Count);
        Assert.All(result.ToolCalls, toolCall => Assert.True(toolCall.Succeeded));
        Assert.Contains("needle in README", result.ToolCalls[0].OutputSummary, StringComparison.Ordinal);
        Assert.Contains("README.md:1: needle in README", result.ToolCalls[1].OutputSummary, StringComparison.Ordinal);
    }

    private static ToolExecutionContext CreateContext(string workspaceRoot, string argumentsJson)
    {
        WorkspaceContext workspace = WorkspaceContext.Detect(workspaceRoot, workspaceRoot);
        return new ToolExecutionContext("call_search", workspace, argumentsJson);
    }

    private sealed class FakeToolCallingModel : IToolCallingModel
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
            turn++;
            return turn == 1
                ? AgentModelTurn.RequestTools(new AgentToolCallRequest(
                    "call_search",
                    "workspace.search_text",
                    """{"query":"needle"}"""))
                : AgentModelTurn.Final("done");
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
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
