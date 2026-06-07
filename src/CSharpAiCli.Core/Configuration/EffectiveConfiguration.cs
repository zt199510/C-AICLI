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
    public bool HasApiKey => ApiKey is not null;
}
