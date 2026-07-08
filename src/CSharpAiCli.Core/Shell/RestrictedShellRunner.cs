using System.Diagnostics;
using System.Text;

namespace CSharpAiCli.Core;

public sealed class RestrictedShellRunner : IShellRunner
{
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
                cwdResult.ErrorCode ?? "shell-cwd-denied",
                cwdResult.SafeMessage.Length == 0 ? "Shell cwd must be inside the workspace." : cwdResult.SafeMessage);
        }

        if (DangerousCommandDetector.IsDangerous(request.Command, out string dangerReason))
        {
            return ShellCommandResult.Failure("dangerous-command-denied", dangerReason);
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
                TryKill(process);
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
                    ErrorCode: "shell-timeout",
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
                ErrorCode: succeeded ? null : "shell-exit-code",
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
                "shell-execution-failed",
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

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
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
