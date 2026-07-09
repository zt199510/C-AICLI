using System.Text.Json.Nodes;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ToolSchemaRendererTests
{
    [Fact]
    public void Render_parameters_schema_string_normalizes_valid_object_schema()
    {
        ToolDefinition definition = new(
            "workspace.read_text",
            "Read a text file.",
            """
            {
              "type": "object",
              "properties": {
                "path": {
                  "type": "string"
                }
              },
              "required": [
                "path"
              ]
            }
            """,
            ToolRiskLevel.Read);

        string schema = ToolSchemaRenderer.RenderParametersSchemaString(definition);

        Assert.Equal(
            """{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}""",
            schema);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{not json")]
    [InlineData("[]")]
    [InlineData("\"string schema\"")]
    public void Render_parameters_schema_falls_back_for_blank_invalid_or_non_object_schema(
        string parametersSchema)
    {
        ToolDefinition definition = new(
            "test.tool",
            "Test tool.",
            parametersSchema);

        JsonObject parameters = ToolSchemaRenderer.RenderParametersSchemaObject(definition);

        Assert.Equal("""{"type":"object"}""", ToolSchemaRenderer.RenderParametersSchemaString(definition));
        Assert.Equal("object", parameters["type"]?.GetValue<string>());
    }

    [Fact]
    public void Render_definition_includes_metadata_and_normalized_schema_string()
    {
        ToolDefinition definition = new(
            "workspace.run_shell",
            "Run a shell command.",
            """{"type":"object","properties":{"command":{"type":"string"}}}""",
            ToolRiskLevel.DangerousShell);

        RenderedToolDefinition rendered = ToolSchemaRenderer.Render(definition);

        Assert.Equal("workspace.run_shell", rendered.Name);
        Assert.Equal("Run a shell command.", rendered.Description);
        Assert.Equal("dangerous-shell", rendered.RiskLevel);
        Assert.Equal(
            """{"type":"object","properties":{"command":{"type":"string"}}}""",
            rendered.ParametersSchema);
    }

    [Fact]
    public void Render_parameters_schema_object_returns_fresh_nodes_for_different_parents()
    {
        ToolDefinition definition = new(
            "test.tool",
            "Test tool.",
            """{"type":"object","properties":{"path":{"type":"string"}}}""");

        JsonObject firstParent = new()
        {
            ["parameters"] = ToolSchemaRenderer.RenderParametersSchemaObject(definition)
        };
        JsonObject secondParent = new()
        {
            ["parameters"] = ToolSchemaRenderer.RenderParametersSchemaObject(definition)
        };

        Assert.Equal(
            """{"parameters":{"type":"object","properties":{"path":{"type":"string"}}}}""",
            firstParent.ToJsonString());
        Assert.Equal(
            """{"parameters":{"type":"object","properties":{"path":{"type":"string"}}}}""",
            secondParent.ToJsonString());
    }

    [Fact]
    public void Mutating_rendered_parameter_object_does_not_affect_another_render()
    {
        ToolDefinition definition = new(
            "test.tool",
            "Test tool.",
            """{"type":"object","properties":{"path":{"type":"string"}}}""");

        JsonObject mutated = ToolSchemaRenderer.RenderParametersSchemaObject(definition);
        mutated["additionalProperties"] = false;

        JsonObject fresh = ToolSchemaRenderer.RenderParametersSchemaObject(definition);

        Assert.True(mutated.ContainsKey("additionalProperties"));
        Assert.False(fresh.ContainsKey("additionalProperties"));
    }
}
