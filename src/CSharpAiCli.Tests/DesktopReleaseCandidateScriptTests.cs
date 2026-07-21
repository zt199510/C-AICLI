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
    }

    [Fact]
    public void Performance_script_records_per_role_versioned_samples_and_cleanup()
    {
        string script = Read("tools", "Measure-DesktopBaseline.ps1");

        Assert.Contains("schemaVersion = 1", script, StringComparison.Ordinal);
        Assert.Contains("week76-cold-start-idle-v1", script, StringComparison.Ordinal);
        Assert.Contains("renderer", script, StringComparison.Ordinal);
        Assert.Contains("apphost", script, StringComparison.Ordinal);
        Assert.Contains("workingSetBytes", script, StringComparison.Ordinal);
        Assert.Contains("privateBytes", script, StringComparison.Ordinal);
        Assert.Contains("processDelta = 0", script, StringComparison.Ordinal);
        Assert.Contains("tempDelta = 0", script, StringComparison.Ordinal);
        Assert.Contains("Performance evidence must remain under artifacts", script, StringComparison.Ordinal);
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

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(Root(), Path.Combine(parts)));

    private static string Root()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "global.json"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
