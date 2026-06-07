using System.Diagnostics;
using System.Text;

namespace CSharpAiCli.Core;

internal sealed class GitCommandRunner
{
    private const int DefaultTimeoutMilliseconds = 5000;
    private const int DefaultMaxOutputBytes = 64 * 1024;

    public GitCommandResult Run(string workspaceRoot, string arguments)
    {
        try
        {
            using Process process = new()
            {
                StartInfo = new ProcessStartInfo("git", arguments)
                {
                    WorkingDirectory = workspaceRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };

            process.Start();
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(DefaultTimeoutMilliseconds))
            {
                TryKill(process);
                string timedOutStdout = stdoutTask.IsCompletedSuccessfully ? stdoutTask.Result : string.Empty;
                string timedOutStderr = stderrTask.IsCompletedSuccessfully ? stderrTask.Result : string.Empty;
                return new GitCommandResult(
                    Succeeded: false,
                    ExitCode: null,
                    Stdout: Truncate(timedOutStdout, DefaultMaxOutputBytes),
                    Stderr: Truncate(timedOutStderr, DefaultMaxOutputBytes),
                    ErrorCode: "git-timeout",
                    Summary: "Git command timed out.");
            }

            string stdout = Truncate(stdoutTask.GetAwaiter().GetResult(), DefaultMaxOutputBytes);
            string stderr = Truncate(stderrTask.GetAwaiter().GetResult(), DefaultMaxOutputBytes);
            return new GitCommandResult(
                Succeeded: process.ExitCode == 0,
                ExitCode: process.ExitCode,
                Stdout: stdout,
                Stderr: stderr,
                ErrorCode: process.ExitCode == 0 ? null : "git-command-failed",
                Summary: process.ExitCode == 0
                    ? $"Git command completed with exit code {process.ExitCode}."
                    : $"Git command failed with exit code {process.ExitCode}.");
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or IOException
            or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception)
        {
            return new GitCommandResult(
                Succeeded: false,
                ExitCode: null,
                Stdout: string.Empty,
                Stderr: string.Empty,
                ErrorCode: "git-unavailable",
                Summary: "Git command could not be started.");
        }
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

    private static string Truncate(string text, int maxBytes)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        return bytes.Length <= maxBytes
            ? text
            : Encoding.UTF8.GetString(bytes.AsSpan(0, maxBytes));
    }
}
