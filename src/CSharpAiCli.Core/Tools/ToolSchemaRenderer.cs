using System.Text.Json;
using System.Text.Json.Nodes;

namespace CSharpAiCli.Core;

public static class ToolSchemaRenderer
{
    public static RenderedToolDefinition Render(ToolDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return new RenderedToolDefinition(
            Name: definition.Name,
            Description: definition.Description,
            RiskLevel: definition.RiskLevel.ToCanonicalName(),
            ParametersSchema: RenderParametersSchemaString(definition));
    }

    public static string RenderParametersSchemaString(ToolDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return RenderParametersSchemaObject(definition).ToJsonString();
    }

    public static JsonObject RenderParametersSchemaObject(ToolDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (string.IsNullOrWhiteSpace(definition.ParametersSchema))
        {
            return CreateFallbackParametersSchema();
        }

        try
        {
            return JsonNode.Parse(definition.ParametersSchema) as JsonObject
                ?? CreateFallbackParametersSchema();
        }
        catch (JsonException)
        {
            return CreateFallbackParametersSchema();
        }
    }

    private static JsonObject CreateFallbackParametersSchema()
    {
        return new JsonObject
        {
            ["type"] = "object"
        };
    }
}

public sealed record RenderedToolDefinition(
    string Name,
    string Description,
    string RiskLevel,
    string ParametersSchema);
