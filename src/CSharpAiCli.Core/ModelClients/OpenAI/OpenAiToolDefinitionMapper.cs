namespace CSharpAiCli.Core;

public static class OpenAiToolDefinitionMapper
{
    private const string FunctionType = "function";

    public static IReadOnlyList<OpenAiToolDefinition> FromRegistry(IToolRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        return FromDefinitions(registry.List());
    }

    public static IReadOnlyList<OpenAiToolDefinition> FromDefinitions(
        IReadOnlyList<ToolDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        return definitions
            .Select(definition => new OpenAiToolDefinition(
                Type: FunctionType,
                Name: definition.Name,
                Description: definition.Description,
                ParametersSchema: ToolSchemaRenderer.RenderParametersSchemaString(definition)))
            .ToArray();
    }
}
