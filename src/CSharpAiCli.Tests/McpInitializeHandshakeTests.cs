using System.Diagnostics;
using System.Text.Json;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class McpInitializeHandshakeTests
{
    [Fact]
    public void Initialize_sends_initialize_params_and_initialized_notification_on_same_session()
    {
        using TempDirectory temp = TempDirectory.Create();
        string markerPath = Path.Combine(temp.Path, "handshake-observed.json");
        string markerPathLiteral = ToPowerShellStringLiteral(markerPath);
        string scriptPath = WritePowerShellScript(
            temp.Path,
            $$"""
            $requestLine = [Console]::In.ReadLine()
            $request = $requestLine | ConvertFrom-Json
            $response = [ordered]@{
                jsonrpc = '2.0'
                id = $request.id
                result = [ordered]@{
                    protocolVersion = $request.params.protocolVersion
                    capabilities = [ordered]@{}
                    serverInfo = [ordered]@{
                        name = 'fixture-server'
                        version = '1.2.3'
                    }
                }
            } | ConvertTo-Json -Compress -Depth 10
            [Console]::Out.WriteLine($response)
            $notificationLine = [Console]::In.ReadLine()
            $notification = $notificationLine | ConvertFrom-Json
            $observation = [ordered]@{
                requestMethod = $request.method
                protocolVersion = $request.params.protocolVersion
                clientName = $request.params.clientInfo.name
                clientVersion = $request.params.clientInfo.version
                hasCapabilities = $request.params.PSObject.Properties.Name -contains 'capabilities'
                notificationMethod = $notification.method
                notificationHasId = $notification.PSObject.Properties.Name -contains 'id'
            } | ConvertTo-Json -Compress -Depth 5
            [System.IO.File]::WriteAllText({{markerPathLiteral}}, $observation)
            Start-Sleep -Milliseconds 100
            """);

        using McpStdioSession session = OpenSession(temp.Path, scriptPath);
        McpProtocolClient client = new(session);

        McpInitializeResult result = client.Initialize();

        Assert.True(result.Succeeded, result.SafeMessage);
        Assert.Equal(McpProtocolClient.ProtocolVersion, result.ProtocolVersion);
        Assert.NotNull(result.ServerInfo);
        Assert.Equal("fixture-server", result.ServerInfo.Name);
        Assert.Equal("1.2.3", result.ServerInfo.Version);
        Assert.True(WaitForFile(markerPath, TimeSpan.FromSeconds(2)));
        using JsonDocument observation = JsonDocument.Parse(File.ReadAllText(markerPath));
        JsonElement root = observation.RootElement;
        Assert.Equal("initialize", root.GetProperty("requestMethod").GetString());
        Assert.Equal(McpProtocolClient.ProtocolVersion, root.GetProperty("protocolVersion").GetString());
        Assert.Equal(ProductInfo.CommandName, root.GetProperty("clientName").GetString());
        Assert.Equal(ProductInfo.Version, root.GetProperty("clientVersion").GetString());
        Assert.True(root.GetProperty("hasCapabilities").GetBoolean());
        Assert.Equal("notifications/initialized", root.GetProperty("notificationMethod").GetString());
        Assert.False(root.GetProperty("notificationHasId").GetBoolean());
    }

    [Fact]
    public void Initialize_parses_protocol_version_capabilities_and_server_info()
    {
        using TempDirectory temp = TempDirectory.Create();
        string scriptPath = WritePowerShellScript(
            temp.Path,
            """
            $request = [Console]::In.ReadLine() | ConvertFrom-Json
            $response = [ordered]@{
                jsonrpc = '2.0'
                id = $request.id
                result = [ordered]@{
                    protocolVersion = '2025-03-26'
                    capabilities = [ordered]@{
                        tools = [ordered]@{
                            listChanged = $true
                        }
                    }
                    serverInfo = [ordered]@{
                        name = 'parse-fixture'
                        version = '9.8.7'
                    }
                }
            } | ConvertTo-Json -Compress -Depth 10
            [Console]::Out.WriteLine($response)
            $null = [Console]::In.ReadLine()
            """);

        using McpStdioSession session = OpenSession(temp.Path, scriptPath);
        McpProtocolClient client = new(session);

        McpInitializeResult result = client.Initialize();

        Assert.True(result.Succeeded, result.SafeMessage);
        Assert.Equal("2025-03-26", result.ProtocolVersion);
        JsonElement capabilities = AssertJsonElement(result.Capabilities);
        Assert.True(capabilities.GetProperty("tools").GetProperty("listChanged").GetBoolean());
        Assert.NotNull(result.ServerInfo);
        Assert.Equal("parse-fixture", result.ServerInfo.Name);
        Assert.Equal("9.8.7", result.ServerInfo.Version);
    }

    [Fact]
    public void Initialize_unsupported_protocol_version_fails_without_sending_initialized_notification()
    {
        JsonElement responseResult = JsonSerializer.SerializeToElement(new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            serverInfo = new
            {
                name = "fixture-server",
                version = "1.0.0"
            }
        });
        FakeMcpSession session = new(
            McpStdioTransportResult.Success(
                new McpJsonRpcResponse
                {
                    Id = JsonSerializer.SerializeToElement(1),
                    Result = responseResult
                },
                stderrSnippet: "",
                stderrTruncated: false),
            McpStdioTransportResult.NotificationSent(
                stderrSnippet: "",
                stderrTruncated: false));
        McpProtocolClient client = new(session);

        McpInitializeResult result = client.Initialize();

        Assert.False(result.Succeeded);
        Assert.Equal("mcp-protocol-version-mismatch", result.ErrorCode);
        Assert.Contains("unsupported protocol version", result.SafeMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2024-11-05", result.SafeMessage, StringComparison.Ordinal);
        Assert.Empty(session.Notifications);
    }

    [Fact]
    public void Initialize_missing_server_info_returns_invalid_response_without_sending_initialized_notification()
    {
        JsonElement responseResult = JsonSerializer.SerializeToElement(new
        {
            protocolVersion = McpProtocolClient.ProtocolVersion,
            capabilities = new { }
        });
        FakeMcpSession session = new(
            McpStdioTransportResult.Success(
                new McpJsonRpcResponse
                {
                    Id = JsonSerializer.SerializeToElement(1),
                    Result = responseResult
                },
                stderrSnippet: "",
                stderrTruncated: false),
            McpStdioTransportResult.NotificationSent(
                stderrSnippet: "",
                stderrTruncated: false));
        McpProtocolClient client = new(session);

        McpInitializeResult result = client.Initialize();

        Assert.False(result.Succeeded);
        Assert.Equal("mcp-invalid-response", result.ErrorCode);
        Assert.Contains("invalid", result.SafeMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(session.Notifications);
    }

    [Fact]
    public void Initialize_json_rpc_error_returns_safe_failure_with_error_details()
    {
        using TempDirectory temp = TempDirectory.Create();
        string scriptPath = WritePowerShellScript(
            temp.Path,
            """
            $request = [Console]::In.ReadLine() | ConvertFrom-Json
            $response = [ordered]@{
                jsonrpc = '2.0'
                id = $request.id
                error = [ordered]@{
                    code = -32602
                    message = 'Invalid initialize params'
                }
            } | ConvertTo-Json -Compress -Depth 10
            [Console]::Out.WriteLine($response)
            Start-Sleep -Seconds 2
            """);

        using McpStdioSession session = OpenSession(temp.Path, scriptPath);
        McpProtocolClient client = new(session);

        McpInitializeResult result = client.Initialize();

        Assert.False(result.Succeeded);
        Assert.Equal("mcp-json-rpc-error", result.ErrorCode);
        Assert.Equal(-32602, result.JsonRpcErrorCode);
        Assert.Equal("Invalid initialize params", result.JsonRpcErrorMessage);
        Assert.Contains("JSON-RPC error", result.SafeMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Invalid initialize params", result.SafeMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Initialize_missing_or_invalid_result_returns_invalid_response_failure()
    {
        using TempDirectory temp = TempDirectory.Create();
        string scriptPath = WritePowerShellScript(
            temp.Path,
            """
            $request = [Console]::In.ReadLine() | ConvertFrom-Json
            $response = [ordered]@{
                jsonrpc = '2.0'
                id = $request.id
                result = [ordered]@{
                    capabilities = [ordered]@{}
                }
            } | ConvertTo-Json -Compress -Depth 10
            [Console]::Out.WriteLine($response)
            Start-Sleep -Seconds 2
            """);

        using McpStdioSession session = OpenSession(temp.Path, scriptPath);
        McpProtocolClient client = new(session);

        McpInitializeResult result = client.Initialize();

        Assert.False(result.Succeeded);
        Assert.Equal(McpErrorCode.InvalidResponse, result.ErrorCode);
        Assert.Null(result.ProtocolVersion);
        Assert.Null(result.ServerInfo);
        Assert.Contains("invalid", result.SafeMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Initialize_fails_safely_when_initialized_notification_send_fails()
    {
        JsonElement responseResult = JsonSerializer.SerializeToElement(new
        {
            protocolVersion = McpProtocolClient.ProtocolVersion,
            capabilities = new { },
            serverInfo = new
            {
                name = "fixture-server",
                version = "1.0.0"
            }
        });
        FakeMcpSession session = new(
            McpStdioTransportResult.Success(
                new McpJsonRpcResponse
                {
                    Id = JsonSerializer.SerializeToElement(1),
                    Result = responseResult
                },
                stderrSnippet: "",
                stderrTruncated: false),
            McpStdioTransportResult.Failure(
                McpErrorCode.ServerExited,
                "MCP stdio server exited before accepting a notification."));
        McpProtocolClient client = new(session);

        McpInitializeResult result = client.Initialize();

        Assert.False(result.Succeeded);
        Assert.Equal(McpErrorCode.ServerExited, result.ErrorCode);
        Assert.Contains("notification", result.SafeMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Single(session.Notifications);
        Assert.Equal("notifications/initialized", session.Notifications[0].Method);
    }

    private static McpStdioSession OpenSession(string workspacePath, string scriptPath)
    {
        McpStdioTransport transport = new(new WorkspaceGuard());
        McpStdioSessionOpenResult openResult = transport.OpenSession(
            WorkspaceContext.Detect(workspacePath, workspacePath),
            CreateOptions(scriptPath));

        Assert.True(openResult.Succeeded, openResult.SafeMessage);
        Assert.NotNull(openResult.Session);
        return openResult.Session;
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
        string scriptPath = Path.Combine(directory, "mcp-initialize-fixture-" + Guid.NewGuid().ToString("N") + ".ps1");
        File.WriteAllText(scriptPath, script);
        return scriptPath;
    }

    private static string PowerShellExecutable => OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh";

    private static bool WaitForFile(string path, TimeSpan timeout)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (File.Exists(path))
            {
                return true;
            }

            Thread.Sleep(25);
        }

        return File.Exists(path);
    }

    private static JsonElement AssertJsonElement(JsonElement? element)
    {
        Assert.True(element.HasValue);
        return element.Value;
    }

    private static string ToPowerShellStringLiteral(string value)
    {
        return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    private sealed class FakeMcpSession : IMcpJsonRpcSession
    {
        private readonly McpStdioTransportResult requestResult;
        private readonly McpStdioTransportResult notificationResult;

        public FakeMcpSession(
            McpStdioTransportResult requestResult,
            McpStdioTransportResult notificationResult)
        {
            this.requestResult = requestResult;
            this.notificationResult = notificationResult;
        }

        public List<McpJsonRpcRequest> Requests { get; } = [];

        public List<McpJsonRpcNotification> Notifications { get; } = [];

        public McpStdioTransportResult Send(
            McpJsonRpcRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return requestResult;
        }

        public McpStdioTransportResult SendNotification(
            McpJsonRpcNotification notification,
            CancellationToken cancellationToken = default)
        {
            Notifications.Add(notification);
            return notificationResult;
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
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "caicli-tests-" + Guid.NewGuid().ToString("N"));
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
