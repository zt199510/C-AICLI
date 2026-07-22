namespace CSharpAiCli.Tests;

public sealed class DesktopReleaseCandidateScriptTests
{
    [Fact]
    public void Candidate_script_fails_closed_for_dirty_overwrite_and_path_escape()
    {
        string script = Read("tools", "Build-DesktopReleaseCandidate.ps1");

        Assert.Contains("Release candidate requires a clean Git source tree", script, StringComparison.Ordinal);
        Assert.Contains("Refusing to overwrite an existing candidate directory", script, StringComparison.Ordinal);
        Assert.Contains("RC output must remain under artifacts", script, StringComparison.Ordinal);
        Assert.Contains("Release payload contains a reparse point", script, StringComparison.Ordinal);
        Assert.Contains("Release payload escaped its root", script, StringComparison.Ordinal);
        Assert.Contains("ValidationOnly", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Candidate_script_cross_checks_identity_inventory_notices_and_evidence()
    {
        string script = Read("tools", "Build-DesktopReleaseCandidate.ps1");

        Assert.Contains("release-manifest.json", script, StringComparison.Ordinal);
        Assert.Contains("payload-inventory.json", script, StringComparison.Ordinal);
        Assert.Contains("checksums.json", script, StringComparison.Ordinal);
        Assert.Contains("security-review.json", script, StringComparison.Ordinal);
        Assert.Contains("accessibility-review.json", script, StringComparison.Ordinal);
        Assert.Contains("performance.json", script, StringComparison.Ordinal);
        Assert.Contains("smoke.json", script, StringComparison.Ordinal);
        Assert.Contains("THIRD-PARTY-NOTICES-MAGICK.NET.txt", script, StringComparison.Ordinal);
        Assert.Contains("LICENSES.chromium.html", script, StringComparison.Ordinal);
        Assert.Contains("sourceRevision", script, StringComparison.Ordinal);
        Assert.Contains("appHostSha256", script, StringComparison.Ordinal);
        Assert.Contains("contractSha256", script, StringComparison.Ordinal);
        Assert.Contains("Get-FileHash", script, StringComparison.Ordinal);
        Assert.Contains("IndexOf($repoRoot, [System.StringComparison]::OrdinalIgnoreCase)", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Contains($repoRoot, [System.StringComparison]::OrdinalIgnoreCase)", script, StringComparison.Ordinal);
        Assert.Contains("week77-performance-gate-v2", script, StringComparison.Ordinal);
        Assert.Contains("five consecutive independent profiles", script, StringComparison.Ordinal);
        Assert.Contains("15 percent idle retention gate", script, StringComparison.Ordinal);
        Assert.Contains("process or temp cleanup delta", script, StringComparison.Ordinal);
        Assert.Contains("week77-accessibility-automation-v1", script, StringComparison.Ordinal);
        Assert.Contains("unpacked and packaged hardening passes", script, StringComparison.Ordinal);
        Assert.Contains("week77-packaged-smoke-v1", script, StringComparison.Ordinal);
        Assert.Contains("all eight packaged scenarios to pass", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Performance_script_records_per_role_versioned_samples_and_cleanup()
    {
        string script = Read("apps", "desktop", "scripts", "measure-performance.mjs");
        string scenario = Read("apps", "desktop", "e2e", "long-session.spec.ts");
        string baseline = Read("tools", "Measure-DesktopBaseline.ps1");

        Assert.Contains("schemaVersion: 2", script, StringComparison.Ordinal);
        Assert.Contains("week77-performance-gate-v2", script, StringComparison.Ordinal);
        Assert.Contains("--repeat-each=5", script, StringComparison.Ordinal);
        Assert.Contains("--workers=1", script, StringComparison.Ordinal);
        Assert.Contains("--retries=0", script, StringComparison.Ordinal);
        Assert.Contains("Performance evidence must remain under artifacts", script, StringComparison.Ordinal);
        Assert.Contains("warmBaselineSeconds", scenario, StringComparison.Ordinal);
        Assert.Contains("postWorkloadIdleSeconds", scenario, StringComparison.Ordinal);
        Assert.Contains("median-of-settled-suffix", scenario, StringComparison.Ordinal);
        Assert.Contains("rendererSettledMedian", scenario, StringComparison.Ordinal);
        Assert.Contains("processes", scenario, StringComparison.Ordinal);
        Assert.Contains("processDelta", scenario, StringComparison.Ordinal);
        Assert.Contains("tempDelta", scenario, StringComparison.Ordinal);
        Assert.Contains("schemaVersion = 2", baseline, StringComparison.Ordinal);
        Assert.Contains("week77-cold-start-idle-v2", baseline, StringComparison.Ordinal);
        Assert.Contains("status = if ([string]::IsNullOrWhiteSpace($safeFailure))", baseline, StringComparison.Ordinal);
        Assert.Contains("cleanupExitTimeoutMilliseconds = 10000", baseline, StringComparison.Ordinal);
        Assert.Contains("remainingProcesses = $remainingProcesses", baseline, StringComparison.Ordinal);
        Assert.Contains("if (-not $allRunsPassed) { exit 1 }", baseline, StringComparison.Ordinal);
    }

    [Fact]
    public void Protocol_measurement_uses_reviewed_limits_and_records_three_lossless_rounds()
    {
        string script = Read("apps", "desktop", "scripts", "measure-protocol.mjs");

        Assert.Contains("integerArgument(\"--rounds\", 3", script, StringComparison.Ordinal);
        Assert.Contains("integerArgument(\"--frames\", 48", script, StringComparison.Ordinal);
        Assert.Contains("maxInFlight: 8", script, StringComparison.Ordinal);
        Assert.Contains("requestRateBurst: 64", script, StringComparison.Ordinal);
        Assert.Contains("missing or duplicate response id", script, StringComparison.Ordinal);
        Assert.Contains("framesPerSecond", script, StringComparison.Ordinal);
        Assert.Contains("peakWorkingSetBytes", script, StringComparison.Ordinal);
        Assert.Contains("processDelta: 0", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Candidate_comparison_requires_identical_payload_two_smokes_and_manual_narrator()
    {
        string script = Read("tools", "Compare-DesktopReleaseCandidates.ps1");

        Assert.Contains("Candidate comparison requires the confirmed clean source revision", script, StringComparison.Ordinal);
        Assert.Contains("Candidate payloads differ", script, StringComparison.Ordinal);
        Assert.Contains("week77-packaged-smoke-v1", script, StringComparison.Ordinal);
        Assert.Contains("all eight packaged scenarios", script, StringComparison.Ordinal);
        Assert.Contains("week77-narrator-manual-v1", script, StringComparison.Ordinal);
        Assert.Contains("all seven manual steps to pass", script, StringComparison.Ordinal);
        Assert.Contains("packageSha256", script, StringComparison.Ordinal);
        Assert.Contains("archiveSha256", script, StringComparison.Ordinal);
        Assert.DoesNotContain("[System.IO.Path]::GetRelativePath", script, StringComparison.Ordinal);
        Assert.Contains("candidatePath.StartsWith", script, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(Root(), Path.Combine(parts)));

    private static string Root()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "global.json"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
