using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ChatUnavailableReportTests
{
    [Fact]
    public void Create_explains_phase_02_boundary_without_printing_secret_values()
    {
        CliEnvironmentSnapshot snapshot = CreateSnapshot(apiKey: "sk-test-secret");

        string text = ChatUnavailableReport.Create(snapshot).ToDisplayText();

        Assert.Contains("C# AI CLI chat", text);
        Assert.Contains("status: unavailable in Phase 01", text);
        Assert.Contains("planned phase: Phase 02", text);
        Assert.Contains("workspace: workspace-root", text);
        Assert.Contains("workspace status: ready", text);
        Assert.Contains("next: run doctor and config get to verify readiness before Phase 02", text);
        Assert.DoesNotContain("sk-test-secret", text, StringComparison.Ordinal);
    }

    private static CliEnvironmentSnapshot CreateSnapshot(string? apiKey)
    {
        WorkspaceContext workspace = new(
            RootPath: "workspace-root",
            ConfigPath: Path.Combine("workspace-root", ".caicli", "config.json"),
            Status: WorkspaceStatus.Ready);

        EffectiveConfiguration configuration = new(
            WorkspaceRoot: "workspace-root",
            UserConfigPath: Path.Combine("user-home", ".caicli", "config.json"),
            WorkspaceConfigPath: Path.Combine("workspace-root", ".caicli", "config.json"),
            Model: "not configured",
            ModelSource: "default",
            AgentBackend: "direct",
            AgentBackendSource: "default",
            DisabledTools: new HashSet<string>(StringComparer.Ordinal),
            ApiKey: SecretValue.From(apiKey),
            ApiKeySource: apiKey is null ? "missing" : "OPENAI_API_KEY",
            LoadedConfigPaths: [],
            Warnings: [],
            ConfigSources: []);

        return new CliEnvironmentSnapshot(
            Workspace: workspace,
            Configuration: configuration,
            DotnetSdkVersion: "9.0.308",
            DotnetRuntime: ".NET 9.0.0",
            TargetFramework: "net9.0",
            HasGlobalJson: false);
    }
}
