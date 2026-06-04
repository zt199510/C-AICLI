namespace CSharpAiCli.Core;

public sealed record EffectiveConfiguration(
    string WorkspaceRoot,
    string UserConfigPath,
    string WorkspaceConfigPath,
    string Model,
    string ModelSource,
    SecretValue? ApiKey,
    string ApiKeySource,
    IReadOnlyList<string> LoadedConfigPaths,
    IReadOnlyList<string> Warnings)
{
    public bool HasApiKey => ApiKey is not null;
}
