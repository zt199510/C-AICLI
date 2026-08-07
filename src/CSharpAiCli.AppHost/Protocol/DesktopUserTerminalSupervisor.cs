using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using CSharpAiCli.AppHost.Protocol.Generated;

namespace CSharpAiCli.AppHost.Protocol;

internal sealed class DesktopUserTerminalSupervisor : IDisposable
{
    private const int MaxOutputBytes = 65_536;
    private const int MaxRetainedSessions = 32;
    private readonly object sync = new();
    private readonly Dictionary<string, TerminalSession> sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MutationRecord> mutations = new(StringComparer.Ordinal);
    private bool disposed;

    public TerminalStateResult Open(string root, string profile, string mutationId)
    {
        lock (sync)
        {
            if (disposed) return Failure("terminal-unavailable", "Terminal supervisor is unavailable.", true);
            TerminalStateResult? replay = Replay("open:" + mutationId, profile);
            if (replay is not null) return replay;
            if (!Directory.Exists(root)) return Failure("terminal-workspace-invalid", "Workspace is unavailable.", false);
            if (!TryResolveCommandLine(profile, out string commandLine))
                return Failure("terminal-profile-unavailable", "The selected shell profile is unavailable.", false);

            string sessionId = "terminal_" + Guid.NewGuid().ToString("N")[..24];
            TerminalSession session = new(sessionId, root, profile);
            sessions.Add(sessionId, session);
            try
            {
                PersistAudit(root, "terminal.user.open", mutationId, sessionId);
                if (OperatingSystem.IsWindows())
                {
                    session.PseudoConsole = WindowsPseudoConsoleSession.Start(
                        commandLine, root, cols: 120, rows: 30, AllowedEnvironment(),
                        bytes => OnPtyOutput(sessionId, bytes),
                        code => OnExit(sessionId, code));
                }
                else
                {
                    ProcessStartInfo start = CreateStartInfo(profile, root);
                    Process process = new() { StartInfo = start, EnableRaisingEvents = true };
                    process.OutputDataReceived += (_, args) => OnOutput(sessionId, args.Data);
                    process.ErrorDataReceived += (_, args) => OnOutput(sessionId, args.Data);
                    process.Exited += (_, _) => OnExit(sessionId, SafeExitCode(process));
                    if (!process.Start()) throw new InvalidOperationException("Terminal process did not start.");
                    session.Process = process;
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                }
                mutations["open:" + mutationId] = new(profile, sessionId);
                EvictClosedSessions();
                return Snapshot(session);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                sessions.Remove(sessionId);
                session.PseudoConsole?.Dispose();
                session.Process?.Dispose();
                return Failure("terminal-start-failed", "Terminal process could not be started safely.", true);
            }
        }
    }

    public TerminalStateResult Input(string sessionId, string text, string mutationId)
    {
        lock (sync)
        {
            if (!TryGetRunning(sessionId, out TerminalSession session)) return NotFound();
            string key = "input:" + mutationId;
            string digest = sessionId + "\n" + text;
            TerminalStateResult? replay = Replay(key, digest);
            if (replay is not null) return replay;
            try
            {
                if (session.PseudoConsole is not null)
                {
                    string terminalText = text.Replace("\r\n", "\r", StringComparison.Ordinal)
                        .Replace("\n", "\r", StringComparison.Ordinal);
                    session.PseudoConsole.Send(Encoding.UTF8.GetBytes(terminalText));
                }
                else
                {
                    session.Process!.StandardInput.Write(text);
                    session.Process.StandardInput.Flush();
                }
                mutations[key] = new(digest, sessionId);
                return Snapshot(session);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                RefreshExit(session);
                return Failure("terminal-input-failed", "Terminal input could not be written.", true);
            }
        }
    }

    public TerminalStateResult Resize(string sessionId, int cols, int rows, string mutationId)
    {
        lock (sync)
        {
            if (!sessions.TryGetValue(sessionId, out TerminalSession? session)) return NotFound();
            string key = "resize:" + mutationId;
            string digest = $"{sessionId}:{cols}:{rows}";
            TerminalStateResult? replay = Replay(key, digest);
            if (replay is not null) return replay;
            try
            {
                session.PseudoConsole?.Resize(cols, rows);
                session.Cols = cols;
                session.Rows = rows;
                mutations[key] = new(digest, sessionId);
                return Snapshot(session);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or
                System.ComponentModel.Win32Exception or COMException)
            {
                return Failure("terminal-resize-failed", "Terminal dimensions could not be updated.", true);
            }
        }
    }

    public TerminalStateResult Cancel(string sessionId, string mutationId)
    {
        lock (sync)
        {
            if (!TryGetRunning(sessionId, out TerminalSession session)) return NotFound();
            const string Kind = "terminal.user.interrupt";
            string key = Kind + ":" + mutationId;
            TerminalStateResult? replay = Replay(key, sessionId);
            if (replay is not null) return replay;
            try
            {
                PersistAudit(session.WorkspaceRoot, Kind, mutationId, sessionId);
                if (session.PseudoConsole is not null) session.PseudoConsole.Interrupt();
                else
                {
                    session.Process!.StandardInput.Write("\u0003");
                    session.Process.StandardInput.Flush();
                }
                mutations[key] = new(sessionId, sessionId);
                return Snapshot(session);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                return Failure("terminal-interrupt-failed", "Terminal interrupt could not be delivered.", true);
            }
        }
    }

    public TerminalStateResult Close(string sessionId, string mutationId)
    {
        WindowsPseudoConsoleSession? pty;
        Process? process;
        TerminalSession session;
        lock (sync)
        {
            if (!sessions.TryGetValue(sessionId, out session!)) return NotFound();
            string key = "terminal.user.close:" + mutationId;
            TerminalStateResult? replay = Replay(key, sessionId);
            if (replay is not null) return replay;
            try
            {
                PersistAudit(session.WorkspaceRoot, "terminal.user.close", mutationId, sessionId);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return Failure("terminal-audit-failed", "Terminal action was not performed because its audit record could not be persisted.", true);
            }
            mutations[key] = new(sessionId, sessionId);
            pty = session.PseudoConsole;
            process = session.Process;
            session.PseudoConsole = null;
            session.Process = null;
            session.Status = "closed";
            session.ExitedAtUtc ??= DateTimeOffset.UtcNow;
        }

        pty?.Dispose();
        StopProcessNoThrow(process);
        int? finalExitCode = process is null ? null : SafeExitCode(process);
        lock (sync)
        {
            session.ExitCode ??= finalExitCode;
            TerminalStateResult result = Snapshot(session);
            process?.Dispose();
            return result;
        }
    }

    public TerminalStateResult Get(string sessionId, long afterCursor)
    {
        lock (sync)
        {
            if (!sessions.TryGetValue(sessionId, out TerminalSession? session)) return NotFound();
            if (session.PseudoConsole?.OutputFailure is Exception failure)
                return Failure("terminal-output-failed", "Terminal output is unavailable.", true);
            RefreshExit(session);
            return Snapshot(session, afterCursor);
        }
    }

    public static TerminalProfileListResult GetProfiles()
    {
        List<TerminalProfileData> profiles =
        [
            new() { ProfileId = "system-default", DisplayName = "PowerShell (default)", IsDefault = true }
        ];
        if (OperatingSystem.IsWindows())
        {
            string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
            if (File.Exists(Path.Combine(system, "cmd.exe")))
                profiles.Add(new TerminalProfileData { ProfileId = "cmd", DisplayName = "Command Prompt", IsDefault = false });
            if (File.Exists(Path.Combine(system, "wsl.exe")))
                profiles.Add(new TerminalProfileData { ProfileId = "wsl", DisplayName = "WSL", IsDefault = false });
            if (TryFindGitBash(out _))
                profiles.Add(new TerminalProfileData { ProfileId = "git-bash", DisplayName = "Git Bash", IsDefault = false });
        }
        return new TerminalProfileListResult
        {
            SchemaVersion = 1,
            Succeeded = true,
            Data = new TerminalProfileListData { Profiles = profiles },
            Error = null,
            Diagnostics = [],
            Truncated = false
        };
    }

    public void Reset() => DisposeSessions(markDisposed: false);

    public void Dispose() => DisposeSessions(markDisposed: true);

    private void DisposeSessions(bool markDisposed)
    {
        List<(WindowsPseudoConsoleSession? Pty, Process? Process)> targets;
        lock (sync)
        {
            if (disposed) return;
            if (markDisposed) disposed = true;
            targets = sessions.Values.Select(session => (session.PseudoConsole, session.Process)).ToList();
            foreach (TerminalSession session in sessions.Values)
            {
                session.PseudoConsole = null;
                session.Process = null;
                if (session.Status == "running") session.Status = "closed";
                session.ExitedAtUtc ??= DateTimeOffset.UtcNow;
            }
        }
        foreach ((WindowsPseudoConsoleSession? pty, Process? process) in targets)
        {
            pty?.Dispose();
            StopProcessNoThrow(process);
            process?.Dispose();
        }
    }

    private static void StopProcessNoThrow(Process? process)
    {
        try
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5_000);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    private void RefreshExit(TerminalSession session)
    {
        if (session.Process is null || !session.Process.HasExited) return;
        session.ExitCode ??= SafeExitCode(session.Process);
        session.ExitedAtUtc ??= DateTimeOffset.UtcNow;
        if (session.Status == "running") session.Status = "exited";
    }

    private void OnOutput(string sessionId, string? value)
    {
        if (value is null) return;
        lock (sync)
        {
            if (sessions.TryGetValue(sessionId, out TerminalSession? session))
                AppendOutput(session, value + Environment.NewLine);
        }
    }

    private void OnPtyOutput(string sessionId, ReadOnlyMemory<byte> bytes)
    {
        lock (sync)
        {
            if (!sessions.TryGetValue(sessionId, out TerminalSession? session)) return;
            char[] characters = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
            session.Decoder.Convert(bytes.Span, characters, flush: false, out int bytesUsed, out int charsUsed, out _);
            session.Cursor += bytesUsed;
            if (charsUsed > 0) AppendOutput(session, new string(characters, 0, charsUsed), countCursor: false);
        }
    }

    private void OnExit(string sessionId, int code)
    {
        lock (sync)
        {
            if (!sessions.TryGetValue(sessionId, out TerminalSession? session)) return;
            session.ExitCode ??= code;
            session.ExitedAtUtc ??= DateTimeOffset.UtcNow;
            if (session.Status == "running") session.Status = "exited";
        }
    }

    private static void AppendOutput(TerminalSession session, string value, bool countCursor = true)
    {
        if (countCursor) session.Cursor += Encoding.UTF8.GetByteCount(value);
        session.Output.Append(value);
        while (Encoding.UTF8.GetByteCount(session.Output.ToString()) > MaxOutputBytes && session.Output.Length > 0)
        {
            int remove = Math.Min(1024, session.Output.Length);
            session.Output.Remove(0, remove);
            session.Truncated = true;
        }
    }

    private TerminalStateResult Snapshot(TerminalSession session, long afterCursor = 0)
    {
        RefreshExit(session);
        string value = afterCursor >= session.Cursor ? string.Empty : session.Output.ToString();
        return new TerminalStateResult
        {
            SchemaVersion = 1,
            Succeeded = true,
            Data = new TerminalStateData
            {
                SessionId = session.SessionId,
                Status = session.Status,
                ShellProfile = session.ShellProfile,
                Output = value,
                Cursor = session.Cursor,
                Truncated = session.Truncated,
                ExitCode = session.ExitCode,
                StartedAtUtc = session.StartedAtUtc,
                ExitedAtUtc = session.ExitedAtUtc
            },
            Error = null,
            Diagnostics = [],
            Truncated = session.Truncated
        };
    }

    private static TerminalStateResult Failure(string code, string message, bool retryable) => new()
    {
        SchemaVersion = 1,
        Succeeded = false,
        Data = null,
        Error = new ApplicationErrorData
        {
            Code = code,
            Category = retryable ? "unavailable" : "conflict",
            SafeMessage = message,
            Retryable = retryable
        },
        Diagnostics = [],
        Truncated = false
    };

    private static TerminalStateResult NotFound() => Failure(
        "terminal-not-found", "Terminal session was not found.", false);

    private TerminalStateResult? Replay(string key, string digest)
    {
        if (!mutations.TryGetValue(key, out MutationRecord? record)) return null;
        if (!string.Equals(record.Digest, digest, StringComparison.Ordinal))
            return Failure("terminal-mutation-conflict", "Terminal mutation identity was reused with different input.", false);
        return sessions.TryGetValue(record.SessionId, out TerminalSession? session)
            ? Snapshot(session)
            : NotFound();
    }

    private bool TryGetRunning(string sessionId, out TerminalSession session)
    {
        if (!sessions.TryGetValue(sessionId, out TerminalSession? found) || found.Status != "running" ||
            found.PseudoConsole is null && found.Process is not { HasExited: false })
        {
            session = null!;
            return false;
        }
        session = found;
        return true;
    }

    private void EvictClosedSessions()
    {
        foreach (TerminalSession session in sessions.Values
                     .Where(value => value.Status != "running")
                     .OrderBy(value => value.StartedAtUtc)
                     .Take(Math.Max(0, sessions.Count - MaxRetainedSessions))
                     .ToArray())
        {
            sessions.Remove(session.SessionId);
        }
    }

    private static int SafeExitCode(Process process)
    {
        try { return process.ExitCode; }
        catch (InvalidOperationException) { return -1; }
    }

    private static bool TryResolveCommandLine(string profile, out string commandLine)
    {
        if (!OperatingSystem.IsWindows())
        {
            commandLine = profile == "system-default" ? "/bin/sh" : string.Empty;
            return commandLine.Length > 0;
        }
        string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        string powershell = Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe");
        switch (profile)
        {
            case "system-default":
            case "powershell":
                commandLine = $"\"{powershell}\" -NoLogo -NoProfile";
                return File.Exists(powershell);
            case "cmd":
                string cmd = Path.Combine(system, "cmd.exe");
                commandLine = $"\"{cmd}\" /D /Q";
                return File.Exists(cmd);
            case "wsl":
                string wsl = Path.Combine(system, "wsl.exe");
                commandLine = $"\"{wsl}\"";
                return File.Exists(wsl);
            case "git-bash" when TryFindGitBash(out string gitBash):
                commandLine = $"\"{gitBash}\" --login -i";
                return true;
            default:
                commandLine = string.Empty;
                return false;
        }
    }

    private static bool TryFindGitBash(out string path)
    {
        string?[] roots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        ];
        foreach (string? root in roots.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (string relative in new[] { Path.Combine("Git", "bin", "bash.exe"), Path.Combine("Git", "usr", "bin", "bash.exe") })
            {
                string candidate = Path.Combine(root!, relative);
                if (File.Exists(candidate))
                {
                    path = candidate;
                    return true;
                }
            }
        }
        path = string.Empty;
        return false;
    }

    private static IReadOnlyDictionary<string, string> AllowedEnvironment()
    {
        string[] allowed = ["SystemRoot", "WINDIR", "COMSPEC", "PATH", "PATHEXT", "TEMP", "TMP", "HOME", "USERPROFILE"];
        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (string name in allowed)
        {
            string? value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value)) result[name] = value;
        }
        result["TERM"] = "xterm-256color";
        return result;
    }

    private static ProcessStartInfo CreateStartInfo(string profile, string root)
    {
        string fileName;
        string arguments;
        if (OperatingSystem.IsWindows())
        {
            fileName = profile == "cmd" ? "cmd.exe" : "powershell.exe";
            arguments = profile == "cmd" ? "/D /Q" : "-NoLogo -NoProfile";
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
        string[] names = ["SystemRoot", "WINDIR", "COMSPEC", "PATH", "PATHEXT", "TEMP", "TMP", "HOME", "USERPROFILE"];
        Dictionary<string, string?> values = names.ToDictionary(
            name => name, Environment.GetEnvironmentVariable, StringComparer.OrdinalIgnoreCase);
        start.Environment.Clear();
        foreach ((string name, string? value) in values)
        {
            if (!string.IsNullOrWhiteSpace(value)) start.Environment[name] = value;
        }
        start.Environment["TERM"] = "xterm-256color";
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
            schemaVersion = 1,
            source = "terminal.user",
            kind,
            mutationId,
            terminalId,
            occurredAtUtc = DateTimeOffset.UtcNow
        });
        File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
    }

    private static void EnsureNotReparse(string path)
    {
        if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("Terminal audit path contains a reparse point.");
    }

    private sealed class TerminalSession(string sessionId, string workspaceRoot, string shellProfile)
    {
        public string SessionId { get; } = sessionId;
        public string WorkspaceRoot { get; } = workspaceRoot;
        public string ShellProfile { get; } = shellProfile;
        public StringBuilder Output { get; } = new();
        public Decoder Decoder { get; } = Encoding.UTF8.GetDecoder();
        public WindowsPseudoConsoleSession? PseudoConsole { get; set; }
        public Process? Process { get; set; }
        public string Status { get; set; } = "running";
        public DateTimeOffset StartedAtUtc { get; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? ExitedAtUtc { get; set; }
        public int? ExitCode { get; set; }
        public long Cursor { get; set; }
        public bool Truncated { get; set; }
        public int Cols { get; set; } = 120;
        public int Rows { get; set; } = 30;
    }

    private sealed record MutationRecord(string Digest, string SessionId);
}
