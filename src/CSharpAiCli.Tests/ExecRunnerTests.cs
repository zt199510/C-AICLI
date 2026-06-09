using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ExecRunnerTests
{
    [Fact]
    public void Run_whitespace_task_returns_empty_run_task_failure_without_invoking_a_tool()
    {
        WorkspaceContext workspace = new(
            RootPath: Path.GetTempPath(),
            ConfigPath: Path.Combine(Path.GetTempPath(), ".caicli", "config.json"),
            Status: WorkspaceStatus.Ready);
        ExecRequest request = new(
            Task: "   ",
            WorkspaceRoot: workspace.RootPath);
        RecordingToolExecutor executor = new();
        ExecRunner runner = new();

        ExecResult result = runner.Run(request, workspace, executor);

        Assert.False(result.IsSuccess);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("Run task is empty.", result.Summary);
        Assert.Equal("empty-run-task", result.ErrorCode);
        Assert.Empty(executor.Calls);
        Assert.Equal(2, result.Events.Count);
        Assert.Equal("task.started", result.Events[0].Type);
        Assert.Equal("task.failed", result.Events[1].Type);
        Assert.Equal(1, result.Events[1].Sequence);
    }

    [Fact]
    public void Run_read_task_uses_workspace_read_text_and_returns_deterministic_events()
    {
        using TempDirectory temp = TempDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "note.txt"), "hello runner");
        WorkspaceContext workspace = new(
            RootPath: temp.Path,
            ConfigPath: Path.Combine(temp.Path, ".caicli", "config.json"),
            Status: WorkspaceStatus.Ready);
        ExecRequest request = new(
            Task: "read note.txt",
            WorkspaceRoot: temp.Path);
        RecordingToolExecutor executor = new();
        ExecRunner runner = new();

        ExecResult result = runner.Run(request, workspace, executor);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("hello runner", result.Summary);
        Assert.Null(result.ErrorCode);
        Assert.Single(executor.Calls);
        Assert.Equal("workspace.read_text", executor.Calls[0].ToolName);
        Assert.Equal("""{"path":"note.txt"}""", executor.Calls[0].Context.ArgumentsJson);
        Assert.Equal(2, result.Events.Count);
        Assert.Equal("task.started", result.Events[0].Type);
        Assert.Equal(0, result.Events[0].Sequence);
        Assert.Equal("task.completed", result.Events[1].Type);
        Assert.Equal(1, result.Events[1].Sequence);
        Assert.Equal("read note.txt", result.Events[0].Payload!["task"]);
        Assert.Equal("workspace.read_text", result.Events[0].Payload!["toolName"]);
        Assert.Equal("note.txt", result.Events[0].Payload!["path"]);
        Assert.Equal("hello runner", result.Events[1].Summary);
    }

    [Fact]
    public void Run_unsupported_task_returns_failure_without_invoking_a_tool()
    {
        WorkspaceContext workspace = new(
            RootPath: Path.GetTempPath(),
            ConfigPath: Path.Combine(Path.GetTempPath(), ".caicli", "config.json"),
            Status: WorkspaceStatus.Ready);
        ExecRequest request = new(
            Task: "paint the moon",
            WorkspaceRoot: workspace.RootPath);
        RecordingToolExecutor executor = new();
        ExecRunner runner = new();

        ExecResult result = runner.Run(request, workspace, executor);

        Assert.False(result.IsSuccess);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("Supported run tasks are: create smoke note, read <path>, shell <command>.", result.Summary);
        Assert.Equal("unsupported-run-task", result.ErrorCode);
        Assert.Empty(executor.Calls);
        Assert.Equal(2, result.Events.Count);
        Assert.Equal("task.failed", result.Events[1].Type);
        Assert.Equal(1, result.Events[1].Sequence);
        Assert.Equal("unsupported-run-task", result.Events[1].ErrorCode);
    }

    private sealed class RecordingToolExecutor : IToolExecutor
    {
        public List<(string ToolName, ToolExecutionContext Context)> Calls { get; } = new();

        public ToolExecutionResult Execute(
            string toolName,
            ToolExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((toolName, context));

            if (toolName == "workspace.read_text")
            {
                return ToolExecutionResult.Success("hello runner");
            }

            return ToolExecutionResult.Failure("unexpected-tool", "Unexpected tool invocation.");
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; }

        private TempDirectory(string path)
        {
            Path = path;
        }

        public static TempDirectory Create()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("n"));
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
