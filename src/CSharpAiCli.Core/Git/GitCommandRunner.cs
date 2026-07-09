using System.Diagnostics;
using System.Text;

namespace CSharpAiCli.Core;

internal interface IGitCommandRunner
{
    GitCommandResult Run(string workspaceRoot, string arguments);

    GitCommandResult RunArgumentList(
        string workspaceRoot,
        IEnumerable<string> arguments,
        IReadOnlySet<int>? successfulExitCodes = null);

    GitCommandResult RunArgumentListToFile(
        string workspaceRoot,
        IEnumerable<string> arguments,
        string stdoutPath,
        IReadOnlySet<int>? successfulExitCodes = null);
}

internal sealed class GitCommandRunner : IGitCommandRunner
{
    private const int DefaultTimeoutMilliseconds = 5000;
    private const int DefaultMaxOutputBytes = 64 * 1024;
    private static readonly IReadOnlySet<int> DefaultSuccessfulExitCodes = new HashSet<int> { 0 };

    public GitCommandResult Run(string workspaceRoot, string arguments)
    {
        try
        {
            return RunProcess(
                new ProcessStartInfo("git", arguments)
                {
                    WorkingDirectory = workspaceRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
                DefaultSuccessfulExitCodes);
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or IOException
            or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception)
        {
            return GitUnavailable();
        }
    }

    public GitCommandResult RunArgumentList(
        string workspaceRoot,
        IEnumerable<string> arguments,
        IReadOnlySet<int>? successfulExitCodes = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        try
        {
            ProcessStartInfo startInfo = new("git")
            {
                WorkingDirectory = workspaceRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            return RunProcess(startInfo, successfulExitCodes ?? DefaultSuccessfulExitCodes);
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or IOException
            or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception)
        {
            return GitUnavailable();
        }
    }

    public GitCommandResult RunArgumentListToFile(
        string workspaceRoot,
        IEnumerable<string> arguments,
        string stdoutPath,
        IReadOnlySet<int>? successfulExitCodes = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        try
        {
            ProcessStartInfo startInfo = new("git")
            {
                WorkingDirectory = workspaceRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            return RunProcessToFile(startInfo, stdoutPath, successfulExitCodes ?? DefaultSuccessfulExitCodes);
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or IOException
            or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception)
        {
            return GitUnavailable();
        }
    }

    private static GitCommandResult RunProcess(
        ProcessStartInfo startInfo,
        IReadOnlySet<int> successfulExitCodes)
    {
        using Process process = new()
        {
            StartInfo = startInfo
        };

        process.Start();
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(DefaultTimeoutMilliseconds))
        {
            TryKill(process);
            string timedOutStdout = stdoutTask.IsCompletedSuccessfully ? stdoutTask.Result : string.Empty;
            string timedOutStderr = stderrTask.IsCompletedSuccessfully ? stderrTask.Result : string.Empty;
            TruncatedText timedOutTruncatedStdout = Truncate(timedOutStdout, DefaultMaxOutputBytes);
            TruncatedText timedOutTruncatedStderr = Truncate(timedOutStderr, DefaultMaxOutputBytes);
            return new GitCommandResult(
                Succeeded: false,
                ExitCode: null,
                Stdout: timedOutTruncatedStdout.Text,
                Stderr: timedOutTruncatedStderr.Text,
                StdoutTruncated: timedOutTruncatedStdout.Truncated,
                StderrTruncated: timedOutTruncatedStderr.Truncated,
                ErrorCode: ToolErrorCode.GitTimeout,
                Summary: "Git command timed out.");
        }

        TruncatedText stdout = Truncate(stdoutTask.GetAwaiter().GetResult(), DefaultMaxOutputBytes);
        TruncatedText stderr = Truncate(stderrTask.GetAwaiter().GetResult(), DefaultMaxOutputBytes);
        bool succeeded = successfulExitCodes.Contains(process.ExitCode);
        return new GitCommandResult(
            Succeeded: succeeded,
            ExitCode: process.ExitCode,
            Stdout: stdout.Text,
            Stderr: stderr.Text,
            StdoutTruncated: stdout.Truncated,
            StderrTruncated: stderr.Truncated,
            ErrorCode: succeeded ? null : ToolErrorCode.GitCommandFailed,
            Summary: succeeded
                ? $"Git command completed with exit code {process.ExitCode}."
                : $"Git command failed with exit code {process.ExitCode}.");
    }

    private static GitCommandResult RunProcessToFile(
        ProcessStartInfo startInfo,
        string stdoutPath,
        IReadOnlySet<int> successfulExitCodes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(stdoutPath)!);
        using Process process = new()
        {
            StartInfo = startInfo
        };

        using FileStream stdoutFile = File.Create(stdoutPath);
        process.Start();
        Task stdoutTask = process.StandardOutput.BaseStream.CopyToAsync(stdoutFile);
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(DefaultTimeoutMilliseconds))
        {
            TryKill(process);
            string timedOutStderr = stderrTask.IsCompletedSuccessfully ? stderrTask.Result : string.Empty;
            TruncatedText timedOutTruncatedStderr = Truncate(timedOutStderr, DefaultMaxOutputBytes);
            return new GitCommandResult(
                Succeeded: false,
                ExitCode: null,
                Stdout: string.Empty,
                Stderr: timedOutTruncatedStderr.Text,
                StdoutTruncated: false,
                StderrTruncated: timedOutTruncatedStderr.Truncated,
                ErrorCode: ToolErrorCode.GitTimeout,
                Summary: "Git command timed out.");
        }

        stdoutTask.GetAwaiter().GetResult();
        TruncatedText stderr = Truncate(stderrTask.GetAwaiter().GetResult(), DefaultMaxOutputBytes);
        bool succeeded = successfulExitCodes.Contains(process.ExitCode);
        return new GitCommandResult(
            Succeeded: succeeded,
            ExitCode: process.ExitCode,
            Stdout: string.Empty,
            Stderr: stderr.Text,
            StdoutTruncated: false,
            StderrTruncated: stderr.Truncated,
            ErrorCode: succeeded ? null : ToolErrorCode.GitCommandFailed,
            Summary: succeeded
                ? $"Git command completed with exit code {process.ExitCode}."
                : $"Git command failed with exit code {process.ExitCode}.");
    }

    private static GitCommandResult GitUnavailable()
    {
        return new GitCommandResult(
            Succeeded: false,
            ExitCode: null,
            Stdout: string.Empty,
            Stderr: string.Empty,
            StdoutTruncated: false,
            StderrTruncated: false,
            ErrorCode: ToolErrorCode.GitUnavailable,
            Summary: "Git command could not be started.");
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

    private static TruncatedText Truncate(string text, int maxBytes)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length <= maxBytes)
        {
            return new TruncatedText(text, false);
        }

        return new TruncatedText(Encoding.UTF8.GetString(bytes.AsSpan(0, maxBytes)), true);
    }

    private readonly record struct TruncatedText(string Text, bool Truncated);
}
