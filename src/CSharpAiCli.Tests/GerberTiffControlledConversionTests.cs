using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Security.Cryptography;
using CSharpAiCli.Core;
using CSharpAiCli.ProjectPacks;
using CSharpAiCli.ProjectPacks.Runtime;

namespace CSharpAiCli.Tests;

public sealed class GerberTiffControlledConversionTests
{
    [Fact]
    public void ArgumentList_preserves_spaces_unicode_and_special_characters_as_one_value()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using ProcessTestDirectory test = ProcessTestDirectory.Create("space 中文 & value");
        string expected = "one value with spaces 中文 & | ; ` $()";
        FrozenProcessInvocation invocation = test.CreateInvocation("success", value: expected);

        BoundedProcessResult result = new BoundedExternalProcessRunner().Run(invocation);

        Assert.True(result.Succeeded, result.Summary + " " + result.Stderr);
        Assert.Equal(expected, File.ReadAllText(invocation.DeclaredOutputPath));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void Approval_denial_captures_exact_risk_and_does_not_start_process()
    {
        using ProcessTestDirectory test = ProcessTestDirectory.Create("approval-denied");
        string executable = Path.Combine(test.RootPath, "fake.exe");
        File.WriteAllText(executable, "not-started");
        FrozenProcessInvocation invocation = test.CreateInvocation("success", executablePath: executable);
        ExternalToolRequirement requirement = Requirement("fake.exe");
        ExternalToolIdentity identity = Inspect(requirement, executable) with { Version = "1.0.0" };
        RecordingApprovalPolicy approval = new(ApprovalDecision.Deny("Denied for controlled test."));

        GerberTiffProcessExecutionResult result = new GerberTiffTypedProcessAdapter().ExecuteFrozen(
            requirement,
            identity,
            invocation,
            approval);

        Assert.Equal(ProjectPackRunErrorCode.ApprovalRequired, result.ErrorCode);
        Assert.False(File.Exists(invocation.DeclaredOutputPath));
        ApprovalRequest request = Assert.Single(approval.Requests);
        Assert.Equal(ToolRiskLevel.Shell, request.RiskLevel);
        Assert.Equal(Path.GetFullPath(executable), request.Metadata?["toolPath"]);
        Assert.Equal(identity.Sha256, request.Metadata?["toolSha256"]);
        Assert.Equal("1.0.0", request.Metadata?["toolVersion"]);
        Assert.Equal(Path.GetFullPath(test.InputPath), request.Metadata?["inputPath"]);
        Assert.Equal(test.InputSha256, request.Metadata?["inputSha256"]);
        Assert.Equal(Path.GetFullPath(invocation.DeclaredOutputPath), request.Metadata?["outputPath"]);
        Assert.Equal(invocation.TimeoutMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            request.Metadata?["timeoutMilliseconds"]);
        Assert.Equal(invocation.ArgumentTemplate, request.Metadata?["argumentTemplate"]);
    }

    [Fact]
    public void Tool_swap_after_approval_fails_closed_before_process_start()
    {
        using ProcessTestDirectory test = ProcessTestDirectory.Create("tool-swap");
        string executable = Path.Combine(test.RootPath, "fake.exe");
        File.WriteAllText(executable, "before");
        FrozenProcessInvocation invocation = test.CreateInvocation("success", executablePath: executable);
        ExternalToolRequirement requirement = Requirement("fake.exe");
        ExternalToolIdentity identity = Inspect(requirement, executable) with { Version = "1.0.0" };
        MutatingApprovalPolicy approval = new(executable);

        GerberTiffProcessExecutionResult result = new GerberTiffTypedProcessAdapter().ExecuteFrozen(
            requirement,
            identity,
            invocation,
            approval);

        Assert.Equal(ProjectPackRunErrorCode.ToolIdentityChanged, result.ErrorCode);
        Assert.False(File.Exists(invocation.DeclaredOutputPath));
        Assert.Equal(1, approval.RequestCount);
    }

    [Theory]
    [InlineData("missing", ProjectPackRunErrorCode.PartialOutput)]
    [InlineData("unexpected", ProjectPackRunErrorCode.OutputBoundaryViolation)]
    [InlineData("oversized", ProjectPackRunErrorCode.OutputLimitExceeded)]
    [InlineData("secret-stderr", ProjectPackRunErrorCode.PartialOutput)]
    public void Failure_and_malicious_output_modes_have_stable_codes(string mode, string expectedErrorCode)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using ProcessTestDirectory test = ProcessTestDirectory.Create(mode);
        FrozenProcessInvocation invocation = test.CreateInvocation(
            mode,
            maxOutputBytes: mode == "oversized" ? 32 : 1024 * 1024);

        BoundedProcessResult result = new BoundedExternalProcessRunner().Run(invocation);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedErrorCode, result.ErrorCode);
        if (mode == "secret-stderr")
        {
            Assert.Contains("[redacted]", result.Stderr, StringComparison.Ordinal);
            Assert.DoesNotContain("super-secret-value", result.Stderr, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Timeout_and_cancel_kill_the_process_without_leaving_parent_pid()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using ProcessTestDirectory timeoutTest = ProcessTestDirectory.Create("timeout");
        string timeoutPid = Path.Combine(timeoutTest.WorkingPath, "timeout.pid");
        FrozenProcessInvocation timeoutInvocation = timeoutTest.CreateInvocation(
            "hang",
            timeoutMilliseconds: 5_000,
            pidPath: timeoutPid);
        BoundedProcessResult timeout = new BoundedExternalProcessRunner().Run(timeoutInvocation);

        Assert.True(timeout.TimedOut);
        Assert.True(timeout.ProcessCleanedUp);
        Assert.Equal(ProjectPackRunErrorCode.ExecutionTimeout, timeout.ErrorCode);
        AssertProcessExited(timeoutPid);

        using ProcessTestDirectory cancelTest = ProcessTestDirectory.Create("cancel");
        string cancelPid = Path.Combine(cancelTest.WorkingPath, "cancel.pid");
        FrozenProcessInvocation cancelInvocation = cancelTest.CreateInvocation(
            "hang",
            timeoutMilliseconds: 10_000,
            pidPath: cancelPid);
        using CancellationTokenSource cancellation = new();
        Task<BoundedProcessResult> cancelTask = Task.Run(() =>
            new BoundedExternalProcessRunner().Run(cancelInvocation, cancellation.Token));
        Stopwatch wait = Stopwatch.StartNew();
        while (!File.Exists(cancelPid) && wait.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(25);
        }

        Assert.True(File.Exists(cancelPid), "The controlled child must start before cancellation is requested.");
        cancellation.Cancel();
        BoundedProcessResult canceled = await cancelTask;

        Assert.True(canceled.Canceled);
        Assert.True(canceled.ProcessCleanedUp);
        Assert.Equal(ProjectPackRunErrorCode.ExecutionCanceled, canceled.ErrorCode);
        AssertProcessExited(cancelPid);
    }

    [Fact]
    public void Detached_child_is_detected_cleaned_and_reported_as_interrupted()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using ProcessTestDirectory test = ProcessTestDirectory.Create("child");
        string childPid = Path.Combine(test.WorkingPath, "child.pid");
        FrozenProcessInvocation invocation = test.CreateInvocation("child", childPidPath: childPid);

        BoundedProcessResult result = new BoundedExternalProcessRunner().Run(invocation);

        Assert.True(result.ResidualProcessDetected);
        Assert.True(result.ProcessCleanedUp);
        Assert.Equal(ProjectPackStageStatus.Interrupted, result.Status);
        Assert.Equal(ProjectPackRunErrorCode.ResidualProcessDetected, result.ErrorCode);
        AssertProcessExited(childPid);
    }

    [Fact]
    public void Outside_declared_output_is_rejected_before_malicious_driver_starts()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using ProcessTestDirectory test = ProcessTestDirectory.Create("outside-boundary");
        string outside = Path.Combine(test.RootPath, "outside", "result.bin");
        FrozenProcessInvocation safe = test.CreateInvocation("success");
        FrozenProcessInvocation malicious = safe with
        {
            DeclaredOutputRoot = Path.GetDirectoryName(outside)!,
            DeclaredOutputPath = outside,
            AllowedOutputPaths = [outside]
        };

        BoundedProcessResult result = new BoundedExternalProcessRunner().Run(malicious);

        Assert.Equal(ProjectPackRunErrorCode.OutputBoundaryViolation, result.ErrorCode);
        Assert.False(File.Exists(outside));
    }

    private static ExternalToolRequirement Requirement(string fileName) => new(
        "test-tool",
        "Test Tool",
        "1.0.0",
        "test-only",
        false,
        "test-tool",
        [fileName],
        ["--version"]);

    private static ExternalToolIdentity Inspect(ExternalToolRequirement requirement, string executable)
    {
        ExternalToolInspectionResult result = new ExternalToolPathInspector().Inspect(
            requirement,
            executable,
            "test fixture");
        Assert.True(result.Succeeded, string.Join(" | ", result.Diagnostics.Select(item => item.Code)));
        return Assert.IsType<ExternalToolIdentity>(result.Identity);
    }

    private static void AssertProcessExited(string pidPath)
    {
        Assert.True(File.Exists(pidPath));
        int pid = int.Parse(File.ReadAllText(pidPath), System.Globalization.CultureInfo.InvariantCulture);
        Assert.Throws<ArgumentException>(() => Process.GetProcessById(pid));
    }

    private static string PowerShellPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        "WindowsPowerShell",
        "v1.0",
        "powershell.exe");

    private static string DriverPath => Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "GerberTiff",
        "fake",
        "controlled-process-driver.ps1");

    private sealed class RecordingApprovalPolicy(ApprovalDecision decision) : IApprovalPolicy
    {
        public List<ApprovalRequest> Requests { get; } = [];

        public ApprovalDecision RequestApproval(ApprovalRequest request)
        {
            Requests.Add(request);
            return decision;
        }
    }

    private sealed class MutatingApprovalPolicy(string executablePath) : IApprovalPolicy
    {
        public int RequestCount { get; private set; }

        public ApprovalDecision RequestApproval(ApprovalRequest request)
        {
            RequestCount++;
            File.AppendAllText(executablePath, "changed-after-approval");
            return ApprovalDecision.Approve("Approved before controlled mutation.");
        }
    }

    private sealed class ProcessTestDirectory : IDisposable
    {
        private ProcessTestDirectory(string rootPath, string managedRoot, string workingPath, string inputPath)
        {
            RootPath = rootPath;
            ManagedRoot = managedRoot;
            WorkingPath = workingPath;
            InputPath = inputPath;
            InputSha256 = Hash(inputPath);
        }

        public string RootPath { get; }
        public string ManagedRoot { get; }
        public string WorkingPath { get; }
        public string InputPath { get; }
        public string InputSha256 { get; }

        public static ProcessTestDirectory Create(string name)
        {
            string root = Path.Combine(Path.GetTempPath(), "caicli-controlled-process-" + Guid.NewGuid().ToString("N"));
            string managed = Path.Combine(root, "managed " + name);
            string working = Path.Combine(managed, "working");
            Directory.CreateDirectory(working);
            string input = Path.Combine(managed, "staging", "input 中文 & value.gbr");
            Directory.CreateDirectory(Path.GetDirectoryName(input)!);
            File.WriteAllText(input, "controlled-input");
            return new ProcessTestDirectory(root, managed, working, input);
        }

        public FrozenProcessInvocation CreateInvocation(
            string mode,
            string? value = null,
            int timeoutMilliseconds = 5_000,
            long maxOutputBytes = 1024 * 1024,
            string? pidPath = null,
            string? childPidPath = null,
            string? executablePath = null)
        {
            string outputRoot = Path.Combine(ManagedRoot, "artifacts", "output 中文 & value");
            string output = Path.Combine(outputRoot, "result 中文 & value.bin");
            List<string> arguments =
            [
                "-NoLogo",
                "-NoProfile",
                "-NonInteractive",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                DriverPath,
                "-Mode",
                mode,
                "-OutputPath",
                output
            ];
            if (value is not null)
            {
                arguments.Add("-Value");
                arguments.Add(value);
            }

            if (pidPath is not null)
            {
                arguments.Add("-PidPath");
                arguments.Add(pidPath);
            }

            if (childPidPath is not null)
            {
                arguments.Add("-ChildPidPath");
                arguments.Add(childPidPath);
            }

            return new FrozenProcessInvocation(
                "fake",
                "render",
                "test.process",
                executablePath ?? PowerShellPath,
                arguments,
                WorkingPath,
                Path.Combine(WorkingPath, "temp"),
                ManagedRoot,
                ManagedRoot,
                outputRoot,
                output,
                [output],
                true,
                [new FrozenProcessInput(InputPath, InputSha256)],
                timeoutMilliseconds,
                maxOutputBytes,
                4096,
                new ReadOnlyDictionary<string, string>(new Dictionary<string, string>()),
                "no-overwrite",
                "test-only-typed-template");
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(RootPath))
                {
                    Directory.Delete(RootPath, recursive: true);
                }
            }
            catch
            {
            }
        }

        private static string Hash(string path)
        {
            using FileStream stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
    }
}
