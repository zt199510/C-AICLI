using System.Text.Json.Nodes;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ConfigFileEditorTests
{
    [Fact]
    public void UnsetUserScalar_missing_config_file_succeeds_unchanged_without_creating_file()
    {
        using TempDirectory temp = TempDirectory.Create();
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");

        ConfigFileEditResult result = ConfigFileEditor.UnsetUserScalar(userConfigPath, "model");

        Assert.True(result.Succeeded);
        Assert.Equal("unchanged", result.Status);
        Assert.Equal("model", result.Key);
        Assert.Equal(userConfigPath, result.Path);
        Assert.False(File.Exists(userConfigPath));
    }

    [Fact]
    public void UnsetUserScalar_preserves_other_fields_when_removing_base_url()
    {
        using TempDirectory temp = TempDirectory.Create();
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(userConfigPath)!);
        File.WriteAllText(userConfigPath, """
        {
          "baseUrl": "https://gateway.example.test/v1",
          "model": "gpt-existing",
          "customSetting": 42
        }
        """);

        ConfigFileEditResult result = ConfigFileEditor.UnsetUserScalar(userConfigPath, "baseUrl");

        Assert.True(result.Succeeded);
        Assert.Equal("updated", result.Status);
        JsonObject json = ReadJsonObject(userConfigPath);
        Assert.False(json.ContainsKey("baseUrl"));
        Assert.Equal("gpt-existing", json["model"]?.GetValue<string>());
        Assert.Equal(42, json["customSetting"]?.GetValue<int>());
    }

    [Fact]
    public void UnsetUserScalar_deletes_config_file_when_removing_last_property()
    {
        using TempDirectory temp = TempDirectory.Create();
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(userConfigPath)!);
        File.WriteAllText(userConfigPath, """
        {
          "model": "gpt-existing"
        }
        """);

        ConfigFileEditResult result = ConfigFileEditor.UnsetUserScalar(userConfigPath, "model");

        Assert.True(result.Succeeded);
        Assert.Equal("updated", result.Status);
        Assert.False(File.Exists(userConfigPath));
    }

    [Fact]
    public void UnsetUserScalar_keeps_config_file_when_other_properties_remain()
    {
        using TempDirectory temp = TempDirectory.Create();
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(userConfigPath)!);
        File.WriteAllText(userConfigPath, """
        {
          "model": "gpt-existing",
          "baseUrl": "https://gateway.example.test/v1"
        }
        """);

        ConfigFileEditResult result = ConfigFileEditor.UnsetUserScalar(userConfigPath, "model");

        Assert.True(result.Succeeded);
        Assert.Equal("updated", result.Status);
        JsonObject json = ReadJsonObject(userConfigPath);
        Assert.False(json.ContainsKey("model"));
        Assert.Equal("https://gateway.example.test/v1", json["baseUrl"]?.GetValue<string>());
    }

    [Fact]
    public void UnsetUserScalar_missing_key_succeeds_unchanged_without_rewriting_file()
    {
        using TempDirectory temp = TempDirectory.Create();
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(userConfigPath)!);
        string originalJson = """
        {
          "model": "gpt-existing"
        }
        """;
        File.WriteAllText(userConfigPath, originalJson);

        ConfigFileEditResult result = ConfigFileEditor.UnsetUserScalar(userConfigPath, "baseUrl");

        Assert.True(result.Succeeded);
        Assert.Equal("unchanged", result.Status);
        Assert.Equal(originalJson, File.ReadAllText(userConfigPath));
    }

    [Fact]
    public void UnsetUserScalar_unknown_key_fails_without_creating_file()
    {
        using TempDirectory temp = TempDirectory.Create();
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");

        ConfigFileEditResult result = ConfigFileEditor.UnsetUserScalar(userConfigPath, "temperature");

        Assert.False(result.Succeeded);
        Assert.Equal("unknown-config-key", result.ErrorCode);
        Assert.False(File.Exists(userConfigPath));
    }

    [Fact]
    public void UnsetUserScalar_invalid_json_fails_without_overwriting()
    {
        using TempDirectory temp = TempDirectory.Create();
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(userConfigPath)!);
        File.WriteAllText(userConfigPath, "{not json");

        ConfigFileEditResult result = ConfigFileEditor.UnsetUserScalar(userConfigPath, "model");

        Assert.False(result.Succeeded);
        Assert.Equal("invalid-config-file", result.ErrorCode);
        Assert.Equal("{not json", File.ReadAllText(userConfigPath));
    }

    [Fact]
    public void UnsetUserScalar_root_array_fails_without_overwriting()
    {
        using TempDirectory temp = TempDirectory.Create();
        string userConfigPath = Path.Combine(temp.Path, ".caicli", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(userConfigPath)!);
        File.WriteAllText(userConfigPath, "[]");

        ConfigFileEditResult result = ConfigFileEditor.UnsetUserScalar(userConfigPath, "model");

        Assert.False(result.Succeeded);
        Assert.Equal("invalid-config-file", result.ErrorCode);
        Assert.Equal("[]", File.ReadAllText(userConfigPath));
    }

    private static JsonObject ReadJsonObject(string path)
    {
        JsonNode? node = JsonNode.Parse(File.ReadAllText(path));
        return Assert.IsType<JsonObject>(node);
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempDirectory Create()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "caicli-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
