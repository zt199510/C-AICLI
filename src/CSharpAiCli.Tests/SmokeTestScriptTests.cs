using System.Text.Json;

namespace CSharpAiCli.Tests;

public sealed class SmokeTestScriptTests
{
    [Fact]
    public void Real_tool_baseline_matches_frozen_toolchain_evidence()
    {
        using JsonDocument document = JsonDocument.Parse(ReadRepositoryFile(
            "src", "CSharpAiCli.Tests", "Fixtures", "GerberTiff", "real", "verification-baseline.json"));

        JsonElement root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("gerber-tiff.verification-baseline", root.GetProperty("type").GetString());
        Assert.Equal("1FBA765C24534A0707BFA5A28709A8FBC6915407BA3880B8C203D0863A66609E", root.GetProperty("inputFingerprint").GetString());

        JsonElement output = Assert.Single(root.GetProperty("outputs").EnumerateArray());
        Assert.True(output.GetProperty("byteDeterministic").GetBoolean());
        Assert.Equal("FDDFA21D29EF870D94EC953ADF757BD61585957E1519D79BB2480AFF81C89823", output.GetProperty("exactSha256").GetString());

        JsonElement metadata = output.GetProperty("metadata");
        Assert.Equal(1, metadata.GetProperty("frameCount").GetInt32());
        Assert.Equal(126, metadata.GetProperty("width").GetInt32());
        Assert.Equal(126, metadata.GetProperty("height").GetInt32());
        Assert.Equal(300, metadata.GetProperty("dpiX").GetDouble());
        Assert.Equal(300, metadata.GetProperty("dpiY").GetDouble());
        Assert.Equal("rgb8", metadata.GetProperty("pixelFormat").GetString());
        Assert.Equal("lzw", metadata.GetProperty("compression").GetString());
        Assert.Equal("top-left", metadata.GetProperty("orientation").GetString());
        Assert.False(metadata.GetProperty("hasAlpha").GetBoolean());
    }

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
        Assert.Contains("--report", script, StringComparison.Ordinal);
        Assert.Contains("--report-path", script, StringComparison.Ordinal);
        Assert.Contains("--expert", script, StringComparison.Ordinal);
        Assert.Contains("--max-turns", script, StringComparison.Ordinal);
        Assert.Contains("--max-tool-calls", script, StringComparison.Ordinal);
        Assert.Contains("--timeout-seconds", script, StringComparison.Ordinal);
        Assert.Contains("--cwd", script, StringComparison.Ordinal);
        Assert.Contains("CAICLI_REAL_MODEL_SMOKE", script, StringComparison.Ordinal);
        Assert.Contains("real model smoke skipped: set CAICLI_REAL_MODEL_SMOKE=1", script, StringComparison.Ordinal);
        Assert.Contains("requires caller $($missingRealModelSettings -join ' and ')", script, StringComparison.Ordinal);
        Assert.Contains("$oldOpenAiBaseUrl = $env:OPENAI_BASE_URL", script, StringComparison.Ordinal);
        Assert.Contains("$env:OPENAI_BASE_URL = $oldOpenAiBaseUrl", script, StringComparison.Ordinal);
        Assert.Contains("Remove-Item Env:OPENAI_BASE_URL -ErrorAction SilentlyContinue", script, StringComparison.Ordinal);
        Assert.Contains("$realModelWorkspaceConfigBytes", script, StringComparison.Ordinal);
        Assert.Contains("\"agent.plan\"", script, StringComparison.Ordinal);
        Assert.Contains("\"workspace.apply_patch\"", script, StringComparison.Ordinal);
        Assert.Contains("[IO.File]::WriteAllBytes($realModelWorkspaceConfigPath, $realModelWorkspaceConfigBytes)", script, StringComparison.Ordinal);
        Assert.Contains("\"--approval\", \"never\"", script, StringComparison.Ordinal);
        Assert.Contains("Assert-ExitCode $realModelExec 0 \"real model read-only exec\"", script, StringComparison.Ordinal);
        Assert.Contains("Assert-Contains $realModelExec.Output \"result: success\" \"real model read-only exec\"", script, StringComparison.Ordinal);
        Assert.Contains("Assert-Contains $realModelExec.Output \"workspace.read_text\" \"real model read-only exec tool call\"", script, StringComparison.Ordinal);
        Assert.Contains("CAICLI_DAEMON_SMOKE", script, StringComparison.Ordinal);
        Assert.Contains("daemon/API smoke skipped: set CAICLI_DAEMON_SMOKE=1", script, StringComparison.Ordinal);
        Assert.Contains("\"daemon\", \"doctor\", \"--output\", \"json\"", script, StringComparison.Ordinal);
        Assert.Contains("\"api\", \"routes\", \"--output\", \"json\"", script, StringComparison.Ordinal);
        Assert.Contains("\"daemon\", \"start\", \"--preview\", \"--bind\", \"0.0.0.0\"", script, StringComparison.Ordinal);
        Assert.Contains("Start-Process", script, StringComparison.Ordinal);
        Assert.Contains("-WindowStyle Hidden", script, StringComparison.Ordinal);
        Assert.Contains("\"api\", \"smoke\", \"--port\", [string]$daemonPort", script, StringComparison.Ordinal);
        Assert.Contains("http://127.0.0.1:$daemonPort/v1/jobs?limit=10", script, StringComparison.Ordinal);
        Assert.Contains("http://127.0.0.1:$daemonPort/v1/queue?limit=10", script, StringComparison.Ordinal);
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
        Assert.Contains("\"skills\", \"list\", \"--workspace\", $workspace", script, StringComparison.Ordinal);
        Assert.Contains("\"skills\", \"list\", \"--output\", \"json\", \"--workspace\", $workspace", script, StringComparison.Ordinal);
        Assert.Contains("\"skills\", \"run\", \"review-only\", \"--dry-run\"", script, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"skills.list\"", script, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"skills.runPlan\"", script, StringComparison.Ordinal);
        Assert.Contains("dryRun: true", script, StringComparison.Ordinal);
        Assert.Contains("skill: review-only", script, StringComparison.Ordinal);
        Assert.Contains("allowWrites=false", script, StringComparison.Ordinal);
        Assert.Contains("CAICLI_GERBER_TIFF_TOOL_SMOKE", script, StringComparison.Ordinal);
        Assert.Contains("CAICLI_GERBV_PATH", script, StringComparison.Ordinal);
        Assert.Contains("CAICLI_IMAGEMAGICK_PATH", script, StringComparison.Ordinal);
        Assert.Contains("CAICLI_GERBER_TIFF_FIXTURE", script, StringComparison.Ordinal);
        Assert.Contains("CAICLI_GERBER_TIFF_BASELINE", script, StringComparison.Ordinal);
        Assert.Contains("executable identity differs from the Week 58 reviewed toolchain", script, StringComparison.Ordinal);
        Assert.Contains("\"packs\", \"list\", \"--workspace\", $workspace", script, StringComparison.Ordinal);
        Assert.Contains("\"packs\", \"doctor\", \"gerber-tiff\"", script, StringComparison.Ordinal);
        Assert.Contains("\"packs\", \"plan\", \"gerber-tiff\"", script, StringComparison.Ordinal);
        Assert.Contains("\"--input\", \"gerber-input\", \"--output-dir\", \"gerber-output\"", script, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"packs.plan\"", script, StringComparison.Ordinal);
        Assert.Contains("\"readyForStaging\":true", script, StringComparison.Ordinal);
        Assert.Contains("\"runnable\":false", script, StringComparison.Ordinal);
        Assert.Contains("\"conversionExecuted\":false", script, StringComparison.Ordinal);
        Assert.Contains("packs plan created the output directory", script, StringComparison.Ordinal);
        Assert.Contains("packs list/doctor/plan created persistent job state", script, StringComparison.Ordinal);
        Assert.Contains("\"packs\", \"run\", \"gerber-tiff\"", script, StringComparison.Ordinal);
        Assert.Contains("\"--plan\", \"gerber-tiff-plan.json\", \"--dry-run\"", script, StringComparison.Ordinal);
        Assert.Contains("controlled fake partial-output fixture does not preserve explicit failure evidence", script, StringComparison.Ordinal);
        Assert.Contains("packs run missing tool", script, StringComparison.Ordinal);
        Assert.Contains("pack-tool-not-found", script, StringComparison.Ordinal);
        Assert.Contains("controlled-fake-tool-identities", script, StringComparison.Ordinal);
        Assert.Contains("packs run approval denied", script, StringComparison.Ordinal);
        Assert.Contains("pack-approval-required", script, StringComparison.Ordinal);
        Assert.Contains("packs missing-tool or approval denial created run state before execution authorization", script, StringComparison.Ordinal);
        Assert.Contains("\"packs\", \"runs\", \"show\", $packRunId", script, StringComparison.Ordinal);
        Assert.Contains("\"packs\", \"resume\", $packRunId, \"--output\"", script, StringComparison.Ordinal);
        Assert.Contains("\"packs\", \"verify\", $packRunId", script, StringComparison.Ordinal);
        Assert.Contains("packs verify ready-state rejection created fake verification or preview evidence", script, StringComparison.Ordinal);
        Assert.Contains("\"packs\", \"cancel\", $packRunId", script, StringComparison.Ordinal);
        Assert.Contains("packs run staging changed source input or failed post-copy hash verification", script, StringComparison.Ordinal);
        Assert.Contains("packs run dry-run produced fake or real conversion artifacts", script, StringComparison.Ordinal);
        Assert.Contains("packs run left atomic temporary files behind", script, StringComparison.Ordinal);
        Assert.Contains("packs run dry-run changed the caicli/gerbv/magick process set", script, StringComparison.Ordinal);
        Assert.Contains("packs corrupt state", script, StringComparison.Ordinal);
        Assert.Contains("pack-run-record-corrupt", script, StringComparison.Ordinal);
        Assert.Contains("real Gerber/TIFF tool smoke skipped", script, StringComparison.Ordinal);
        Assert.Contains("\"packs\", \"verify\", $realRunId", script, StringComparison.Ordinal);
        Assert.Contains("\"packs\", \"preview\", $realRunId", script, StringComparison.Ordinal);
        Assert.Contains("\"packs\", \"accept\", $realRunId", script, StringComparison.Ordinal);
        Assert.Contains("\"packs\", \"reject\", $realRejectRunId", script, StringComparison.Ordinal);
        Assert.Contains("\"artifacts\", \"prune\"", script, StringComparison.Ordinal);
        Assert.Contains("\"hardVerificationPassed\":true", script, StringComparison.Ordinal);
        Assert.Contains("contentCompared=passed", script, StringComparison.Ordinal);
        Assert.Contains("left probe temporary directories behind", script, StringComparison.Ordinal);
        Assert.Contains("left managed process temporary content behind", script, StringComparison.Ordinal);
        Assert.Contains("\"state\":\"awaiting-acceptance\"", script, StringComparison.Ordinal);
        Assert.Contains("real Gerber/TIFF conversion, hard verification, explicit human accept/reject, and controlled managed prune passed", script, StringComparison.Ordinal);
        Assert.Contains("\"--approval\", \"always\"", script, StringComparison.Ordinal);
        Assert.Contains("\"state\":\"verifying\"", script, StringComparison.Ordinal);
        Assert.Contains("\"tiffVerificationPassed\": false", script, StringComparison.Ordinal);
        Assert.Contains("real Gerber/TIFF controlled conversion left a gerbv or magick process behind", script, StringComparison.Ordinal);
        Assert.Contains("\"taskReport\": null", script, StringComparison.Ordinal);
        Assert.Contains("\"jobs\", \"list\", \"--output\", \"json\", \"--workspace\", $workspace", script, StringComparison.Ordinal);
        Assert.Contains("\"exec\", \"--record-job\", \"--job-name\", \"smoke-missing-model\"", script, StringComparison.Ordinal);
        Assert.Contains("\"skills\", \"run\", \"review-only\", \"--record-job\", \"--dry-run\"", script, StringComparison.Ordinal);
        Assert.Contains("\"jobs\", \"show\", $recordedJobId, \"--output\", \"json\", \"--workspace\", $workspace", script, StringComparison.Ordinal);
        Assert.Contains("\"jobs\", \"export\", $recordedJobId, \"--format\", \"markdown\", \"--workspace\", $workspace", script, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"jobs.list\"", script, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"jobs.show\"", script, StringComparison.Ordinal);
        Assert.Contains("# C# AI CLI Job", script, StringComparison.Ordinal);
        Assert.Contains("\"ci\", \"summarize\", \"--job\", $recordedJobId", script, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"caicli.ci.summary\"", script, StringComparison.Ordinal);
        Assert.Contains("\"rawReferencesStored\":false", script, StringComparison.Ordinal);
        Assert.Contains("\"rawToolArgumentsStored\":false", script, StringComparison.Ordinal);
        Assert.Contains("\"fullDiffStored\":false", script, StringComparison.Ordinal);
        Assert.Contains("\"ci\", \"check\", \"--job\", $recordedJobId", script, StringComparison.Ordinal);
        Assert.Contains("Assert-ExitCode $ciCheck 1 \"ci check failed job\"", script, StringComparison.Ordinal);
        Assert.Contains("Assert-ExitCode $ciMissing 2 \"ci check config error\"", script, StringComparison.Ordinal);
        Assert.Contains(".caicli\\reports\\ci-summary.md", script, StringComparison.Ordinal);
        Assert.Contains("smoke-storage-secret", script, StringComparison.Ordinal);
        Assert.Contains("corrupt-job-record", script, StringComparison.Ordinal);
        Assert.Contains("corrupt-queue-record", script, StringComparison.Ordinal);
        Assert.Contains("jobs corrupt diagnostic redaction", script, StringComparison.Ordinal);
        Assert.Contains("queue corrupt diagnostic redaction", script, StringComparison.Ordinal);
        Assert.Contains("queue cleanup must preserve corrupt records", script, StringComparison.Ordinal);
        Assert.Contains("\"queue\", \"list\", \"--output\", \"json\", \"--workspace\", $workspace", script, StringComparison.Ordinal);
        Assert.Contains("\"queue\", \"add\", \"exec\"", script, StringComparison.Ordinal);
        Assert.Contains("\"queue\", \"add\", \"skill\", \"review-only\"", script, StringComparison.Ordinal);
        Assert.Contains("\"queue\", \"show\", $queuedExecId", script, StringComparison.Ordinal);
        Assert.Contains("\"queue\", \"run\", $queuedExecId", script, StringComparison.Ordinal);
        Assert.Contains("\"queue\", \"cancel\", $queuedSkillId", script, StringComparison.Ordinal);
        Assert.Contains("\"queue\", \"cleanup\", \"--status\", \"canceled\"", script, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"queue.run.completed\"", script, StringComparison.Ordinal);
        Assert.Contains("\"errorCode\":\"queue-invalid-state\"", script, StringComparison.Ordinal);
        Assert.Contains("latestJobId", script, StringComparison.Ordinal);
        Assert.Contains("\"deletedCount\":0", script, StringComparison.Ordinal);
        Assert.Contains("\"status\", \"--workspace\", $workspace", script, StringComparison.Ordinal);
        Assert.Contains("\"models\", \"--workspace\", $workspace", script, StringComparison.Ordinal);
        Assert.Contains("modelListApi: not called", script, StringComparison.Ordinal);
        Assert.Contains("apiKey: missing", script, StringComparison.Ordinal);
        Assert.Contains("\"diff\", \"--workspace\", $workspace", script, StringComparison.Ordinal);
        Assert.Contains("\"diff\", \"--stat\", \"--workspace\", $workspace", script, StringComparison.Ordinal);
        Assert.Contains("diff --git", script, StringComparison.Ordinal);
        Assert.Contains("1 file changed", script, StringComparison.Ordinal);
        Assert.Contains("\"changes\", \"--workspace\", $workspace", script, StringComparison.Ordinal);
        Assert.Contains("\"changes\", \"--output\", \"json\", \"--workspace\", $workspace", script, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"changes.view\"", script, StringComparison.Ordinal);
        Assert.Contains("context.references", script, StringComparison.Ordinal);
        Assert.Contains("references=count=1", script, StringComparison.Ordinal);
        Assert.Contains("# C# AI CLI Task Report", script, StringComparison.Ordinal);
        Assert.Contains("reportMode=markdown", script, StringComparison.Ordinal);
        Assert.Contains("reportStatus=stdout", script, StringComparison.Ordinal);
        Assert.Contains("reportStatus=written", script, StringComparison.Ordinal);
        Assert.Contains("expert=reviewer", script, StringComparison.Ordinal);
        Assert.Contains("\"expert\":{\"name\":\"security\"", script, StringComparison.Ordinal);
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
        Assert.Contains("\"changes\", \"--session\", \"smoke\", \"--workspace\", $workspace", script, StringComparison.Ordinal);
        Assert.Contains("Session transcript does not contain an agent task report.", script, StringComparison.Ordinal);
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
