using System.Diagnostics;
using System.Text.Json;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

internal sealed class FakeMcpStdioServer : IDisposable
{
    private const string SuccessMode = "success";
    private const string TimeoutMode = "timeout";
    private const string InvalidJsonMode = "invalid-json";

    private readonly TempDirectory tempDirectory;
    private readonly string mode;

    private FakeMcpStdioServer(TempDirectory tempDirectory, string mode)
    {
        this.tempDirectory = tempDirectory;
        this.mode = mode;
        WorkspacePath = Path.Combine(tempDirectory.Path, "workspace");
        Directory.CreateDirectory(WorkspacePath);
        ScriptPath = WritePowerShellScript(tempDirectory.Path);
        StartedPath = Path.Combine(tempDirectory.Path, "started.txt");
        ObservationPath = Path.Combine(tempDirectory.Path, "observations.jsonl");
    }

    public string WorkspacePath { get; }

    private string ScriptPath { get; }

    private string StartedPath { get; }

    private string ObservationPath { get; }

    public bool HasStarted => File.Exists(StartedPath);

    public static FakeMcpStdioServer CreateSuccessful()
    {
        return Create(SuccessMode);
    }

    public static FakeMcpStdioServer CreateTimeout()
    {
        return Create(TimeoutMode);
    }

    public static FakeMcpStdioServer CreateInvalidJson()
    {
        return Create(InvalidJsonMode);
    }

    public McpServerConfig CreateConfig(
        bool enabled = true,
        int timeoutMilliseconds = 10_000,
        string? cwd = null)
    {
        return new McpServerConfig
        {
            Enabled = enabled,
            Transport = "stdio",
            Command = PowerShellExecutable,
            Args = CreatePowerShellScriptArgs(),
            Cwd = cwd,
            TimeoutMilliseconds = timeoutMilliseconds
        };
    }

    public McpServerDefinition CreateServerDefinition(
        string name = "fake",
        bool enabled = true,
        int timeoutMilliseconds = 10_000,
        string? cwd = null)
    {
        return new McpServerDefinition(
            name,
            enabled,
            enabled ? "configured" : "inactive",
            "stdio command: fake-mcp-server",
            "test fixture")
        {
            Transport = "stdio",
            Command = PowerShellExecutable,
            Args = CreatePowerShellScriptArgs(),
            Cwd = cwd,
            TimeoutMilliseconds = timeoutMilliseconds
        };
    }

    public McpStdioServerOptions CreateOptions(
        string serverName = "fake",
        int timeoutMilliseconds = 10_000,
        string? workingDirectory = null)
    {
        return new McpStdioServerOptions(
            serverName,
            PowerShellExecutable,
            CreatePowerShellScriptArgs(),
            workingDirectory,
            timeoutMilliseconds);
    }

    public WorkspaceContext CreateWorkspace()
    {
        return WorkspaceContext.Detect(WorkspacePath, WorkspacePath);
    }

    public IReadOnlyList<JsonElement> ReadObservations()
    {
        if (!File.Exists(ObservationPath))
        {
            return [];
        }

        List<JsonElement> observations = [];
        foreach (string line in File.ReadAllLines(ObservationPath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using JsonDocument document = JsonDocument.Parse(line);
            observations.Add(document.RootElement.Clone());
        }

        return observations;
    }

    public bool WaitForObservationCount(int count, TimeSpan timeout)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (ReadObservations().Count >= count)
            {
                return true;
            }

            Thread.Sleep(25);
        }

        return ReadObservations().Count >= count;
    }

    public void Dispose()
    {
        tempDirectory.Dispose();
    }

    private static FakeMcpStdioServer Create(string mode)
    {
        return new FakeMcpStdioServer(TempDirectory.Create(), mode);
    }

    private string[] CreatePowerShellScriptArgs()
    {
        return
        [
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            ScriptPath,
            "-Mode",
            mode,
            "-StartedPath",
            StartedPath,
            "-ObservationPath",
            ObservationPath
        ];
    }

    private static string WritePowerShellScript(string directory)
    {
        string scriptPath = Path.Combine(directory, "fake-mcp-stdio-server-" + Guid.NewGuid().ToString("N") + ".ps1");
        File.WriteAllText(scriptPath, FakeServerScript);
        return scriptPath;
    }

    private static string PowerShellExecutable => OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh";

    private const string FakeServerScript =
        """
        param(
            [Parameter(Mandatory=$true)][string]$Mode,
            [Parameter(Mandatory=$true)][string]$StartedPath,
            [Parameter(Mandatory=$true)][string]$ObservationPath
        )

        $ErrorActionPreference = 'Stop'
        [System.IO.File]::WriteAllText($StartedPath, 'started')

        function Get-PropertyValue {
            param($Object, [string]$Name)
            if ($null -eq $Object) {
                return $null
            }

            $property = $Object.PSObject.Properties[$Name]
            if ($null -eq $property) {
                return $null
            }

            return $property.Value
        }

        function Add-Observation {
            param($Message)

            $params = Get-PropertyValue $Message 'params'
            $arguments = Get-PropertyValue $params 'arguments'
            $entry = [ordered]@{
                method = [string](Get-PropertyValue $Message 'method')
                hasId = $null -ne $Message.PSObject.Properties['id']
                protocolVersion = Get-PropertyValue $params 'protocolVersion'
                toolName = Get-PropertyValue $params 'name'
                textArgument = Get-PropertyValue $arguments 'text'
            }
            $json = $entry | ConvertTo-Json -Compress -Depth 20
            [System.IO.File]::AppendAllText($ObservationPath, $json + [Environment]::NewLine)
        }

        function Write-ResultResponse {
            param($Id, $Result)

            $response = [ordered]@{
                jsonrpc = '2.0'
                id = $Id
                result = $Result
            } | ConvertTo-Json -Compress -Depth 20
            [Console]::Out.WriteLine($response)
            [Console]::Out.Flush()
        }

        function Write-ErrorResponse {
            param($Id, [int]$Code, [string]$Message)

            $response = [ordered]@{
                jsonrpc = '2.0'
                id = $Id
                error = [ordered]@{
                    code = $Code
                    message = $Message
                }
            } | ConvertTo-Json -Compress -Depth 20
            [Console]::Out.WriteLine($response)
            [Console]::Out.Flush()
        }

        if ($Mode -eq 'timeout') {
            $line = [Console]::In.ReadLine()
            if ($null -ne $line) {
                Add-Observation ($line | ConvertFrom-Json)
            }
            Start-Sleep -Seconds 5
            exit 0
        }

        if ($Mode -eq 'invalid-json') {
            $line = [Console]::In.ReadLine()
            if ($null -ne $line) {
                Add-Observation ($line | ConvertFrom-Json)
            }
            [Console]::Out.WriteLine('not-json')
            [Console]::Out.Flush()
            Start-Sleep -Milliseconds 200
            exit 0
        }

        while (($line = [Console]::In.ReadLine()) -ne $null) {
            $message = $line | ConvertFrom-Json
            Add-Observation $message

            $method = Get-PropertyValue $message 'method'
            $id = Get-PropertyValue $message 'id'

            if ($method -eq 'initialize') {
                $params = Get-PropertyValue $message 'params'
                Write-ResultResponse $id ([ordered]@{
                    protocolVersion = Get-PropertyValue $params 'protocolVersion'
                    capabilities = [ordered]@{
                        tools = [ordered]@{
                            listChanged = $false
                        }
                    }
                    serverInfo = [ordered]@{
                        name = 'fake-mcp-stdio-server'
                        version = '1.0.0'
                    }
                })
                continue
            }

            if ($method -eq 'notifications/initialized') {
                continue
            }

            if ($method -eq 'tools/list') {
                Write-ResultResponse $id ([ordered]@{
                    tools = @(
                        [ordered]@{
                            name = 'echo'
                            description = 'Echo input.'
                            inputSchema = [ordered]@{
                                type = 'object'
                                properties = [ordered]@{
                                    text = [ordered]@{
                                        type = 'string'
                                    }
                                }
                                required = @('text')
                            }
                        }
                    )
                })
                continue
            }

            if ($method -eq 'tools/call') {
                $params = Get-PropertyValue $message 'params'
                $arguments = Get-PropertyValue $params 'arguments'
                $text = [string](Get-PropertyValue $arguments 'text')
                Write-ResultResponse $id ([ordered]@{
                    content = @(
                        [ordered]@{
                            type = 'text'
                            text = "echo: $text"
                        }
                    )
                    structuredContent = [ordered]@{
                        echoed = $text
                    }
                })
                continue
            }

            Write-ErrorResponse $id -32601 'Method not found.'
        }
        """;

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
                DeleteDirectoryWithRetry(Path);
            }
        }

        private static void DeleteDirectoryWithRetry(string path)
        {
            IOException? lastIOException = null;
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    Directory.Delete(path, recursive: true);
                    return;
                }
                catch (IOException exception)
                {
                    lastIOException = exception;
                    Thread.Sleep(100);
                }
            }

            if (lastIOException is not null)
            {
                throw lastIOException;
            }
        }
    }
}
