using CSharpAiCli.Core;
using CSharpAiCli.ProjectPacks.GerberTiff;

namespace CSharpAiCli.Tests;

public sealed class GerberTiffWorkflowPackTests
{
    [Fact]
    public void Status_report_reads_plan_document_fixture_without_hardcoded_path()
    {
        using TempDirectory temp = TempDirectory.Create();
        string plansDirectory = Path.Combine(temp.Path, "docs_md", "plans");
        Directory.CreateDirectory(plansDirectory);
        File.WriteAllText(Path.Combine(plansDirectory, "00_master.md"), "# Master");
        GerberTiffWorkflowPack pack = new();

        GerberTiffStatusReport report = pack.CreateStatusReport(temp.Path);

        Assert.Equal(temp.Path, report.WorkspaceRoot);
        Assert.Equal("plans-found", report.Status);
        Assert.Contains("00_master.md", report.PlanFiles);
        Assert.DoesNotContain(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), report.WorkspaceRoot, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Status_report_handles_missing_plans_directory()
    {
        using TempDirectory temp = TempDirectory.Create();
        GerberTiffWorkflowPack pack = new();

        GerberTiffStatusReport report = pack.CreateStatusReport(temp.Path);

        Assert.Equal("plans-missing", report.Status);
        Assert.Empty(report.PlanFiles);
    }

    [Fact]
    public void Validation_suggestion_uses_configured_profile_command_and_path()
    {
        WorkflowRegistry registry = new(new WorkflowConfiguration(
            [
                new WorkflowProfile(
                    Name: GerberTiffWorkflowPack.ProfileName,
                    WorkspacePath: "configured-gerber-root",
                    ValidationCommand: "ctest --preset windows",
                    Description: "Gerber/TIFF validation",
                    Source: "workspace config")
            ]));
        WorkspaceContext workspace = new("cli-root", Path.Combine("cli-root", ".caicli", "config.json"), WorkspaceStatus.Ready);
        GerberTiffWorkflowPack pack = new();

        WorkflowValidationSuggestion suggestion = pack.CreateValidationSuggestion(registry, workspace);

        Assert.Equal("configured-gerber-root", suggestion.WorkspacePath);
        Assert.Equal("profile", suggestion.WorkspacePathSource);
        Assert.Equal("ctest --preset windows", suggestion.ValidationCommand);
        Assert.True(suggestion.RequiresApproval);
    }

    [Fact]
    public void Validation_suggestion_falls_back_to_workspace_when_profile_missing()
    {
        WorkflowRegistry registry = new(WorkflowConfiguration.Empty);
        WorkspaceContext workspace = new("cli-root", Path.Combine("cli-root", ".caicli", "config.json"), WorkspaceStatus.Ready);
        GerberTiffWorkflowPack pack = new();

        WorkflowValidationSuggestion suggestion = pack.CreateValidationSuggestion(registry, workspace);

        Assert.Equal("cli-root", suggestion.WorkspacePath);
        Assert.Equal("--workspace", suggestion.WorkspacePathSource);
        Assert.Null(suggestion.ValidationCommand);
        Assert.False(suggestion.RequiresApproval);
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
