using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CSharpAiCli.Core;
using CSharpAiCli.ProjectPacks.GerberTiff;

namespace CSharpAiCli.ProjectPacks.Runtime;

public sealed record GerberTiffExecutionRiskSummary(
    string Operation,
    string ToolPath,
    string ToolSha256,
    string ToolVersion,
    string InputPath,
    string InputSha256,
    string OutputPath,
    int TimeoutMilliseconds,
    string OverwritePolicy,
    string ArgumentTemplate);

internal sealed record FrozenProcessInput(string Path, string Sha256);

internal sealed record FrozenProcessInvocation(
    string DependencyId,
    string StageId,
    string Operation,
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    string TemporaryDirectory,
    string ManagedRunRoot,
    string AllowedOutputBoundaryRoot,
    string DeclaredOutputRoot,
    string DeclaredOutputPath,
    IReadOnlyList<string> AllowedOutputPaths,
    bool RequireNewOutputRoot,
    IReadOnlyList<FrozenProcessInput> Inputs,
    int TimeoutMilliseconds,
    long MaxOutputBytes,
    int MaxOutputCharacters,
    IReadOnlyDictionary<string, string> Environment,
    string OverwritePolicy,
    string ArgumentTemplate);

internal sealed record BoundedProcessResult(
    string Status,
    string? ErrorCode,
    string Summary,
    int? ExitCode,
    long DurationMilliseconds,
    string Stdout,
    string Stderr,
    bool StdoutTruncated,
    bool StderrTruncated,
    bool TimedOut,
    bool Canceled,
    bool ProcessCleanedUp,
    bool ResidualProcessDetected,
    bool OutputExists,
    long? OutputSize,
    string? OutputSha256)
{
    public bool Succeeded => Status == ProjectPackStageStatus.Succeeded;
}

internal sealed class BoundedExternalProcessRunner
{
    private const int PollMilliseconds = 25;
    private const int CleanupMilliseconds = 3_000;
    private const int OutputDrainMilliseconds = 10_000;

    public BoundedProcessResult Run(
        FrozenProcessInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        Stopwatch clock = Stopwatch.StartNew();
        string? boundaryError = ValidateBoundary(invocation);
        if (boundaryError is not null)
        {
            return Failure(ProjectPackRunErrorCode.OutputBoundaryViolation, boundaryError, clock);
        }

        if (File.Exists(invocation.DeclaredOutputPath) || Directory.Exists(invocation.DeclaredOutputPath))
        {
            return Failure(ProjectPackRunErrorCode.ConversionOutputConflict,
                "Declared conversion output already exists; overwrite is denied.", clock);
        }

        List<FileStream> inputLocks = [];
        try
        {
            foreach (FrozenProcessInput input in invocation.Inputs)
            {
                FileStream inputLock = new(
                    input.Path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    128 * 1024,
                    FileOptions.SequentialScan);
                string inputHash = Convert.ToHexString(SHA256.HashData(inputLock));
                if (inputLock.Length <= 0 || inputLock.Length > invocation.MaxOutputBytes ||
                    !string.Equals(inputHash, input.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    inputLock.Dispose();
                    DisposeInputLocks(inputLocks);
                    return Failure(ProjectPackRunErrorCode.InputChanged,
                        "A frozen process input is missing or changed.", clock);
                }

                inputLocks.Add(inputLock);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            DisposeInputLocks(inputLocks);
            return Failure(ProjectPackRunErrorCode.InputChanged,
                "A frozen process input could not be locked read-only for execution.", clock);
        }

        try
        {
            ManagedProjectPackRunStore.EnsureNoReparseInExistingChain(invocation.ManagedRunRoot);
            ManagedProjectPackRunStore.EnsureNoReparseInExistingChain(invocation.WorkingDirectory);
            ManagedProjectPackRunStore.EnsureNoReparseInExistingChain(invocation.DeclaredOutputRoot);
            Directory.CreateDirectory(invocation.WorkingDirectory);
            Directory.CreateDirectory(invocation.TemporaryDirectory);
            string? outputParent = Path.GetDirectoryName(invocation.DeclaredOutputRoot);
            if (string.IsNullOrWhiteSpace(outputParent))
            {
                throw new IOException();
            }

            Directory.CreateDirectory(outputParent);
            if (invocation.RequireNewOutputRoot)
            {
                if (!AtomicDirectoryCreator.TryCreateNew(invocation.DeclaredOutputRoot))
                {
                    DisposeInputLocks(inputLocks);
                    return Failure(ProjectPackRunErrorCode.ConversionOutputConflict,
                        "Declared conversion output root was created concurrently; overwrite is denied.", clock);
                }
            }
            else
            {
                Directory.CreateDirectory(invocation.DeclaredOutputRoot);
            }

            ManagedProjectPackRunStore.EnsureNoReparseInExistingChain(invocation.TemporaryDirectory);
            ManagedProjectPackRunStore.EnsureNoReparseInExistingChain(invocation.DeclaredOutputRoot);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            DisposeInputLocks(inputLocks);
            return Failure(ProjectPackRunErrorCode.OutputBoundaryViolation,
                "Managed process directories could not be prepared safely.", clock);
        }

        Process? process = null;
        Task<BoundedText>? stdoutTask = null;
        Task<BoundedText>? stderrTask = null;
        bool timedOut = false;
        bool canceled = false;
        bool processCleanedUp = true;
        bool residualDetected = false;
        int? exitCode = null;
        BoundedText stdout = BoundedText.Empty;
        BoundedText stderr = BoundedText.Empty;
        IReadOnlyList<int> observedDescendants = [];

        try
        {
            process = new Process { StartInfo = CreateStartInfo(invocation) };
            process.Start();
            process.StandardInput.Close();
            stdoutTask = ReadBoundedAsync(process.StandardOutput, invocation.MaxOutputCharacters);
            stderrTask = ReadBoundedAsync(process.StandardError, invocation.MaxOutputCharacters);

            while (!process.WaitForExit(PollMilliseconds))
            {
                observedDescendants = MergeProcessIds(
                    observedDescendants,
                    WindowsProcessTree.FindDescendants(process.Id));
                if (cancellationToken.IsCancellationRequested)
                {
                    canceled = true;
                    break;
                }

                if (clock.ElapsedMilliseconds >= invocation.TimeoutMilliseconds)
                {
                    timedOut = true;
                    break;
                }
            }

            observedDescendants = MergeProcessIds(
                observedDescendants,
                WindowsProcessTree.FindDescendants(process.Id));
            if (timedOut || canceled)
            {
                processCleanedUp = ProcessTreeCleanup.KillAndWait(process, observedDescendants, CleanupMilliseconds);
            }
            else
            {
                residualDetected = ProcessTreeCleanup.AnyRunning(observedDescendants);
                if (residualDetected)
                {
                    processCleanedUp = ProcessTreeCleanup.KillDescendants(observedDescendants, CleanupMilliseconds);
                }
            }

            if (process.HasExited)
            {
                exitCode = process.ExitCode;
            }

            if (stdoutTask is not null && stderrTask is not null)
            {
                Task allOutput = Task.WhenAll(stdoutTask, stderrTask);
                if (allOutput.Wait(OutputDrainMilliseconds))
                {
                    stdout = stdoutTask.GetAwaiter().GetResult();
                    stderr = stderrTask.GetAwaiter().GetResult();
                }
                else
                {
                    processCleanedUp = false;
                }
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or IOException
            or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception)
        {
            processCleanedUp = process is null || ProcessTreeCleanup.KillAndWait(
                process,
                observedDescendants,
                CleanupMilliseconds);
            return Finish(
                ProjectPackStageStatus.Failed,
                ProjectPackRunErrorCode.ExecutionFailed,
                "External conversion process could not be started safely.",
                invocation,
                clock,
                exitCode,
                stdout,
                stderr,
                timedOut,
                canceled,
                processCleanedUp,
                residualDetected);
        }
        finally
        {
            clock.Stop();
            process?.Dispose();
            DisposeInputLocks(inputLocks);
            TryDeleteTemporaryDirectory(invocation);
        }

        if (!processCleanedUp)
        {
            return Finish(
                ProjectPackStageStatus.Interrupted,
                ProjectPackRunErrorCode.ProcessCleanupFailed,
                "External conversion process cleanup did not complete within the bound.",
                invocation, clock, exitCode, stdout, stderr, timedOut, canceled, false, residualDetected);
        }

        if (residualDetected)
        {
            return Finish(
                ProjectPackStageStatus.Interrupted,
                ProjectPackRunErrorCode.ResidualProcessDetected,
                "External conversion process left a child process; the child was terminated.",
                invocation, clock, exitCode, stdout, stderr, timedOut, canceled, true, true);
        }

        if (timedOut)
        {
            return Finish(
                ProjectPackStageStatus.TimedOut,
                ProjectPackRunErrorCode.ExecutionTimeout,
                $"External conversion process timed out after {invocation.TimeoutMilliseconds} ms.",
                invocation, clock, exitCode, stdout, stderr, true, false, true, false);
        }

        if (canceled)
        {
            return Finish(
                ProjectPackStageStatus.Canceled,
                ProjectPackRunErrorCode.ExecutionCanceled,
                "External conversion process was canceled and its process tree was cleaned up.",
                invocation, clock, exitCode, stdout, stderr, false, true, true, false);
        }

        BoundedProcessResult inspected = Finish(
            ProjectPackStageStatus.Succeeded,
            null,
            "External conversion process completed and declared output was inventoried.",
            invocation, clock, exitCode, stdout, stderr, false, false, true, false);
        if (exitCode is not 0)
        {
            return inspected with
            {
                Status = inspected.OutputExists ? ProjectPackStageStatus.PartialOutput : ProjectPackStageStatus.Failed,
                ErrorCode = inspected.OutputExists ? ProjectPackRunErrorCode.PartialOutput : ProjectPackRunErrorCode.ExecutionFailed,
                Summary = exitCode is null
                    ? "External conversion process did not produce an exit code."
                    : $"External conversion process failed with exit code {exitCode}."
            };
        }

        if (!string.IsNullOrWhiteSpace(stderr.Text))
        {
            return inspected with
            {
                Status = inspected.OutputExists ? ProjectPackStageStatus.PartialOutput : ProjectPackStageStatus.Failed,
                ErrorCode = inspected.OutputExists ? ProjectPackRunErrorCode.PartialOutput : ProjectPackRunErrorCode.ExecutionFailed,
                Summary = "External conversion process emitted stderr; the output is not accepted as successful conversion evidence."
            };
        }

        return inspected;
    }

    private static BoundedProcessResult Finish(
        string status,
        string? errorCode,
        string summary,
        FrozenProcessInvocation invocation,
        Stopwatch clock,
        int? exitCode,
        BoundedText stdout,
        BoundedText stderr,
        bool timedOut,
        bool canceled,
        bool processCleanedUp,
        bool residualDetected)
    {
        bool outputExists = false;
        long? outputSize = null;
        string? outputSha256 = null;
        if (File.Exists(invocation.DeclaredOutputPath))
        {
            outputExists = TryInspectFile(
                invocation.DeclaredOutputPath,
                invocation.MaxOutputBytes,
                out long size,
                out string? sha256);
            outputSize = size;
            outputSha256 = sha256;
            if (!outputExists || size <= 0)
            {
                status = ProjectPackStageStatus.PartialOutput;
                errorCode = ProjectPackRunErrorCode.PartialOutput;
                summary = "Declared conversion output is empty, unreadable, or changed during inventory.";
            }
            else if (size > invocation.MaxOutputBytes)
            {
                status = ProjectPackStageStatus.PartialOutput;
                errorCode = ProjectPackRunErrorCode.OutputLimitExceeded;
                summary = "Declared conversion output exceeds the frozen size limit.";
            }
        }
        else if (status == ProjectPackStageStatus.Succeeded)
        {
            status = ProjectPackStageStatus.PartialOutput;
            errorCode = ProjectPackRunErrorCode.PartialOutput;
            summary = "External conversion exited without creating the declared output.";
        }

        if (TryFindUnexpectedOutput(invocation, out _))
        {
            status = ProjectPackStageStatus.PartialOutput;
            errorCode = ProjectPackRunErrorCode.OutputBoundaryViolation;
            summary = "External conversion created output outside its declared inventory.";
        }

        return new BoundedProcessResult(
            status,
            errorCode,
            summary,
            exitCode,
            Math.Max(0, clock.ElapsedMilliseconds),
            DiagnosticSecretRedactor.Redact(stdout.Text),
            DiagnosticSecretRedactor.Redact(stderr.Text),
            stdout.Truncated,
            stderr.Truncated,
            timedOut,
            canceled,
            processCleanedUp,
            residualDetected,
            outputExists,
            outputSize,
            outputSha256);
    }

    private static BoundedProcessResult Failure(string errorCode, string summary, Stopwatch clock) =>
        new(
            ProjectPackStageStatus.Failed,
            errorCode,
            summary,
            null,
            Math.Max(0, clock.ElapsedMilliseconds),
            string.Empty,
            string.Empty,
            false,
            false,
            false,
            false,
            true,
            false,
            false,
            null,
            null);

    private static ProcessStartInfo CreateStartInfo(FrozenProcessInvocation invocation)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = invocation.ExecutablePath,
            WorkingDirectory = invocation.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true
        };
        foreach (string argument in invocation.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment.Clear();
        AddEnvironment(startInfo, "SystemRoot", Environment.GetEnvironmentVariable("SystemRoot"));
        AddEnvironment(startInfo, "WINDIR", Environment.GetEnvironmentVariable("WINDIR"));
        AddEnvironment(startInfo, "ComSpec", Environment.GetEnvironmentVariable("ComSpec"));
        startInfo.Environment["TEMP"] = invocation.TemporaryDirectory;
        startInfo.Environment["TMP"] = invocation.TemporaryDirectory;
        startInfo.Environment["LANG"] = "C";
        startInfo.Environment["LC_ALL"] = "C";
        startInfo.Environment["NO_COLOR"] = "1";
        foreach ((string key, string value) in invocation.Environment)
        {
            startInfo.Environment[key] = value;
        }

        return startInfo;
    }

    private static void AddEnvironment(ProcessStartInfo startInfo, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            startInfo.Environment[name] = value;
        }
    }

    private static async Task<BoundedText> ReadBoundedAsync(StreamReader reader, int maxCharacters)
    {
        char[] buffer = new char[4_096];
        StringBuilder builder = new(Math.Min(maxCharacters, 16_384));
        bool truncated = false;
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) > 0)
        {
            int remaining = maxCharacters - builder.Length;
            if (remaining > 0)
            {
                builder.Append(buffer, 0, Math.Min(remaining, read));
            }

            if (read > remaining)
            {
                truncated = true;
            }
        }

        return new BoundedText(builder.ToString(), truncated);
    }

    private static string? ValidateBoundary(FrozenProcessInvocation invocation)
    {
        try
        {
            if (invocation.TimeoutMilliseconds is <= 0 or > 120_000 ||
                invocation.MaxOutputBytes is <= 0 or > 512L * 1024 * 1024 ||
                invocation.MaxOutputCharacters is <= 0 or > 1_048_576 ||
                invocation.Arguments.Count == 0 ||
                invocation.Inputs.Count == 0 ||
                !Path.IsPathFullyQualified(invocation.ExecutablePath) ||
                !IsWithin(invocation.ManagedRunRoot, invocation.WorkingDirectory) ||
                !IsWithin(invocation.ManagedRunRoot, invocation.TemporaryDirectory) ||
                !IsWithin(invocation.AllowedOutputBoundaryRoot, invocation.DeclaredOutputRoot) ||
                !IsWithin(invocation.DeclaredOutputRoot, invocation.DeclaredOutputPath) ||
                !invocation.AllowedOutputPaths.Any(path => PathsEqual(path, invocation.DeclaredOutputPath)) ||
                invocation.AllowedOutputPaths.Any(path => !IsWithin(invocation.DeclaredOutputRoot, path)))
            {
                return "Frozen process invocation violates its executable, argument, cwd, timeout, or output boundary.";
            }

            foreach (FrozenProcessInput input in invocation.Inputs)
            {
                ProjectPackContractGuard.RequireSha256(input.Sha256, nameof(input.Sha256));
                if (!IsWithin(invocation.ManagedRunRoot, input.Path))
                {
                    return "Frozen process input is outside the managed run.";
                }
            }

            return null;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return "Frozen process invocation contains an invalid path or identity.";
        }
    }

    private static bool TryFindUnexpectedOutput(FrozenProcessInvocation invocation, out string? unexpected)
    {
        unexpected = null;
        if (!Directory.Exists(invocation.DeclaredOutputRoot))
        {
            return false;
        }

        HashSet<string> allowed = invocation.AllowedOutputPaths
            .Select(Path.GetFullPath)
            .ToHashSet(PathComparer);
        try
        {
            foreach (string file in Directory.EnumerateFiles(
                invocation.DeclaredOutputRoot,
                "*",
                SearchOption.AllDirectories))
            {
                if (!allowed.Contains(Path.GetFullPath(file)))
                {
                    unexpected = file;
                    return true;
                }
            }

            foreach (string directory in Directory.EnumerateDirectories(
                invocation.DeclaredOutputRoot,
                "*",
                SearchOption.AllDirectories))
            {
                if (!invocation.AllowedOutputPaths.Any(path => IsWithin(directory, path)))
                {
                    unexpected = directory;
                    return true;
                }
            }

            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            unexpected = invocation.DeclaredOutputRoot;
            return true;
        }
    }

    private static bool TryInspectFile(string path, long maxBytes, out long size, out string? sha256)
    {
        size = 0;
        sha256 = null;
        try
        {
            ManagedProjectPackRunStore.EnsureNoReparseInExistingChain(path);
            FileInfo before = new(path);
            before.Refresh();
            if (!before.Exists || before.Attributes.HasFlag(FileAttributes.Directory))
            {
                return false;
            }

            size = before.Length;
            if (size > maxBytes)
            {
                return true;
            }

            DateTime beforeWrite = before.LastWriteTimeUtc;
            using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                128 * 1024,
                FileOptions.SequentialScan);
            sha256 = Convert.ToHexString(SHA256.HashData(stream));
            before.Refresh();
            return before.Exists && before.Length == size && before.LastWriteTimeUtc == beforeWrite;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool IsWithin(string root, string candidate)
    {
        string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string fullCandidate = Path.GetFullPath(candidate);
        if (PathsEqual(fullRoot, fullCandidate))
        {
            return false;
        }

        return fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, PathComparison);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), PathComparison);

    private static IReadOnlyList<int> MergeProcessIds(IReadOnlyList<int> left, IReadOnlyList<int> right) =>
        left.Concat(right).Distinct().ToArray();

    private static void DisposeInputLocks(IEnumerable<FileStream> inputLocks)
    {
        foreach (FileStream inputLock in inputLocks)
        {
            inputLock.Dispose();
        }
    }

    private static void TryDeleteTemporaryDirectory(FrozenProcessInvocation invocation)
    {
        try
        {
            if (IsWithin(invocation.ManagedRunRoot, invocation.TemporaryDirectory) &&
                Directory.Exists(invocation.TemporaryDirectory))
            {
                Directory.Delete(invocation.TemporaryDirectory, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private sealed record BoundedText(string Text, bool Truncated)
    {
        public static BoundedText Empty { get; } = new(string.Empty, false);
    }
}

internal static class AtomicDirectoryCreator
{
    public static bool TryCreateNew(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            if (Directory.Exists(path) || File.Exists(path))
            {
                return false;
            }

            Directory.CreateDirectory(path);
            return true;
        }

        if (CreateDirectory(path, IntPtr.Zero))
        {
            return true;
        }

        int error = Marshal.GetLastWin32Error();
        if (error is 80 or 183)
        {
            return false;
        }

        throw new IOException("Declared output directory could not be created safely.",
            new System.ComponentModel.Win32Exception(error));
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateDirectoryW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateDirectory(string path, IntPtr securityAttributes);
}

internal static class ProcessTreeCleanup
{
    public static bool KillAndWait(Process process, IReadOnlyList<int> descendants, int timeoutMilliseconds)
    {
        IReadOnlyList<int> allDescendants = descendants
            .Concat(WindowsProcessTree.FindDescendants(process.Id))
            .Distinct()
            .ToArray();
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }

        long deadline = Environment.TickCount64 + timeoutMilliseconds;
        bool parentExited = TryWait(process, Remaining(deadline));
        bool descendantsExited = KillDescendants(allDescendants, Remaining(deadline));
        if ((!parentExited || !descendantsExited) && OperatingSystem.IsWindows())
        {
            TryTaskKill(process.Id, Remaining(deadline));
            parentExited = TryWait(process, Remaining(deadline));
            descendantsExited = KillDescendants(allDescendants, Remaining(deadline));
        }

        return parentExited && descendantsExited;
    }

    public static bool KillDescendants(IReadOnlyList<int> processIds, int timeoutMilliseconds)
    {
        long deadline = Environment.TickCount64 + timeoutMilliseconds;
        foreach (int processId in processIds.Distinct())
        {
            try
            {
                using Process child = Process.GetProcessById(processId);
                if (!child.HasExited)
                {
                    child.Kill(entireProcessTree: true);
                    child.WaitForExit(Remaining(deadline));
                }
            }
            catch (ArgumentException)
            {
            }
            catch (InvalidOperationException)
            {
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
        }

        return !AnyRunning(processIds);
    }

    public static bool AnyRunning(IReadOnlyList<int> processIds)
    {
        foreach (int processId in processIds.Distinct())
        {
            try
            {
                using Process process = Process.GetProcessById(processId);
                if (!process.HasExited)
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
            }
            catch (InvalidOperationException)
            {
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
        }

        return false;
    }

    private static bool TryWait(Process process, int milliseconds)
    {
        try
        {
            return process.HasExited || milliseconds > 0 && process.WaitForExit(milliseconds);
        }
        catch
        {
            return false;
        }
    }

    private static void TryTaskKill(int processId, int milliseconds)
    {
        if (milliseconds <= 0)
        {
            return;
        }

        try
        {
            using Process cleanup = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.SystemDirectory, "taskkill.exe"),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            cleanup.StartInfo.ArgumentList.Add("/PID");
            cleanup.StartInfo.ArgumentList.Add(processId.ToString(CultureInfo.InvariantCulture));
            cleanup.StartInfo.ArgumentList.Add("/T");
            cleanup.StartInfo.ArgumentList.Add("/F");
            cleanup.Start();
            cleanup.WaitForExit(milliseconds);
        }
        catch
        {
        }
    }

    private static int Remaining(long deadline)
    {
        long remaining = deadline - Environment.TickCount64;
        return remaining <= 0 ? 0 : remaining > int.MaxValue ? int.MaxValue : (int)remaining;
    }
}

internal static class WindowsProcessTree
{
    private const uint SnapshotProcesses = 0x00000002;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    public static IReadOnlyList<int> FindDescendants(int processId)
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        Dictionary<int, List<int>> children = new();
        IntPtr snapshot = CreateToolhelp32Snapshot(SnapshotProcesses, 0);
        if (snapshot == InvalidHandleValue)
        {
            return [];
        }

        try
        {
            ProcessEntry32 entry = new() { Size = (uint)Marshal.SizeOf<ProcessEntry32>() };
            if (!Process32First(snapshot, ref entry))
            {
                return [];
            }

            do
            {
                int parent = unchecked((int)entry.ParentProcessId);
                int child = unchecked((int)entry.ProcessId);
                if (!children.TryGetValue(parent, out List<int>? list))
                {
                    list = [];
                    children[parent] = list;
                }

                list.Add(child);
                entry.Size = (uint)Marshal.SizeOf<ProcessEntry32>();
            }
            while (Process32Next(snapshot, ref entry));
        }
        finally
        {
            CloseHandle(snapshot);
        }

        List<int> result = [];
        Queue<int> pending = new();
        pending.Enqueue(processId);
        while (pending.Count > 0)
        {
            int parent = pending.Dequeue();
            if (!children.TryGetValue(parent, out List<int>? direct))
            {
                continue;
            }

            foreach (int child in direct)
            {
                if (child != processId && !result.Contains(child))
                {
                    result.Add(child);
                    pending.Enqueue(child);
                }
            }
        }

        return result;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct ProcessEntry32
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int BasePriority;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExecutableFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}

internal sealed record GerberTiffProcessExecutionResult(
    string Status,
    string? ErrorCode,
    string Summary,
    ExternalToolIdentity ToolIdentity,
    GerberTiffExecutionRiskSummary RiskSummary,
    string ApprovalStatus,
    long ApprovalDurationMilliseconds,
    BoundedProcessResult ProcessResult);

internal sealed class GerberTiffTypedProcessAdapter
{
    internal const int RenderTimeoutMilliseconds = 30_000;
    internal const int EncodeTimeoutMilliseconds = 30_000;
    internal const long MaxOutputBytes = 256L * 1024 * 1024;
    internal const int MaxOutputCharacters = 32 * 1024;

    private readonly ExternalToolPathInspector inspector;
    private readonly BoundedExternalProcessRunner processRunner;

    public GerberTiffTypedProcessAdapter(
        ExternalToolPathInspector? inspector = null,
        BoundedExternalProcessRunner? processRunner = null)
    {
        this.inspector = inspector ?? new ExternalToolPathInspector();
        this.processRunner = processRunner ?? new BoundedExternalProcessRunner();
    }

    public GerberTiffProcessExecutionResult Render(
        ExternalToolRequirement requirement,
        string executablePath,
        ExternalToolIdentity expectedIdentity,
        string stagedInputPath,
        string stagedInputSha256,
        string outputPath,
        IReadOnlyList<string> allowedOutputPaths,
        ManagedProjectPackRunLayout layout,
        IApprovalPolicy approvalPolicy,
        int operationIndex,
        CancellationToken cancellationToken = default)
    {
        string outputRoot = Path.GetDirectoryName(outputPath)
            ?? throw new ArgumentException("Render output must have a parent directory.", nameof(outputPath));
        FrozenProcessInvocation invocation = new(
            requirement.Id,
            "render",
            "gerber.render",
            Path.GetFullPath(executablePath),
            ["-x", "png", "-D", "300", "-o", Path.GetFullPath(outputPath), Path.GetFullPath(stagedInputPath)],
            layout.WorkingPath,
            Path.Combine(layout.WorkingPath, "temp", $"render-{operationIndex:D4}"),
            layout.RunRoot,
            layout.RunRoot,
            outputRoot,
            Path.GetFullPath(outputPath),
            allowedOutputPaths.Select(Path.GetFullPath).ToArray(),
            operationIndex == 1,
            [new FrozenProcessInput(Path.GetFullPath(stagedInputPath), stagedInputSha256)],
            RenderTimeoutMilliseconds,
            MaxOutputBytes,
            MaxOutputCharacters,
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)),
            "no-overwrite",
            "gerbv-render-v1:-x,png,-D,300,-o,<managed-png>,<staged-input>");
        return ExecuteFrozen(requirement, expectedIdentity, invocation, approvalPolicy, cancellationToken);
    }

    public GerberTiffProcessExecutionResult Encode(
        ExternalToolRequirement requirement,
        string executablePath,
        ExternalToolIdentity expectedIdentity,
        string managedPngPath,
        string managedPngSha256,
        string outputPath,
        string outputRoot,
        string workspaceRoot,
        IReadOnlyList<string> allowedOutputPaths,
        ManagedProjectPackRunLayout layout,
        IApprovalPolicy approvalPolicy,
        int operationIndex,
        CancellationToken cancellationToken = default)
    {
        string toolDirectory = Path.GetDirectoryName(Path.GetFullPath(executablePath))
            ?? throw new ArgumentException("ImageMagick executable must have a parent directory.", nameof(executablePath));
        FrozenProcessInvocation invocation = new(
            requirement.Id,
            "encode",
            "tiff.encode",
            Path.GetFullPath(executablePath),
            [
                Path.GetFullPath(managedPngPath),
                "-alpha", "off",
                "-colorspace", "sRGB",
                "-units", "PixelsPerInch",
                "-density", "300",
                "-compress", "LZW",
                Path.GetFullPath(outputPath)
            ],
            layout.WorkingPath,
            Path.Combine(layout.WorkingPath, "temp", $"encode-{operationIndex:D4}"),
            layout.RunRoot,
            Path.GetFullPath(workspaceRoot),
            Path.GetFullPath(outputRoot),
            Path.GetFullPath(outputPath),
            allowedOutputPaths.Select(Path.GetFullPath).ToArray(),
            operationIndex == 1,
            [new FrozenProcessInput(Path.GetFullPath(managedPngPath), managedPngSha256)],
            EncodeTimeoutMilliseconds,
            MaxOutputBytes,
            MaxOutputCharacters,
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["MAGICK_CONFIGURE_PATH"] = toolDirectory,
                ["MAGICK_TEMPORARY_PATH"] = Path.Combine(layout.WorkingPath, "temp", $"encode-{operationIndex:D4}")
            }),
            "no-overwrite",
            "imagemagick-tiff-v1:<managed-png>,-alpha,off,-colorspace,sRGB,-units,PixelsPerInch,-density,300,-compress,LZW,<declared-tiff>");
        return ExecuteFrozen(requirement, expectedIdentity, invocation, approvalPolicy, cancellationToken);
    }

    internal GerberTiffProcessExecutionResult ExecuteFrozen(
        ExternalToolRequirement requirement,
        ExternalToolIdentity expectedIdentity,
        FrozenProcessInvocation invocation,
        IApprovalPolicy approvalPolicy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(expectedIdentity);
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(approvalPolicy);
        ExternalToolInspectionResult before = inspector.Inspect(
            requirement,
            invocation.ExecutablePath,
            expectedIdentity.Source,
            expectedIdentity.Sha256);
        if (!before.Succeeded || before.Identity is null || before.ExecutablePath is null)
        {
            return NotStarted(
                MapIdentityError(before),
                "External tool identity is missing or changed before execution approval.",
                expectedIdentity,
                invocation,
                "not-requested");
        }

        if (string.IsNullOrWhiteSpace(expectedIdentity.Version))
        {
            return NotStarted(
                ProjectPackRunErrorCode.ToolVersionUnsupported,
                "External tool version must be probed before conversion execution.",
                expectedIdentity,
                invocation,
                "not-requested");
        }

        GerberTiffExecutionRiskSummary risk = CreateRisk(invocation, before.Identity, expectedIdentity.Version);
        Stopwatch approvalClock = Stopwatch.StartNew();
        ApprovalDecision approval = approvalPolicy.RequestApproval(new ApprovalRequest(
            $"project-pack.gerber-tiff.{invocation.StageId}",
            $"Execute {risk.Operation} with tool '{risk.ToolPath}' (SHA256 {risk.ToolSha256}, version '{risk.ToolVersion}'), " +
                $"input '{risk.InputPath}' (SHA256 {risk.InputSha256}), output '{risk.OutputPath}', timeout {risk.TimeoutMilliseconds} ms, " +
                $"operations '{risk.ArgumentTemplate}', overwrite policy '{risk.OverwritePolicy}'.",
            Diff: null,
            IsDirtyWorkspace: false,
            Metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["operation"] = risk.Operation,
                ["toolPath"] = risk.ToolPath,
                ["toolSha256"] = risk.ToolSha256,
                ["toolVersion"] = risk.ToolVersion,
                ["inputPath"] = risk.InputPath,
                ["inputSha256"] = risk.InputSha256,
                ["outputPath"] = risk.OutputPath,
                ["timeoutMilliseconds"] = risk.TimeoutMilliseconds.ToString(CultureInfo.InvariantCulture),
                ["overwritePolicy"] = risk.OverwritePolicy,
                ["argumentTemplate"] = risk.ArgumentTemplate
            },
            RiskLevel: ToolRiskLevel.Shell));
        approvalClock.Stop();
        if (!approval.Approved)
        {
            return NotStarted(
                ProjectPackRunErrorCode.ApprovalRequired,
                approval.SafeMessage,
                expectedIdentity,
                invocation,
                approval.Status,
                approvalClock.ElapsedMilliseconds,
                risk);
        }

        ExternalToolInspectionResult afterApproval = inspector.Inspect(
            requirement,
            before.ExecutablePath,
            expectedIdentity.Source,
            expectedIdentity.Sha256);
        if (!afterApproval.Succeeded || afterApproval.Identity is null || afterApproval.ExecutablePath is null ||
            !SameIdentity(before.Identity, afterApproval.Identity))
        {
            return NotStarted(
                ProjectPackRunErrorCode.ToolIdentityChanged,
                "External tool identity changed after approval and before process start.",
                expectedIdentity,
                invocation,
                approval.Status,
                approvalClock.ElapsedMilliseconds,
                risk);
        }

        BoundedProcessResult processResult = processRunner.Run(invocation, cancellationToken);
        ExternalToolInspectionResult afterExecution = inspector.Inspect(
            requirement,
            afterApproval.ExecutablePath,
            expectedIdentity.Source,
            expectedIdentity.Sha256);
        if (!afterExecution.Succeeded || afterExecution.Identity is null ||
            !SameIdentity(afterApproval.Identity, afterExecution.Identity))
        {
            processResult = processResult with
            {
                Status = processResult.OutputExists ? ProjectPackStageStatus.PartialOutput : ProjectPackStageStatus.Failed,
                ErrorCode = ProjectPackRunErrorCode.ToolIdentityChanged,
                Summary = "External tool identity changed during conversion execution."
            };
        }

        ExternalToolIdentity finalIdentity = (afterExecution.Identity ?? afterApproval.Identity) with
        {
            TrustStatus = expectedIdentity.TrustStatus,
            ProbeStatus = expectedIdentity.ProbeStatus,
            Version = expectedIdentity.Version
        };
        return new GerberTiffProcessExecutionResult(
            processResult.Status,
            processResult.ErrorCode,
            processResult.Summary,
            finalIdentity,
            risk,
            approval.Status,
            Math.Max(0, approvalClock.ElapsedMilliseconds),
            processResult);
    }

    private static GerberTiffProcessExecutionResult NotStarted(
        string errorCode,
        string summary,
        ExternalToolIdentity identity,
        FrozenProcessInvocation invocation,
        string approvalStatus,
        long approvalDurationMilliseconds = 0,
        GerberTiffExecutionRiskSummary? risk = null)
    {
        risk ??= CreateRisk(invocation, identity, identity.Version ?? "not-probed");
        BoundedProcessResult process = new(
            ProjectPackStageStatus.Failed,
            errorCode,
            summary,
            null,
            0,
            string.Empty,
            string.Empty,
            false,
            false,
            false,
            false,
            true,
            false,
            false,
            null,
            null);
        return new GerberTiffProcessExecutionResult(
            process.Status,
            errorCode,
            summary,
            identity,
            risk,
            approvalStatus,
            Math.Max(0, approvalDurationMilliseconds),
            process);
    }

    private static GerberTiffExecutionRiskSummary CreateRisk(
        FrozenProcessInvocation invocation,
        ExternalToolIdentity identity,
        string version) =>
        new(
            invocation.Operation,
            Path.GetFullPath(invocation.ExecutablePath),
            identity.Sha256,
            version,
            Path.GetFullPath(invocation.Inputs[0].Path),
            invocation.Inputs[0].Sha256,
            Path.GetFullPath(invocation.DeclaredOutputPath),
            invocation.TimeoutMilliseconds,
            invocation.OverwritePolicy,
            invocation.ArgumentTemplate);

    private static string MapIdentityError(ExternalToolInspectionResult inspection) =>
        inspection.Diagnostics.Any(diagnostic => diagnostic.Code is ExternalToolDiagnosticCode.PathMissing or ExternalToolDiagnosticCode.PathNotFound)
            ? ProjectPackRunErrorCode.ToolNotFound
            : ProjectPackRunErrorCode.ToolIdentityChanged;

    private static bool SameIdentity(ExternalToolIdentity left, ExternalToolIdentity right) =>
        left.FileName == right.FileName &&
        left.FileSize == right.FileSize &&
        left.LastWriteTimeUtc == right.LastWriteTimeUtc &&
        left.Sha256.Equals(right.Sha256, StringComparison.OrdinalIgnoreCase);
}

public sealed class GerberTiffControlledConversionDriver : IProjectPackRunDriver
{
    private readonly GerberTiffRunPlanSnapshot plan;
    private readonly WorkspaceContext workspace;
    private readonly IReadOnlyDictionary<string, string> toolPaths;
    private readonly IReadOnlyDictionary<string, ExternalToolIdentity> toolIdentities;
    private readonly IApprovalPolicy approvalPolicy;
    private readonly GerberTiffTypedProcessAdapter adapter;
    private readonly Func<DateTimeOffset> utcNowProvider;
    private readonly ProjectPackManifest manifest = new GerberTiffWorkflowPack().Manifest;

    public GerberTiffControlledConversionDriver(
        GerberTiffRunPlanSnapshot plan,
        WorkspaceContext workspace,
        IReadOnlyDictionary<string, string> toolPaths,
        IReadOnlyDictionary<string, ExternalToolIdentity> toolIdentities,
        IApprovalPolicy approvalPolicy)
        : this(plan, workspace, toolPaths, toolIdentities, approvalPolicy, new GerberTiffTypedProcessAdapter(), () => DateTimeOffset.UtcNow)
    {
    }

    internal GerberTiffControlledConversionDriver(
        GerberTiffRunPlanSnapshot plan,
        WorkspaceContext workspace,
        IReadOnlyDictionary<string, string> toolPaths,
        IReadOnlyDictionary<string, ExternalToolIdentity> toolIdentities,
        IApprovalPolicy approvalPolicy,
        GerberTiffTypedProcessAdapter adapter,
        Func<DateTimeOffset> utcNowProvider)
    {
        this.plan = plan ?? throw new ArgumentNullException(nameof(plan));
        this.workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        this.toolPaths = toolPaths ?? throw new ArgumentNullException(nameof(toolPaths));
        this.toolIdentities = toolIdentities ?? throw new ArgumentNullException(nameof(toolIdentities));
        this.approvalPolicy = approvalPolicy ?? throw new ArgumentNullException(nameof(approvalPolicy));
        this.adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        this.utcNowProvider = utcNowProvider ?? throw new ArgumentNullException(nameof(utcNowProvider));
    }

    public ProjectPackRunDriverResult Execute(
        ProjectPackRunExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!string.Equals(context.Record.PlanFingerprint, plan.Fingerprint, StringComparison.OrdinalIgnoreCase))
        {
            return Failure("render", ProjectPackStageStatus.Failed, ProjectPackRunErrorCode.PlanFingerprintChanged,
                "Controlled conversion plan fingerprint does not match the managed run.", [], []);
        }

        ProjectPackInputManifest inputManifest;
        try
        {
            inputManifest = ProjectPackStagingService.LoadManifest(
                ManagedProjectPackRunStore.ReadTextBounded(context.Layout.InputManifestPath, 2 * 1024 * 1024));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ProjectPackContractException)
        {
            return Failure("render", ProjectPackStageStatus.Failed, ProjectPackRunErrorCode.RecordCorrupt,
                "Controlled conversion input manifest could not be read safely.", [], []);
        }

        ProjectPackStagedInput[] inputs = inputManifest.Inputs
            .Where(input => input.PassedToExternalTool)
            .OrderBy(input => input.SourceRelativePath, StringComparer.Ordinal)
            .ToArray();
        if (inputs.Length == 0)
        {
            return Failure("render", ProjectPackStageStatus.Failed, ProjectPackRunErrorCode.InputChanged,
                "Controlled conversion has no frozen external-tool inputs.", [], []);
        }

        if (!TryGetTool("gerbv", out ExternalToolRequirement? gerbvRequirement, out string? gerbvPath, out ExternalToolIdentity? gerbvIdentity) ||
            !TryGetTool("imagemagick", out ExternalToolRequirement? magickRequirement, out string? magickPath, out ExternalToolIdentity? magickIdentity))
        {
            return Failure("render", ProjectPackStageStatus.Failed, ProjectPackRunErrorCode.ToolNotFound,
                "Controlled conversion requires probed Gerbv and ImageMagick identities.", [], []);
        }

        string outputRoot;
        try
        {
            WorkspaceGuardResult outputGuard = new WorkspaceGuard().ResolvePath(workspace, plan.OutputDirectory);
            if (!outputGuard.IsAllowed || outputGuard.FullPath is null ||
                Directory.Exists(outputGuard.FullPath) || File.Exists(outputGuard.FullPath))
            {
                return Failure("encode", ProjectPackStageStatus.Failed, ProjectPackRunErrorCode.ConversionOutputConflict,
                    "Declared workspace output directory is outside the workspace or already exists.", [], []);
            }

            outputRoot = outputGuard.FullPath;
            ManagedProjectPackRunStore.EnsureNoReparseInExistingChain(outputRoot);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ProjectPackContractException)
        {
            return Failure("encode", ProjectPackStageStatus.Failed, ProjectPackRunErrorCode.OutputBoundaryViolation,
                "Declared workspace output directory failed boundary validation.", [], []);
        }

        List<GerberTiffProcessExecutionResult> processEvidence = [];
        List<ProjectPackRunArtifactPointer> artifacts = [];
        List<ProjectPackDriverStageEvent> events = [];
        DateTimeOffset renderStarted = utcNowProvider();
        string renderRoot = Path.Combine(context.Layout.ArtifactsPath, "render");
        string[] renderPaths = inputs.Select((input, index) =>
            Path.Combine(context.Layout.ArtifactsPath,
                GerberTiffArtifactNaming.RenderRelativePath(index + 1, input.SourceRelativePath).Replace('/', Path.DirectorySeparatorChar)))
            .ToArray();
        string[] tiffPaths = inputs.Select((input, index) =>
            Path.Combine(outputRoot, GerberTiffArtifactNaming.TiffFileName(index + 1, input.SourceRelativePath)))
            .ToArray();

        for (int index = 0; index < inputs.Length; index++)
        {
            ProjectPackStagedInput input = inputs[index];
            string stagedPath = ResolveManagedPath(context.Layout.RunRoot, input.StagedRelativePath);
            GerberTiffProcessExecutionResult result = adapter.Render(
                gerbvRequirement!,
                gerbvPath!,
                gerbvIdentity!,
                stagedPath,
                input.Sha256,
                renderPaths[index],
                renderPaths,
                context.Layout,
                approvalPolicy,
                index + 1,
                cancellationToken);
            processEvidence.Add(result);
            AddOutputEvidence(
                artifacts,
                result,
                $"render-{index + 1:D4}",
                "render-intermediate",
                "managed-run",
                "artifacts/" + GerberTiffArtifactNaming.RenderRelativePath(index + 1, input.SourceRelativePath));
            if (result.Status != ProjectPackStageStatus.Succeeded)
            {
                events.Add(new ProjectPackDriverStageEvent(
                    "render",
                    result.Status,
                    renderStarted,
                    utcNowProvider(),
                    result.ErrorCode,
                    result.Summary,
                    artifacts.ToArray()));
                return Complete(result.Status, result.ErrorCode, result.Summary, events, artifacts, processEvidence, context.Layout);
            }
        }

        events.Add(new ProjectPackDriverStageEvent(
            "render",
            ProjectPackStageStatus.Succeeded,
            renderStarted,
            utcNowProvider(),
            Summary: "Frozen Gerber render template completed for every declared input.",
            Outputs: artifacts.ToArray()));

        DateTimeOffset encodeStarted = utcNowProvider();
        for (int index = 0; index < inputs.Length; index++)
        {
            ProjectPackStagedInput input = inputs[index];
            ProjectPackRunArtifactPointer renderArtifact = artifacts.Single(artifact => artifact.Id == $"render-{index + 1:D4}");
            GerberTiffProcessExecutionResult result = adapter.Encode(
                magickRequirement!,
                magickPath!,
                magickIdentity!,
                renderPaths[index],
                renderArtifact.Sha256!,
                tiffPaths[index],
                outputRoot,
                workspace.RootPath,
                tiffPaths,
                context.Layout,
                approvalPolicy,
                index + 1,
                cancellationToken);
            processEvidence.Add(result);
            AddOutputEvidence(
                artifacts,
                result,
                $"tiff-{index + 1:D4}",
                "tiff-output",
                "workspace-output",
                NormalizeRelative(Path.Combine(plan.OutputDirectory,
                    GerberTiffArtifactNaming.TiffFileName(index + 1, input.SourceRelativePath))));
            if (result.Status != ProjectPackStageStatus.Succeeded)
            {
                events.Add(new ProjectPackDriverStageEvent(
                    "encode",
                    result.Status,
                    encodeStarted,
                    utcNowProvider(),
                    result.ErrorCode,
                    result.Summary,
                    artifacts.Where(artifact => artifact.Kind == "tiff-output").ToArray()));
                return Complete(result.Status, result.ErrorCode, result.Summary, events, artifacts, processEvidence, context.Layout);
            }
        }

        events.Add(new ProjectPackDriverStageEvent(
            "encode",
            ProjectPackStageStatus.Succeeded,
            encodeStarted,
            utcNowProvider(),
            Summary: "Frozen TIFF encode template completed for every declared output; TIFF verification has not run.",
            Outputs: artifacts.Where(artifact => artifact.Kind == "tiff-output").ToArray()));
        return Complete(
            ProjectPackStageStatus.Succeeded,
            null,
            "Gerber to TIFF conversion executed with declared outputs inventoried; Week 62 engineering verification is pending.",
            events,
            artifacts,
            processEvidence,
            context.Layout);
    }

    private bool TryGetTool(
        string dependencyId,
        out ExternalToolRequirement? requirement,
        out string? path,
        out ExternalToolIdentity? identity)
    {
        requirement = manifest.Dependencies.FirstOrDefault(item => item.Id == dependencyId);
        path = null;
        identity = null;
        return requirement is not null &&
            toolPaths.TryGetValue(dependencyId, out path) &&
            toolIdentities.TryGetValue(dependencyId, out identity) &&
            identity.Version is not null;
    }

    private ProjectPackRunDriverResult Complete(
        string status,
        string? errorCode,
        string summary,
        IReadOnlyList<ProjectPackDriverStageEvent> events,
        List<ProjectPackRunArtifactPointer> artifacts,
        IReadOnlyList<GerberTiffProcessExecutionResult> evidence,
        ManagedProjectPackRunLayout layout)
    {
        try
        {
            ProjectPackRunArtifactPointer log = WriteExecutionLog(layout, evidence);
            artifacts.Add(log);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return Failure(
                events.LastOrDefault()?.StageId ?? "render",
                ProjectPackStageStatus.Failed,
                ProjectPackRunErrorCode.ExecutionFailed,
                "Redacted conversion execution evidence could not be written.",
                events,
                artifacts);
        }

        return new ProjectPackRunDriverResult(status, events, artifacts, errorCode, summary);
    }

    private static ProjectPackRunDriverResult Failure(
        string stageId,
        string status,
        string errorCode,
        string summary,
        IReadOnlyList<ProjectPackDriverStageEvent> existingEvents,
        IReadOnlyList<ProjectPackRunArtifactPointer> outputs)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ProjectPackDriverStageEvent[] events = existingEvents.Count == 0
            ? [new ProjectPackDriverStageEvent(stageId, status, now, now, errorCode, summary, outputs)]
            : existingEvents.ToArray();
        return new ProjectPackRunDriverResult(status, events, outputs, errorCode, summary);
    }

    private static void AddOutputEvidence(
        List<ProjectPackRunArtifactPointer> artifacts,
        GerberTiffProcessExecutionResult result,
        string id,
        string kind,
        string scope,
        string relativePath)
    {
        if (!result.ProcessResult.OutputExists || result.ProcessResult.OutputSize is null ||
            result.ProcessResult.OutputSha256 is null)
        {
            return;
        }

        artifacts.Add(new ProjectPackRunArtifactPointer(
            id,
            kind,
            scope,
            NormalizeRelative(relativePath),
            true,
            result.ProcessResult.OutputSize,
            result.ProcessResult.OutputSha256));
    }

    private static ProjectPackRunArtifactPointer WriteExecutionLog(
        ManagedProjectPackRunLayout layout,
        IReadOnlyList<GerberTiffProcessExecutionResult> evidence)
    {
        ManagedProjectPackRunStore.EnsureNoReparseInExistingChain(layout.LogsPath);
        string path = Path.Combine(layout.LogsPath, "conversion-execution.json");
        object payload = new
        {
            schemaVersion = 1,
            type = "gerber-tiff.conversion-execution",
            conversionExecuted = evidence.Any(item => item.ProcessResult.ExitCode.HasValue),
            tiffVerificationPassed = false,
            operations = evidence.Select(item => new
            {
                item.RiskSummary.Operation,
                tool = new
                {
                    item.ToolIdentity.FileName,
                    item.ToolIdentity.FileSize,
                    item.ToolIdentity.Sha256,
                    item.ToolIdentity.Version
                },
                inputSha256 = item.RiskSummary.InputSha256,
                outputName = Path.GetFileName(item.RiskSummary.OutputPath),
                item.RiskSummary.TimeoutMilliseconds,
                item.RiskSummary.OverwritePolicy,
                item.RiskSummary.ArgumentTemplate,
                item.ApprovalStatus,
                item.ApprovalDurationMilliseconds,
                item.ProcessResult.Status,
                item.ProcessResult.ErrorCode,
                summary = DiagnosticSecretRedactor.Redact(item.ProcessResult.Summary),
                item.ProcessResult.ExitCode,
                item.ProcessResult.DurationMilliseconds,
                stdout = DiagnosticSecretRedactor.Redact(item.ProcessResult.Stdout),
                stderr = DiagnosticSecretRedactor.Redact(item.ProcessResult.Stderr),
                item.ProcessResult.StdoutTruncated,
                item.ProcessResult.StderrTruncated,
                item.ProcessResult.TimedOut,
                item.ProcessResult.Canceled,
                item.ProcessResult.ProcessCleanedUp,
                item.ProcessResult.ResidualProcessDetected,
                item.ProcessResult.OutputExists,
                item.ProcessResult.OutputSize,
                item.ProcessResult.OutputSha256
            })
        };
        byte[] content = JsonSerializer.SerializeToUtf8Bytes(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        });
        using (FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024, FileOptions.WriteThrough))
        {
            stream.Write(content);
            stream.Flush(flushToDisk: true);
        }

        return new ProjectPackRunArtifactPointer(
            "execution-log",
            "conversion-execution-log",
            "managed-run",
            "logs/conversion-execution.json",
            true,
            content.Length,
            Convert.ToHexString(SHA256.HashData(content)));
    }

    private static string ResolveManagedPath(string runRoot, string relativePath)
    {
        string normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        string path = Path.GetFullPath(Path.Combine(runRoot, normalized));
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(runRoot)) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new ProjectPackContractException(ProjectPackRunErrorCode.PathUnsafe, "Managed input path escaped the run root.");
        }

        return path;
    }

    private static string NormalizeRelative(string path) => path.Replace('\\', '/');
}
