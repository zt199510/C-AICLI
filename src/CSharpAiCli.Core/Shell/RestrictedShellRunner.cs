using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace CSharpAiCli.Core;

public sealed class RestrictedShellRunner : IShellRunner
{
    private const int PostTimeoutCleanupWaitMilliseconds = 1000;

    private readonly IWorkspaceGuard workspaceGuard;

    public RestrictedShellRunner(IWorkspaceGuard workspaceGuard)
    {
        ArgumentNullException.ThrowIfNull(workspaceGuard);
        this.workspaceGuard = workspaceGuard;
    }

    public ShellCommandResult Run(
        WorkspaceContext workspace,
        ShellCommandRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(request);

        WorkspaceGuardResult cwdResult = workspaceGuard.ResolvePath(workspace, request.WorkingDirectory);
        if (!cwdResult.IsAllowed || cwdResult.FullPath is null || !Directory.Exists(cwdResult.FullPath))
        {
            return ShellCommandResult.Failure(
                cwdResult.ErrorCode ?? ToolErrorCode.ShellCwdDenied,
                cwdResult.SafeMessage.Length == 0 ? "Shell cwd must be inside the workspace." : cwdResult.SafeMessage);
        }

        DangerousCommandDetection detection = DangerousCommandDetector.Detect(request.Command);
        if (detection.IsDangerous)
        {
            return ShellCommandResult.Failure(
                ToolErrorCode.DangerousCommandDenied,
                FormatDangerousCommandSummary(detection));
        }

        int timeoutMilliseconds = request.TimeoutMilliseconds > 0 ? request.TimeoutMilliseconds : 30_000;
        int maxStdoutBytes = request.MaxStdoutBytes > 0 ? request.MaxStdoutBytes : 32 * 1024;
        int maxStderrBytes = request.MaxStderrBytes > 0 ? request.MaxStderrBytes : 32 * 1024;

        try
        {
            using Process process = new()
            {
                StartInfo = CreateStartInfo(request.Command, cwdResult.FullPath)
            };

            process.Start();
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            if (!process.WaitForExit(timeoutMilliseconds))
            {
                long cleanupDeadline = Environment.TickCount64 + PostTimeoutCleanupWaitMilliseconds;
                TryKill(process, cleanupDeadline);
                TryWaitForExit(process, RemainingMilliseconds(cleanupDeadline));
                TryWaitForCompletion(Task.WhenAll(stdoutTask, stderrTask), RemainingMilliseconds(cleanupDeadline));
                string timedOutStdout = TryReadCompleted(stdoutTask);
                string timedOutStderr = TryReadCompleted(stderrTask);
                TruncatedText stdout = Truncate(timedOutStdout, maxStdoutBytes);
                TruncatedText stderr = Truncate(timedOutStderr, maxStderrBytes);
                return new ShellCommandResult(
                    Succeeded: false,
                    ExitCode: null,
                    Stdout: stdout.Text,
                    Stderr: stderr.Text,
                    TimedOut: true,
                    StdoutTruncated: stdout.Truncated,
                    StderrTruncated: stderr.Truncated,
                    ErrorCode: ToolErrorCode.ShellTimeout,
                    Summary: $"Shell command timed out after {timeoutMilliseconds} ms.");
            }

            string rawStdout = stdoutTask.GetAwaiter().GetResult();
            string rawStderr = stderrTask.GetAwaiter().GetResult();
            TruncatedText truncatedStdout = Truncate(rawStdout, maxStdoutBytes);
            TruncatedText truncatedStderr = Truncate(rawStderr, maxStderrBytes);
            bool succeeded = process.ExitCode == 0;
            return new ShellCommandResult(
                Succeeded: succeeded,
                ExitCode: process.ExitCode,
                Stdout: truncatedStdout.Text,
                Stderr: truncatedStderr.Text,
                TimedOut: false,
                StdoutTruncated: truncatedStdout.Truncated,
                StderrTruncated: truncatedStderr.Truncated,
                ErrorCode: succeeded ? null : ToolErrorCode.ShellExitCode,
                Summary: succeeded
                    ? $"Shell command completed with exit code {process.ExitCode}."
                    : $"Shell command failed with exit code {process.ExitCode}.");
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or IOException
            or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception)
        {
            return ShellCommandResult.Failure(
                ToolErrorCode.ShellExecutionFailed,
                "Shell command could not be started safely.");
        }
    }

    private static ProcessStartInfo CreateStartInfo(string command, string workingDirectory)
    {
        if (OperatingSystem.IsWindows())
        {
            return new ProcessStartInfo("cmd.exe", "/d /c " + command)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
        }

        return new ProcessStartInfo("/bin/sh", "-c " + command)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
    }

    private static string FormatDangerousCommandSummary(DangerousCommandDetection detection)
    {
        return string.IsNullOrWhiteSpace(detection.MatchedRule)
            ? detection.Reason
            : $"{detection.Reason} Matched rule: {detection.MatchedRule}.";
    }

    private static void TryKill(Process process, long cleanupDeadline)
    {
        if (OperatingSystem.IsWindows() &&
            TryKillWindowsProcessTree(process.Id, cleanupDeadline))
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

    private static bool TryKillWindowsProcessTree(int processId, long cleanupDeadline)
    {
        if (RemainingMilliseconds(cleanupDeadline) <= 0 ||
            !TryGetWindowsTaskkillPath(out string taskkillPath))
        {
            return false;
        }

        try
        {
            using Process taskkill = new()
            {
                StartInfo = new ProcessStartInfo(taskkillPath)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }
            };

            taskkill.StartInfo.ArgumentList.Add("/PID");
            taskkill.StartInfo.ArgumentList.Add(processId.ToString(CultureInfo.InvariantCulture));
            taskkill.StartInfo.ArgumentList.Add("/T");
            taskkill.StartInfo.ArgumentList.Add("/F");

            taskkill.Start();
            Task<string> stdoutTask = taskkill.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = taskkill.StandardError.ReadToEndAsync();
            if (!taskkill.WaitForExit(RemainingMilliseconds(cleanupDeadline)))
            {
                TryKillTaskkill(taskkill);
                return false;
            }

            TryWaitForCompletion(Task.WhenAll(stdoutTask, stderrTask), RemainingMilliseconds(cleanupDeadline));
            return taskkill.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetWindowsTaskkillPath(out string taskkillPath)
    {
        string systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        if (string.IsNullOrWhiteSpace(systemDirectory))
        {
            taskkillPath = string.Empty;
            return false;
        }

        taskkillPath = Path.Combine(systemDirectory, "taskkill.exe");
        return File.Exists(taskkillPath);
    }

    private static void TryKillTaskkill(Process process)
    {
        try
        {
            process.Kill();
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

    private static string TryReadCompleted(Task<string> task)
    {
        return task.IsCompletedSuccessfully ? task.Result : string.Empty;
    }

    private static TruncatedText Truncate(string text, int maxBytes)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length <= maxBytes)
        {
            return new TruncatedText(text, false);
        }

        string truncated = Encoding.UTF8.GetString(bytes.AsSpan(0, maxBytes));
        return new TruncatedText(truncated, true);
    }

    private readonly record struct TruncatedText(string Text, bool Truncated);
}
