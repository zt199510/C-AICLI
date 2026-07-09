using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CSharpAiCli.Core;

public sealed class McpStdioTransport
{
    private const int DefaultTimeoutMilliseconds = 30_000;
    private const int CleanupWaitMilliseconds = 1000;
    private const int StderrSnippetMaxBytes = 4096;

    private static readonly Regex AuthorizationHeaderPattern = new(
        @"\b(authorization)\b\s*([:=])\s*(?:(Bearer)\s+)?(""[^""]*""|'[^']*'|[^\s]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex SecretAssignmentPattern = new(
        @"\b(api[-_]?key|access[-_]?token|refresh[-_]?token|token|secret|password)\b\s*([:=])\s*(""[^""]*""|'[^']*'|[^\s]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex CommandLineSecretPattern = new(
        @"(--?(?:api[-_]?key|access[-_]?token|refresh[-_]?token|token|secret|password)(?:=|\s+))(""[^""]*""|'[^']*'|[^\s]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex BearerTokenPattern = new(
        @"\bBearer\s+[A-Za-z0-9._~+/=-]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private readonly IWorkspaceGuard workspaceGuard;

    public McpStdioTransport(IWorkspaceGuard workspaceGuard)
    {
        ArgumentNullException.ThrowIfNull(workspaceGuard);
        this.workspaceGuard = workspaceGuard;
    }

    public McpStdioTransportResult Send(
        WorkspaceContext workspace,
        McpStdioServerOptions options,
        McpJsonRpcRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(options.Command))
        {
            return McpStdioTransportResult.Failure(
                McpErrorCode.StartFailed,
                "MCP stdio command is not configured.");
        }

        WorkspaceGuardResult cwdResult = ResolveWorkingDirectory(workspace, options.WorkingDirectory);
        if (!cwdResult.IsAllowed || cwdResult.FullPath is null)
        {
            return McpStdioTransportResult.Failure(
                McpErrorCode.CwdDenied,
                cwdResult.SafeMessage.Length == 0
                    ? "MCP stdio cwd must remain inside the workspace."
                    : cwdResult.SafeMessage);
        }

        if (!Directory.Exists(cwdResult.FullPath))
        {
            return McpStdioTransportResult.Failure(
                McpErrorCode.CwdDenied,
                "MCP stdio cwd must exist inside the workspace.");
        }

        int timeoutMilliseconds = options.TimeoutMilliseconds > 0
            ? options.TimeoutMilliseconds
            : DefaultTimeoutMilliseconds;

        using Process process = new()
        {
            StartInfo = CreateStartInfo(options, cwdResult.FullPath)
        };

        try
        {
            if (!process.Start())
            {
                return McpStdioTransportResult.Failure(
                    McpErrorCode.StartFailed,
                    "MCP stdio server could not be started safely.");
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or IOException
            or UnauthorizedAccessException
            or Win32Exception)
        {
            return McpStdioTransportResult.Failure(
                McpErrorCode.StartFailed,
                "MCP stdio server could not be started safely.");
        }

        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        try
        {
            string requestJson = JsonSerializer.Serialize(request);
            process.StandardInput.WriteLine(requestJson);
            process.StandardInput.Flush();
            process.StandardInput.Close();

            Task<string?> stdoutLineTask = process.StandardOutput.ReadLineAsync();
            using TaskWaiter waiter = TaskWaiter.Start(timeoutMilliseconds, cancellationToken);
            int completedTask = Task.WaitAny([stdoutLineTask, waiter.Task]);
            if (completedTask == 1)
            {
                TryKill(process);
                TryWaitForExit(process, CleanupWaitMilliseconds);
                StderrCapture timedOutStderr = CaptureStderr(stderrTask);
                if (cancellationToken.IsCancellationRequested)
                {
                    return McpStdioTransportResult.Failure(
                        McpErrorCode.Cancelled,
                        "MCP stdio request was cancelled.",
                        stderrSnippet: timedOutStderr.Text,
                        stderrTruncated: timedOutStderr.Truncated);
                }

                return McpStdioTransportResult.Failure(
                    McpErrorCode.Timeout,
                    $"MCP stdio server timed out after {timeoutMilliseconds} ms.",
                    timedOut: true,
                    stderrSnippet: timedOutStderr.Text,
                    stderrTruncated: timedOutStderr.Truncated);
            }

            string? responseLine = stdoutLineTask.GetAwaiter().GetResult();
            CleanupProcess(process);
            StderrCapture stderr = CaptureStderr(stderrTask);

            if (responseLine is null)
            {
                return McpStdioTransportResult.Failure(
                    McpErrorCode.ServerExited,
                    "MCP stdio server exited before returning a response.",
                    stderrSnippet: stderr.Text,
                    stderrTruncated: stderr.Truncated);
            }

            if (!TryDeserializeResponse(responseLine, out McpJsonRpcResponse? response))
            {
                return McpStdioTransportResult.Failure(
                    McpErrorCode.InvalidResponse,
                    "MCP stdio server returned an invalid JSON-RPC response.",
                    stderrSnippet: stderr.Text,
                    stderrTruncated: stderr.Truncated);
            }

            return McpStdioTransportResult.Success(response, stderr.Text, stderr.Truncated);
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or IOException
            or ObjectDisposedException)
        {
            CleanupProcess(process);
            StderrCapture stderr = CaptureStderr(stderrTask);
            return McpStdioTransportResult.Failure(
                McpErrorCode.ServerExited,
                "MCP stdio server exited before returning a response.",
                stderrSnippet: stderr.Text,
                stderrTruncated: stderr.Truncated);
        }
    }

    private WorkspaceGuardResult ResolveWorkingDirectory(WorkspaceContext workspace, string? workingDirectory)
    {
        string requestedPath = string.IsNullOrWhiteSpace(workingDirectory)
            ? workspace.RootPath
            : workingDirectory;

        return workspaceGuard.ResolvePath(workspace, requestedPath);
    }

    private static ProcessStartInfo CreateStartInfo(McpStdioServerOptions options, string workingDirectory)
    {
        ProcessStartInfo startInfo = new(options.Command)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (string argument in options.Args)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static bool TryDeserializeResponse(string responseLine, out McpJsonRpcResponse response)
    {
        response = null!;

        try
        {
            using JsonDocument document = JsonDocument.Parse(responseLine);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("jsonrpc", out JsonElement jsonRpc) ||
                jsonRpc.ValueKind != JsonValueKind.String ||
                jsonRpc.GetString() != "2.0" ||
                !root.TryGetProperty("id", out _) ||
                (!root.TryGetProperty("result", out _) && !root.TryGetProperty("error", out _)))
            {
                return false;
            }

            McpJsonRpcResponse? parsed = JsonSerializer.Deserialize<McpJsonRpcResponse>(responseLine);
            if (parsed is null)
            {
                return false;
            }

            response = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void CleanupProcess(Process process)
    {
        TryWaitForExit(process, 200);
        if (HasExited(process))
        {
            return;
        }

        TryKill(process);
        TryWaitForExit(process, CleanupWaitMilliseconds);
    }

    private static void TryKill(Process process)
    {
        if (HasExited(process))
        {
            return;
        }

        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }

    private static void TryWaitForExit(Process process, int timeoutMilliseconds)
    {
        if (timeoutMilliseconds <= 0)
        {
            return;
        }

        try
        {
            process.WaitForExit(timeoutMilliseconds);
        }
        catch
        {
        }
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch
        {
            return true;
        }
    }

    private static StderrCapture CaptureStderr(Task<string> stderrTask)
    {
        TryWaitForCompletion(stderrTask, CleanupWaitMilliseconds);
        string rawStderr = stderrTask.IsCompletedSuccessfully ? stderrTask.Result : string.Empty;
        string sanitized = SanitizeDiagnostics(rawStderr);
        return TruncateUtf8(sanitized, StderrSnippetMaxBytes);
    }

    private static void TryWaitForCompletion(Task task, int timeoutMilliseconds)
    {
        if (timeoutMilliseconds <= 0)
        {
            return;
        }

        try
        {
            task.Wait(timeoutMilliseconds);
        }
        catch
        {
        }
    }

    private static string SanitizeDiagnostics(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        StringBuilder builder = new(text.Length);
        foreach (char character in text)
        {
            if (character is '\r' or '\n' or '\t' || !char.IsControl(character))
            {
                builder.Append(character);
            }
        }

        string cleaned = builder.ToString();
        cleaned = AuthorizationHeaderPattern.Replace(cleaned, RedactAuthorizationHeader);
        cleaned = BearerTokenPattern.Replace(cleaned, "Bearer [redacted]");
        cleaned = SecretAssignmentPattern.Replace(
            cleaned,
            match => $"{match.Groups[1].Value}{match.Groups[2].Value}[redacted]");
        cleaned = CommandLineSecretPattern.Replace(
            cleaned,
            match => $"{match.Groups[1].Value}[redacted]");
        return cleaned;
    }

    private static string RedactAuthorizationHeader(Match match)
    {
        string separator = match.Groups[2].Value == ":" ? ": " : match.Groups[2].Value;
        string scheme = match.Groups[3].Success ? $"{match.Groups[3].Value} " : string.Empty;
        return $"{match.Groups[1].Value}{separator}{scheme}[redacted]";
    }

    private static StderrCapture TruncateUtf8(string text, int maxBytes)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length <= maxBytes)
        {
            return new StderrCapture(text, false);
        }

        string truncated = Encoding.UTF8.GetString(bytes.AsSpan(0, maxBytes));
        return new StderrCapture(truncated, true);
    }

    private sealed class TaskWaiter : IDisposable
    {
        private readonly CancellationTokenSource timeoutSource = new();
        private readonly CancellationTokenSource linkedSource;

        private TaskWaiter(int timeoutMilliseconds, CancellationToken cancellationToken)
        {
            linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
                timeoutSource.Token,
                cancellationToken);
            Task = Task.Delay(timeoutMilliseconds, linkedSource.Token);
        }

        public Task Task { get; }

        public static TaskWaiter Start(int timeoutMilliseconds, CancellationToken cancellationToken)
        {
            return new TaskWaiter(timeoutMilliseconds, cancellationToken);
        }

        public void Dispose()
        {
            timeoutSource.Cancel();
            linkedSource.Dispose();
            timeoutSource.Dispose();
        }
    }

    private readonly record struct StderrCapture(string Text, bool Truncated);
}
