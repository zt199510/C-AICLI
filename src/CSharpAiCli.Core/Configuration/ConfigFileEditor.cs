using System.Text.Json;
using System.Text.Json.Nodes;

namespace CSharpAiCli.Core;

public static class ConfigFileEditor
{
    private static readonly JsonDocumentOptions JsonDocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static ConfigFileEditResult SetUserScalar(string path, string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

        if (!TryGetScalarPropertyName(key, out string propertyName))
        {
            return ConfigFileEditResult.Failure(
                "unknown-config-key",
                $"Unknown config key '{key}'. Supported scalar keys are: model, baseUrl, agentBackend, apiKey.");
        }

        string valueToWrite = value;
        if (propertyName == "baseUrl")
        {
            if (!ConfigLoader.TryNormalizeBaseUrl(value, out string? normalizedBaseUrl))
            {
                return ConfigFileEditResult.Failure(
                    "invalid-base-url",
                    "baseUrl must be an absolute http or https URL without user info, query, or fragment.");
            }

            valueToWrite = normalizedBaseUrl!;
        }

        JsonObject json;
        if (File.Exists(path))
        {
            if (!TryReadJsonObject(path, out json))
            {
                return ConfigFileEditResult.Failure(
                    "invalid-config-file",
                    $"User config file is not a valid JSON object: {path}");
            }
        }
        else
        {
            json = [];
        }

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        json[propertyName] = valueToWrite;
        File.WriteAllText(path, json.ToJsonString(JsonSerializerOptions) + Environment.NewLine);

        return ConfigFileEditResult.Success(propertyName, path);
    }

    private static bool TryGetScalarPropertyName(string key, out string propertyName)
    {
        propertyName = key.Trim() switch
        {
            string value when value.Equals("model", StringComparison.OrdinalIgnoreCase) => "model",
            string value when value.Equals("baseUrl", StringComparison.OrdinalIgnoreCase) => "baseUrl",
            string value when value.Equals("agentBackend", StringComparison.OrdinalIgnoreCase) => "agentBackend",
            string value when value.Equals("apiKey", StringComparison.OrdinalIgnoreCase) => "apiKey",
            _ => string.Empty,
        };

        return propertyName.Length > 0;
    }

    private static bool TryReadJsonObject(string path, out JsonObject json)
    {
        json = [];

        try
        {
            JsonNode? node = JsonNode.Parse(File.ReadAllText(path), documentOptions: JsonDocumentOptions);
            if (node is not JsonObject parsedObject)
            {
                return false;
            }

            json = parsedObject;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

public sealed record ConfigFileEditResult(
    bool Succeeded,
    string? ErrorCode,
    string Summary,
    string? Key,
    string? Path)
{
    public static ConfigFileEditResult Success(string key, string path)
    {
        return new ConfigFileEditResult(
            Succeeded: true,
            ErrorCode: null,
            Summary: "User config updated.",
            Key: key,
            Path: path);
    }

    public static ConfigFileEditResult Failure(string errorCode, string summary)
    {
        return new ConfigFileEditResult(
            Succeeded: false,
            ErrorCode: errorCode,
            Summary: summary,
            Key: null,
            Path: null);
    }
}
