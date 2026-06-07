namespace CSharpAiCli.Core;

public sealed record ToolDefinition
{
    public ToolDefinition(string name, string description, string parametersSchema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name;
        Description = description ?? string.Empty;
        ParametersSchema = parametersSchema ?? "{}";
    }

    public string Name { get; }
    public string Description { get; }
    public string ParametersSchema { get; }
}
