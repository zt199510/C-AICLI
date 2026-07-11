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
        ShellPolicyConfiguration shellPolicy = SelectShellPolicy(userConfig, workspaceConfig, warnings);
        (
            AgentRunLimits agentRunLimits,
            string agentRunMaxStepsSource,
            string agentRunMaxToolCallsSource,
            string agentRunMaxRetriesSource,
            string agentRunTimeoutSource) = SelectAgentRunLimits(userConfig, workspaceConfig, warnings);

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
            ApprovalModeSource = approvalModeSource,
            ShellPolicy = shellPolicy,
            AgentRunLimits = agentRunLimits,
            AgentRunMaxStepsSource = agentRunMaxStepsSource,
            AgentRunMaxToolCallsSource = agentRunMaxToolCallsSource,
            AgentRunMaxRetriesSource = agentRunMaxRetriesSource,
            AgentRunTimeoutSource = agentRunTimeoutSource
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
            || HasMeaningfulShellPolicy(config.ShellPolicy)
            || HasMeaningfulAgentRunLimits(config.AgentRunLimits)
            || config.McpServers is { Count: > 0 }
            || config.WorkflowProfiles is { Count: > 0 };
    }

    private static bool HasMeaningfulShellPolicy(ShellPolicyConfig? policy)
    {
        return policy is not null
            && (policy.AllowedCommands is not null
                || policy.DeniedCommands is not null
                || policy.MaxTimeoutMilliseconds.HasValue);
    }

    private static bool HasMeaningfulAgentRunLimits(AgentRunLimitsConfig? limits)
    {
        return limits is not null &&
            (limits.MaxSteps.HasValue ||
                limits.MaxTurns.HasValue ||
                limits.MaxToolCalls.HasValue ||
                limits.MaxRetries.HasValue ||
                limits.TimeoutSeconds.HasValue);
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

    private static ShellPolicyConfiguration SelectShellPolicy(
        CliConfigFile? userConfig,
        CliConfigFile? workspaceConfig,
        List<string> warnings)
    {
        (List<string> allowedCommands, bool allowedCommandsConfigured, string allowedCommandsSource) =
            SelectAllowedShellPolicyCommands(
            userConfig?.ShellPolicy?.AllowedCommands,
            workspaceConfig?.ShellPolicy?.AllowedCommands);

        List<string> deniedCommands = [];
        HashSet<string> seenDeniedCommands = new(StringComparer.Ordinal);
        AddShellPolicyCommands(deniedCommands, seenDeniedCommands, userConfig?.ShellPolicy?.DeniedCommands);
        AddShellPolicyCommands(deniedCommands, seenDeniedCommands, workspaceConfig?.ShellPolicy?.DeniedCommands);

        (int? maxTimeoutMilliseconds, string maxTimeoutMillisecondsSource) =
            SelectShellMaxTimeoutMilliseconds(userConfig, workspaceConfig, warnings);

        return new ShellPolicyConfiguration(
            AllowedCommands: allowedCommands,
            AllowedCommandsConfigured: allowedCommandsConfigured,
            AllowedCommandsSource: allowedCommandsSource,
            DeniedCommands: deniedCommands,
            MaxTimeoutMilliseconds: maxTimeoutMilliseconds,
            MaxTimeoutMillisecondsSource: maxTimeoutMillisecondsSource);
    }

    private static (List<string> Commands, bool Configured, string Source) SelectAllowedShellPolicyCommands(
        string[]? userCommands,
        string[]? workspaceCommands)
    {
        bool userConfigured = userCommands is not null;
        bool workspaceConfigured = workspaceCommands is not null;
        List<string> normalizedUserCommands = NormalizeShellPolicyCommands(userCommands);
        List<string> normalizedWorkspaceCommands = NormalizeShellPolicyCommands(workspaceCommands);

        if (userConfigured && workspaceConfigured)
        {
            HashSet<string> workspaceCommandSet = new(normalizedWorkspaceCommands, StringComparer.Ordinal);
            List<string> allowedCommands = [];
            foreach (string command in normalizedUserCommands)
            {
                if (workspaceCommandSet.Contains(command))
                {
                    allowedCommands.Add(command);
                }
            }

            return (allowedCommands, true, "user config, workspace config");
        }

        if (userConfigured)
        {
            return (normalizedUserCommands, true, "user config");
        }

        if (workspaceConfigured)
        {
            return (normalizedWorkspaceCommands, true, "workspace config");
        }

        return ([], false, "default");
    }

    private static List<string> NormalizeShellPolicyCommands(string[]? configuredCommands)
    {
        List<string> commands = [];
        HashSet<string> seenCommands = new(StringComparer.Ordinal);
        AddShellPolicyCommands(commands, seenCommands, configuredCommands);
        return commands;
    }

    private static void AddShellPolicyCommands(
        List<string> commands,
        HashSet<string> seenCommands,
        string[]? configuredCommands)
    {
        if (configuredCommands is null)
        {
            return;
        }

        foreach (string command in configuredCommands)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                continue;
            }

            string trimmed = command.Trim();
            if (seenCommands.Add(trimmed))
            {
                commands.Add(trimmed);
            }
        }
    }

    private static (int? MaxTimeoutMilliseconds, string Source) SelectShellMaxTimeoutMilliseconds(
        CliConfigFile? userConfig,
        CliConfigFile? workspaceConfig,
        List<string> warnings)
    {
        bool hasUserTimeout = TryNormalizeShellMaxTimeoutMilliseconds(
            userConfig?.ShellPolicy?.MaxTimeoutMilliseconds,
            warnings,
            "user config",
            out int userMaxTimeoutMilliseconds);

        bool hasWorkspaceTimeout = TryNormalizeShellMaxTimeoutMilliseconds(
            workspaceConfig?.ShellPolicy?.MaxTimeoutMilliseconds,
            warnings,
            "workspace config",
            out int workspaceMaxTimeoutMilliseconds);

        if (hasUserTimeout && hasWorkspaceTimeout)
        {
            return userMaxTimeoutMilliseconds <= workspaceMaxTimeoutMilliseconds
                ? (userMaxTimeoutMilliseconds, "user config")
                : (workspaceMaxTimeoutMilliseconds, "workspace config");
        }

        if (hasUserTimeout)
        {
            return (userMaxTimeoutMilliseconds, "user config");
        }

        if (hasWorkspaceTimeout)
        {
            return (workspaceMaxTimeoutMilliseconds, "workspace config");
        }

        return (null, "default");
    }

    private static bool TryNormalizeShellMaxTimeoutMilliseconds(
        int? value,
        List<string> warnings,
        string source,
        out int maxTimeoutMilliseconds)
    {
        maxTimeoutMilliseconds = 0;
        if (!value.HasValue)
        {
            return false;
        }

        if (value.Value > 0)
        {
            maxTimeoutMilliseconds = value.Value;
            return true;
        }

        warnings.Add($"ignored invalid shellPolicy.maxTimeoutMilliseconds from {source}");
        return false;
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

    private static (
        AgentRunLimits Limits,
        string MaxStepsSource,
        string MaxToolCallsSource,
        string MaxRetriesSource,
        string TimeoutSource) SelectAgentRunLimits(
            CliConfigFile? userConfig,
            CliConfigFile? workspaceConfig,
            List<string> warnings)
    {
        (int? maxSteps, string maxStepsSource) = SelectAgentRunMaxSteps(userConfig, workspaceConfig, warnings);
        (int? maxToolCalls, string maxToolCallsSource) = SelectPositiveAgentRunLimit(
            userConfig?.AgentRunLimits?.MaxToolCalls,
            workspaceConfig?.AgentRunLimits?.MaxToolCalls,
            "agentRunLimits.maxToolCalls",
            warnings);
        (int? maxRetries, string maxRetriesSource) = SelectNonNegativeAgentRunLimit(
            userConfig?.AgentRunLimits?.MaxRetries,
            workspaceConfig?.AgentRunLimits?.MaxRetries,
            "agentRunLimits.maxRetries",
            warnings);
        (int? timeoutSeconds, string timeoutSource) = SelectPositiveAgentRunLimit(
            userConfig?.AgentRunLimits?.TimeoutSeconds,
            workspaceConfig?.AgentRunLimits?.TimeoutSeconds,
            "agentRunLimits.timeoutSeconds",
            warnings);

        TimeSpan? timeout = timeoutSeconds is null
            ? null
            : TimeSpan.FromSeconds(timeoutSeconds.Value);

        AgentRunLimits limits = new(
            MaxSteps: maxSteps,
            MaxToolCalls: maxToolCalls,
            MaxRetries: maxRetries,
            ModelCallTimeout: timeout,
            OverallTimeout: timeout);
        return (limits, maxStepsSource, maxToolCallsSource, maxRetriesSource, timeoutSource);
    }

    private static (int? Value, string Source) SelectAgentRunMaxSteps(
        CliConfigFile? userConfig,
        CliConfigFile? workspaceConfig,
        List<string> warnings)
    {
        if (TryNormalizeAgentRunMaxSteps(userConfig?.AgentRunLimits, "user config", warnings, out int userMaxSteps))
        {
            return (userMaxSteps, "user config");
        }

        if (TryNormalizeAgentRunMaxSteps(workspaceConfig?.AgentRunLimits, "workspace config", warnings, out int workspaceMaxSteps))
        {
            return (workspaceMaxSteps, "workspace config");
        }

        return (null, "default");
    }

    private static bool TryNormalizeAgentRunMaxSteps(
        AgentRunLimitsConfig? limits,
        string source,
        List<string> warnings,
        out int maxSteps)
    {
        maxSteps = 0;
        if (limits is null || (!limits.MaxSteps.HasValue && !limits.MaxTurns.HasValue))
        {
            return false;
        }

        int? selected = limits.MaxSteps ?? limits.MaxTurns;
        string selectedName = limits.MaxSteps.HasValue
            ? "agentRunLimits.maxSteps"
            : "agentRunLimits.maxTurns";
        if (selected is <= 0)
        {
            warnings.Add($"ignored invalid {selectedName} from {source}");
            return false;
        }

        if (limits.MaxSteps.HasValue &&
            limits.MaxTurns.HasValue &&
            limits.MaxSteps.Value != limits.MaxTurns.Value)
        {
            warnings.Add($"ignored conflicting agentRunLimits.maxSteps/maxTurns from {source}");
            return false;
        }

        maxSteps = selected!.Value;
        return true;
    }

    private static (int? Value, string Source) SelectPositiveAgentRunLimit(
        int? userValue,
        int? workspaceValue,
        string name,
        List<string> warnings)
    {
        if (TryNormalizePositiveAgentRunLimit(userValue, name, "user config", warnings, out int selected))
        {
            return (selected, "user config");
        }

        if (TryNormalizePositiveAgentRunLimit(workspaceValue, name, "workspace config", warnings, out selected))
        {
            return (selected, "workspace config");
        }

        return (null, "default");
    }

    private static (int? Value, string Source) SelectNonNegativeAgentRunLimit(
        int? userValue,
        int? workspaceValue,
        string name,
        List<string> warnings)
    {
        if (TryNormalizeNonNegativeAgentRunLimit(userValue, name, "user config", warnings, out int selected))
        {
            return (selected, "user config");
        }

        if (TryNormalizeNonNegativeAgentRunLimit(workspaceValue, name, "workspace config", warnings, out selected))
        {
            return (selected, "workspace config");
        }

        return (null, "default");
    }

    private static bool TryNormalizePositiveAgentRunLimit(
        int? value,
        string name,
        string source,
        List<string> warnings,
        out int selected)
    {
        selected = 0;
        if (!value.HasValue)
        {
            return false;
        }

        if (value.Value > 0)
        {
            selected = value.Value;
            return true;
        }

        warnings.Add($"ignored invalid {name} from {source}");
        return false;
    }

    private static bool TryNormalizeNonNegativeAgentRunLimit(
        int? value,
        string name,
        string source,
        List<string> warnings,
        out int selected)
    {
        selected = 0;
        if (!value.HasValue)
        {
            return false;
        }

        if (value.Value < 0)
        {
            warnings.Add($"ignored invalid {name} from {source}");
            return false;
        }

        selected = value.Value;
        return true;
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
