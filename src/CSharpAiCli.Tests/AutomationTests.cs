using System.Text.Json;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class AutomationTests
{
    [Fact]
    public void Manifest_schema_describes_strict_trigger_target_and_safety_contracts()
    {
        using JsonDocument document = JsonDocument.Parse(AutomationManifestJsonSchema.Render());
        JsonElement root = document.RootElement;

        Assert.False(root.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(1, root.GetProperty("properties").GetProperty("schemaVersion").GetProperty("const").GetInt32());
        Assert.False(root.GetProperty("properties").GetProperty("trigger").GetProperty("additionalProperties").GetBoolean());
        Assert.False(root.GetProperty("properties").GetProperty("target").GetProperty("additionalProperties").GetBoolean());
        Assert.True(root.GetProperty("properties").GetProperty("safety").GetProperty("properties").GetProperty("manualOnly").GetProperty("const").GetBoolean());
    }

    [Fact]
    public void Catalog_loads_valid_schedule_preview_without_enabling_a_scheduler()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspaceRoot = temp.CreateDirectory("workspace");
        WriteManifest(workspaceRoot, "nightly-review.json", ValidReviewManifest("Review source"));
        WorkspaceContext workspace = CreateWorkspace(workspaceRoot);

        AutomationCatalog catalog = AutomationCatalog.Load(workspace);
        AutomationCatalogItem item = Assert.Single(catalog.Items);
        AutomationSchedulePreview preview = AutomationManifestValidator.CreateSchedulePreview(item.Manifest.Trigger!);

        Assert.Empty(catalog.Diagnostics);
        Assert.Equal("nightly-review", item.Manifest.Name);
        Assert.Equal("0 2 * * *", preview.Schedule);
        Assert.False(preview.Enabled);
        Assert.Contains("preview only", preview.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Catalog_rejects_unknown_script_fields_and_unsafe_target_capabilities()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspaceRoot = temp.CreateDirectory("workspace");
        WriteManifest(workspaceRoot, "script.json", """
            {
              "schemaVersion": 1,
              "name": "script-target",
              "description": "must fail",
              "trigger": { "type": "manual" },
              "target": { "type": "queue", "family": "exec", "task": "run", "script": "pwsh evil.ps1" },
              "safety": { "manualOnly": true, "allowWrites": true, "allowShell": true, "allowMcp": true }
            }
            """);
        WriteManifest(workspaceRoot, "unsafe.json", """
            {
              "schemaVersion": 1,
              "name": "unsafe-exec",
              "description": "capabilities are understated",
              "trigger": { "type": "manual" },
              "target": { "type": "queue", "family": "exec", "task": "change files" },
              "safety": { "manualOnly": true, "allowWrites": false, "allowShell": false, "allowMcp": false }
            }
            """);

        AutomationCatalog catalog = AutomationCatalog.Load(CreateWorkspace(workspaceRoot));

        Assert.Empty(catalog.Items);
        Assert.Contains(catalog.Diagnostics, diagnostic => diagnostic.ErrorCode == AutomationErrorCode.ManifestInvalid);
        Assert.Contains(catalog.Diagnostics, diagnostic => diagnostic.ErrorCode == AutomationErrorCode.UnsafeTarget);
    }

    internal static string ValidReviewManifest(string task) => $$"""
        {
          "schemaVersion": 1,
          "name": "nightly-review",
          "description": "Review the workspace on a local schedule preview.",
          "trigger": { "type": "schedule", "schedule": "0 2 * * *", "timeZone": "UTC" },
          "target": { "type": "skill", "name": "review-only", "task": {{JsonSerializer.Serialize(task)}}, "report": "none" },
          "safety": { "manualOnly": true, "allowWrites": false, "allowShell": false, "allowMcp": false }
        }
        """;

    internal static string ValidExecManifest(string task) => $$"""
        {
          "schemaVersion": 1,
          "name": "manual-check",
          "description": "Run a manually triggered controlled exec target.",
          "trigger": { "type": "manual" },
          "target": { "type": "queue", "family": "exec", "task": {{JsonSerializer.Serialize(task)}}, "report": "none" },
          "safety": { "manualOnly": true, "allowWrites": true, "allowShell": true, "allowMcp": true }
        }
        """;

    internal static string ValidPipelineManifest(string task) => $$"""
        {
          "schemaVersion": 1,
          "name": "pipeline-check",
          "description": "Run the fixed review and test pipeline manually.",
          "trigger": { "type": "manual" },
          "target": { "type": "pipeline", "name": "review-test", "task": {{JsonSerializer.Serialize(task)}}, "report": "none" },
          "safety": { "manualOnly": true, "allowWrites": true, "allowShell": true, "allowMcp": true }
        }
        """;

    internal static void WriteManifest(string workspaceRoot, string fileName, string json)
    {
        string directory = Path.Combine(workspaceRoot, ".caicli", "automations");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), json);
    }

    private static WorkspaceContext CreateWorkspace(string root) => new(
        root,
        Path.Combine(root, ".caicli", "config.json"),
        WorkspaceStatus.Ready);

    internal sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;

        public string Path { get; }

        public string UserConfigPath => System.IO.Path.Combine(Path, "user", ".caicli", "config.json");

        public static TempDirectory Create()
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "caicli-automation-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public string CreateDirectory(string name)
        {
            string path = System.IO.Path.Combine(Path, name);
            Directory.CreateDirectory(path);
            return path;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
            }
        }
    }
}
