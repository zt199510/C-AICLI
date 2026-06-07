using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class WorkspaceFileReadToolTests
{
    [Fact]
    public void Execute_reads_workspace_text_file()
    {
        using TempDirectory temp = TempDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "notes.txt"), "hello workspace");
        WorkspaceFileReadTool tool = new(new WorkspaceGuard());

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path, """{"path":"notes.txt"}"""));

        Assert.True(result.Succeeded);
        Assert.Equal("hello workspace", result.Summary);
    }

    [Fact]
    public void Execute_denies_workspace_outside_path()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspaceRoot = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(workspaceRoot);
        File.WriteAllText(Path.Combine(temp.Path, "outside.txt"), "outside");
        WorkspaceFileReadTool tool = new(new WorkspaceGuard());

        ToolExecutionResult result = tool.Execute(CreateContext(workspaceRoot, """{"path":"../outside.txt"}"""));

        Assert.False(result.Succeeded);
        Assert.Equal("workspace-boundary-denied", result.ErrorCode);
    }

    [Fact]
    public void Execute_rejects_missing_file()
    {
        using TempDirectory temp = TempDirectory.Create();
        WorkspaceFileReadTool tool = new(new WorkspaceGuard());

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path, """{"path":"missing.txt"}"""));

        Assert.False(result.Succeeded);
        Assert.Equal("file-not-found", result.ErrorCode);
    }

    [Fact]
    public void Execute_rejects_large_file_without_returning_content()
    {
        using TempDirectory temp = TempDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "large.txt"), "sk-secret-large-content");
        WorkspaceFileReadTool tool = new(new WorkspaceGuard(), maxFileBytes: 4);

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path, """{"path":"large.txt"}"""));

        Assert.False(result.Succeeded);
        Assert.Equal("file-too-large", result.ErrorCode);
        Assert.DoesNotContain("sk-secret", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_rejects_binary_file()
    {
        using TempDirectory temp = TempDirectory.Create();
        File.WriteAllBytes(Path.Combine(temp.Path, "binary.bin"), [0x01, 0x00, 0x02]);
        WorkspaceFileReadTool tool = new(new WorkspaceGuard());

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path, """{"path":"binary.bin"}"""));

        Assert.False(result.Succeeded);
        Assert.Equal("binary-file-not-supported", result.ErrorCode);
    }

    [Fact]
    public void Execute_returns_argument_failure_for_missing_path()
    {
        using TempDirectory temp = TempDirectory.Create();
        WorkspaceFileReadTool tool = new(new WorkspaceGuard());

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path, "{}"));

        Assert.False(result.Succeeded);
        Assert.Equal("invalid-tool-arguments", result.ErrorCode);
    }

    private static ToolExecutionContext CreateContext(string workspaceRoot, string argumentsJson)
    {
        WorkspaceContext workspace = WorkspaceContext.Detect(workspaceRoot, workspaceRoot);
        return new ToolExecutionContext("call_read", workspace, argumentsJson);
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
