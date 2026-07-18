using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CSharpAiCli.Core;

namespace CSharpAiCli.ProjectPacks;

public static class ExternalToolDiagnosticCode
{
    public const string PathMissing = "pack-tool-path-missing";
    public const string PathInvalid = "pack-tool-path-invalid";
    public const string PathNotFound = "pack-tool-path-not-found";
    public const string PathDirectory = "pack-tool-path-directory";
    public const string PathReparsePoint = "pack-tool-path-reparse-point";
    public const string FileNameDenied = "pack-tool-filename-denied";
    public const string FileTooLarge = "pack-tool-file-too-large";
    public const string FileChanged = "pack-tool-file-changed";
    public const string HashFailed = "pack-tool-hash-failed";
    public const string HashInvalid = "pack-tool-hash-invalid";
    public const string HashChanged = "pack-tool-hash-changed";
    public const string HashUntrusted = "pack-tool-hash-untrusted";
    public const string ApprovalDenied = "pack-tool-approval-denied";
    public const string ProbeStartFailed = "pack-tool-probe-start-failed";
    public const string ProbeTimedOut = "pack-tool-probe-timeout";
    public const string ProbeCanceled = "pack-tool-probe-canceled";
    public const string ProbeExitCode = "pack-tool-probe-exit-code";
    public const string VersionUnsupported = "pack-tool-version-unsupported";
    public const string ProbeCleanupFailed = "pack-tool-probe-cleanup-failed";
}

public sealed class ExternalToolInspectionResult
{
    internal ExternalToolInspectionResult(
        ExternalToolIdentity? identity,
        string? executablePath,
        IReadOnlyList<ProjectPackDiagnostic> diagnostics)
    {
        Identity = identity;
        ExecutablePath = executablePath;
        Diagnostics = new ReadOnlyCollection<ProjectPackDiagnostic>(diagnostics.ToArray());
    }

    public ExternalToolIdentity? Identity { get; }

    public IReadOnlyList<ProjectPackDiagnostic> Diagnostics { get; }

    public bool Succeeded => Identity is not null && Diagnostics.All(
        diagnostic => diagnostic.Severity != ProjectPackDiagnosticSeverity.Error);

    internal string? ExecutablePath { get; }
}

public sealed class ExternalToolPathInspector
{
    public ExternalToolInspectionResult Inspect(
        ExternalToolRequirement requirement,
        string? requestedPath,
        string source,
        string? expectedSha256 = null)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            return Failure(requirement.Id, ExternalToolDiagnosticCode.PathMissing, "External tool path is not configured.");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(requestedPath);
        }
        catch (Exception exception) when (exception is ArgumentException
            or IOException
            or NotSupportedException
            or PathTooLongException
            or UnauthorizedAccessException)
        {
            return Failure(requirement.Id, ExternalToolDiagnosticCode.PathInvalid, "External tool path is invalid.");
        }

        if (Directory.Exists(fullPath))
        {
            return Failure(requirement.Id, ExternalToolDiagnosticCode.PathDirectory, "External tool path must identify a regular file.");
        }

        if (!File.Exists(fullPath))
        {
            return Failure(requirement.Id, ExternalToolDiagnosticCode.PathNotFound, "External tool file was not found.");
        }

        string fileName = Path.GetFileName(fullPath);
        if (!requirement.ExecutableFileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase))
        {
            return Failure(
                requirement.Id,
                ExternalToolDiagnosticCode.FileNameDenied,
                $"Executable file name '{ProjectPackContractGuard.Safe(fileName, 256)}' is not allowed for dependency '{requirement.Id}'.");
        }

        try
        {
            if (ContainsReparsePoint(fullPath))
            {
                return Failure(
                    requirement.Id,
                    ExternalToolDiagnosticCode.PathReparsePoint,
                    "External tool path cannot contain a reparse point.");
            }

            FileInfo before = new(fullPath);
            before.Refresh();
            if (before.Length > requirement.MaxExecutableBytes)
            {
                return Failure(
                    requirement.Id,
                    ExternalToolDiagnosticCode.FileTooLarge,
                    $"External tool exceeds the {requirement.MaxExecutableBytes} byte identity limit.");
            }

            long beforeLength = before.Length;
            DateTime beforeLastWriteUtc = before.LastWriteTimeUtc;
            string sha256;
            using (FileStream stream = new(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.SequentialScan))
            {
                sha256 = Convert.ToHexString(SHA256.HashData(stream));
            }

            FileInfo after = new(fullPath);
            after.Refresh();
            if (after.Length != beforeLength || after.LastWriteTimeUtc != beforeLastWriteUtc)
            {
                return Failure(
                    requirement.Id,
                    ExternalToolDiagnosticCode.FileChanged,
                    "External tool changed while its identity was being calculated.");
            }

            string trustStatus = ExternalToolTrustStatus.Untrusted;
            List<ProjectPackDiagnostic> diagnostics = [];
            if (!string.IsNullOrWhiteSpace(expectedSha256))
            {
                if (!TryNormalizeSha256(expectedSha256, out string normalizedExpected))
                {
                    return Failure(
                        requirement.Id,
                        ExternalToolDiagnosticCode.HashInvalid,
                        "Configured trusted SHA256 is invalid.");
                }

                if (!string.Equals(sha256, normalizedExpected, StringComparison.Ordinal))
                {
                    diagnostics.Add(new ProjectPackDiagnostic(
                        ExternalToolDiagnosticCode.HashChanged,
                        ProjectPackDiagnosticSeverity.Error,
                        "External tool SHA256 differs from the trusted identity.",
                        requirement.Id));
                    trustStatus = ExternalToolTrustStatus.HashChanged;
                }
                else
                {
                    trustStatus = ExternalToolTrustStatus.Trusted;
                }
            }
            else
            {
                diagnostics.Add(new ProjectPackDiagnostic(
                    ExternalToolDiagnosticCode.HashUntrusted,
                    ProjectPackDiagnosticSeverity.Warning,
                    "External tool hash is observed for this invocation but is not persistently trusted.",
                    requirement.Id));
            }

            ExternalToolIdentity identity = new(
                DependencyId: requirement.Id,
                FileName: ProjectPackContractGuard.Safe(fileName, 256),
                FileSize: beforeLength,
                LastWriteTimeUtc: new DateTimeOffset(beforeLastWriteUtc, TimeSpan.Zero),
                Sha256: sha256,
                Source: ProjectPackContractGuard.Safe(source, 128),
                TrustStatus: trustStatus,
                ProbeStatus: "not-run");
            return new ExternalToolInspectionResult(identity, fullPath, diagnostics);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception)
        {
            return Failure(
                requirement.Id,
                ExternalToolDiagnosticCode.HashFailed,
                "External tool identity could not be calculated safely.");
        }
    }

    private static ExternalToolInspectionResult Failure(string dependencyId, string code, string summary)
    {
        return new ExternalToolInspectionResult(
            null,
            null,
            [new ProjectPackDiagnostic(code, ProjectPackDiagnosticSeverity.Error, summary, dependencyId)]);
    }

    private static bool TryNormalizeSha256(string value, out string normalized)
    {
        string candidate = value.Trim().ToUpperInvariant();
        if (candidate.Length == 64 && candidate.All(Uri.IsHexDigit))
        {
            normalized = candidate;
            return true;
        }

        normalized = string.Empty;
        return false;
    }

    private static bool ContainsReparsePoint(string fullPath)
    {
        string? root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            return true;
        }

        string relative = Path.GetRelativePath(root, fullPath);
        string current = root;
        foreach (string segment in relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileAttributes attributes = File.GetAttributes(current);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return true;
            }
        }

        return false;
    }
}

public sealed record ExternalToolProbeResult
{
    public ExternalToolProbeResult(
        string Status,
        ExternalToolIdentity? Identity,
        string ApprovalStatus,
        long ApprovalDurationMilliseconds,
        int? ExitCode,
        long DurationMilliseconds,
        string Stdout,
        string Stderr,
        bool StdoutTruncated,
        bool StderrTruncated,
        bool TimedOut,
        bool Canceled,
        bool ProcessCleanedUp,
        IReadOnlyList<ProjectPackDiagnostic>? Diagnostics)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Status);
        ArgumentException.ThrowIfNullOrWhiteSpace(ApprovalStatus);
        this.Status = Status;
        this.Identity = Identity;
        this.ApprovalStatus = ProjectPackContractGuard.Safe(ApprovalStatus, 64);
        this.ApprovalDurationMilliseconds = Math.Max(0, ApprovalDurationMilliseconds);
        this.ExitCode = ExitCode;
        this.DurationMilliseconds = Math.Max(0, DurationMilliseconds);
        this.Stdout = ProjectPackContractGuard.Safe(Stdout, 1_048_576);
        this.Stderr = ProjectPackContractGuard.Safe(Stderr, 1_048_576);
        this.StdoutTruncated = StdoutTruncated;
        this.StderrTruncated = StderrTruncated;
        this.TimedOut = TimedOut;
        this.Canceled = Canceled;
        this.ProcessCleanedUp = ProcessCleanedUp;
        this.Diagnostics = new ReadOnlyCollection<ProjectPackDiagnostic>((Diagnostics ?? []).ToArray());
    }

    public string Status { get; }

    public ExternalToolIdentity? Identity { get; }

    public string ApprovalStatus { get; }

    public long ApprovalDurationMilliseconds { get; }

    public int? ExitCode { get; }

    public long DurationMilliseconds { get; }

    public string Stdout { get; }

    public string Stderr { get; }

    public bool StdoutTruncated { get; }

    public bool StderrTruncated { get; }

    public bool TimedOut { get; }

    public bool Canceled { get; }

    public bool ProcessCleanedUp { get; }

    public IReadOnlyList<ProjectPackDiagnostic> Diagnostics { get; }

    public bool Succeeded => Status == "succeeded";
}

public sealed class ExternalToolProbeRunner
{
    private const int PollMilliseconds = 25;
    private const int CleanupMilliseconds = 2_000;

    private readonly ExternalToolPathInspector inspector;

    public ExternalToolProbeRunner(ExternalToolPathInspector? inspector = null)
    {
        this.inspector = inspector ?? new ExternalToolPathInspector();
    }

    public ExternalToolProbeResult Run(
        ExternalToolRequirement requirement,
        ExternalToolInspectionResult inspection,
        IApprovalPolicy approvalPolicy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(inspection);
        ArgumentNullException.ThrowIfNull(approvalPolicy);

        if (!inspection.Succeeded || inspection.Identity is null || inspection.ExecutablePath is null)
        {
            return CreateNotStarted("identity-invalid", inspection, "not-requested", 0, inspection.Diagnostics);
        }

        Stopwatch approvalClock = Stopwatch.StartNew();
        ApprovalDecision approval = approvalPolicy.RequestApproval(new ApprovalRequest(
            Operation: "project-pack.tool-probe",
            Summary: $"Probe external dependency '{requirement.Id}' using '{inspection.Identity.FileName}' with SHA256 {inspection.Identity.Sha256}.",
            Diff: null,
            IsDirtyWorkspace: false,
            Metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["dependencyId"] = requirement.Id,
                ["fileName"] = inspection.Identity.FileName,
                ["sha256"] = inspection.Identity.Sha256,
                ["probeArgumentCount"] = requirement.ProbeArguments.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
            },
            RiskLevel: ToolRiskLevel.Shell));
        approvalClock.Stop();
        if (!approval.Approved)
        {
            ProjectPackDiagnostic diagnostic = new(
                ExternalToolDiagnosticCode.ApprovalDenied,
                ProjectPackDiagnosticSeverity.Error,
                approval.SafeMessage,
                requirement.Id);
            return CreateNotStarted(
                "approval-denied",
                inspection,
                approval.Status,
                approvalClock.ElapsedMilliseconds,
                [.. inspection.Diagnostics, diagnostic]);
        }

        ExternalToolInspectionResult preflight = inspector.Inspect(
            requirement,
            inspection.ExecutablePath,
            inspection.Identity.Source,
            inspection.Identity.Sha256);
        if (!preflight.Succeeded || preflight.Identity is null || preflight.ExecutablePath is null)
        {
            return CreateNotStarted(
                "identity-changed",
                preflight,
                approval.Status,
                approvalClock.ElapsedMilliseconds,
                preflight.Diagnostics);
        }

        string workingDirectory = CreateProbeDirectory();
        Process? process = null;
        Stopwatch executionClock = Stopwatch.StartNew();
        Task<BoundedText>? stdoutTask = null;
        Task<BoundedText>? stderrTask = null;
        bool timedOut = false;
        bool canceled = false;
        bool processCleanedUp = true;
        int? exitCode = null;
        List<ProjectPackDiagnostic> diagnostics = [.. inspection.Diagnostics];
        BoundedText stdout = BoundedText.Empty;
        BoundedText stderr = BoundedText.Empty;
        using CancellationTokenSource outputReadCancellation = new();

        try
        {
            ProcessStartInfo startInfo = CreateStartInfo(
                preflight.ExecutablePath,
                requirement.ProbeArguments,
                workingDirectory);
            process = new Process { StartInfo = startInfo };
            process.Start();
            process.StandardInput.Close();
            stdoutTask = ReadBoundedAsync(process.StandardOutput, requirement.MaxProbeOutputCharacters, outputReadCancellation.Token);
            stderrTask = ReadBoundedAsync(process.StandardError, requirement.MaxProbeOutputCharacters, outputReadCancellation.Token);

            while (!process.WaitForExit(PollMilliseconds))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    canceled = true;
                    break;
                }

                if (executionClock.ElapsedMilliseconds >= requirement.ProbeTimeoutMilliseconds)
                {
                    timedOut = true;
                    break;
                }
            }

            if (timedOut || canceled)
            {
                processCleanedUp = TryKillAndWait(process);
                outputReadCancellation.Cancel();
            }

            if (process.HasExited)
            {
                exitCode = process.ExitCode;
            }

            if (stdoutTask is not null && stderrTask is not null)
            {
                Task allOutput = Task.WhenAll(stdoutTask, stderrTask);
                if (allOutput.Wait(CleanupMilliseconds))
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
            processCleanedUp = process is null || IsExitedOrCleanup(process);
            diagnostics.Add(new ProjectPackDiagnostic(
                ExternalToolDiagnosticCode.ProbeStartFailed,
                ProjectPackDiagnosticSeverity.Error,
                "External tool probe could not be started safely.",
                requirement.Id));
        }
        finally
        {
            executionClock.Stop();
            process?.Dispose();
            TryDeleteProbeDirectory(workingDirectory);
        }

        if (timedOut)
        {
            diagnostics.Add(new ProjectPackDiagnostic(
                ExternalToolDiagnosticCode.ProbeTimedOut,
                ProjectPackDiagnosticSeverity.Error,
                $"External tool probe timed out after {requirement.ProbeTimeoutMilliseconds} ms.",
                requirement.Id));
        }
        else if (canceled)
        {
            diagnostics.Add(new ProjectPackDiagnostic(
                ExternalToolDiagnosticCode.ProbeCanceled,
                ProjectPackDiagnosticSeverity.Error,
                "External tool probe was canceled.",
                requirement.Id));
        }
        else if (exitCode is not 0)
        {
            diagnostics.Add(new ProjectPackDiagnostic(
                ExternalToolDiagnosticCode.ProbeExitCode,
                ProjectPackDiagnosticSeverity.Error,
                exitCode is null
                    ? "External tool probe did not produce an exit code."
                    : $"External tool probe failed with exit code {exitCode}.",
                requirement.Id));
        }

        if (!processCleanedUp)
        {
            diagnostics.Add(new ProjectPackDiagnostic(
                ExternalToolDiagnosticCode.ProbeCleanupFailed,
                ProjectPackDiagnosticSeverity.Error,
                "External tool probe process cleanup did not complete within the bound.",
                requirement.Id));
        }

        ExternalToolInspectionResult postflight = inspector.Inspect(
            requirement,
            preflight.ExecutablePath,
            inspection.Identity.Source,
            inspection.Identity.Sha256);
        if (!postflight.Succeeded || postflight.Identity is null)
        {
            diagnostics.AddRange(postflight.Diagnostics);
        }

        string? probedVersion = ExtractVersion(stdout.Text, stderr.Text, requirement.VersionOutputMarker);
        if (!timedOut && !canceled && exitCode == 0 &&
            !MeetsMinimumVersion(probedVersion, requirement.MinimumVersion))
        {
            diagnostics.Add(new ProjectPackDiagnostic(
                ExternalToolDiagnosticCode.VersionUnsupported,
                ProjectPackDiagnosticSeverity.Error,
                $"External tool version does not satisfy minimum version {requirement.MinimumVersion}.",
                requirement.Id));
        }

        bool succeeded = !timedOut && !canceled && processCleanedUp && exitCode == 0 &&
            postflight.Succeeded && diagnostics.All(diagnostic => diagnostic.Severity != ProjectPackDiagnosticSeverity.Error);
        ExternalToolIdentity? identity = postflight.Identity is null
            ? inspection.Identity
            : postflight.Identity with
            {
                TrustStatus = inspection.Identity.TrustStatus,
                ProbeStatus = succeeded ? "succeeded" : "failed",
                Version = probedVersion
            };

        return new ExternalToolProbeResult(
            Status: succeeded ? "succeeded" : timedOut ? "timed-out" : canceled ? "canceled" : "failed",
            Identity: identity,
            ApprovalStatus: approval.Status,
            ApprovalDurationMilliseconds: approvalClock.ElapsedMilliseconds,
            ExitCode: exitCode,
            DurationMilliseconds: executionClock.ElapsedMilliseconds,
            Stdout: stdout.Text,
            Stderr: stderr.Text,
            StdoutTruncated: stdout.Truncated,
            StderrTruncated: stderr.Truncated,
            TimedOut: timedOut,
            Canceled: canceled,
            ProcessCleanedUp: processCleanedUp,
            Diagnostics: diagnostics);
    }

    private static ExternalToolProbeResult CreateNotStarted(
        string status,
        ExternalToolInspectionResult inspection,
        string approvalStatus,
        long approvalDurationMilliseconds,
        IReadOnlyList<ProjectPackDiagnostic> diagnostics)
    {
        return new ExternalToolProbeResult(
            status,
            inspection.Identity,
            approvalStatus,
            approvalDurationMilliseconds,
            null,
            0,
            string.Empty,
            string.Empty,
            false,
            false,
            false,
            false,
            true,
            diagnostics);
    }

    private static ProcessStartInfo CreateStartInfo(
        string executablePath,
        IReadOnlyList<string> arguments,
        string workingDirectory)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = executablePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment.Clear();
        AddEnvironment(startInfo, "SystemRoot", Environment.GetEnvironmentVariable("SystemRoot"));
        AddEnvironment(startInfo, "WINDIR", Environment.GetEnvironmentVariable("WINDIR"));
        AddEnvironment(startInfo, "ComSpec", Environment.GetEnvironmentVariable("ComSpec"));
        startInfo.Environment["TEMP"] = workingDirectory;
        startInfo.Environment["TMP"] = workingDirectory;
        startInfo.Environment["LANG"] = "C";
        startInfo.Environment["LC_ALL"] = "C";
        startInfo.Environment["NO_COLOR"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        return startInfo;
    }

    private static void AddEnvironment(ProcessStartInfo startInfo, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            startInfo.Environment[name] = value;
        }
    }

    private static async Task<BoundedText> ReadBoundedAsync(
        StreamReader reader,
        int maxCharacters,
        CancellationToken cancellationToken)
    {
        char[] buffer = new char[4_096];
        StringBuilder builder = new(Math.Min(maxCharacters, 16_384));
        bool truncated = false;
        try
        {
            int read;
            while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
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
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Timeout/cancel owns the business outcome. Return the bounded tail
            // already observed instead of leaving a pipe read pending after kill.
        }

        return new BoundedText(builder.ToString(), truncated);
    }

    private static bool TryKillAndWait(Process process)
    {
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

        try
        {
            if (process.WaitForExit(CleanupMilliseconds))
            {
                return true;
            }
        }
        catch
        {
        }

        if (OperatingSystem.IsWindows())
        {
            TryTaskKill(process.Id);
            try
            {
                return process.WaitForExit(CleanupMilliseconds);
            }
            catch
            {
            }
        }

        return false;
    }

    private static bool IsExitedOrCleanup(Process process)
    {
        try
        {
            return process.HasExited || TryKillAndWait(process);
        }
        catch
        {
            return TryKillAndWait(process);
        }
    }

    private static void TryTaskKill(int processId)
    {
        try
        {
            string executable = Path.Combine(Environment.SystemDirectory, "taskkill.exe");
            using Process cleanup = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            cleanup.StartInfo.ArgumentList.Add("/PID");
            cleanup.StartInfo.ArgumentList.Add(processId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            cleanup.StartInfo.ArgumentList.Add("/T");
            cleanup.StartInfo.ArgumentList.Add("/F");
            cleanup.Start();
            cleanup.WaitForExit(CleanupMilliseconds);
        }
        catch
        {
        }
    }

    private static string CreateProbeDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "caicli-pack-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteProbeDirectory(string path)
    {
        try
        {
            string fullPath = Path.GetFullPath(path);
            string tempRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(tempRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) &&
                Path.GetFileName(fullPath).StartsWith("caicli-pack-probe-", StringComparison.Ordinal))
            {
                Directory.Delete(fullPath, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static string? ExtractVersion(string stdout, string stderr, string? marker)
    {
        string[] lines = EnumerateLines(stdout).Concat(EnumerateLines(stderr)).ToArray();
        IEnumerable<string> candidates = string.IsNullOrWhiteSpace(marker)
            ? lines
            : lines.Where(line => line.Contains(marker, StringComparison.OrdinalIgnoreCase));
        string? line = candidates.FirstOrDefault(candidate => TryParseNumericVersion(candidate, out _));
        return line is null ? null : ProjectPackContractGuard.Safe(line, 256);
    }

    private static bool MeetsMinimumVersion(string? probedVersion, string minimumVersion)
    {
        if (!TryParseNumericVersion(probedVersion, out int[] actual) ||
            !TryParseNumericVersion(minimumVersion, out int[] minimum))
        {
            return false;
        }

        int length = Math.Max(actual.Length, minimum.Length);
        for (int index = 0; index < length; index++)
        {
            int actualPart = index < actual.Length ? actual[index] : 0;
            int minimumPart = index < minimum.Length ? minimum[index] : 0;
            if (actualPart != minimumPart)
            {
                return actualPart > minimumPart;
            }
        }

        return true;
    }

    private static bool TryParseNumericVersion(string? value, out int[] components)
    {
        Match match = Regex.Match(
            value ?? string.Empty,
            @"(?<![0-9])(?<version>[0-9]+(?:[.-][0-9]+){0,7})(?![0-9])",
            RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            components = [];
            return false;
        }

        string[] parts = match.Groups["version"].Value.Split(['.', '-']);
        components = new int[parts.Length];
        for (int index = 0; index < parts.Length; index++)
        {
            if (!int.TryParse(parts[index], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out components[index]))
            {
                components = [];
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<string> EnumerateLines(string value) => value
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(line => !string.IsNullOrWhiteSpace(line));

    private sealed record BoundedText(string Text, bool Truncated)
    {
        public static BoundedText Empty { get; } = new(string.Empty, false);
    }
}
