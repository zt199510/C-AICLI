using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CSharpAiCli.Core;

public sealed class McpStdioSession : IDisposable
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

    private readonly Process process;
    private readonly BoundedStderrCapture stderrCapture;
    private readonly int timeoutMilliseconds;
    private readonly object sendGate = new();

    private bool disposed;

    private McpStdioSession(
        Process process,
        BoundedStderrCapture stderrCapture,
        int timeoutMilliseconds)
    {
        this.process = process;
        this.stderrCapture = stderrCapture;
        this.timeoutMilliseconds = timeoutMilliseconds;
    }

    public static McpStdioSessionOpenResult Open(
        WorkspaceContext workspace,
        McpStdioServerOptions options,
        IWorkspaceGuard workspaceGuard,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(workspaceGuard);

        if (cancellationToken.IsCancellationRequested)
        {
            return McpStdioSessionOpenResult.Failure(
                McpErrorCode.Cancelled,
                "MCP stdio request was cancelled.");
        }

        if (string.IsNullOrWhiteSpace(options.Command))
        {
            return McpStdioSessionOpenResult.Failure(
                McpErrorCode.StartFailed,
                "MCP stdio command is not configured.");
        }

        WorkspaceGuardResult cwdResult = ResolveWorkingDirectory(
            workspaceGuard,
            workspace,
            options.WorkingDirectory);
        if (!cwdResult.IsAllowed || cwdResult.FullPath is null)
        {
            return McpStdioSessionOpenResult.Failure(
                McpErrorCode.CwdDenied,
                cwdResult.SafeMessage.Length == 0
                    ? "MCP stdio cwd must remain inside the workspace."
                    : cwdResult.SafeMessage);
        }

        if (!Directory.Exists(cwdResult.FullPath))
        {
            return McpStdioSessionOpenResult.Failure(
                McpErrorCode.CwdDenied,
                "MCP stdio cwd must exist inside the workspace.");
        }

        Process process = new()
        {
            StartInfo = CreateStartInfo(options, cwdResult.FullPath)
        };

        try
        {
            if (!process.Start())
            {
                process.Dispose();
                return McpStdioSessionOpenResult.Failure(
                    McpErrorCode.StartFailed,
                    "MCP stdio server could not be started safely.");
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or IOException
            or UnauthorizedAccessException
            or Win32Exception)
        {
            process.Dispose();
            return McpStdioSessionOpenResult.Failure(
                McpErrorCode.StartFailed,
                "MCP stdio server could not be started safely.");
        }

        int timeout = options.TimeoutMilliseconds > 0
            ? options.TimeoutMilliseconds
            : DefaultTimeoutMilliseconds;
        BoundedStderrCapture stderrCapture = BoundedStderrCapture.Start(
            process.StandardError.BaseStream,
            StderrSnippetMaxBytes);

        return McpStdioSessionOpenResult.Success(new McpStdioSession(
            process,
            stderrCapture,
            timeout));
    }

    public McpStdioTransportResult Send(
        McpJsonRpcRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (sendGate)
        {
            if (disposed || HasExited(process))
            {
                StderrCapture stderr = stderrCapture.Snapshot();
                return McpStdioTransportResult.Failure(
                    McpErrorCode.ServerExited,
                    "MCP stdio server exited before returning a response.",
                    stderrSnippet: stderr.Text,
                    stderrTruncated: stderr.Truncated);
            }

            return SendCoreAsync(request, cancellationToken).GetAwaiter().GetResult();
        }
    }

    public McpStdioTransportResult SendNotification(
        McpJsonRpcNotification notification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        lock (sendGate)
        {
            if (disposed || HasExited(process))
            {
                StderrCapture stderr = stderrCapture.Snapshot();
                return McpStdioTransportResult.Failure(
                    McpErrorCode.ServerExited,
                    "MCP stdio server exited before accepting a notification.",
                    stderrSnippet: stderr.Text,
                    stderrTruncated: stderr.Truncated);
            }

            return SendNotificationCoreAsync(notification, cancellationToken).GetAwaiter().GetResult();
        }
    }

    public void Dispose()
    {
        lock (sendGate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            CleanupProcess(process);
            stderrCapture.Dispose();
            process.Dispose();
        }
    }

    private async Task<McpStdioTransportResult> SendCoreAsync(
        McpJsonRpcRequest request,
        CancellationToken cancellationToken)
    {
        long deadline = Environment.TickCount64 + timeoutMilliseconds;

        try
        {
            DeadlineOutcome writeOutcome = await WriteJsonLineAsync(
                request,
                deadline,
                cancellationToken).ConfigureAwait(false);
            if (writeOutcome != DeadlineOutcome.Completed)
            {
                return DeadlineFailure(writeOutcome);
            }

            bool sawMismatchedResponse = false;
            while (true)
            {
                DeadlineValueOutcome<string?> readOutcome = await WaitForDeadlineAsync(
                    process.StandardOutput.ReadLineAsync(),
                    deadline,
                    cancellationToken).ConfigureAwait(false);
                if (readOutcome.Outcome != DeadlineOutcome.Completed)
                {
                    return readOutcome.Outcome == DeadlineOutcome.TimedOut && sawMismatchedResponse
                        ? MismatchedIdFailure()
                        : DeadlineFailure(readOutcome.Outcome);
                }

                string? responseLine = readOutcome.Value;
                StderrCapture stderr = stderrCapture.Snapshot();
                if (responseLine is null)
                {
                    CleanupProcess(process);
                    return sawMismatchedResponse
                        ? MismatchedIdFailure()
                        : McpStdioTransportResult.Failure(
                            McpErrorCode.ServerExited,
                            "MCP stdio server exited before returning a response.",
                            stderrSnippet: stderr.Text,
                            stderrTruncated: stderr.Truncated);
                }

                JsonRpcLineKind lineKind = ClassifyJsonRpcLine(responseLine, out McpJsonRpcResponse? response);
                if (lineKind == JsonRpcLineKind.Notification)
                {
                    continue;
                }

                if (lineKind != JsonRpcLineKind.Response || response is null)
                {
                    CleanupProcess(process);
                    return McpStdioTransportResult.Failure(
                        McpErrorCode.InvalidResponse,
                        "MCP stdio server returned an invalid JSON-RPC response.",
                        stderrSnippet: stderr.Text,
                        stderrTruncated: stderr.Truncated);
                }

                if (!JsonElementIdsEqual(request.Id, response.Id))
                {
                    sawMismatchedResponse = true;
                    continue;
                }

                return McpStdioTransportResult.Success(response, stderr.Text, stderr.Truncated);
            }
        }
        catch (OperationCanceledException)
        {
            return DeadlineFailure(
                cancellationToken.IsCancellationRequested
                    ? DeadlineOutcome.Cancelled
                    : DeadlineOutcome.TimedOut);
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or IOException
            or ObjectDisposedException)
        {
            CleanupProcess(process);
            StderrCapture stderr = stderrCapture.Snapshot();
            return McpStdioTransportResult.Failure(
                McpErrorCode.ServerExited,
                "MCP stdio server exited before returning a response.",
                stderrSnippet: stderr.Text,
                stderrTruncated: stderr.Truncated);
        }
    }

    private async Task<McpStdioTransportResult> SendNotificationCoreAsync(
        McpJsonRpcNotification notification,
        CancellationToken cancellationToken)
    {
        long deadline = Environment.TickCount64 + timeoutMilliseconds;

        try
        {
            DeadlineOutcome writeOutcome = await WriteJsonLineAsync(
                notification,
                deadline,
                cancellationToken).ConfigureAwait(false);
            if (writeOutcome != DeadlineOutcome.Completed)
            {
                return DeadlineFailure(writeOutcome);
            }

            StderrCapture stderr = stderrCapture.Snapshot();
            return McpStdioTransportResult.NotificationSent(stderr.Text, stderr.Truncated);
        }
        catch (OperationCanceledException)
        {
            return DeadlineFailure(
                cancellationToken.IsCancellationRequested
                    ? DeadlineOutcome.Cancelled
                    : DeadlineOutcome.TimedOut);
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or IOException
            or ObjectDisposedException)
        {
            CleanupProcess(process);
            StderrCapture stderr = stderrCapture.Snapshot();
            return McpStdioTransportResult.Failure(
                McpErrorCode.ServerExited,
                "MCP stdio server exited before accepting a notification.",
                stderrSnippet: stderr.Text,
                stderrTruncated: stderr.Truncated);
        }
    }

    private async Task<DeadlineOutcome> WriteJsonLineAsync<T>(
        T message,
        long deadline,
        CancellationToken cancellationToken)
    {
        string json = JsonSerializer.Serialize(message);
        byte[] bytes = Encoding.UTF8.GetBytes(json + "\n");
        DeadlineOutcome writeOutcome = await WaitForDeadlineAsync(
            process.StandardInput.BaseStream.WriteAsync(bytes).AsTask(),
            deadline,
            cancellationToken).ConfigureAwait(false);
        if (writeOutcome != DeadlineOutcome.Completed)
        {
            return writeOutcome;
        }

        return await WaitForDeadlineAsync(
            process.StandardInput.BaseStream.FlushAsync(),
            deadline,
            cancellationToken).ConfigureAwait(false);
    }

    private McpStdioTransportResult DeadlineFailure(DeadlineOutcome outcome)
    {
        CleanupProcess(process);
        StderrCapture stderr = stderrCapture.FinalSnapshot(CleanupWaitMilliseconds);
        if (outcome == DeadlineOutcome.Cancelled)
        {
            return McpStdioTransportResult.Failure(
                McpErrorCode.Cancelled,
                "MCP stdio request was cancelled.",
                stderrSnippet: stderr.Text,
                stderrTruncated: stderr.Truncated);
        }

        return McpStdioTransportResult.Failure(
            McpErrorCode.Timeout,
            $"MCP stdio server timed out after {timeoutMilliseconds} ms.",
            timedOut: true,
            stderrSnippet: stderr.Text,
            stderrTruncated: stderr.Truncated);
    }

    private McpStdioTransportResult MismatchedIdFailure()
    {
        CleanupProcess(process);
        StderrCapture stderr = stderrCapture.FinalSnapshot(CleanupWaitMilliseconds);
        return McpStdioTransportResult.Failure(
            McpErrorCode.InvalidResponse,
            "MCP stdio server returned a response with a mismatched id.",
            stderrSnippet: stderr.Text,
            stderrTruncated: stderr.Truncated);
    }

    private static async Task<DeadlineOutcome> WaitForDeadlineAsync(
        Task operation,
        long deadline,
        CancellationToken cancellationToken)
    {
        int remainingMilliseconds = RemainingMilliseconds(deadline);
        if (remainingMilliseconds <= 0)
        {
            ObserveFault(operation);
            return DeadlineOutcome.TimedOut;
        }

        Task delayTask = Task.Delay(remainingMilliseconds, cancellationToken);
        Task completedTask = await Task.WhenAny(operation, delayTask).ConfigureAwait(false);
        if (completedTask == operation)
        {
            await operation.ConfigureAwait(false);
            return DeadlineOutcome.Completed;
        }

        ObserveFault(operation);
        return cancellationToken.IsCancellationRequested
            ? DeadlineOutcome.Cancelled
            : DeadlineOutcome.TimedOut;
    }

    private static async Task<DeadlineValueOutcome<T>> WaitForDeadlineAsync<T>(
        Task<T> operation,
        long deadline,
        CancellationToken cancellationToken)
    {
        int remainingMilliseconds = RemainingMilliseconds(deadline);
        if (remainingMilliseconds <= 0)
        {
            ObserveFault(operation);
            return new DeadlineValueOutcome<T>(DeadlineOutcome.TimedOut, default);
        }

        Task delayTask = Task.Delay(remainingMilliseconds, cancellationToken);
        Task completedTask = await Task.WhenAny(operation, delayTask).ConfigureAwait(false);
        if (completedTask == operation)
        {
            T value = await operation.ConfigureAwait(false);
            return new DeadlineValueOutcome<T>(DeadlineOutcome.Completed, value);
        }

        ObserveFault(operation);
        return new DeadlineValueOutcome<T>(
            cancellationToken.IsCancellationRequested
                ? DeadlineOutcome.Cancelled
                : DeadlineOutcome.TimedOut,
            default);
    }

    private static void ObserveFault(Task task)
    {
        _ = task.ContinueWith(
            completedTask => _ = completedTask.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static WorkspaceGuardResult ResolveWorkingDirectory(
        IWorkspaceGuard workspaceGuard,
        WorkspaceContext workspace,
        string? workingDirectory)
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

    private static JsonRpcLineKind ClassifyJsonRpcLine(
        string responseLine,
        out McpJsonRpcResponse? response)
    {
        response = null;

        try
        {
            using JsonDocument document = JsonDocument.Parse(responseLine);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("jsonrpc", out JsonElement jsonRpc) ||
                jsonRpc.ValueKind != JsonValueKind.String ||
                jsonRpc.GetString() != "2.0")
            {
                return JsonRpcLineKind.Invalid;
            }

            bool hasId = root.TryGetProperty("id", out _);
            bool hasMethod = root.TryGetProperty("method", out JsonElement method) &&
                method.ValueKind == JsonValueKind.String;
            bool hasResponsePayload = root.TryGetProperty("result", out _) ||
                root.TryGetProperty("error", out _);
            if (!hasId && hasMethod)
            {
                return JsonRpcLineKind.Notification;
            }

            if (!hasId || !hasResponsePayload)
            {
                return JsonRpcLineKind.Invalid;
            }

            McpJsonRpcResponse? parsed = JsonSerializer.Deserialize<McpJsonRpcResponse>(responseLine);
            if (parsed is null)
            {
                return JsonRpcLineKind.Invalid;
            }

            response = parsed;
            return JsonRpcLineKind.Response;
        }
        catch (JsonException)
        {
            return JsonRpcLineKind.Invalid;
        }
    }

    private static bool JsonElementIdsEqual(JsonElement expected, JsonElement actual)
    {
        if (expected.ValueKind != actual.ValueKind)
        {
            return false;
        }

        return expected.ValueKind == JsonValueKind.String
            ? string.Equals(expected.GetString(), actual.GetString(), StringComparison.Ordinal)
            : string.Equals(expected.GetRawText(), actual.GetRawText(), StringComparison.Ordinal);
    }

    private static void CleanupProcess(Process process)
    {
        TryCloseStandardInput(process);
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

    private static void TryCloseStandardInput(Process process)
    {
        try
        {
            process.StandardInput.Close();
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

    private static int RemainingMilliseconds(long deadline)
    {
        long remaining = deadline - Environment.TickCount64;
        if (remaining <= 0)
        {
            return 0;
        }

        return remaining > int.MaxValue ? int.MaxValue : (int)remaining;
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

    private sealed class BoundedStderrCapture : IDisposable
    {
        private readonly byte[] capturedBytes;
        private readonly CancellationTokenSource cancellationTokenSource = new();
        private readonly Task readTask;
        private readonly object gate = new();

        private int capturedLength;
        private bool truncated;

        private BoundedStderrCapture(Stream stream, int maxBytes)
        {
            capturedBytes = new byte[maxBytes];
            readTask = Task.Run(() => ReadLoopAsync(stream, cancellationTokenSource.Token));
        }

        public static BoundedStderrCapture Start(Stream stream, int maxBytes)
        {
            return new BoundedStderrCapture(stream, maxBytes);
        }

        public StderrCapture Snapshot()
        {
            byte[] snapshotBytes;
            bool isTruncated;
            lock (gate)
            {
                snapshotBytes = capturedBytes.AsSpan(0, capturedLength).ToArray();
                isTruncated = truncated;
            }

            string text = Encoding.UTF8.GetString(snapshotBytes);
            return new StderrCapture(SanitizeDiagnostics(text), isTruncated);
        }

        public StderrCapture FinalSnapshot(int timeoutMilliseconds)
        {
            TryWaitForCompletion(readTask, timeoutMilliseconds);
            return Snapshot();
        }

        public void Dispose()
        {
            cancellationTokenSource.Cancel();
            TryWaitForCompletion(readTask, CleanupWaitMilliseconds);
            cancellationTokenSource.Dispose();
        }

        private async Task ReadLoopAsync(Stream stream, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[1024];

            try
            {
                while (true)
                {
                    int bytesRead = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (bytesRead == 0)
                    {
                        return;
                    }

                    lock (gate)
                    {
                        int remaining = capturedBytes.Length - capturedLength;
                        int bytesToStore = Math.Min(remaining, bytesRead);
                        if (bytesToStore > 0)
                        {
                            buffer.AsSpan(0, bytesToStore).CopyTo(
                                capturedBytes.AsSpan(capturedLength, bytesToStore));
                            capturedLength += bytesToStore;
                        }

                        if (bytesToStore < bytesRead)
                        {
                            truncated = true;
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception) when (exception is IOException
                or ObjectDisposedException
                or InvalidOperationException)
            {
            }
        }
    }

    private enum DeadlineOutcome
    {
        Completed,
        TimedOut,
        Cancelled
    }

    private enum JsonRpcLineKind
    {
        Invalid,
        Notification,
        Response
    }

    private readonly record struct DeadlineValueOutcome<T>(DeadlineOutcome Outcome, T? Value);

    private readonly record struct StderrCapture(string Text, bool Truncated);
}
