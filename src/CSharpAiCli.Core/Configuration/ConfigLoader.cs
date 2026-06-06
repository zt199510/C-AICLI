using System.Text.Json;

namespace CSharpAiCli.Core;

public static class ConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static EffectiveConfiguration Load(
        WorkspaceContext workspace,
        string? userProfile = null,
        string? openAiApiKey = null,
        string? openAiModel = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        userProfile ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        openAiApiKey ??= Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        openAiModel ??= Environment.GetEnvironmentVariable("OPENAI_MODEL");

        string userConfigPath = Path.Combine(userProfile, ".caicli", "config.json");
        string workspaceConfigPath = workspace.ConfigPath;

        List<string> loadedConfigPaths = [];
        List<string> warnings = [];

        CliConfigFile? userConfig = ReadConfig(userConfigPath, loadedConfigPaths, warnings);
        CliConfigFile? workspaceConfig = workspace.IsUsable
            ? ReadConfig(workspaceConfigPath, loadedConfigPaths, warnings)
            : null;

        AddWorkspaceApiKeyWarning(workspaceConfig, workspaceConfigPath, warnings);

        (string model, string modelSource) = SelectModel(openAiModel, userConfig, workspaceConfig);
        (SecretValue? apiKey, string apiKeySource) = SelectApiKey(openAiApiKey, userConfig);

        return new EffectiveConfiguration(
            WorkspaceRoot: workspace.RootPath,
            UserConfigPath: userConfigPath,
            WorkspaceConfigPath: workspaceConfigPath,
            Model: model,
            ModelSource: modelSource,
            ApiKey: apiKey,
            ApiKeySource: apiKeySource,
            LoadedConfigPaths: loadedConfigPaths,
            Warnings: warnings);
    }

    private static CliConfigFile? ReadConfig(
        string path,
        List<string> loadedConfigPaths,
        List<string> warnings)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            string json = File.ReadAllText(path);

            if (string.IsNullOrWhiteSpace(json))
            {
                warnings.Add($"ignored invalid config: {path}");
                return null;
            }

            CliConfigFile? config = JsonSerializer.Deserialize<CliConfigFile>(json, JsonOptions);
            if (config is null)
            {
                warnings.Add($"ignored invalid config: {path}");
                return null;
            }

            if (!HasMeaningfulValue(config))
            {
                warnings.Add($"ignored invalid config: {path}");
                return null;
            }

            loadedConfigPaths.Add(path);
            return config;
        }
        catch (JsonException)
        {
            warnings.Add($"ignored invalid config: {path}");
            return null;
        }
    }

    private static bool HasMeaningfulValue(CliConfigFile config)
    {
        return !string.IsNullOrWhiteSpace(config.Model)
            || !string.IsNullOrWhiteSpace(config.ApiKey);
    }

    private static (string Model, string Source) SelectModel(
        string? openAiModel,
        CliConfigFile? userConfig,
        CliConfigFile? workspaceConfig)
    {
        if (!string.IsNullOrWhiteSpace(openAiModel))
        {
            return (openAiModel, "OPENAI_MODEL");
        }

        if (!string.IsNullOrWhiteSpace(userConfig?.Model))
        {
            return (userConfig.Model, "user config");
        }

        if (!string.IsNullOrWhiteSpace(workspaceConfig?.Model))
        {
            return (workspaceConfig.Model, "workspace config");
        }

        return ("not configured", "default");
    }

    private static (SecretValue? ApiKey, string Source) SelectApiKey(
        string? openAiApiKey,
        CliConfigFile? userConfig)
    {
        SecretValue? envApiKey = SecretValue.From(openAiApiKey);
        if (envApiKey is not null)
        {
            return (envApiKey, "OPENAI_API_KEY");
        }

        SecretValue? userApiKey = SecretValue.From(userConfig?.ApiKey);
        if (userApiKey is not null)
        {
            return (userApiKey, "user config");
        }

        return (null, "missing");
    }

    private static void AddWorkspaceApiKeyWarning(
        CliConfigFile? workspaceConfig,
        string workspaceConfigPath,
        List<string> warnings)
    {
        if (!string.IsNullOrWhiteSpace(workspaceConfig?.ApiKey))
        {
            warnings.Add($"ignored workspace config apiKey: {workspaceConfigPath}");
        }
    }
}
