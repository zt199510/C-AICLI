using System.Diagnostics;
using System.Text;

namespace CSharpAiCli.Core;

internal interface IGitCommandRunner
{
    GitCommandResult Run(
        string workspaceRoot,
        string arguments,
        CancellationToken cancellationToken = default);

    GitCommandResult RunArgumentList(
        string workspaceRoot,
        IEnumerable<string> arguments,
        IReadOnlySet<int>? successfulExitCodes = null,
        CancellationToken cancellationToken = default);

    GitCommandResult RunArgumentListToFile(
        string workspaceRoot,
        IEnumerable<string> arguments,
        string stdoutPath,
        IReadOnlySet<int>? successfulExitCodes = null,
        CancellationToken cancellationToken = default);
}

internal sealed class GitCommandRunner : IGitCommandRunner
{
    private const int DefaultTimeoutMilliseconds = 5000;
    private const int DefaultMaxOutputBytes = 64 * 1024;
    private static readonly IReadOnlySet<int> DefaultSuccessfulExitCodes = new HashSet<int> { 0 };
    private readonly string executable;

    public GitCommandRunner()
        : this("git")
    {
    }

    internal GitCommandRunner(string executable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        this.executable = executable;
    }

    public GitCommandResult Run(
        string workspaceRoot,
        string arguments,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            ProcessStartInfo startInfo = new(executable, arguments)
            {
                WorkingDirectory = workspaceRoot,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            HardenEnvironment(startInfo);
            return RunProcess(
                startInfo,
                DefaultSuccessfulExitCodes,
                cancellationToken);
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
        IReadOnlySet<int>? successfulExitCodes = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            ProcessStartInfo startInfo = new(executable)
            {
                WorkingDirectory = workspaceRoot,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
            HardenEnvironment(startInfo);

            return RunProcess(
                startInfo,
                successfulExitCodes ?? DefaultSuccessfulExitCodes,
                cancellationToken);
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
        IReadOnlySet<int>? successfulExitCodes = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            ProcessStartInfo startInfo = new(executable)
            {
                WorkingDirectory = workspaceRoot,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
            HardenEnvironment(startInfo);

            return RunProcessToFile(
                startInfo,
                stdoutPath,
                successfulExitCodes ?? DefaultSuccessfulExitCodes,
                cancellationToken);
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
        IReadOnlySet<int> successfulExitCodes,
        CancellationToken cancellationToken)
    {
        using Process process = new()
        {
            StartInfo = startInfo
        };

        process.Start();
        process.StandardInput.Close();
        using CancellationTokenRegistration cancellationRegistration =
            cancellationToken.Register(() => TryKill(process));
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

        cancellationToken.ThrowIfCancellationRequested();

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
        IReadOnlySet<int> successfulExitCodes,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(stdoutPath)!);
        using Process process = new()
        {
            StartInfo = startInfo
        };

        using FileStream stdoutFile = File.Create(stdoutPath);
        process.Start();
        process.StandardInput.Close();
        using CancellationTokenRegistration cancellationRegistration =
            cancellationToken.Register(() => TryKill(process));
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

        cancellationToken.ThrowIfCancellationRequested();

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

    private static void HardenEnvironment(ProcessStartInfo startInfo)
    {
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GCM_INTERACTIVE"] = "Never";
        startInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        startInfo.Environment["GIT_PAGER"] = "cat";
        startInfo.Environment["PAGER"] = "cat";
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
