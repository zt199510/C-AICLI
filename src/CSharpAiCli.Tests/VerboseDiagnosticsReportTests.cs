using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class VerboseDiagnosticsReportTests
{
    [Fact]
    public void Create_replaces_control_characters_in_safe_fields()
    {
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            model: "gpt\tmodel\u001b[2J\u0007end",
            baseUrl: "https://gateway.example.test/v1\u001b[2J\tbeta",
            userConfigPath: "user\tconfig\u001b[2J.json");
        DiagnosticContext context = new(
            CommandId: "cmd\t001",
            SessionId: "session\u001b[2Jabc",
            Workspace: "workspace\troot\u0007",
            TimestampUtc: DateTimeOffset.Parse("2024-01-01T00:00:00Z"));

        string text = VerboseDiagnosticsReport
            .Create("config\tlist\u001b[2J", snapshot, context)
            .ToDisplayText();

        Assert.DoesNotContain('\t', text);
        Assert.DoesNotContain('\u001b', text);
        Assert.DoesNotContain('\u0007', text);
        Assert.Contains("commandName: config list [2J", text);
        Assert.Contains("commandId: cmd 001", text);
        Assert.Contains("sessionId: session [2Jabc", text);
        Assert.Contains("workspace: workspace root", text);
        Assert.Contains("model: gpt model [2J end", text);
        Assert.Contains("baseUrl: https://gateway.example.test/v1 [2J beta", text);
        Assert.Contains("userConfigPath: user config [2J.json", text);
    }

    [Fact]
    public void Create_redacts_quoted_secret_keys_and_github_pat_tokens()
    {
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            model: """{"apiKey":"plain-secret-value","clientSecret":"another-secret"}""",
            baseUrl: "https://gateway.example.test/v1?token=github_pat_1234567890abcdef",
            apiKeySource: "github_pat_source");
        DiagnosticContext context = new(
            CommandId: "cmd-001",
            SessionId: "session-abc",
            Workspace: "workspace-root",
            TimestampUtc: DateTimeOffset.Parse("2024-01-01T00:00:00Z"));

        string text = VerboseDiagnosticsReport
            .Create("models", snapshot, context)
            .ToDisplayText();

        Assert.DoesNotContain("plain-secret-value", text, StringComparison.Ordinal);
        Assert.DoesNotContain("another-secret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("github_pat_", text, StringComparison.Ordinal);
        Assert.Contains("[redacted]", text);
        Assert.Contains("apiKey: missing", text);
    }

    private static CliEnvironmentSnapshot CreateSnapshot(
        string model = "gpt-test",
        string baseUrl = "https://api.openai.com/v1",
        string apiKeySource = "missing",
        string userConfigPath = "user-home/.caicli/config.json")
    {
        WorkspaceContext workspace = new(
            RootPath: "workspace-root",
            ConfigPath: "workspace-root/.caicli/config.json",
            Status: WorkspaceStatus.Ready);

        EffectiveConfiguration configuration = new(
            WorkspaceRoot: workspace.RootPath,
            UserConfigPath: userConfigPath,
            WorkspaceConfigPath: workspace.ConfigPath,
            Model: model,
            ModelSource: "workspace config",
            AgentBackend: "direct",
            AgentBackendSource: "default",
            DisabledTools: new HashSet<string>(StringComparer.Ordinal),
            ApiKey: null,
            ApiKeySource: apiKeySource,
            LoadedConfigPaths: [],
            Warnings: [],
            ConfigSources: [])
        {
            BaseUrl = baseUrl,
            BaseUrlSource = "workspace config"
        };

        return new CliEnvironmentSnapshot(
            Workspace: workspace,
            Configuration: configuration,
            DotnetSdkVersion: "9.0.308",
            DotnetRuntime: ".NET 9.0.0",
            TargetFramework: "net9.0",
            HasGlobalJson: false);
    }
}
