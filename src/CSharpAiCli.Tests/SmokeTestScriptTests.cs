namespace CSharpAiCli.Tests;

public sealed class SmokeTestScriptTests
{
    [Fact]
    public void Invoke_smoke_tests_covers_release_acceptance_paths()
    {
        string script = ReadRepositoryFile("tools", "Invoke-SmokeTests.ps1");

        Assert.Contains("Directory.Build.props", script, StringComparison.Ordinal);
        Assert.Contains("caicli-$releaseVersion-win-x64", script, StringComparison.Ordinal);
        Assert.Contains("caicli $releaseVersion", script, StringComparison.Ordinal);
        Assert.Contains("\"config\", \"get\", \"--workspace\", $workspace", script, StringComparison.Ordinal);
        Assert.Contains("\"config\", \"set\", \"baseUrl\"", script, StringComparison.Ordinal);
        Assert.Contains("\"config\", \"list\", \"--workspace\", $workspace", script, StringComparison.Ordinal);
        Assert.Contains("\"config\", \"unset\", \"baseUrl\"", script, StringComparison.Ordinal);
        Assert.Contains("baseUrlSource: user config", script, StringComparison.Ordinal);
        Assert.Contains("\"doctor\", \"--workspace\", $workspace, \"--trace\"", script, StringComparison.Ordinal);
        Assert.Contains("\"logs\", \"path\", \"--workspace\", $workspace", script, StringComparison.Ordinal);
        Assert.Contains("\"logs\", \"show\", \"--tail\", \"20\", \"--workspace\", $workspace", script, StringComparison.Ordinal);
        Assert.Contains("\"logs\", \"clear\", \"--workspace\", $workspace", script, StringComparison.Ordinal);
        Assert.Contains("command.start", script, StringComparison.Ordinal);
        Assert.Contains("instruction source: 0:", script, StringComparison.Ordinal);
        Assert.Contains("localErrorCode: missing-model", script, StringComparison.Ordinal);
        Assert.Contains("localErrorCode: missing-openai-api-key", script, StringComparison.Ordinal);
        Assert.Contains("exec", script, StringComparison.Ordinal);
        Assert.Contains("--output", script, StringComparison.Ordinal);
        Assert.Contains("--max-turns", script, StringComparison.Ordinal);
        Assert.Contains("--max-tool-calls", script, StringComparison.Ordinal);
        Assert.Contains("--timeout-seconds", script, StringComparison.Ordinal);
        Assert.Contains("--cwd", script, StringComparison.Ordinal);
        Assert.Contains("CAICLI_REAL_MODEL_SMOKE", script, StringComparison.Ordinal);
        Assert.Contains("real model smoke skipped: set CAICLI_REAL_MODEL_SMOKE=1", script, StringComparison.Ordinal);
        Assert.Contains("requires caller $($missingRealModelSettings -join ' and ')", script, StringComparison.Ordinal);
        Assert.Contains("\"--approval\", \"never\"", script, StringComparison.Ordinal);
        Assert.Contains("Assert-ExitCode $realModelExec 0 \"real model read-only exec\"", script, StringComparison.Ordinal);
        Assert.Contains("Assert-Contains $realModelExec.Output \"result: success\" \"real model read-only exec\"", script, StringComparison.Ordinal);
        Assert.Contains("Assert-Contains $realModelExec.Output \"workspace.read_text\" \"real model read-only exec tool call\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-smoke-local", script, StringComparison.Ordinal);
        Assert.DoesNotContain("agent-backend-unavailable", script, StringComparison.Ordinal);
        Assert.Contains("errorCode: approval-denied", script, StringComparison.Ordinal);
        Assert.Contains("approvalStatus: approval-required", script, StringComparison.Ordinal);
        Assert.Contains("errorCode: workspace-boundary-denied", script, StringComparison.Ordinal);
        Assert.Contains("\"tools\", \"list\", \"--workspace\", $workspace, \"--json\"", script, StringComparison.Ordinal);
        Assert.Contains("--stdin", script, StringComparison.Ordinal);
        Assert.Contains("--approval", script, StringComparison.Ordinal);
        Assert.Contains("approvalStatus: dangerous-shell-denied", script, StringComparison.Ordinal);
        Assert.Contains("disabledTools", script, StringComparison.Ordinal);
        Assert.Contains("errorCode: shell-timeout", script, StringComparison.Ordinal);
        Assert.Contains("workflow", script, StringComparison.Ordinal);
        Assert.Contains("validationCommand: dotnet test", script, StringComparison.Ordinal);
        Assert.Contains("\"status\", \"--workspace\", $workspace", script, StringComparison.Ordinal);
        Assert.Contains("\"models\", \"--workspace\", $workspace", script, StringComparison.Ordinal);
        Assert.Contains("modelListApi: not called", script, StringComparison.Ordinal);
        Assert.Contains("apiKey: missing", script, StringComparison.Ordinal);
        Assert.Contains("\"diff\", \"--workspace\", $workspace", script, StringComparison.Ordinal);
        Assert.Contains("\"diff\", \"--stat\", \"--workspace\", $workspace", script, StringComparison.Ordinal);
        Assert.Contains("diff --git", script, StringComparison.Ordinal);
        Assert.Contains("1 file changed", script, StringComparison.Ordinal);
        Assert.Contains("commit.gpgSign=false", script, StringComparison.Ordinal);
        Assert.Contains("core.autocrlf", script, StringComparison.Ordinal);
        Assert.Contains("--no-gpg-sign", script, StringComparison.Ordinal);
        Assert.Contains("--no-verify", script, StringComparison.Ordinal);
        Assert.Contains("src\\BuggyApp\\Calculator.txt", script, StringComparison.Ordinal);
        Assert.Contains("tests\\Verify-BuggyApp.ps1", script, StringComparison.Ordinal);
        Assert.Contains("expected: 41", script, StringComparison.Ordinal);
        Assert.Contains("expected: 42", script, StringComparison.Ordinal);
        Assert.Contains("workspace.search_text", script, StringComparison.Ordinal);
        Assert.Contains("bugfix fixture read", script, StringComparison.Ordinal);
        Assert.Contains("bugfix fixture search", script, StringComparison.Ordinal);
        Assert.Contains("bugfix fixture patch", script, StringComparison.Ordinal);
        Assert.Contains("bugfix fixture verify", script, StringComparison.Ordinal);
        Assert.Contains("bugfix verification passed", script, StringComparison.Ordinal);
        Assert.Contains("errorCode: tool-disabled", script, StringComparison.Ordinal);
        Assert.Contains("run", script, StringComparison.Ordinal);
        Assert.Contains("session", script, StringComparison.Ordinal);
        Assert.Contains("\"session\", \"list\", \"--workspace\", $workspace", script, StringComparison.Ordinal);
        Assert.Contains("\"session\", \"show\", \"--workspace\", $workspace, \"smoke\"", script, StringComparison.Ordinal);
        Assert.Contains("\"session\", \"export\", \"--format\", \"markdown\"", script, StringComparison.Ordinal);
        Assert.Contains("\"session\", \"rename\", \"--workspace\", $workspace, \"smoke\", \"smoke-archive\"", script, StringComparison.Ordinal);
        Assert.Contains("\"session\", \"delete\", \"--workspace\", $workspace, \"smoke-archive\"", script, StringComparison.Ordinal);
        Assert.Contains("\"mcp\", \"list\", \"--workspace\", $workspace", script, StringComparison.Ordinal);
        Assert.Contains("\"mcp\", \"doctor\", \"--workspace\", $workspace", script, StringComparison.Ordinal);
        Assert.Contains("mcp.smoke.echo", script, StringComparison.Ordinal);
        Assert.Contains("fake-mcp-smoke.ps1", script, StringComparison.Ordinal);
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
