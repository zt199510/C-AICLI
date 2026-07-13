using CSharpAiCli.Core;
using System.Text.Json.Nodes;

namespace CSharpAiCli.Tests;

public sealed class SkillPackTests
{
    [Fact]
    public void Catalog_loads_builtin_dotnet_packs()
    {
        using TempDirectory temp = TempDirectory.Create();
        SkillPackCatalog catalog = SkillPackCatalog.Load(CreateWorkspace(temp.Path));

        string[] names = catalog.Packs.Select(pack => pack.Manifest.Name ?? string.Empty).ToArray();

        Assert.Empty(catalog.Diagnostics);
        Assert.Contains("test-fix", names);
        Assert.Contains("review-only", names);
        Assert.Contains("upgrade-package", names);
        Assert.Contains("doc-sync", names);
        Assert.All(catalog.Packs, pack => Assert.Equal("built-in", pack.Source.Kind));
    }

    [Fact]
    public void Catalog_loads_workspace_local_json_manifest()
    {
        using TempDirectory temp = TempDirectory.Create();
        string skillsDirectory = Path.Combine(temp.Path, ".caicli", "skills");
        Directory.CreateDirectory(skillsDirectory);
        File.WriteAllText(Path.Combine(skillsDirectory, "local-review.json"), """
        {
          "name": "local-review",
          "version": "0.1.0",
          "description": "Local read-only review skill.",
          "entry": {
            "mode": "exec",
            "expert": "reviewer",
            "report": "markdown"
          },
          "references": {
            "suggested": ["@file:README.md"],
            "maxFiles": 5
          },
          "instructions": ["Stay read-only."],
          "safety": {
            "allowWrites": false,
            "allowShell": "none",
            "allowMcp": false
          }
        }
        """);

        SkillPackCatalog catalog = SkillPackCatalog.Load(CreateWorkspace(temp.Path));

        SkillPackCatalogItem local = Assert.Single(catalog.Packs, pack => pack.Manifest.Name == "local-review");
        Assert.Empty(catalog.Diagnostics);
        Assert.Equal("local", local.Source.Kind);
        Assert.Equal(".caicli/skills/local-review.json", local.Source.Path);
    }

    [Fact]
    public void Catalog_reports_invalid_and_duplicate_local_packs_without_hiding_builtins()
    {
        using TempDirectory temp = TempDirectory.Create();
        string skillsDirectory = Path.Combine(temp.Path, ".caicli", "skills");
        Directory.CreateDirectory(skillsDirectory);
        File.WriteAllText(Path.Combine(skillsDirectory, "broken.json"), "{");
        File.WriteAllText(Path.Combine(skillsDirectory, "duplicate.json"), """
        {
          "name": "test-fix",
          "version": "0.1.0",
          "description": "Duplicate built-in.",
          "entry": {
            "mode": "exec",
            "expert": "tester",
            "report": "markdown"
          },
          "safety": {
            "allowWrites": true,
            "allowShell": "verification-only",
            "allowMcp": false
          }
        }
        """);

        SkillPackCatalog catalog = SkillPackCatalog.Load(CreateWorkspace(temp.Path));

        Assert.Contains(catalog.Packs, pack => pack.Manifest.Name == "test-fix" && pack.Source.Kind == "built-in");
        Assert.DoesNotContain(catalog.Packs, pack => pack.Manifest.Name == "test-fix" && pack.Source.Kind == "local");
        Assert.Contains(catalog.Diagnostics, diagnostic => diagnostic.ErrorCode == SkillPackErrorCode.ManifestInvalid);
        Assert.True(catalog.Diagnostics.Count >= 2);
    }

    [Fact]
    public void Validator_rejects_unsupported_version_mcp_and_invalid_reference()
    {
        SkillPackManifest manifest = new()
        {
            Name = "bad-pack",
            Version = "1.0.0",
            Description = "Invalid local pack.",
            Entry = new SkillPackEntry
            {
                Mode = "exec",
                Expert = "reviewer",
                Report = "markdown"
            },
            References = new SkillPackReferences
            {
                Suggested = ["src"],
                MaxFiles = 0
            },
            Safety = new SkillPackSafety
            {
                AllowWrites = false,
                AllowShell = "verification-only",
                AllowMcp = true
            }
        };

        SkillValidationResult result = new SkillPackValidator().Validate(
            manifest,
            SkillPackSource.Local(".caicli/skills/bad-pack.json"));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.ErrorCode == SkillPackErrorCode.VersionUnsupported);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.ErrorCode == SkillPackErrorCode.SafetyPolicyInvalid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Summary.Contains("references.suggested", StringComparison.Ordinal));
    }

    [Fact]
    public void Planner_expands_test_fix_without_auto_loading_suggested_references()
    {
        using TempDirectory temp = TempDirectory.Create();
        SkillPackCatalog catalog = SkillPackCatalog.Load(CreateWorkspace(temp.Path));

        SkillRunPlanResult result = new SkillRunPlanner().Plan(
            catalog,
            new SkillRunRequest("test-fix", "Fix failing tests", null));

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Plan);
        SkillRunPlan plan = result.Plan!;
        Assert.Equal("tester", plan.Expert);
        Assert.Equal("markdown", plan.Report);
        Assert.Equal("dotnet test", plan.ValidationCommand);
        Assert.Contains("@folder:src", plan.SuggestedReferences);
        Assert.DoesNotContain("@folder:src", plan.ExpandedTask, StringComparison.Ordinal);
        Assert.True(plan.ToolBoundary.AllowsRisk(ToolRiskLevel.Shell));
        Assert.False(plan.ToolBoundary.AllowsRisk(ToolRiskLevel.DangerousShell));
    }

    [Fact]
    public void Planner_keeps_review_only_read_only()
    {
        using TempDirectory temp = TempDirectory.Create();
        SkillPackCatalog catalog = SkillPackCatalog.Load(CreateWorkspace(temp.Path));

        SkillRunPlanResult result = new SkillRunPlanner().Plan(
            catalog,
            new SkillRunRequest("review-only", "Review @folder:src", "none"));

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Plan);
        SkillRunPlan plan = result.Plan!;
        Assert.Equal("reviewer", plan.Expert);
        Assert.Equal("none", plan.Report);
        Assert.True(plan.ToolBoundary.IsDisabledByName("workspace.apply_patch"));
        Assert.True(plan.ToolBoundary.IsDisabledByName("workspace.run_shell"));
        Assert.True(plan.ToolBoundary.IsDisabledByPrefix("mcp.anything"));
        Assert.False(plan.ToolBoundary.AllowsRisk(ToolRiskLevel.Write));
    }

    [Fact]
    public void Task_report_renderers_include_skill_metadata()
    {
        AgentTaskReport report = new(
            Status: "success",
            StopReason: "completed",
            Prompt: "review docs",
            Plan: null,
            Tools: [],
            ChangedFiles: [],
            Commands: [],
            Verification: [],
            Risks: [],
            TracePath: "trace.log",
            Skill: new AgentTaskSkillReport(
                Name: "review-only",
                Version: "0.1.0",
                Description: "Review only.",
                SourceKind: "built-in",
                SourcePath: null,
                EntryMode: "exec",
                Expert: "reviewer",
                Report: "markdown",
                SafetySummary: "allowWrites=false allowShell=none allowMcp=false",
                ValidationCommand: null,
                SuggestedReferences: ["@folder:src"]));

        string markdown = new MarkdownTaskReportRenderer().Render(report);
        ExecResult result = ExecResult.Success("done", []).WithTaskReport(
            report,
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        using StringWriter output = new();
        new ExecJsonRenderer(output).WriteResult(result);
        JsonObject json = Assert.IsType<JsonObject>(JsonNode.Parse(output.ToString()));

        Assert.Contains("## Skill", markdown, StringComparison.Ordinal);
        Assert.Contains("review-only", markdown, StringComparison.Ordinal);
        Assert.Equal("review-only", json["payload"]?["taskReport"]?["skill"]?["name"]?.GetValue<string>());
        Assert.Equal("built-in", json["payload"]?["taskReport"]?["skill"]?["sourceKind"]?.GetValue<string>());
    }

    private static WorkspaceContext CreateWorkspace(string path)
    {
        return new WorkspaceContext(
            RootPath: path,
            ConfigPath: Path.Combine(path, ".caicli", "config.json"),
            Status: WorkspaceStatus.Ready);
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
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "caicli-skill-tests-" + Guid.NewGuid().ToString("N"));
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
