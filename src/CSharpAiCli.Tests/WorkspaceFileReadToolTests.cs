using CSharpAiCli.Core;
using System.Text;
using System.Text.Json;

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
        IReadOnlyDictionary<string, JsonElement> payload = AssertPayload(result);
        Assert.Equal("notes.txt", payload["path"].GetString());
        Assert.Equal(Encoding.UTF8.GetByteCount("hello workspace"), payload["byteCount"].GetInt64());
        Assert.Equal("hello workspace".Length, payload["characterCount"].GetInt32());
        Assert.False(payload.ContainsKey("content"));
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
        Assert.Equal(ToolErrorCode.FileNotFound, result.ErrorCode);
        IReadOnlyDictionary<string, JsonElement> payload = AssertPayload(result);
        Assert.Equal("missing.txt", payload["path"].GetString());
    }

    [Fact]
    public void Execute_rejects_large_file_without_returning_content()
    {
        using TempDirectory temp = TempDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "large.txt"), "sk-secret-large-content");
        WorkspaceFileReadTool tool = new(new WorkspaceGuard(), maxFileBytes: 4);

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path, """{"path":"large.txt"}"""));

        Assert.False(result.Succeeded);
        Assert.Equal(ToolErrorCode.FileTooLarge, result.ErrorCode);
        Assert.DoesNotContain("sk-secret", result.Summary, StringComparison.Ordinal);
        IReadOnlyDictionary<string, JsonElement> payload = AssertPayload(result);
        Assert.Equal("large.txt", payload["path"].GetString());
        Assert.Equal(23, payload["byteCount"].GetInt64());
        Assert.Equal(4, payload["maxFileBytes"].GetInt64());
    }

    [Fact]
    public void Execute_rejects_binary_file()
    {
        using TempDirectory temp = TempDirectory.Create();
        File.WriteAllBytes(Path.Combine(temp.Path, "binary.bin"), [0x01, 0x00, 0x02]);
        WorkspaceFileReadTool tool = new(new WorkspaceGuard());

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path, """{"path":"binary.bin"}"""));

        Assert.False(result.Succeeded);
        Assert.Equal(ToolErrorCode.BinaryFileNotSupported, result.ErrorCode);
        IReadOnlyDictionary<string, JsonElement> payload = AssertPayload(result);
        Assert.Equal("binary.bin", payload["path"].GetString());
        Assert.Equal(3, payload["byteCount"].GetInt64());
    }

    [Fact]
    public void Execute_returns_argument_failure_for_missing_path()
    {
        using TempDirectory temp = TempDirectory.Create();
        WorkspaceFileReadTool tool = new(new WorkspaceGuard());

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path, "{}"));

        Assert.False(result.Succeeded);
        Assert.Equal(ToolErrorCode.InvalidToolArguments, result.ErrorCode);
        IReadOnlyDictionary<string, JsonElement> payload = AssertPayload(result);
        Assert.Equal(ToolErrorCode.InvalidToolArguments, payload["errorCode"].GetString());
        Assert.Equal("workspace.read_text", payload["toolName"].GetString());
        Assert.Equal("path", payload["argument"].GetString());
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    public void Execute_returns_argument_failure_for_non_object_root(string argumentsJson)
    {
        using TempDirectory temp = TempDirectory.Create();
        WorkspaceFileReadTool tool = new(new WorkspaceGuard());

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path, argumentsJson));

        Assert.False(result.Succeeded);
        Assert.Equal(ToolErrorCode.InvalidToolArguments, result.ErrorCode);
        Assert.Equal("Tool arguments must be a JSON object.", result.Summary);
        IReadOnlyDictionary<string, JsonElement> payload = AssertPayload(result);
        Assert.Equal(ToolErrorCode.InvalidToolArguments, payload["errorCode"].GetString());
        Assert.Equal("workspace.read_text", payload["toolName"].GetString());
        Assert.Equal("arguments", payload["argument"].GetString());
    }

    private static ToolExecutionContext CreateContext(string workspaceRoot, string argumentsJson)
    {
        WorkspaceContext workspace = WorkspaceContext.Detect(workspaceRoot, workspaceRoot);
        return new ToolExecutionContext("call_read", workspace, argumentsJson);
    }

    private static IReadOnlyDictionary<string, JsonElement> AssertPayload(ToolExecutionResult result)
    {
        return result.StructuredPayload ?? throw new InvalidOperationException("Structured payload was not set.");
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
