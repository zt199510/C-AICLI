using System.Text.Json;

namespace CSharpAiCli.Core;

public static class ConfigLoader
{
    public static readonly string DefaultOpenAiBaseUrl = "https://api.openai.com/v1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static EffectiveConfiguration Load(
        WorkspaceContext workspace,
        string? userProfile = null,
        string? openAiApiKey = null,
        string? openAiModel = null,
        string? agentBackend = null,
        string? openAiBaseUrl = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        userProfile ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        openAiApiKey ??= Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        openAiModel ??= Environment.GetEnvironmentVariable("OPENAI_MODEL");
        agentBackend ??= Environment.GetEnvironmentVariable("CAICLI_AGENT_BACKEND");
        openAiBaseUrl ??= Environment.GetEnvironmentVariable("OPENAI_BASE_URL");

        string userConfigPath = Path.Combine(userProfile, ".caicli", "config.json");
        string workspaceConfigPath = workspace.ConfigPath;

        List<string> loadedConfigPaths = [];
        List<string> warnings = [];

        CliConfigFile? userConfig = ReadConfig(userConfigPath, loadedConfigPaths, warnings);
        CliConfigFile? workspaceConfig = workspace.IsUsable
            ? ReadConfig(workspaceConfigPath, loadedConfigPaths, warnings)
            : null;
        List<CliConfigFileSource> configSources = [];
        if (userConfig is not null)
        {
            configSources.Add(new CliConfigFileSource("user config", userConfigPath, userConfig));
        }

        if (workspaceConfig is not null)
        {
            configSources.Add(new CliConfigFileSource("workspace config", workspaceConfigPath, workspaceConfig));
        }

        AddWorkspaceApiKeyWarning(workspaceConfig, workspaceConfigPath, warnings);

        (string model, string modelSource) = SelectModel(openAiModel, userConfig, workspaceConfig);
        (string effectiveAgentBackend, string agentBackendSource) = SelectAgentBackend(agentBackend, userConfig, workspaceConfig, warnings);
        (ApprovalMode approvalMode, string approvalModeSource) = SelectApprovalMode(userConfig, workspaceConfig, warnings);
        (string baseUrl, string baseUrlSource) = SelectBaseUrl(openAiBaseUrl, userConfig, workspaceConfig, warnings);
        (SecretValue? apiKey, string apiKeySource) = SelectApiKey(openAiApiKey, userConfig);

        return new EffectiveConfiguration(
            WorkspaceRoot: workspace.RootPath,
            UserConfigPath: userConfigPath,
            WorkspaceConfigPath: workspaceConfigPath,
            Model: model,
            ModelSource: modelSource,
            AgentBackend: effectiveAgentBackend,
            AgentBackendSource: agentBackendSource,
            DisabledTools: SelectDisabledTools(userConfig, workspaceConfig),
            ApiKey: apiKey,
            ApiKeySource: apiKeySource,
            LoadedConfigPaths: loadedConfigPaths,
            Warnings: warnings,
            ConfigSources: configSources)
        {
            BaseUrl = baseUrl,
            BaseUrlSource = baseUrlSource,
            ApprovalMode = approvalMode,
            ApprovalModeSource = approvalModeSource
        };
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
            || !string.IsNullOrWhiteSpace(config.ApiKey)
            || !string.IsNullOrWhiteSpace(config.BaseUrl)
            || !string.IsNullOrWhiteSpace(config.AgentBackend)
            || !string.IsNullOrWhiteSpace(config.ApprovalMode)
            || config.DisabledTools is { Length: > 0 }
            || config.McpServers is { Count: > 0 }
            || config.WorkflowProfiles is { Count: > 0 };
    }

    public static bool TryNormalizeBaseUrl(string? value, out string? baseUrl)
    {
        baseUrl = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string trimmed = value.Trim();
        if (ContainsWhitespaceOrControlCharacter(trimmed))
        {
            return false;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        baseUrl = trimmed;
        return true;
    }

    private static bool ContainsWhitespaceOrControlCharacter(string value)
    {
        foreach (char character in value)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character))
            {
                return true;
            }
        }

        return false;
    }

    public static bool TryNormalizeAgentBackend(string? value, out string? backend)
    {
        backend = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string normalized = value.Trim().ToLowerInvariant();
        if (normalized is "direct" or "openai")
        {
            backend = "direct";
            return true;
        }

        if (normalized is "framework" or "maf" or "agent-framework")
        {
            backend = "framework";
            return true;
        }

        return false;
    }

    public static bool TryNormalizeApprovalMode(string? value, out ApprovalMode approvalMode)
    {
        approvalMode = ApprovalMode.OnRequest;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string normalized = value.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "never":
                approvalMode = ApprovalMode.Never;
                return true;
            case "on-request":
                approvalMode = ApprovalMode.OnRequest;
                return true;
            case "on-failure":
                approvalMode = ApprovalMode.OnFailure;
                return true;
            case "always":
                approvalMode = ApprovalMode.Always;
                return true;
            default:
                return false;
        }
    }

    private static (string BaseUrl, string Source) SelectBaseUrl(
        string? openAiBaseUrl,
        CliConfigFile? userConfig,
        CliConfigFile? workspaceConfig,
        List<string> warnings)
    {
        if (TryNormalizeBaseUrl(openAiBaseUrl, warnings, "OPENAI_BASE_URL", out string? baseUrl))
        {
            return (baseUrl!, "OPENAI_BASE_URL");
        }

        if (TryNormalizeBaseUrl(userConfig?.BaseUrl, warnings, "user config", out baseUrl))
        {
            return (baseUrl!, "user config");
        }

        if (TryNormalizeBaseUrl(workspaceConfig?.BaseUrl, warnings, "workspace config", out baseUrl))
        {
            return (baseUrl!, "workspace config");
        }

        return (DefaultOpenAiBaseUrl, "default");
    }

    private static bool TryNormalizeBaseUrl(
        string? value,
        List<string> warnings,
        string source,
        out string? baseUrl)
    {
        if (TryNormalizeBaseUrl(value, out baseUrl))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(value))
        {
            warnings.Add($"ignored invalid baseUrl from {source}");
        }

        return false;
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

    private static IReadOnlySet<string> SelectDisabledTools(
        CliConfigFile? userConfig,
        CliConfigFile? workspaceConfig)
    {
        HashSet<string> disabledTools = new(StringComparer.Ordinal);
        AddDisabledTools(disabledTools, userConfig);
        AddDisabledTools(disabledTools, workspaceConfig);
        return disabledTools;
    }

    private static void AddDisabledTools(HashSet<string> disabledTools, CliConfigFile? config)
    {
        if (config?.DisabledTools is null)
        {
            return;
        }

        foreach (string toolName in config.DisabledTools)
        {
            if (!string.IsNullOrWhiteSpace(toolName))
            {
                disabledTools.Add(toolName.Trim());
            }
        }
    }

    private static (string Backend, string Source) SelectAgentBackend(
        string? environmentBackend,
        CliConfigFile? userConfig,
        CliConfigFile? workspaceConfig,
        List<string> warnings)
    {
        if (TryNormalizeBackend(environmentBackend, warnings, "CAICLI_AGENT_BACKEND", out string? backend))
        {
            return (backend!, "CAICLI_AGENT_BACKEND");
        }

        if (TryNormalizeBackend(userConfig?.AgentBackend, warnings, "user config", out backend))
        {
            return (backend!, "user config");
        }

        if (TryNormalizeBackend(workspaceConfig?.AgentBackend, warnings, "workspace config", out backend))
        {
            return (backend!, "workspace config");
        }

        return ("direct", "default");
    }

    private static (ApprovalMode Mode, string Source) SelectApprovalMode(
        CliConfigFile? userConfig,
        CliConfigFile? workspaceConfig,
        List<string> warnings)
    {
        if (TryNormalizeApprovalMode(userConfig?.ApprovalMode, warnings, "user config", out ApprovalMode mode))
        {
            return (mode, "user config");
        }

        if (TryNormalizeApprovalMode(workspaceConfig?.ApprovalMode, warnings, "workspace config", out mode))
        {
            return (mode, "workspace config");
        }

        return (ApprovalMode.OnRequest, "default");
    }

    private static bool TryNormalizeApprovalMode(
        string? value,
        List<string> warnings,
        string source,
        out ApprovalMode approvalMode)
    {
        if (TryNormalizeApprovalMode(value, out approvalMode))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(value))
        {
            warnings.Add($"ignored invalid approvalMode from {source}");
        }

        return false;
    }

    private static bool TryNormalizeBackend(
        string? value,
        List<string> warnings,
        string source,
        out string? backend)
    {
        if (TryNormalizeAgentBackend(value, out backend))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(value))
        {
            warnings.Add($"ignored invalid agent backend from {source}");
        }

        return false;
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
