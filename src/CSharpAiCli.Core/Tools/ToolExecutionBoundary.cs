namespace CSharpAiCli.Core;

public sealed record ToolExecutionBoundary(
    IReadOnlySet<ToolRiskLevel>? AllowedRiskLevels = null,
    IReadOnlySet<string>? DisabledToolNames = null,
    IReadOnlyList<string>? DisabledToolPrefixes = null,
    bool AllowMcpDiscovery = true,
    string? Summary = null)
{
    public static ToolExecutionBoundary Default { get; } = new();

    public static ToolExecutionBoundary ReadOnly(string? summary = null)
    {
        return new ToolExecutionBoundary(
            AllowedRiskLevels: new HashSet<ToolRiskLevel> { ToolRiskLevel.Read },
            DisabledToolNames: new HashSet<string>(StringComparer.Ordinal)
            {
                "workspace.apply_patch",
                "workspace.run_shell"
            },
            DisabledToolPrefixes: ["mcp."],
            AllowMcpDiscovery: false,
            Summary: summary ?? "read-only tools only");
    }

    public bool IsDisabledByName(string toolName)
    {
        if (DisabledToolNames is null)
        {
            return false;
        }

        return DisabledToolNames.Contains(toolName);
    }

    public bool IsDisabledByPrefix(string toolName)
    {
        if (DisabledToolPrefixes is null)
        {
            return false;
        }

        return DisabledToolPrefixes.Any(prefix =>
            toolName.StartsWith(prefix, StringComparison.Ordinal));
    }

    public bool AllowsRisk(ToolRiskLevel riskLevel)
    {
        return AllowedRiskLevels is null || AllowedRiskLevels.Contains(riskLevel);
    }

    public bool AllowsTool(ToolDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return !IsDisabledByName(definition.Name) &&
            !IsDisabledByPrefix(definition.Name) &&
            AllowsRisk(definition.RiskLevel);
    }
}

public static class ExpertToolBoundary
{
    public static ToolExecutionBoundary FromExpert(ExpertProfile? expert)
    {
        return expert is { IsReadOnly: true }
            ? ToolExecutionBoundary.ReadOnly(expert.ToolBoundary)
            : ToolExecutionBoundary.Default;
    }
}

