using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CSharpAiCli.AppHost.Protocol.Generated;

namespace CSharpAiCli.AppHost.Protocol;

internal sealed class DesktopUserTerminalSupervisor : IDisposable
{
    private const int MaxOutputBytes = 65_536;
    private readonly object sync = new();
    private readonly StringBuilder output = new();
    private readonly Dictionary<string, string> mutations = new(StringComparer.Ordinal);
    private Process? process;
    private string? workspaceRoot;
    private string? sessionId;
    private string shellProfile = "system-default";
    private string status = "closed";
    private DateTimeOffset startedAtUtc;
    private DateTimeOffset? exitedAtUtc;
    private int? exitCode;
    private long cursor;
    private bool truncated;
    private bool disposed;

    public TerminalStateResult Open(string root, string profile, string mutationId)
    {
        lock (sync)
        {
            if (disposed) return Failure("terminal-unavailable", "Terminal supervisor is unavailable.", true);
            TerminalStateResult? replay = Replay("open:" + mutationId, profile);
            if (replay is not null) return replay;
            if (process is { HasExited: false })
                return Failure("terminal-busy", "A user terminal session is already active.", false);
            if (!Directory.Exists(root)) return Failure("terminal-workspace-invalid", "Workspace is unavailable.", false);

            ProcessStartInfo start = CreateStartInfo(profile, root);
            Process candidate = new() { StartInfo = start, EnableRaisingEvents = true };
            candidate.OutputDataReceived += OnOutput;
            candidate.ErrorDataReceived += OnOutput;
            try
            {
                PersistAudit(root, "terminal.user.open", mutationId, null);
                if (!candidate.Start()) return Failure("terminal-start-failed", "Terminal process could not be started.", true);
                process = candidate;
                workspaceRoot = root;
                sessionId = "terminal_" + Guid.NewGuid().ToString("N")[..24];
                shellProfile = profile;
                status = "running";
                startedAtUtc = DateTimeOffset.UtcNow;
                exitedAtUtc = null;
                exitCode = null;
                output.Clear();
                cursor = 0;
                truncated = false;
                mutations["open:" + mutationId] = profile;
                candidate.BeginOutputReadLine();
                candidate.BeginErrorReadLine();
                return Snapshot();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                candidate.Dispose();
                return Failure("terminal-start-failed", "Terminal process could not be started safely.", true);
            }
        }
    }

    public TerminalStateResult Input(string requestedSessionId, string text, string mutationId)
    {
        lock (sync)
        {
            if (!MatchesRunning(requestedSessionId)) return NotFound();
            TerminalStateResult? replay = Replay("input:" + mutationId, requestedSessionId + "\n" + text);
            if (replay is not null) return replay;
            try
            {
                process!.StandardInput.Write(text);
                process.StandardInput.Flush();
                mutations["input:" + mutationId] = requestedSessionId + "\n" + text;
                return Snapshot();
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                RefreshExit();
                return Failure("terminal-input-failed", "Terminal input could not be written.", true);
            }
        }
    }

    public TerminalStateResult Resize(string requestedSessionId, int cols, int rows, string mutationId)
    {
        lock (sync)
        {
            if (!Matches(requestedSessionId)) return NotFound();
            string key = "resize:" + mutationId;
            string digest = $"{requestedSessionId}:{cols}:{rows}";
            TerminalStateResult? replay = Replay(key, digest);
            if (replay is not null) return replay;
            mutations[key] = digest;
            return Snapshot();
        }
    }

    public TerminalStateResult Cancel(string requestedSessionId, string mutationId) => Stop(
        requestedSessionId, mutationId, "terminal.user.cancel", close: false);

    public TerminalStateResult Close(string requestedSessionId, string mutationId) => Stop(
        requestedSessionId, mutationId, "terminal.user.close", close: true);

    public TerminalStateResult Get(string requestedSessionId, long afterCursor)
    {
        lock (sync)
        {
            if (!Matches(requestedSessionId)) return NotFound();
            RefreshExit();
            return Snapshot(afterCursor);
        }
    }

    public void Reset()
    {
        Process? target;
        lock (sync)
        {
            target = process;
            status = "closed";
            exitedAtUtc ??= DateTimeOffset.UtcNow;
        }
        StopProcessNoThrow(target);
    }

    public void Dispose()
    {
        Process? target;
        lock (sync)
        {
            if (disposed) return;
            target = process;
            process = null;
            disposed = true;
        }
        StopProcessNoThrow(target);
        target?.Dispose();
    }

    private TerminalStateResult Stop(string requestedSessionId, string mutationId, string auditEvent, bool close)
    {
        Process? target;
        lock (sync)
        {
            if (!Matches(requestedSessionId)) return NotFound();
            string key = auditEvent + ":" + mutationId;
            TerminalStateResult? replay = Replay(key, requestedSessionId);
            if (replay is not null) return replay;
            try
            {
                PersistAudit(workspaceRoot!, auditEvent, mutationId, requestedSessionId);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return Failure("terminal-audit-failed", "Terminal action was not performed because its audit record could not be persisted.", true);
            }
            mutations[key] = requestedSessionId;
            target = process;
            status = close ? "closed" : "exited";
            exitedAtUtc ??= DateTimeOffset.UtcNow;
        }
        StopProcessNoThrow(target);
        lock (sync)
        {
            if (target is { HasExited: true }) exitCode ??= target.ExitCode;
            return Snapshot();
        }
    }

    private static void StopProcessNoThrow(Process? target)
    {
        try
        {
            if (target is { HasExited: false })
            {
                target.Kill(entireProcessTree: true);
                target.WaitForExit(5_000);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    private void RefreshExit()
    {
        if (process is null || !process.HasExited) return;
        exitCode ??= process.ExitCode;
        exitedAtUtc ??= DateTimeOffset.UtcNow;
        if (status == "running") status = "exited";
    }

    private void OnOutput(object sender, DataReceivedEventArgs args)
    {
        if (args.Data is null) return;
        lock (sync)
        {
            string value = args.Data + Environment.NewLine;
            cursor += Encoding.UTF8.GetByteCount(value);
            output.Append(value);
            while (Encoding.UTF8.GetByteCount(output.ToString()) > MaxOutputBytes && output.Length > 0)
            {
                int remove = Math.Min(1024, output.Length);
                output.Remove(0, remove);
                truncated = true;
            }
        }
    }

    private TerminalStateResult Snapshot(long afterCursor = 0)
    {
        RefreshExit();
        string value = afterCursor >= cursor ? string.Empty : output.ToString();
        return new TerminalStateResult
        {
            SchemaVersion = 1,
            Succeeded = true,
            Data = new TerminalStateData
            {
                SessionId = sessionId!, Status = status, ShellProfile = shellProfile, Output = value,
                Cursor = cursor, Truncated = truncated, ExitCode = exitCode,
                StartedAtUtc = startedAtUtc, ExitedAtUtc = exitedAtUtc
            },
            Error = null, Diagnostics = [], Truncated = truncated
        };
    }

    private static TerminalStateResult Failure(string code, string message, bool retryable) => new()
    {
        SchemaVersion = 1, Succeeded = false, Data = null,
        Error = new ApplicationErrorData { Code = code, Category = retryable ? "unavailable" : "conflict", SafeMessage = message, Retryable = retryable },
        Diagnostics = [], Truncated = false
    };

    private static TerminalStateResult NotFound() => Failure(
        "terminal-not-found", "Terminal session was not found.", false);

    private TerminalStateResult? Replay(string key, string digest)
    {
        if (!mutations.TryGetValue(key, out string? existing)) return null;
        return string.Equals(existing, digest, StringComparison.Ordinal)
            ? Snapshot()
            : Failure("terminal-mutation-conflict", "Terminal mutation identity was reused with different input.", false);
    }

    private bool Matches(string value) => sessionId is not null && string.Equals(sessionId, value, StringComparison.Ordinal);
    private bool MatchesRunning(string value) => Matches(value) && process is { HasExited: false } && status == "running";

    private static ProcessStartInfo CreateStartInfo(string profile, string root)
    {
        string fileName;
        string arguments;
        if (OperatingSystem.IsWindows())
        {
            fileName = profile == "cmd" ? "cmd.exe" : "powershell.exe";
            arguments = profile == "cmd" ? "/Q" : "-NoLogo -NoProfile";
        }
        else
        {
            fileName = "/bin/sh";
            arguments = string.Empty;
        }
        ProcessStartInfo start = new(fileName, arguments)
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        string[] allowedEnvironment = ["SystemRoot", "WINDIR", "COMSPEC", "PATH", "PATHEXT", "TEMP", "TMP", "HOME", "USERPROFILE"];
        Dictionary<string, string?> values = allowedEnvironment.ToDictionary(
            name => name, Environment.GetEnvironmentVariable, StringComparer.OrdinalIgnoreCase);
        start.Environment.Clear();
        foreach ((string name, string? value) in values)
        {
            if (!string.IsNullOrWhiteSpace(value)) start.Environment[name] = value;
        }
        start.Environment["TERM"] = "dumb";
        return start;
    }

    private static void PersistAudit(string root, string kind, string mutationId, string? terminalId)
    {
        string stateRoot = Path.Combine(root, ".caicli");
        EnsureNotReparse(root);
        if (Directory.Exists(stateRoot)) EnsureNotReparse(stateRoot);
        Directory.CreateDirectory(stateRoot);
        EnsureNotReparse(stateRoot);
        string path = Path.Combine(stateRoot, "terminal-audit.jsonl");
        string line = JsonSerializer.Serialize(new
        {
            schemaVersion = 1, source = "terminal.user", kind, mutationId,
            terminalId, occurredAtUtc = DateTimeOffset.UtcNow
        });
        File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
    }

    private static void EnsureNotReparse(string path)
    {
        if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("Terminal audit path contains a reparse point.");
    }
}
