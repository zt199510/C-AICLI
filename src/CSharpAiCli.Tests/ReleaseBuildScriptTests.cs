using System.Text.Json;

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
        Assert.Contains("dotnet publish failed with exit code $LASTEXITCODE", script, StringComparison.Ordinal);
        Assert.Contains("caicli.exe", script, StringComparison.Ordinal);
        Assert.Contains("release-manifest.json", script, StringComparison.Ordinal);
        Assert.Contains("sourceRevision = $sourceRevision", script, StringComparison.Ordinal);
        Assert.Contains("sourceDirty = $sourceDirty", script, StringComparison.Ordinal);
        Assert.Contains("sdkVersion = $sdkVersion", script, StringComparison.Ordinal);
        Assert.Contains("artifactInventory = $payloadInventory", script, StringComparison.Ordinal);
        Assert.Contains("publishInventory = $publishInventory", script, StringComparison.Ordinal);
        Assert.Contains("$releaseName.checksums.json", script, StringComparison.Ordinal);
        Assert.Contains("THIRD-PARTY-NOTICES-MAGICK.NET.txt", script, StringComparison.Ordinal);
        Assert.Contains("magick.net-q8-x64\\$magickNetVersion\\Notice.txt", script, StringComparison.Ordinal);
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
    public void Build_release_script_uses_stable_manifest_metadata()
    {
        string script = ReadRepositoryFile("tools", "Build-Release.ps1");

        Assert.DoesNotContain("Get-Date", script, StringComparison.Ordinal);
        Assert.DoesNotContain("createdAtUtc", script, StringComparison.Ordinal);
        Assert.Contains("builtFromVersion = $version", script, StringComparison.Ordinal);
        Assert.Contains("git -C $repoRoot rev-parse --verify HEAD", script, StringComparison.Ordinal);
        Assert.Contains("git -C $repoRoot status --porcelain=v1 --untracked-files=all", script, StringComparison.Ordinal);
        Assert.Contains("Release build requires a clean Git source tree", script, StringComparison.Ordinal);
        Assert.Contains("ReleaseAcceptance cannot be combined with AllowDirtySource", script, StringComparison.Ordinal);
        Assert.Contains("-AllowDirtySource only for non-acceptance validation artifacts", script, StringComparison.Ordinal);
        Assert.Contains("does not match global.json version", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_release_script_creates_zip_with_deterministic_entries()
    {
        string script = ReadRepositoryFile("tools", "Build-Release.ps1");

        Assert.DoesNotContain("Compress-Archive", script, StringComparison.Ordinal);
        Assert.Contains("[System.IO.Compression.ZipArchive]", script, StringComparison.Ordinal);
        Assert.Contains("OrderBy", script, StringComparison.Ordinal);
        Assert.Contains("LastWriteTime", script, StringComparison.Ordinal);
        Assert.Contains("Get-FileHash -LiteralPath $resolvedZipPath -Algorithm SHA256", script, StringComparison.Ordinal);
        Assert.Contains("Get-ArtifactInventory -Root $resolvedPublishDir", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_release_script_has_explicit_pdb_and_dirty_validation_policies()
    {
        string script = ReadRepositoryFile("tools", "Build-Release.ps1");

        Assert.Contains("[switch]$ReleaseAcceptance", script, StringComparison.Ordinal);
        Assert.Contains("[switch]$AllowDirtySource", script, StringComparison.Ordinal);
        Assert.Contains("-p:DebugType=None", script, StringComparison.Ordinal);
        Assert.Contains("-p:DebugSymbols=false", script, StringComparison.Ordinal);
        Assert.Contains("pdbPolicy = \"excluded\"", script, StringComparison.Ordinal);
        Assert.Contains("publish produced PDB files", script, StringComparison.Ordinal);
        Assert.Contains("foreach ($staleArtifact in @($resolvedZipPath, $checksumPath))", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Repository_version_metadata_is_defined_for_release_artifacts()
    {
        string props = ReadRepositoryFile("Directory.Build.props");

        Assert.Contains("<Version>0.6.0</Version>", props, StringComparison.Ordinal);
        Assert.Contains("<AssemblyVersion>0.6.0.0</AssemblyVersion>", props, StringComparison.Ordinal);
        Assert.Contains("<FileVersion>0.6.0.0</FileVersion>", props, StringComparison.Ordinal);
        Assert.Contains("<InformationalVersion>$(Version)</InformationalVersion>", props, StringComparison.Ordinal);
    }

    [Fact]
    public void Repository_sdk_is_locked_to_current_net9_sdk()
    {
        string globalJsonPath = Path.Combine(GetRepositoryRoot(), "global.json");

        Assert.True(File.Exists(globalJsonPath), "global.json should lock the repository SDK.");

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(globalJsonPath));
        JsonElement sdk = document.RootElement.GetProperty("sdk");

        Assert.Equal("9.0.308", sdk.GetProperty("version").GetString());
        Assert.Equal("latestPatch", sdk.GetProperty("rollForward").GetString());
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
