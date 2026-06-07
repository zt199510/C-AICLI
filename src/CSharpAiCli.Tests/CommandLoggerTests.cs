using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class CommandLoggerTests
{
    [Fact]
    public void Append_writes_command_log_without_secret_values()
    {
        string tempRoot = CreateTempDirectory();

        try
        {
            string workspaceRoot = Path.Combine(tempRoot, "workspace");
            Directory.CreateDirectory(workspaceRoot);

            CliEnvironmentSnapshot snapshot = CreateSnapshot(
                workspaceRoot: workspaceRoot,
                workspaceStatus: WorkspaceStatus.Ready,
                userProfile: Path.Combine(tempRoot, "home"),
                model: "gpt-workspace",
                modelSource: "workspace config",
                apiKey: "sk-test-secret",
                apiKeySource: "OPENAI_API_KEY");

            CommandLogger.Append(
                "doctor",
                snapshot,
                new DateTimeOffset(2026, 6, 24, 8, 30, 0, TimeSpan.Zero));

            string logPath = Path.Combine(workspaceRoot, ".caicli", "logs", "2026-06-24.log");
            string log = File.ReadAllText(logPath);

            Assert.Contains("timestampUtc=2026-06-24T08:30:00.0000000Z", log);
            Assert.Contains("command=doctor", log);
            Assert.Contains($"workspace={workspaceRoot}", log);
            Assert.Contains("workspaceStatus=ready", log);
            Assert.Contains("model=gpt-workspace", log);
            Assert.Contains("modelSource=workspace config", log);
            Assert.Contains("apiKey=present", log);
            Assert.Contains("apiKeySource=OPENAI_API_KEY", log);
            Assert.Contains("instructionWarnings=none", log);
            Assert.DoesNotContain("sk-test-secret", log);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Append_uses_user_log_directory_when_workspace_is_missing()
    {
        string tempRoot = CreateTempDirectory();

        try
        {
            string userProfile = Path.Combine(tempRoot, "home");
            Directory.CreateDirectory(Path.Combine(userProfile, ".caicli"));

            CliEnvironmentSnapshot snapshot = CreateSnapshot(
                workspaceRoot: Path.Combine(tempRoot, "missing"),
                workspaceStatus: WorkspaceStatus.Missing,
                userProfile: userProfile);

            CommandLogger.Append(
                "config get",
                snapshot,
                new DateTimeOffset(2026, 6, 24, 9, 0, 0, TimeSpan.Zero));

            string logPath = Path.Combine(userProfile, ".caicli", "logs", "2026-06-24.log");
            string log = File.ReadAllText(logPath);

            Assert.Contains("command=config get", log);
            Assert.Contains("workspaceStatus=missing", log);
            Assert.Contains("apiKey=missing", log);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Append_sanitizes_line_breaks_and_pipe_characters()
    {
        string tempRoot = CreateTempDirectory();

        try
        {
            string workspaceRoot = Path.Combine(tempRoot, "workspace");
            Directory.CreateDirectory(workspaceRoot);

            CliEnvironmentSnapshot snapshot = CreateSnapshot(
                workspaceRoot: workspaceRoot,
                workspaceStatus: WorkspaceStatus.Ready,
                userProfile: Path.Combine(tempRoot, "home"),
                model: $"gpt{Environment.NewLine}workspace",
                modelSource: "workspace|config",
                warnings: [$"first warning{Environment.NewLine}second warning"]);

            CommandLogger.Append(
                "doctor|config",
                snapshot,
                new DateTimeOffset(2026, 6, 24, 10, 0, 0, TimeSpan.Zero));

            string logPath = Path.Combine(workspaceRoot, ".caicli", "logs", "2026-06-24.log");
            string log = File.ReadAllText(logPath);
            string[] nonEmptyLines = File.ReadAllLines(logPath)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToArray();

            Assert.Contains("command=doctor/config", log);
            Assert.Contains("model=gpt workspace", log);
            Assert.Contains("modelSource=workspace/config", log);
            Assert.Contains("warnings=first warning second warning", log);
            Assert.Single(nonEmptyLines);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static CliEnvironmentSnapshot CreateSnapshot(
        string workspaceRoot,
        WorkspaceStatus workspaceStatus,
        string userProfile,
        string model = "not configured",
        string modelSource = "default",
        string? apiKey = null,
        string apiKeySource = "missing",
        IReadOnlyList<string>? warnings = null)
    {
        WorkspaceContext workspace = new(
            RootPath: workspaceRoot,
            ConfigPath: Path.Combine(workspaceRoot, ".caicli", "config.json"),
            Status: workspaceStatus);

        EffectiveConfiguration configuration = new(
            WorkspaceRoot: workspaceRoot,
            UserConfigPath: Path.Combine(userProfile, ".caicli", "config.json"),
            WorkspaceConfigPath: Path.Combine(workspaceRoot, ".caicli", "config.json"),
            Model: model,
            ModelSource: modelSource,
            AgentBackend: "direct",
            AgentBackendSource: "default",
            DisabledTools: new HashSet<string>(StringComparer.Ordinal),
            ApiKey: SecretValue.From(apiKey),
            ApiKeySource: apiKeySource,
            LoadedConfigPaths: [],
            Warnings: warnings ?? [],
            ConfigSources: []);

        return new CliEnvironmentSnapshot(
            Workspace: workspace,
            Configuration: configuration,
            DotnetSdkVersion: "9.0.308",
            DotnetRuntime: ".NET 9.0.0",
            TargetFramework: "net9.0",
            HasGlobalJson: false);
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "caicli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
