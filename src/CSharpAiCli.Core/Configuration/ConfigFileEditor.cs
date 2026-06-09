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

        if (propertyName == "agentBackend")
        {
            if (!ConfigLoader.TryNormalizeAgentBackend(value, out string? normalizedBackend))
            {
                return ConfigFileEditResult.Failure(
                    "invalid-agent-backend",
                    "agentBackend must be one of: direct, openai, framework, maf, agent-framework.");
            }

            valueToWrite = normalizedBackend!;
        }

        try
        {
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

            RemoveCaseInsensitiveDuplicateProperties(json, propertyName);
            json[propertyName] = valueToWrite;
            WriteJsonAtomically(path, json.ToJsonString(JsonSerializerOptions) + Environment.NewLine);

            return ConfigFileEditResult.Success(propertyName, path);
        }
        catch (Exception exception) when (IsConfigIoException(exception))
        {
            return ConfigFileEditResult.Failure(
                "config-write-failed",
                $"Unable to write user config file: {path}");
        }
    }

    public static ConfigFileEditResult UnsetUserScalar(string path, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(key);

        if (!TryGetScalarPropertyName(key, out string propertyName))
        {
            return ConfigFileEditResult.Failure(
                "unknown-config-key",
                $"Unknown config key '{key}'. Supported scalar keys are: model, baseUrl, agentBackend, apiKey.");
        }

        try
        {
            if (!File.Exists(path))
            {
                return ConfigFileEditResult.Success(propertyName, path, "unchanged");
            }

            if (!TryReadJsonObject(path, out JsonObject json))
            {
                return ConfigFileEditResult.Failure(
                    "invalid-config-file",
                    $"User config file is not a valid JSON object: {path}");
            }

            if (!RemoveCaseInsensitiveProperties(json, propertyName))
            {
                return ConfigFileEditResult.Success(propertyName, path, "unchanged");
            }

            if (json.Count == 0)
            {
                File.Delete(path);
                return ConfigFileEditResult.Success(propertyName, path);
            }

            WriteJsonAtomically(path, json.ToJsonString(JsonSerializerOptions) + Environment.NewLine);

            return ConfigFileEditResult.Success(propertyName, path);
        }
        catch (Exception exception) when (IsConfigIoException(exception))
        {
            return ConfigFileEditResult.Failure(
                "config-write-failed",
                $"Unable to write user config file: {path}");
        }
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

    private static void RemoveCaseInsensitiveDuplicateProperties(JsonObject json, string propertyName)
    {
        string[] keysToRemove = json
            .Select(property => property.Key)
            .Where(key =>
                key.Equals(propertyName, StringComparison.OrdinalIgnoreCase)
                && !key.Equals(propertyName, StringComparison.Ordinal))
            .ToArray();

        foreach (string key in keysToRemove)
        {
            json.Remove(key);
        }
    }

    private static bool RemoveCaseInsensitiveProperties(JsonObject json, string propertyName)
    {
        string[] keysToRemove = json
            .Select(property => property.Key)
            .Where(key => key.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (string key in keysToRemove)
        {
            json.Remove(key);
        }

        return keysToRemove.Length > 0;
    }

    private static void WriteJsonAtomically(string path, string json)
    {
        string? directory = Path.GetDirectoryName(path);
        string fileName = Path.GetFileName(path);
        string temporaryPath = string.IsNullOrWhiteSpace(directory)
            ? $"{fileName}.{Guid.NewGuid():N}.tmp"
            : Path.Combine(directory, $"{fileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (IsConfigIoException(exception))
        {
        }
    }

    private static bool IsConfigIoException(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException;
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
    string? Path,
    string Status)
{
    public static ConfigFileEditResult Success(string key, string path, string status = "updated")
    {
        return new ConfigFileEditResult(
            Succeeded: true,
            ErrorCode: null,
            Summary: status == "unchanged" ? "User config unchanged." : "User config updated.",
            Key: key,
            Path: path,
            Status: status);
    }

    public static ConfigFileEditResult Failure(string errorCode, string summary)
    {
        return new ConfigFileEditResult(
            Succeeded: false,
            ErrorCode: errorCode,
            Summary: summary,
            Key: null,
            Path: null,
            Status: "failed");
    }
}
