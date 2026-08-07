using CSharpAiCli.AppHost.Protocol;

namespace CSharpAiCli.Tests;

public sealed class DesktopUserTerminalSupervisorTests
{
    [Fact]
    public void User_terminal_profiles_are_detected_and_default_to_powershell()
    {
        var result = DesktopUserTerminalSupervisor.GetProfiles();
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal("system-default", result.Data.Profiles[0].ProfileId);
        Assert.True(result.Data.Profiles[0].IsDefault);
        Assert.Equal(result.Data.Profiles.Count, result.Data.Profiles.Select(profile => profile.ProfileId).Distinct().Count());
        Assert.All(result.Data.Profiles, profile =>
            Assert.Contains(profile.ProfileId, new[] { "system-default", "powershell", "cmd", "wsl", "git-bash" }));
        if (OperatingSystem.IsWindows()) Assert.Contains(result.Data.Profiles, profile => profile.ProfileId == "cmd");
    }

    [Fact]
    public async Task User_terminal_is_bounded_audited_and_cleaned_up()
    {
        string root = Path.Combine(Path.GetTempPath(), "caicli-terminal-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using DesktopUserTerminalSupervisor supervisor = new();
            var opened = supervisor.Open(root, OperatingSystem.IsWindows() ? "cmd" : "system-default", "open-1");
            Assert.True(opened.Succeeded);
            string sessionId = opened.Data!.SessionId;
            var input = supervisor.Input(sessionId, "echo terminal-user-sentinel" + Environment.NewLine, "input-1");
            Assert.True(input.Succeeded);

            string output = string.Empty;
            for (int attempt = 0; attempt < 40 && !output.Contains("terminal-user-sentinel", StringComparison.Ordinal); attempt++)
            {
                await Task.Delay(25);
                output = supervisor.Get(sessionId, 0).Data?.Output ?? string.Empty;
            }

            var finalSnapshot = supervisor.Get(sessionId, 0);
            var closed = supervisor.Close(sessionId, "close-1");
            Assert.True(output.Contains("terminal-user-sentinel", StringComparison.Ordinal),
                $"status={finalSnapshot.Data?.Status}; exit={finalSnapshot.Data?.ExitCode}; output={output}");
            Assert.True(closed.Succeeded);
            Assert.Equal("closed", closed.Data?.Status);
            string audit = File.ReadAllText(Path.Combine(root, ".caicli", "terminal-audit.jsonl"));
            Assert.Contains("terminal.user.open", audit, StringComparison.Ordinal);
            Assert.Contains("terminal.user.close", audit, StringComparison.Ordinal);
            Assert.DoesNotContain("terminal-user-sentinel", audit, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void User_terminal_rejects_unknown_session_and_duplicate_open_is_idempotent()
    {
        string root = Path.Combine(Path.GetTempPath(), "caicli-terminal-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using DesktopUserTerminalSupervisor supervisor = new();
            var first = supervisor.Open(root, OperatingSystem.IsWindows() ? "cmd" : "system-default", "open-1");
            var duplicate = supervisor.Open(root, OperatingSystem.IsWindows() ? "cmd" : "system-default", "open-1");
            var mismatch = supervisor.Open(root, OperatingSystem.IsWindows() ? "powershell" : "system-default", "open-1");
            var missing = supervisor.Input("terminal_missing", "echo unsafe", "input-1");
            Assert.Equal(first.Data?.SessionId, duplicate.Data?.SessionId);
            if (OperatingSystem.IsWindows())
            {
                Assert.False(mismatch.Succeeded);
                Assert.Equal("terminal-mutation-conflict", mismatch.Error?.Code);
            }
            Assert.False(missing.Succeeded);
            Assert.Equal("terminal-not-found", missing.Error?.Code);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task User_terminal_truncates_output_flood_and_survives_resize_storm()
    {
        string root = Path.Combine(Path.GetTempPath(), "caicli-terminal-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using DesktopUserTerminalSupervisor supervisor = new();
            var opened = supervisor.Open(root, OperatingSystem.IsWindows() ? "cmd" : "system-default", "open-flood");
            string sessionId = opened.Data!.SessionId;
            for (int index = 0; index < 200; index++)
            {
                var resized = supervisor.Resize(sessionId, 80 + index % 20, 24 + index % 10, "resize-" + index);
                Assert.True(resized.Succeeded);
            }
            string command = OperatingSystem.IsWindows()
                ? "for /L %i in (1,1,5000) do @echo 01234567890123456789" + Environment.NewLine
                : "yes 01234567890123456789 | head -n 5000" + Environment.NewLine;
            Assert.True(supervisor.Input(sessionId, command, "input-flood").Succeeded);

            var snapshot = supervisor.Get(sessionId, 0);
            for (int attempt = 0; attempt < 100 && snapshot.Data?.Truncated != true; attempt++)
            {
                await Task.Delay(25);
                snapshot = supervisor.Get(sessionId, 0);
            }

            Assert.True(snapshot.Data?.Truncated);
            Assert.True(System.Text.Encoding.UTF8.GetByteCount(snapshot.Data?.Output ?? string.Empty) <= 65_536);
            Assert.True(supervisor.Close(sessionId, "close-flood").Succeeded);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task User_terminal_supports_multiple_independent_sessions()
    {
        string root = Path.Combine(Path.GetTempPath(), "caicli-terminal-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using DesktopUserTerminalSupervisor supervisor = new();
            string profile = OperatingSystem.IsWindows() ? "cmd" : "system-default";
            var first = supervisor.Open(root, profile, "open-first");
            var second = supervisor.Open(root, profile, "open-second");
            Assert.True(first.Succeeded);
            Assert.True(second.Succeeded);
            Assert.NotEqual(first.Data?.SessionId, second.Data?.SessionId);

            string firstId = first.Data!.SessionId;
            string secondId = second.Data!.SessionId;
            Assert.True(supervisor.Input(firstId, "echo first-session" + Environment.NewLine, "input-first").Succeeded);
            Assert.True(supervisor.Input(secondId, "echo second-session" + Environment.NewLine, "input-second").Succeeded);

            string firstOutput = string.Empty;
            string secondOutput = string.Empty;
            for (int attempt = 0; attempt < 80 &&
                 (!firstOutput.Contains("first-session", StringComparison.Ordinal) ||
                  !secondOutput.Contains("second-session", StringComparison.Ordinal)); attempt++)
            {
                await Task.Delay(25);
                firstOutput = supervisor.Get(firstId, 0).Data?.Output ?? string.Empty;
                secondOutput = supervisor.Get(secondId, 0).Data?.Output ?? string.Empty;
            }

            Assert.Contains("first-session", firstOutput, StringComparison.Ordinal);
            Assert.Contains("second-session", secondOutput, StringComparison.Ordinal);
            Assert.True(supervisor.Close(firstId, "close-first").Succeeded);
            Assert.Equal("running", supervisor.Get(secondId, 0).Data?.Status);
            Assert.True(supervisor.Close(secondId, "close-second").Succeeded);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
