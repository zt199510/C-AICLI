using System.Text.Json;
using System.Text.Json.Serialization;

namespace CSharpAiCli.Core;

public sealed record OpenAiToolDefinition
{
    public OpenAiToolDefinition(
        string Type,
        string Name,
        string Description,
        string ParametersSchema)
    {
        this.Type = Type;
        this.Name = Name;
        this.Description = Description;
        this.ParametersSchema = ParametersSchema;
    }

    [JsonPropertyName("type")]
    public string Type { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; }

    [JsonPropertyName("description")]
    public string Description { get; init; }

    [JsonIgnore]
    public string ParametersSchema { get; init; }

    [JsonPropertyName("parameters")]
    public JsonElement Parameters
    {
        get
        {
            using JsonDocument document = JsonDocument.Parse(ParametersSchema);
            return document.RootElement.Clone();
        }
    }
}
