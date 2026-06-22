using System.Text.Json;

namespace CSharpAiCli.Core;

public static class OpenAiToolDefinitionMapper
{
    private const string FunctionType = "function";
    private const string FallbackObjectSchema = """{"type":"object"}""";

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
                ParametersSchema: NormalizeParametersSchema(definition.ParametersSchema)))
            .ToArray();
    }

    private static string NormalizeParametersSchema(string? parametersSchema)
    {
        if (string.IsNullOrWhiteSpace(parametersSchema))
        {
            return FallbackObjectSchema;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(parametersSchema);
            return JsonSerializer.Serialize(document.RootElement);
        }
        catch (JsonException)
        {
            return FallbackObjectSchema;
        }
    }
}
