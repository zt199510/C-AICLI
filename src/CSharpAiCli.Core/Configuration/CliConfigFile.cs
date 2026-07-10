namespace CSharpAiCli.Core;

public sealed class CliConfigFile
{
    public string? Model { get; init; }

    public string? ApiKey { get; init; }

    public string? BaseUrl { get; init; }

    public string? AgentBackend { get; init; }

    public string? ApprovalMode { get; init; }

    public string[]? DisabledTools { get; init; }

    public ShellPolicyConfig? ShellPolicy { get; init; }

    public AgentRunLimitsConfig? AgentRunLimits { get; init; }

    public Dictionary<string, McpServerConfig>? McpServers { get; init; }

    public Dictionary<string, WorkflowProfileConfig>? WorkflowProfiles { get; init; }
}

public sealed class AgentRunLimitsConfig
{
    public int? MaxSteps { get; init; }

    public int? MaxTurns { get; init; }

    public int? MaxToolCalls { get; init; }

    public int? TimeoutSeconds { get; init; }
}
