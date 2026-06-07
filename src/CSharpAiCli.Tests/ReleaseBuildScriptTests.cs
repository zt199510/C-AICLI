namespace CSharpAiCli.Tests;

public sealed class ReleaseBuildScriptTests
{
    [Fact]
    public void Build_release_script_publishes_self_contained_windows_executable()
    {
        string script = ReadRepositoryFile("tools", "Build-Release.ps1");

        Assert.Contains("dotnet publish", script, StringComparison.Ordinal);
        Assert.Contains("--configuration $Configuration", script, StringComparison.Ordinal);
        Assert.Contains("--runtime $Runtime", script, StringComparison.Ordinal);
        Assert.Contains("--self-contained true", script, StringComparison.Ordinal);
        Assert.Contains("-p:PublishSingleFile=true", script, StringComparison.Ordinal);
        Assert.Contains("caicli.exe", script, StringComparison.Ordinal);
        Assert.Contains("release-manifest.json", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_release_script_does_not_embed_secret_configuration()
    {
        string script = ReadRepositoryFile("tools", "Build-Release.ps1");

        Assert.DoesNotContain("OPENAI_API_KEY", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apiKey", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sk-", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Repository_version_metadata_is_defined_for_release_artifacts()
    {
        string props = ReadRepositoryFile("Directory.Build.props");

        Assert.Contains("<Version>0.1.0</Version>", props, StringComparison.Ordinal);
        Assert.Contains("<InformationalVersion>$(Version)</InformationalVersion>", props, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(params string[] relativeParts)
    {
        string path = Path.Combine(GetRepositoryRoot(), Path.Combine(relativeParts));
        return File.ReadAllText(path);
    }

    private static string GetRepositoryRoot()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(Path.Combine(current, "src", "CSharpAiCli.sln")))
            {
                return current;
            }

            string? parent = Directory.GetParent(current)?.FullName;
            if (string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = parent ?? string.Empty;
        }

        throw new InvalidOperationException("Repository root could not be found.");
    }
}
