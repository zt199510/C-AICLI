using System.Text.Json;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class McpStdioTransportTests
{
    [Fact]
    public void Send_writes_request_line_reads_response_and_defaults_cwd_to_workspace_root()
    {
        using TempDirectory temp = TempDirectory.Create();
        string scriptPath = WritePowerShellScript(
            temp.Path,
            """
            $line = [Console]::In.ReadLine()
            $request = $line | ConvertFrom-Json
            $response = [ordered]@{
                jsonrpc = '2.0'
                id = $request.id
                result = [ordered]@{
                    method = $request.method
                    cwd = (Get-Location).Path
                }
            } | ConvertTo-Json -Compress -Depth 5
            [Console]::Out.WriteLine($response)
            """);
        McpStdioTransport transport = new(new WorkspaceGuard());

        McpStdioTransportResult result = transport.Send(
            WorkspaceContext.Detect(temp.Path, temp.Path),
            CreateOptions(scriptPath),
            new McpJsonRpcRequest(JsonSerializer.SerializeToElement(7), "tools/list"));

        Assert.True(result.Succeeded, result.SafeMessage);
        Assert.False(result.TimedOut);
        Assert.Null(result.ErrorCode);
        Assert.NotNull(result.Response);
        McpJsonRpcResponse response = result.Response!;
        Assert.Equal(7, response.Id.GetInt32());
        JsonElement payload = AssertJsonElement(response.Result);
        Assert.Equal("tools/list", payload.GetProperty("method").GetString());
        Assert.Equal(Path.GetFullPath(temp.Path), Path.GetFullPath(payload.GetProperty("cwd").GetString()!));
    }

    [Fact]
    public void Send_captures_and_sanitizes_stderr_without_breaking_response_parsing()
    {
        using TempDirectory temp = TempDirectory.Create();
        string scriptPath = WritePowerShellScript(
            temp.Path,
            """
            [Console]::Error.WriteLine('diagnostic token=super-secret-value')
            $request = [Console]::In.ReadLine() | ConvertFrom-Json
            $response = [ordered]@{
                jsonrpc = '2.0'
                id = $request.id
                result = [ordered]@{ ok = $true }
            } | ConvertTo-Json -Compress -Depth 5
            [Console]::Out.WriteLine($response)
            """);
        McpStdioTransport transport = new(new WorkspaceGuard());

        McpStdioTransportResult result = transport.Send(
            WorkspaceContext.Detect(temp.Path, temp.Path),
            CreateOptions(scriptPath),
            new McpJsonRpcRequest(JsonSerializer.SerializeToElement("stderr"), "initialize"));

        Assert.True(result.Succeeded, result.SafeMessage);
        Assert.Contains("diagnostic", result.StderrSnippet, StringComparison.Ordinal);
        Assert.Contains("token=[redacted]", result.StderrSnippet, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("super-secret-value", result.StderrSnippet, StringComparison.Ordinal);
    }

    [Fact]
    public void Send_times_out_silent_server_and_returns_safe_failure()
    {
        using TempDirectory temp = TempDirectory.Create();
        string scriptPath = WritePowerShellScript(
            temp.Path,
            """
            Start-Sleep -Seconds 10
            """);
        McpStdioTransport transport = new(new WorkspaceGuard());

        McpStdioTransportResult result = transport.Send(
            WorkspaceContext.Detect(temp.Path, temp.Path),
            CreateOptions(scriptPath, timeoutMilliseconds: 300),
            new McpJsonRpcRequest(JsonSerializer.SerializeToElement(1), "initialize"));

        Assert.False(result.Succeeded);
        Assert.True(result.TimedOut);
        Assert.Equal(McpErrorCode.Timeout, result.ErrorCode);
        Assert.Contains("timed out", result.SafeMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Send_denies_cwd_outside_workspace_before_starting_process()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspaceRoot = Path.Combine(temp.Path, "workspace");
        string outsideRoot = Path.Combine(temp.Path, "outside");
        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(outsideRoot);
        McpStdioTransport transport = new(new WorkspaceGuard());

        McpStdioTransportResult result = transport.Send(
            WorkspaceContext.Detect(workspaceRoot, temp.Path),
            new McpStdioServerOptions(
                serverName: "fixture",
                command: "definitely-not-run-caicli-mcp",
                arguments: [],
                workingDirectory: outsideRoot,
                timeoutMilliseconds: 10_000),
            new McpJsonRpcRequest(JsonSerializer.SerializeToElement(1), "initialize"));

        Assert.False(result.Succeeded);
        Assert.Equal(McpErrorCode.CwdDenied, result.ErrorCode);
        Assert.False(result.TimedOut);
    }

    [Fact]
    public void Send_denies_missing_cwd_inside_workspace_before_starting_process()
    {
        using TempDirectory temp = TempDirectory.Create();
        string missingCwd = Path.Combine(temp.Path, "missing");
        McpStdioTransport transport = new(new WorkspaceGuard());

        McpStdioTransportResult result = transport.Send(
            WorkspaceContext.Detect(temp.Path, temp.Path),
            new McpStdioServerOptions(
                serverName: "fixture",
                command: "definitely-not-run-caicli-mcp",
                arguments: [],
                workingDirectory: missingCwd,
                timeoutMilliseconds: 10_000),
            new McpJsonRpcRequest(JsonSerializer.SerializeToElement(1), "initialize"));

        Assert.False(result.Succeeded);
        Assert.Equal(McpErrorCode.CwdDenied, result.ErrorCode);
        Assert.False(result.TimedOut);
    }

    [Fact]
    public void Send_returns_safe_failure_when_process_cannot_start()
    {
        using TempDirectory temp = TempDirectory.Create();
        const string missingCommand = "definitely-missing-caicli-mcp-start-command";
        McpStdioTransport transport = new(new WorkspaceGuard());

        McpStdioTransportResult result = transport.Send(
            WorkspaceContext.Detect(temp.Path, temp.Path),
            new McpStdioServerOptions(
                serverName: "fixture",
                command: missingCommand,
                arguments: [],
                workingDirectory: null,
                timeoutMilliseconds: 10_000),
            new McpJsonRpcRequest(JsonSerializer.SerializeToElement(1), "initialize"));

        Assert.False(result.Succeeded);
        Assert.Equal(McpErrorCode.StartFailed, result.ErrorCode);
        Assert.DoesNotContain(missingCommand, result.SafeMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Send_returns_safe_failure_when_server_exits_without_response()
    {
        using TempDirectory temp = TempDirectory.Create();
        string scriptPath = WritePowerShellScript(
            temp.Path,
            """
            $null = [Console]::In.ReadLine()
            exit 0
            """);
        McpStdioTransport transport = new(new WorkspaceGuard());

        McpStdioTransportResult result = transport.Send(
            WorkspaceContext.Detect(temp.Path, temp.Path),
            CreateOptions(scriptPath),
            new McpJsonRpcRequest(JsonSerializer.SerializeToElement(1), "initialize"));

        Assert.False(result.Succeeded);
        Assert.Equal(McpErrorCode.ServerExited, result.ErrorCode);
        Assert.Contains("before returning a response", result.SafeMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Send_returns_safe_failure_for_invalid_json_response()
    {
        using TempDirectory temp = TempDirectory.Create();
        string scriptPath = WritePowerShellScript(
            temp.Path,
            """
            $null = [Console]::In.ReadLine()
            [Console]::Out.WriteLine('not-json')
            """);
        McpStdioTransport transport = new(new WorkspaceGuard());

        McpStdioTransportResult result = transport.Send(
            WorkspaceContext.Detect(temp.Path, temp.Path),
            CreateOptions(scriptPath),
            new McpJsonRpcRequest(JsonSerializer.SerializeToElement(1), "initialize"));

        Assert.False(result.Succeeded);
        Assert.Equal(McpErrorCode.InvalidResponse, result.ErrorCode);
        Assert.Contains("invalid JSON-RPC response", result.SafeMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static McpStdioServerOptions CreateOptions(string scriptPath, int timeoutMilliseconds = 10_000)
    {
        return new McpStdioServerOptions(
            serverName: "fixture",
            command: PowerShellExecutable,
            arguments:
            [
                "-NoLogo",
                "-NoProfile",
                "-NonInteractive",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                scriptPath
            ],
            workingDirectory: null,
            timeoutMilliseconds: timeoutMilliseconds);
    }

    private static string WritePowerShellScript(string directory, string script)
    {
        string scriptPath = Path.Combine(directory, "mcp-fixture-" + Guid.NewGuid().ToString("N") + ".ps1");
        File.WriteAllText(scriptPath, script);
        return scriptPath;
    }

    private static string PowerShellExecutable => OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh";

    private static JsonElement AssertJsonElement(JsonElement? element)
    {
        Assert.True(element.HasValue);
        return element.Value;
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
