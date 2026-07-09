using System.Collections.ObjectModel;
using System.Text.Json;

namespace CSharpAiCli.Core;

internal static class ToolStructuredPayload
{
    public static IReadOnlyDictionary<string, JsonElement> Create(params (string Name, object? Value)[] properties)
    {
        Dictionary<string, JsonElement> payload = new(StringComparer.Ordinal);
        foreach ((string name, object? value) in properties)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            payload[name] = JsonSerializer.SerializeToElement(value).Clone();
        }

        return new ReadOnlyDictionary<string, JsonElement>(payload);
    }

    public static IReadOnlyDictionary<string, JsonElement> InvalidArguments(string toolName, string argument)
    {
        return Create(
            ("errorCode", ToolErrorCode.InvalidToolArguments),
            ("toolName", toolName),
            ("argument", argument));
    }
}
