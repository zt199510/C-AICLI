namespace CSharpAiCli.Core;

public sealed record EffectiveConfiguration(
    string WorkspaceRoot,
    string UserConfigPath,
    string WorkspaceConfigPath,
    string Model,
    string ModelSource,
    string AgentBackend,
    string AgentBackendSource,
    IReadOnlySet<string> DisabledTools,
    SecretValue? ApiKey,
    string ApiKeySource,
    IReadOnlyList<string> LoadedConfigPaths,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<CliConfigFileSource> ConfigSources)
{
    public string BaseUrl { get; init; } = ConfigLoader.DefaultOpenAiBaseUrl;

    public string BaseUrlSource { get; init; } = "default";

    public ApprovalMode ApprovalMode { get; init; } = ApprovalMode.OnRequest;

    public string ApprovalModeSource { get; init; } = "default";

    public ShellPolicyConfiguration ShellPolicy { get; init; } = ShellPolicyConfiguration.Default;

    public AgentRunLimits AgentRunLimits { get; init; } = AgentRunLimits.Default;

    public string AgentRunMaxStepsSource { get; init; } = "default";

    public string AgentRunMaxToolCallsSource { get; init; } = "default";

    public string AgentRunMaxRetriesSource { get; init; } = "default";

    public string AgentRunTimeoutSource { get; init; } = "default";

    public bool HasApiKey => ApiKey is not null;
}
