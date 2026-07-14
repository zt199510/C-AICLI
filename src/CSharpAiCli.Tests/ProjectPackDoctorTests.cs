using System.Diagnostics;
using CSharpAiCli.Core;
using CSharpAiCli.ProjectPacks;

namespace CSharpAiCli.Tests;

public sealed class ProjectPackDoctorTests
{
    [Fact]
    public void Static_inspection_returns_hash_without_exposing_path()
    {
        using TempDirectory temp = TempDirectory.Create();
        string executable = Path.Combine(temp.Path, "test-tool.exe");
        File.WriteAllText(executable, "tool-content");
        ExternalToolRequirement requirement = CreateRequirement("test-tool.exe");

        ExternalToolInspectionResult result = new ExternalToolPathInspector().Inspect(
            requirement,
            executable,
            "--tool-path");

        Assert.True(result.Succeeded, string.Join(" | ", result.Diagnostics.Select(diagnostic => diagnostic.Code + ": " + diagnostic.Summary)));
        Assert.NotNull(result.Identity);
        Assert.Equal("test-tool.exe", result.Identity.FileName);
        Assert.Equal(64, result.Identity.Sha256.Length);
        Assert.Equal(ExternalToolTrustStatus.Untrusted, result.Identity.TrustStatus);
        Assert.DoesNotContain(executable, System.Text.Json.JsonSerializer.Serialize(result.Identity), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Static_inspection_rejects_missing_directory_and_wrong_filename()
    {
        using TempDirectory temp = TempDirectory.Create();
        ExternalToolPathInspector inspector = new();
        ExternalToolRequirement requirement = CreateRequirement("test-tool.exe");
        string wrongName = Path.Combine(temp.Path, "wrong.exe");
        File.WriteAllText(wrongName, "tool-content");

        ExternalToolInspectionResult missing = inspector.Inspect(requirement, Path.Combine(temp.Path, "missing.exe"), "--tool-path");
        ExternalToolInspectionResult directory = inspector.Inspect(requirement, temp.Path, "--tool-path");
        ExternalToolInspectionResult wrong = inspector.Inspect(requirement, wrongName, "--tool-path");

        Assert.Equal(ExternalToolDiagnosticCode.PathNotFound, Assert.Single(missing.Diagnostics).Code);
        Assert.Equal(ExternalToolDiagnosticCode.PathDirectory, Assert.Single(directory.Diagnostics).Code);
        Assert.Equal(ExternalToolDiagnosticCode.FileNameDenied, Assert.Single(wrong.Diagnostics).Code);
    }

    [Fact]
    public void Static_inspection_reports_hash_change()
    {
        using TempDirectory temp = TempDirectory.Create();
        string executable = Path.Combine(temp.Path, "test-tool.exe");
        File.WriteAllText(executable, "tool-content");

        ExternalToolInspectionResult result = new ExternalToolPathInspector().Inspect(
            CreateRequirement("test-tool.exe"),
            executable,
            "user config",
            new string('0', 64));

        Assert.False(result.Succeeded);
        Assert.Equal(ExternalToolTrustStatus.HashChanged, result.Identity?.TrustStatus);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ExternalToolDiagnosticCode.HashChanged);
    }

    [Fact]
    public void Static_inspection_rejects_reparse_point_in_path()
    {
        using TempDirectory temp = TempDirectory.Create();
        string target = Path.Combine(temp.Path, "target");
        string link = Path.Combine(temp.Path, "linked");
        Directory.CreateDirectory(target);
        string executable = Path.Combine(target, "test-tool.exe");
        File.WriteAllText(executable, "tool-content");
        CreateDirectoryLink(link, target);

        try
        {
            ExternalToolInspectionResult result = new ExternalToolPathInspector().Inspect(
                CreateRequirement("test-tool.exe"),
                Path.Combine(link, "test-tool.exe"),
                "--tool-path");

            Assert.False(result.Succeeded);
            Assert.Equal(ExternalToolDiagnosticCode.PathReparsePoint, Assert.Single(result.Diagnostics).Code);
        }
        finally
        {
            if (Directory.Exists(link))
            {
                Directory.Delete(link);
            }
        }
    }

    [Fact]
    public void Static_doctor_does_not_request_approval_or_start_tool()
    {
        using TempDirectory temp = TempDirectory.Create();
        string executable = Path.Combine(temp.Path, "test-tool.exe");
        File.WriteAllText(executable, "not-an-executable");
        TestPack pack = CreatePack(CreateRequirement("test-tool.exe"));

        ProjectPackDoctorReport report = new ProjectPackDoctorService().Diagnose(
            pack,
            new Dictionary<string, string> { ["test-tool"] = executable },
            trustedHashes: null,
            probe: false,
            new ThrowingApprovalPolicy());

        Assert.True(report.Succeeded);
        Assert.Equal(ProjectPackDoctorStatus.StaticOk, report.Status);
        Assert.Null(Assert.Single(report.Tools).Probe);
    }

    [Fact]
    public void Probe_denial_uses_shell_risk_and_does_not_start_driver()
    {
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Test host path is unavailable.");
        RecordingApprovalPolicy approval = new(ApprovalDecision.Deny("Denied for test."));
        ExternalToolRequirement requirement = CreateRequirement(Path.GetFileName(executable));
        ExternalToolInspectionResult inspection = new ExternalToolPathInspector().Inspect(requirement, executable, "--tool-path");

        ExternalToolProbeResult result = new ExternalToolProbeRunner().Run(requirement, inspection, approval);

        Assert.False(result.Succeeded);
        Assert.Equal("approval-denied", result.Status);
        ApprovalRequest request = Assert.Single(approval.Requests);
        Assert.Equal(ToolRiskLevel.Shell, request.RiskLevel);
        Assert.DoesNotContain(executable, request.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(executable, string.Join(" ", request.Metadata?.Values ?? []), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Fake_driver_success_uses_typed_probe_and_returns_bounded_protocol_json()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        ExternalToolRequirement requirement = CreateFakeDriverRequirement("success", timeoutMilliseconds: 5_000);
        string powershell = GetPowerShellExecutable();
        ExternalToolInspectionResult inspection = new ExternalToolPathInspector().Inspect(requirement, powershell, "test fixture");

        ExternalToolProbeResult result = new ExternalToolProbeRunner().Run(
            requirement,
            inspection,
            new AlwaysApproveApprovalPolicy());

        Assert.True(result.Succeeded, string.Join(" | ", result.Diagnostics.Select(diagnostic => diagnostic.Code + ": " + diagnostic.Summary)));
        Assert.Equal(0, result.ExitCode);
        Assert.True(result.ProcessCleanedUp);
        FakeProjectPackToolResult protocol = FakeProjectPackToolProtocol.Parse(result.Stdout);
        Assert.True(protocol.Succeeded);
    }

    [Fact]
    public void Probe_rejects_version_below_manifest_minimum()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        ExternalToolRequirement requirement = CreateFakeDriverRequirement(
            "success",
            timeoutMilliseconds: 5_000,
            minimumVersion: "2");
        string powershell = GetPowerShellExecutable();
        ExternalToolInspectionResult inspection = new ExternalToolPathInspector().Inspect(requirement, powershell, "test fixture");

        ExternalToolProbeResult result = new ExternalToolProbeRunner().Run(
            requirement,
            inspection,
            new AlwaysApproveApprovalPolicy());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ExternalToolDiagnosticCode.VersionUnsupported);
    }

    [Fact]
    public void Fake_driver_timeout_kills_process_and_leaves_no_live_pid()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TempDirectory temp = TempDirectory.Create();
        string pidPath = Path.Combine(temp.Path, "driver.pid");
        ExternalToolRequirement requirement = CreateFakeDriverRequirement("timeout", 1_500, pidPath);
        string powershell = GetPowerShellExecutable();
        ExternalToolInspectionResult inspection = new ExternalToolPathInspector().Inspect(requirement, powershell, "test fixture");

        ExternalToolProbeResult result = new ExternalToolProbeRunner().Run(
            requirement,
            inspection,
            new AlwaysApproveApprovalPolicy());

        Assert.True(result.TimedOut);
        Assert.False(result.Succeeded);
        Assert.True(result.ProcessCleanedUp);
        AssertProcessExited(pidPath);
    }

    [Fact]
    public void Fake_driver_cancellation_kills_process_and_leaves_no_live_pid()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TempDirectory temp = TempDirectory.Create();
        string pidPath = Path.Combine(temp.Path, "driver.pid");
        ExternalToolRequirement requirement = CreateFakeDriverRequirement("timeout", 5_000, pidPath);
        string powershell = GetPowerShellExecutable();
        ExternalToolInspectionResult inspection = new ExternalToolPathInspector().Inspect(requirement, powershell, "test fixture");
        using CancellationTokenSource cancellation = new(1_500);

        ExternalToolProbeResult result = new ExternalToolProbeRunner().Run(
            requirement,
            inspection,
            new AlwaysApproveApprovalPolicy(),
            cancellation.Token);

        Assert.True(result.Canceled);
        Assert.False(result.Succeeded);
        Assert.True(result.ProcessCleanedUp);
        AssertProcessExited(pidPath);
    }

    private static ExternalToolRequirement CreateRequirement(string executableFileName)
    {
        return new ExternalToolRequirement(
            "test-tool",
            "Test Tool",
            "1.0.0",
            "Test license",
            false,
            "test-tool",
            [executableFileName],
            ["--version"]);
    }

    private static ExternalToolRequirement CreateFakeDriverRequirement(
        string mode,
        int timeoutMilliseconds,
        string? pidPath = null,
        string minimumVersion = "1")
    {
        List<string> arguments =
        [
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            FakeDriverPath,
            "-Mode",
            mode
        ];
        if (pidPath is not null)
        {
            arguments.Add("-PidPath");
            arguments.Add(pidPath);
        }

        return new ExternalToolRequirement(
            "fake-driver",
            "Fake Project Pack Tool",
            minimumVersion,
            "test-only",
            false,
            "fake-driver",
            [Path.GetFileName(GetPowerShellExecutable())],
            arguments,
            ProbeTimeoutMilliseconds: timeoutMilliseconds,
            MaxProbeOutputCharacters: 4_096,
            VersionOutputMarker: "\"protocolVersion\"");
    }

    private static TestPack CreatePack(ExternalToolRequirement requirement)
    {
        ProjectPackManifest manifest = new(
            ProjectPackSchema.CurrentVersion,
            "test-pack",
            "Test Pack",
            "1.0.0",
            "Test pack.",
            [new ProjectPackCapability("probe", "Probe tool.")],
            [requirement],
            [],
            []);
        return new TestPack(manifest);
    }

    private static string FakeDriverPath => Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "GerberTiff",
        "fake",
        "fake-driver.ps1");

    private static string GetPowerShellExecutable()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
    }

    private static void AssertProcessExited(string pidPath)
    {
        Assert.True(File.Exists(pidPath));
        int pid = int.Parse(File.ReadAllText(pidPath), System.Globalization.CultureInfo.InvariantCulture);
        Assert.Throws<ArgumentException>(() => Process.GetProcessById(pid));
    }

    private static void CreateDirectoryLink(string link, string target)
    {
        if (OperatingSystem.IsWindows())
        {
            using Process process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            process.StartInfo.ArgumentList.Add("/d");
            process.StartInfo.ArgumentList.Add("/c");
            process.StartInfo.ArgumentList.Add("mklink");
            process.StartInfo.ArgumentList.Add("/J");
            process.StartInfo.ArgumentList.Add(link);
            process.StartInfo.ArgumentList.Add(target);
            process.Start();
            Assert.True(process.WaitForExit(5_000));
            Assert.Equal(0, process.ExitCode);
            return;
        }

        Directory.CreateSymbolicLink(link, target);
    }

    private sealed class TestPack(ProjectPackManifest manifest) : IProjectPack
    {
        public ProjectPackManifest Manifest { get; } = manifest;
    }

    private sealed class ThrowingApprovalPolicy : IApprovalPolicy
    {
        public ApprovalDecision RequestApproval(ApprovalRequest request) =>
            throw new InvalidOperationException("Static doctor must not request approval.");
    }

    private sealed class RecordingApprovalPolicy(ApprovalDecision decision) : IApprovalPolicy
    {
        public List<ApprovalRequest> Requests { get; } = [];

        public ApprovalDecision RequestApproval(ApprovalRequest request)
        {
            Requests.Add(request);
            return decision;
        }
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
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "caicli-pack-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
