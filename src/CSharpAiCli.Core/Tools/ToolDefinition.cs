namespace CSharpAiCli.Core;

public sealed record ToolDefinition
{
    public ToolDefinition(
        string name,
        string description,
        string parametersSchema,
        ToolRiskLevel riskLevel = ToolRiskLevel.Write)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name;
        Description = description ?? string.Empty;
        ParametersSchema = parametersSchema ?? "{}";
        RiskLevel = riskLevel;
    }

    public string Name { get; }
    public string Description { get; }
    public string ParametersSchema { get; }
    public ToolRiskLevel RiskLevel { get; }
}
