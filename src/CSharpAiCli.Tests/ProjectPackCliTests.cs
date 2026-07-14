using System.Text.Json.Nodes;
using CSharpAiCli.Cli;
using CSharpAiCli.Core;
using CSharpAiCli.ProjectPacks;

namespace CSharpAiCli.Tests;

public sealed class ProjectPackCliTests
{
    [Fact]
    public void Packs_list_text_is_contract_only()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();

        int exitCode = CliCommandFactory.Create(output, path => CreateSnapshot(path, temp.Path))
            .Parse(["packs", "list", "--workspace", temp.Path])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Contains("pack: gerber-tiff", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("status: contract-only", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("real execution accepted", output.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Packs_list_json_has_stable_schema()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();

        int exitCode = CliCommandFactory.Create(output, path => CreateSnapshot(path, temp.Path))
            .Parse(["packs", "list", "--output", "json", "--workspace", temp.Path])
            .Invoke();

        JsonObject root = Assert.IsType<JsonObject>(JsonNode.Parse(output.ToString()));
        Assert.Equal(0, exitCode);
        Assert.Equal("packs.list", root["type"]?.GetValue<string>());
        Assert.Equal(1, root["schemaVersion"]?.GetValue<int>());
        Assert.Single(Assert.IsType<JsonArray>(root["packs"]));
    }

    [Fact]
    public void Packs_doctor_without_paths_is_static_and_reports_unavailable()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();

        int exitCode = CliCommandFactory.Create(output, path => CreateSnapshot(path, temp.Path))
            .Parse(["packs", "doctor", "gerber-tiff", "--output", "json", "--workspace", temp.Path])
            .Invoke();

        JsonObject root = Assert.IsType<JsonObject>(JsonNode.Parse(output.ToString()));
        Assert.Equal(1, exitCode);
        Assert.Equal("packs.doctor", root["type"]?.GetValue<string>());
        Assert.Equal("unavailable", root["status"]?.GetValue<string>());
        Assert.False(root["probeRequested"]?.GetValue<bool>());
    }

    [Fact]
    public void Packs_static_doctor_accepts_dependency_bindings_without_starting_files_or_rendering_paths()
    {
        using TempDirectory temp = TempDirectory.Create();
        string gerbv = Path.Combine(temp.Path, "gerbv.exe");
        string magick = Path.Combine(temp.Path, "magick.exe");
        File.WriteAllText(gerbv, "static-gerbv-fixture");
        File.WriteAllText(magick, "static-magick-fixture");
        using StringWriter output = new();

        int exitCode = CliCommandFactory.Create(output, path => CreateSnapshot(path, temp.Path))
            .Parse(
            [
                "packs", "doctor", "gerber-tiff",
                "--tool-path", "gerbv=" + gerbv, "imagemagick=" + magick,
                "--output", "json", "--workspace", temp.Path
            ])
            .Invoke();

        JsonObject root = Assert.IsType<JsonObject>(JsonNode.Parse(output.ToString()));
        Assert.Equal(0, exitCode);
        Assert.Equal("static-ok", root["status"]?.GetValue<string>());
        Assert.DoesNotContain(temp.Path, output.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Packs_probe_cannot_bypass_default_approval()
    {
        using TempDirectory temp = TempDirectory.Create();
        string gerbv = Path.Combine(temp.Path, "gerbv.exe");
        string magick = Path.Combine(temp.Path, "magick.exe");
        File.WriteAllText(gerbv, "not-executed");
        File.WriteAllText(magick, "not-executed");
        using StringWriter output = new();

        int exitCode = CliCommandFactory.Create(output, path => CreateSnapshot(path, temp.Path))
            .Parse(
            [
                "packs", "doctor", "gerber-tiff",
                "--tool-path", "gerbv=" + gerbv, "imagemagick=" + magick,
                "--probe", "--output", "json", "--workspace", temp.Path
            ])
            .Invoke();

        JsonObject root = Assert.IsType<JsonObject>(JsonNode.Parse(output.ToString()));
        Assert.Equal(1, exitCode);
        Assert.Equal("approval-required", root["status"]?.GetValue<string>());
        Assert.DoesNotContain(temp.Path, output.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Packs_doctor_rejects_unknown_dependency_binding()
    {
        using TempDirectory temp = TempDirectory.Create();
        using StringWriter output = new();

        int exitCode = CliCommandFactory.Invoke(
            CliCommandFactory.Create(output, path => CreateSnapshot(path, temp.Path)),
            ["packs", "doctor", "gerber-tiff", "--tool-path", "unknown=C:\\tool.exe"],
            output);

        Assert.Equal(2, exitCode);
        Assert.Contains("Unknown project pack dependency", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Packs_doctor_json_redacts_secret_like_diagnostics()
    {
        ProjectPackDoctorReport report = new(
            "test-pack",
            ProjectPackDoctorStatus.Failed,
            ProbeRequested: false,
            Tools: [],
            Diagnostics:
            [
                new ProjectPackDiagnostic(
                    "test-diagnostic",
                    ProjectPackDiagnosticSeverity.Error,
                    "apiKey=sk-project-pack-secret")
            ]);

        string json = ProjectPackReportRenderer.RenderDoctorJson(report);

        Assert.Contains("apiKey=[redacted]", json, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-project-pack-secret", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Packs_doctor_json_does_not_render_raw_probe_output_or_paths()
    {
        string machinePath = Path.Combine(Path.GetTempPath(), "sensitive-tool", "tool.exe");
        ExternalToolIdentity identity = new(
            "test-tool",
            "tool.exe",
            1,
            DateTimeOffset.UnixEpoch,
            new string('A', 64),
            "--tool-path",
            ExternalToolTrustStatus.Untrusted,
            "succeeded",
            "tool 1.0.0");
        ExternalToolProbeResult probe = new(
            "succeeded",
            identity,
            "approved",
            0,
            0,
            10,
            "version 1.0 at " + machinePath,
            "stderr path " + machinePath,
            false,
            false,
            false,
            false,
            true,
            []);
        ProjectPackToolDoctorResult tool = new(
            "test-tool",
            "Test Tool",
            true,
            ProjectPackDoctorStatus.Ready,
            "--tool-path",
            identity,
            probe,
            []);
        ProjectPackDoctorReport report = new(
            "test-pack",
            ProjectPackDoctorStatus.Ready,
            true,
            [tool],
            []);

        string json = ProjectPackReportRenderer.RenderDoctorJson(report);

        Assert.DoesNotContain(machinePath, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("version 1.0 at", json, StringComparison.Ordinal);
        Assert.Contains("stdoutCharacters", json, StringComparison.Ordinal);
        Assert.Contains("stderrCharacters", json, StringComparison.Ordinal);
    }

    private static CliEnvironmentSnapshot CreateSnapshot(string? workspacePath, string userProfileRoot)
    {
        string workspace = workspacePath ?? userProfileRoot;
        return CliEnvironmentSnapshot.Create(
            workspacePath: workspace,
            currentDirectory: workspace,
            userProfile: Path.Combine(userProfileRoot, "profile"),
            dotnetSdkVersion: "9.0.308",
            dotnetRuntime: ".NET 9",
            openAiApiKey: null,
            openAiModel: null,
            hasGlobalJson: false);
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
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "caicli-pack-cli-tests-" + Guid.NewGuid().ToString("N"));
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
