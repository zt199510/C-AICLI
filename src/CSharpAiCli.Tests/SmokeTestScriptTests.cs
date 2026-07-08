namespace CSharpAiCli.Tests;

public sealed class SmokeTestScriptTests
{
    [Fact]
    public void Invoke_smoke_tests_covers_release_acceptance_paths()
    {
        string script = ReadRepositoryFile("tools", "Invoke-SmokeTests.ps1");

        Assert.Contains("localErrorCode: missing-model", script, StringComparison.Ordinal);
        Assert.Contains("localErrorCode: missing-openai-api-key", script, StringComparison.Ordinal);
        Assert.Contains("errorCode: approval-denied", script, StringComparison.Ordinal);
        Assert.Contains("errorCode: workspace-boundary-denied", script, StringComparison.Ordinal);
        Assert.Contains("disabledTools", script, StringComparison.Ordinal);
        Assert.Contains("errorCode: shell-timeout", script, StringComparison.Ordinal);
        Assert.Contains("""status", "--workspace", $workspace""", script, StringComparison.Ordinal);
        Assert.Contains("""models", "--workspace", $workspace""", script, StringComparison.Ordinal);
        Assert.Contains("modelListApi: not called", script, StringComparison.Ordinal);
        Assert.Contains("apiKey: missing", script, StringComparison.Ordinal);
        Assert.Contains("""diff", "--workspace", $workspace""", script, StringComparison.Ordinal);
        Assert.Contains("""diff", "--stat", "--workspace", $workspace""", script, StringComparison.Ordinal);
        Assert.Contains("diff --git", script, StringComparison.Ordinal);
        Assert.Contains("1 file changed", script, StringComparison.Ordinal);
        Assert.Contains("commit.gpgSign=false", script, StringComparison.Ordinal);
        Assert.Contains("--no-gpg-sign", script, StringComparison.Ordinal);
        Assert.Contains("--no-verify", script, StringComparison.Ordinal);
        Assert.Contains("run", script, StringComparison.Ordinal);
        Assert.Contains("session", script, StringComparison.Ordinal);
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
