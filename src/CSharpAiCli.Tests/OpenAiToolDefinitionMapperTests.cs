using System.Text.Json;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class OpenAiToolDefinitionMapperTests
{
    [Fact]
    public void From_definitions_converts_tool_metadata_and_schema()
    {
        ToolDefinition registryDefinition = new(
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
            """);

        OpenAiToolDefinition definition = Assert.Single(
            OpenAiToolDefinitionMapper.FromDefinitions([registryDefinition]));

        Assert.Equal("function", definition.Type);
        Assert.Equal("workspace.read_text", definition.Name);
        Assert.Equal("Read a text file.", definition.Description);
        Assert.Equal(
            """{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}""",
            definition.ParametersSchema);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{not json")]
    [InlineData("[]")]
    [InlineData("\"string schema\"")]
    public void From_definitions_falls_back_to_object_schema_for_blank_invalid_or_non_object_schema(
        string parametersSchema)
    {
        ToolDefinition registryDefinition = new(
            "test.tool",
            "Test tool.",
            parametersSchema);

        OpenAiToolDefinition definition = Assert.Single(
            OpenAiToolDefinitionMapper.FromDefinitions([registryDefinition]));

        Assert.Equal("""{"type":"object"}""", definition.ParametersSchema);
    }

    [Fact]
    public void From_registry_preserves_registry_list_order()
    {
        ToolRegistry registry = new();
        registry.Register(new StubTool("tool.first"));
        registry.Register(new StubTool("tool.second"));
        registry.Register(new StubTool("tool.third"));

        IReadOnlyList<OpenAiToolDefinition> definitions =
            OpenAiToolDefinitionMapper.FromRegistry(registry);

        Assert.Collection(
            definitions,
            definition => Assert.Equal("tool.first", definition.Name),
            definition => Assert.Equal("tool.second", definition.Name),
            definition => Assert.Equal("tool.third", definition.Name));
    }

    [Fact]
    public void Openai_tool_definition_serializes_as_responses_function_tool_shape()
    {
        OpenAiToolDefinition definition = new(
            Type: "function",
            Name: "test.tool",
            Description: "Test tool.",
            ParametersSchema: """{"type":"object"}""");

        string json = JsonSerializer.Serialize(definition);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        Assert.Equal("function", root.GetProperty("type").GetString());
        Assert.Equal("test.tool", root.GetProperty("name").GetString());
        Assert.Equal("Test tool.", root.GetProperty("description").GetString());
        Assert.Equal(JsonValueKind.Object, root.GetProperty("parameters").ValueKind);
        Assert.Equal("object", root.GetProperty("parameters").GetProperty("type").GetString());
        Assert.False(root.TryGetProperty("parametersSchema", out _));
    }

    private sealed class StubTool : ITool
    {
        public StubTool(string name)
        {
            Definition = new ToolDefinition(
                name,
                "Stub tool.",
                """{"type":"object"}""");
        }

        public ToolDefinition Definition { get; }

        public ToolExecutionResult Execute(
            ToolExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            return ToolExecutionResult.Success("{}");
        }
    }
}
