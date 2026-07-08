using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class RestrictedShellRunnerTests
{
    [Fact]
    public void Run_executes_harmless_command_inside_workspace()
    {
        using TempDirectory temp = TempDirectory.Create();
        RestrictedShellRunner runner = new(new WorkspaceGuard());

        ShellCommandResult result = runner.Run(
            WorkspaceContext.Detect(temp.Path, temp.Path),
            new ShellCommandRequest(
                Command: "dotnet --version",
                WorkingDirectory: ".",
                TimeoutMilliseconds: 10_000,
                MaxStdoutBytes: 1024,
                MaxStderrBytes: 1024));

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.False(result.StdoutTruncated);
        Assert.False(string.IsNullOrWhiteSpace(result.Stdout));
    }

    [Fact]
    public void Run_denies_cwd_outside_workspace()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspaceRoot = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(Path.Combine(temp.Path, "outside"));
        RestrictedShellRunner runner = new(new WorkspaceGuard());

        ShellCommandResult result = runner.Run(
            WorkspaceContext.Detect(workspaceRoot, temp.Path),
            new ShellCommandRequest(
                Command: "dotnet --version",
                WorkingDirectory: "../outside",
                TimeoutMilliseconds: 10_000,
                MaxStdoutBytes: 1024,
                MaxStderrBytes: 1024));

        Assert.False(result.Succeeded);
        Assert.Equal("workspace-boundary-denied", result.ErrorCode);
    }

    [Fact]
    public void Run_denies_dangerous_command()
    {
        using TempDirectory temp = TempDirectory.Create();
        RestrictedShellRunner runner = new(new WorkspaceGuard());

        ShellCommandResult result = runner.Run(
            WorkspaceContext.Detect(temp.Path, temp.Path),
            new ShellCommandRequest(
                Command: "rm -rf .",
                WorkingDirectory: ".",
                TimeoutMilliseconds: 10_000,
                MaxStdoutBytes: 1024,
                MaxStderrBytes: 1024));

        Assert.False(result.Succeeded);
        Assert.Equal("dangerous-command-denied", result.ErrorCode);
    }

    [Fact]
    public void Run_times_out_long_running_command()
    {
        using TempDirectory temp = TempDirectory.Create();
        RestrictedShellRunner runner = new(new WorkspaceGuard());

        ShellCommandResult result = runner.Run(
            WorkspaceContext.Detect(temp.Path, temp.Path),
            new ShellCommandRequest(
                Command: CreateSleepCommand(),
                WorkingDirectory: ".",
                TimeoutMilliseconds: 200,
                MaxStdoutBytes: 1024,
                MaxStderrBytes: 1024));

        Assert.False(result.Succeeded);
        Assert.True(result.TimedOut);
        Assert.Equal("shell-timeout", result.ErrorCode);
    }

    [Fact]
    public void Run_truncates_stdout()
    {
        using TempDirectory temp = TempDirectory.Create();
        RestrictedShellRunner runner = new(new WorkspaceGuard());

        ShellCommandResult result = runner.Run(
            WorkspaceContext.Detect(temp.Path, temp.Path),
            new ShellCommandRequest(
                Command: CreateLongOutputCommand(),
                WorkingDirectory: ".",
                TimeoutMilliseconds: 10_000,
                MaxStdoutBytes: 8,
                MaxStderrBytes: 1024));

        Assert.True(result.Succeeded);
        Assert.True(result.StdoutTruncated);
        Assert.True(result.Stdout.Length <= 8);
    }

    private static string CreateSleepCommand()
    {
        return OperatingSystem.IsWindows()
            ? "ping -n 3 127.0.0.1 > nul"
            : "sleep 2";
    }

    private static string CreateLongOutputCommand()
    {
        return OperatingSystem.IsWindows()
            ? "echo 12345678901234567890"
            : "printf 12345678901234567890";
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
                DeleteDirectoryWithRetry(Path);
            }
        }

        private static void DeleteDirectoryWithRetry(string path)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    Directory.Delete(path, recursive: true);
                    return;
                }
                catch (Exception exception) when (attempt < 20 && exception is IOException or UnauthorizedAccessException)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }
}
