using System.Text.Json;
using CSharpAiCli.Application;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ApplicationFoundationTests
{
    [Fact]
    public void Result_contract_redacts_and_bounds_diagnostics()
    {
        ApplicationDiagnostic[] diagnostics = Enumerable.Range(0, ApplicationLimits.MaxDiagnostics + 1)
            .Select(index => new ApplicationDiagnostic(
                "diagnostic-" + index,
                ApplicationErrorCategory.Validation,
                "apiKey=plain-secret " + new string('x', ApplicationLimits.MaxDiagnosticBytes * 2)))
            .ToArray();

        ApplicationResult<string> result = ApplicationResult<string>.Success("ok", diagnostics);

        Assert.True(result.Succeeded);
        Assert.True(result.Truncated);
        Assert.Equal(ApplicationLimits.MaxDiagnostics, result.Diagnostics.Count);
        Assert.All(result.Diagnostics, diagnostic =>
        {
            Assert.DoesNotContain("plain-secret", diagnostic.SafeMessage, StringComparison.Ordinal);
            Assert.True(System.Text.Encoding.UTF8.GetByteCount(diagnostic.SafeMessage) <= ApplicationLimits.MaxDiagnosticBytes);
        });
        Assert.All(
            new[]
            {
                ApplicationErrorCategory.Validation,
                ApplicationErrorCategory.Workspace,
                ApplicationErrorCategory.NotFound,
                ApplicationErrorCategory.Denied,
                ApplicationErrorCategory.Conflict,
                ApplicationErrorCategory.CorruptState,
                ApplicationErrorCategory.LimitExceeded,
                ApplicationErrorCategory.Unavailable,
                ApplicationErrorCategory.Internal
            },
            category => Assert.True(ApplicationErrorCategory.IsKnown(category)));
        Assert.Equal(
            "apiKey=[redacted]",
            DiagnosticSecretRedactor.Redact(DiagnosticSecretRedactor.Redact("apiKey=plain-secret")));
    }

    [Fact]
    public void Workspace_snapshot_exposes_capabilities_without_secret_values()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspace = temp.CreateDirectory("workspace");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(temp, workspace, "sk-application-secret");

        ApplicationResult<WorkspaceSnapshotProjection> result = new WorkspaceApplicationService().Snapshot(snapshot);
        string json = JsonSerializer.Serialize(result);

        Assert.True(result.Succeeded);
        Assert.True(result.Data?.Capabilities.ReadOnlyQueries);
        Assert.True(result.Data?.Configuration.HasApiKey);
        Assert.DoesNotContain("sk-application-secret", json, StringComparison.Ordinal);
        Assert.Equal(
            new WorkspaceApplicationService().Open(new WorkspaceOpenRequest(workspace)).WorkspaceId,
            result.Data?.WorkspaceId);
    }

    [Fact]
    public void Workspace_and_catalog_queries_honor_pre_cancellation()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspace = temp.CreateDirectory("workspace");
        CliEnvironmentSnapshot snapshot = CreateSnapshot(temp, workspace);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            new WorkspaceApplicationService().Snapshot(snapshot, cancellation.Token));
        Assert.Throws<OperationCanceledException>(() =>
            new CatalogApplicationService().Query(
                new CatalogQueryRequest(snapshot.Workspace, ApplicationCatalogKind.Skills),
                cancellation.Token));
    }

    [Fact]
    public void Catalog_query_projects_all_supported_catalogs_with_stable_order()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspace = temp.CreateDirectory("workspace");
        AutomationTests.WriteManifest(
            workspace,
            "nightly-review.json",
            AutomationTests.ValidReviewManifest("Review apiKey=catalog-secret"));
        CliEnvironmentSnapshot snapshot = CreateSnapshot(temp, workspace);
        CatalogApplicationService service = new();

        foreach (string kind in new[]
        {
            ApplicationCatalogKind.Skills,
            ApplicationCatalogKind.Experts,
            ApplicationCatalogKind.Automations,
            ApplicationCatalogKind.ProjectPacks
        })
        {
            ApplicationResult<CatalogQueryProjection> result = service.Query(
                new CatalogQueryRequest(snapshot.Workspace, kind, 200));

            Assert.True(result.Succeeded);
            Assert.NotEmpty(result.Data!.Items);
            Assert.Equal(
                result.Data.Items.Select(item => item.Id).Order(StringComparer.OrdinalIgnoreCase),
                result.Data.Items.Select(item => item.Id));
            Assert.DoesNotContain("catalog-secret", JsonSerializer.Serialize(result), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Catalog_query_validates_kind_and_page_size()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspace = temp.CreateDirectory("workspace");
        CatalogApplicationService service = new();
        WorkspaceContext context = WorkspaceContext.Detect(workspace);

        ApplicationResult<CatalogQueryProjection> invalidKind = service.Query(
            new CatalogQueryRequest(context, "arbitrary"));
        ApplicationResult<CatalogQueryProjection> invalidPage = service.Query(
            new CatalogQueryRequest(context, ApplicationCatalogKind.Skills, 201));

        Assert.Equal(ApplicationErrorCategory.Validation, invalidKind.Error?.Category);
        Assert.Equal("application-page-size-invalid", invalidPage.Error?.Code);
    }

    [Fact]
    public void Automation_catalog_read_is_bounded_and_reports_truncation()
    {
        using TempDirectory temp = TempDirectory.Create();
        string workspace = temp.CreateDirectory("workspace");
        for (int index = 0; index < 20; index++)
        {
            string name = $"review-{index:D2}";
            string json = $$"""
                {
                  "schemaVersion": 1,
                  "name": "{{name}}",
                  "description": "Bounded review.",
                  "trigger": { "type": "manual" },
                  "target": { "type": "skill", "name": "review-only", "task": "review", "report": "none" },
                  "safety": { "manualOnly": true, "allowWrites": false, "allowShell": false, "allowMcp": false }
                }
                """;
            AutomationTests.WriteManifest(workspace, name + ".json", json);
        }

        ApplicationResult<CatalogQueryProjection> result = new CatalogApplicationService().Query(
            new CatalogQueryRequest(
                WorkspaceContext.Detect(workspace),
                ApplicationCatalogKind.Automations,
                PageSize: 5));

        Assert.True(result.Succeeded);
        Assert.True(result.Truncated);
        Assert.True(result.Data?.Truncated);
        Assert.Equal(5, result.Data?.Items.Count);
        Assert.Equal("review-00", result.Data?.Items[0].Id);
    }

    private static CliEnvironmentSnapshot CreateSnapshot(
        TempDirectory temp,
        string workspace,
        string? apiKey = null) => CliEnvironmentSnapshot.Create(
            workspace,
            currentDirectory: workspace,
            userProfile: temp.CreateDirectory("profile"),
            dotnetSdkVersion: "9.0.308",
            dotnetRuntime: ".NET 9",
            openAiApiKey: apiKey,
            hasGlobalJson: false);

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;

        public string Path { get; }

        public static TempDirectory Create()
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "caicli-application-foundation-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public string CreateDirectory(string relativePath)
        {
            string path = System.IO.Path.Combine(Path, relativePath);
            Directory.CreateDirectory(path);
            return path;
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
