using System.CommandLine;
using System.Text.Json.Nodes;
using CSharpAiCli.Cli;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class CiCliTests
{
    [Fact]
    public void Summarize_and_check_read_jobs_without_model_tools_or_command_log()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspace = CreateWorkspace(temp.Path);
        string userConfigPath = CreateUserConfigPath(temp.Path);
        CliEnvironmentSnapshot snapshot = CreateSnapshot(workspace, userConfigPath);
        JobTaskReportSummary taskReport = new(
            "success", "completed", "done", null, 0, 0, 1, 1, 0, 0,
            Risks: ["manual review remains"]);
        JobRecord record = CiArtifactTests.CreateRecord(JobStatus.Succeeded, 0, taskReport: taskReport);
        JobRecordStore.Create(snapshot).Create(record);
        List<string> loggedCommands = [];

        using StringWriter summaryOutput = new();
        int summaryExitCode = CliCommandFactory.Invoke(
            CreateCommand(summaryOutput, userConfigPath, loggedCommands),
            ["ci", "summarize", "--job", record.JobId, "--output", "json", "--workspace", workspace],
            summaryOutput);
        JsonObject summary = Assert.IsType<JsonObject>(JsonNode.Parse(summaryOutput.ToString()));

        using StringWriter checkOutput = new();
        int defaultCheckExitCode = CliCommandFactory.Invoke(
            CreateCommand(checkOutput, userConfigPath, loggedCommands),
            ["ci", "check", "--job", record.JobId, "--workspace", workspace],
            checkOutput);
        using StringWriter strictCheckOutput = new();
        int strictCheckExitCode = CliCommandFactory.Invoke(
            CreateCommand(strictCheckOutput, userConfigPath, loggedCommands),
            ["ci", "check", "--job", record.JobId, "--fail-on", "risks", "--workspace", workspace],
            strictCheckOutput);

        Assert.Equal(0, summaryExitCode);
        Assert.Equal(CiArtifactType.Summary, summary["type"]?.GetValue<string>());
        Assert.Equal(CiCheckOutcome.Warning, summary["check"]?["outcome"]?.GetValue<string>());
        Assert.Equal(0, defaultCheckExitCode);
        Assert.Equal(1, strictCheckExitCode);
        Assert.Contains("\"recommendedExitCode\":1", strictCheckOutput.ToString(), StringComparison.Ordinal);
        Assert.Contains("failOn=risks", strictCheckOutput.ToString(), StringComparison.Ordinal);
        Assert.Empty(loggedCommands);
        Assert.False(Directory.Exists(Path.Combine(workspace, ".caicli", "logs")));
    }

    [Fact]
    public void Check_returns_failure_and_config_error_codes_deterministically()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspace = CreateWorkspace(temp.Path);
        string userConfigPath = CreateUserConfigPath(temp.Path);
        CliEnvironmentSnapshot snapshot = CreateSnapshot(workspace, userConfigPath);
        JobRecord failed = CiArtifactTests.CreateRecord(JobStatus.Failed, 1);
        JobRecordStore.Create(snapshot).Create(failed);

        using StringWriter failedOutput = new();
        int failedExitCode = CliCommandFactory.Invoke(
            CreateCommand(failedOutput, userConfigPath),
            ["ci", "check", "--job", failed.JobId, "--workspace", workspace],
            failedOutput);
        using StringWriter missingOutput = new();
        int missingExitCode = CliCommandFactory.Invoke(
            CreateCommand(missingOutput, userConfigPath),
            ["ci", "check", "--job", "job_20260713T000000000Z_00000000", "--workspace", workspace],
            missingOutput);
        JsonObject missing = Assert.IsType<JsonObject>(JsonNode.Parse(missingOutput.ToString()));

        Assert.Equal(1, failedExitCode);
        Assert.Contains("\"outcome\":\"failure\"", failedOutput.ToString(), StringComparison.Ordinal);
        Assert.Equal(2, missingExitCode);
        Assert.Equal("caicli.ci.error", missing["type"]?.GetValue<string>());
        Assert.Equal(CiCheckOutcome.ConfigError, missing["outcome"]?.GetValue<string>());
    }

    [Fact]
    public void Markdown_path_is_explicit_workspace_guarded_and_never_overwrites()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspace = CreateWorkspace(temp.Path);
        string userConfigPath = CreateUserConfigPath(temp.Path);
        JobRecord record = CiArtifactTests.CreateRecord(JobStatus.Succeeded, 0);
        JobRecordStore.Create(CreateSnapshot(workspace, userConfigPath)).Create(record);
        string relativePath = Path.Combine(".caicli", "reports", "ci-summary.md");
        string expectedPath = Path.Combine(workspace, relativePath);

        using StringWriter output = new();
        int firstExitCode = CliCommandFactory.Invoke(
            CreateCommand(output, userConfigPath),
            ["ci", "summarize", "--job", record.JobId, "--output", "markdown", "--markdown-path", relativePath, "--workspace", workspace],
            output);
        using StringWriter overwriteOutput = new();
        int overwriteExitCode = CliCommandFactory.Invoke(
            CreateCommand(overwriteOutput, userConfigPath),
            ["ci", "summarize", "--job", record.JobId, "--markdown-path", relativePath, "--workspace", workspace],
            overwriteOutput);
        string outsidePath = Path.Combine(temp.Path, "outside.md");
        using StringWriter outsideOutput = new();
        int outsideExitCode = CliCommandFactory.Invoke(
            CreateCommand(outsideOutput, userConfigPath),
            ["ci", "summarize", "--job", record.JobId, "--markdown-path", outsidePath, "--workspace", workspace],
            outsideOutput);

        Assert.Equal(0, firstExitCode);
        Assert.True(File.Exists(expectedPath));
        Assert.Contains("# C-AICLI CI summary", File.ReadAllText(expectedPath), StringComparison.Ordinal);
        Assert.Equal(2, overwriteExitCode);
        Assert.Contains("report-path-exists", overwriteOutput.ToString(), StringComparison.Ordinal);
        Assert.Equal(2, outsideExitCode);
        Assert.False(File.Exists(outsidePath));
        Assert.Contains(ToolErrorCode.WorkspaceBoundaryDenied, outsideOutput.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Summarize_returns_config_error_for_unsafe_source_boundary()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspace = CreateWorkspace(temp.Path);
        string userConfigPath = CreateUserConfigPath(temp.Path);
        JobRecord record = CiArtifactTests.CreateRecord(
            JobStatus.Succeeded,
            0,
            redaction: new JobRedactionSummary(false, true, true, true, "unsafe"));
        JobRecordStore.Create(CreateSnapshot(workspace, userConfigPath)).Create(record);
        using StringWriter output = new();

        int exitCode = CliCommandFactory.Invoke(
            CreateCommand(output, userConfigPath),
            ["ci", "summarize", "--job", record.JobId, "--workspace", workspace],
            output);

        Assert.Equal(2, exitCode);
        Assert.Contains("\"outcome\":\"config-error\"", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("\"artifacts\":[]", output.ToString(), StringComparison.Ordinal);
    }

    private static RootCommand CreateCommand(
        StringWriter output,
        string userConfigPath,
        List<string>? loggedCommands = null)
    {
        return CliCommandFactory.Create(
            output,
            workspacePath => CreateSnapshot(workspacePath, userConfigPath),
            (commandName, _) => loggedCommands?.Add(commandName),
            _ => throw new InvalidOperationException("CI commands must not create a model client"),
            writer => new TerminalChatStreamingRenderer(writer),
            _ => throw new InvalidOperationException("CI commands must not create a conversation store"),
            () => DateTimeOffset.Parse("2026-07-13T00:00:00Z"),
            (_, _, _) => throw new InvalidOperationException("CI commands must not create an agent runner"));
    }

    private static CliEnvironmentSnapshot CreateSnapshot(string? workspacePath, string userConfigPath)
    {
        string workspaceRoot = string.IsNullOrWhiteSpace(workspacePath) ? "workspace-root" : workspacePath;
        WorkspaceContext workspace = new(
            workspaceRoot,
            Path.Combine(workspaceRoot, ".caicli", "config.json"),
            WorkspaceStatus.Ready);
        EffectiveConfiguration configuration = new(
            WorkspaceRoot: workspaceRoot,
            UserConfigPath: userConfigPath,
            WorkspaceConfigPath: Path.Combine(workspaceRoot, ".caicli", "config.json"),
            Model: "not configured",
            ModelSource: "default",
            AgentBackend: "direct",
            AgentBackendSource: "default",
            DisabledTools: new HashSet<string>(StringComparer.Ordinal),
            ApiKey: SecretValue.From(null),
            ApiKeySource: "missing",
            LoadedConfigPaths: [],
            Warnings: [],
            ConfigSources: []);
        return new CliEnvironmentSnapshot(
            workspace,
            configuration,
            "9.0.308",
            ".NET 9.0.0",
            "net9.0",
            false);
    }

    private static string CreateWorkspace(string root)
    {
        string workspace = Path.Combine(root, "workspace");
        Directory.CreateDirectory(workspace);
        return workspace;
    }

    private static string CreateUserConfigPath(string root) =>
        Path.Combine(root, "user", ".caicli", "config.json");

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;

        public string Path { get; }

        public static TempDirectory Create()
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "caicli-ci-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
