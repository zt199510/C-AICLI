using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CSharpAiCli.Core;
using CSharpAiCli.ProjectPacks;
using CSharpAiCli.ProjectPacks.GerberTiff;

namespace CSharpAiCli.Application;

public static class ApplicationCatalogKind
{
    public const string Skills = "skills";
    public const string Experts = "experts";
    public const string Automations = "automations";
    public const string ProjectPacks = "project-packs";

    public static bool IsKnown(string? value) => value is Skills or Experts or Automations or ProjectPacks;
}

public sealed record CatalogQueryRequest(
    WorkspaceContext Workspace,
    string Catalog,
    int PageSize = ApplicationLimits.DefaultPageSize);

public sealed record CatalogItemProjection(
    string Id,
    string DisplayName,
    string? Version,
    string Description,
    string SourceKind,
    string? SourcePath,
    bool ReadOnly,
    string ToolBoundary,
    IReadOnlyList<string> Capabilities);

public sealed record CatalogQueryProjection
{
    public CatalogQueryProjection(
        string WorkspaceId,
        string Catalog,
        string CatalogRevision,
        IReadOnlyList<CatalogItemProjection>? Items,
        bool Truncated)
    {
        this.WorkspaceId = WorkspaceId;
        this.Catalog = Catalog;
        this.CatalogRevision = CatalogRevision;
        this.Items = new ReadOnlyCollection<CatalogItemProjection>((Items ?? []).ToArray());
        this.Truncated = Truncated;
    }

    public string WorkspaceId { get; }

    public string Catalog { get; }

    public string CatalogRevision { get; }

    public IReadOnlyList<CatalogItemProjection> Items { get; }

    public bool Truncated { get; }
}

public sealed class CatalogApplicationService
{
    private readonly ProjectPackRegistry projectPackRegistry;

    public CatalogApplicationService()
        : this(new ProjectPackRegistry([new GerberTiffWorkflowPack()]))
    {
    }

    internal CatalogApplicationService(ProjectPackRegistry projectPackRegistry)
    {
        this.projectPackRegistry = projectPackRegistry ?? throw new ArgumentNullException(nameof(projectPackRegistry));
    }

    public ApplicationResult<CatalogQueryProjection> Query(
        CatalogQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Workspace);
        cancellationToken.ThrowIfCancellationRequested();

        if (!ApplicationCatalogKind.IsKnown(request.Catalog))
        {
            return ApplicationResult<CatalogQueryProjection>.Failure(new ApplicationError(
                "application-catalog-invalid",
                ApplicationErrorCategory.Validation,
                "Catalog must be skills, experts, automations, or project-packs.",
                Retryable: false));
        }

        ApplicationError? pageError = ApplicationLimits.ValidatePageSize(request.PageSize);
        if (pageError is not null)
        {
            return ApplicationResult<CatalogQueryProjection>.Failure(pageError);
        }

        int readLimit = Math.Min(ApplicationLimits.MaxCatalogItems, request.PageSize) + 1;
        (IReadOnlyList<CatalogItemProjection> Items, IReadOnlyList<ApplicationDiagnostic> Diagnostics) loaded =
            request.Catalog switch
            {
                ApplicationCatalogKind.Skills => LoadSkills(request.Workspace, readLimit, cancellationToken),
                ApplicationCatalogKind.Experts => LoadExperts(readLimit, cancellationToken),
                ApplicationCatalogKind.Automations => LoadAutomations(request.Workspace, readLimit, cancellationToken),
                ApplicationCatalogKind.ProjectPacks => LoadProjectPacks(readLimit, cancellationToken),
                _ => throw new InvalidOperationException("Catalog validation drifted from dispatch.")
            };

        cancellationToken.ThrowIfCancellationRequested();
        bool truncated = loaded.Items.Count > request.PageSize ||
            loaded.Diagnostics.Count > ApplicationLimits.MaxDiagnostics;
        CatalogItemProjection[] items = loaded.Items
            .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Take(request.PageSize)
            .ToArray();
        string workspaceId = CreateWorkspaceId(request.Workspace.RootPath);
        string revision = CreateRevision(workspaceId, request.Catalog, items, loaded.Diagnostics, truncated);
        return ApplicationResult<CatalogQueryProjection>.Success(
            new CatalogQueryProjection(workspaceId, request.Catalog, revision, items, truncated),
            loaded.Diagnostics,
            truncated);
    }

    private static string CreateRevision(
        string workspaceId,
        string catalog,
        IReadOnlyList<CatalogItemProjection> items,
        IReadOnlyList<ApplicationDiagnostic> diagnostics,
        bool truncated)
    {
        byte[] canonical = JsonSerializer.SerializeToUtf8Bytes(new
        {
            workspaceId,
            catalog,
            items = items.Select(item => new
            {
                item.Id,
                item.DisplayName,
                item.Version,
                item.Description,
                item.SourceKind,
                item.SourcePath,
                item.ReadOnly,
                item.ToolBoundary,
                capabilities = item.Capabilities.Order(StringComparer.Ordinal).ToArray()
            }).ToArray(),
            diagnostics = diagnostics.Select(item => new { item.Code, item.Category, item.SafeMessage }).ToArray(),
            truncated
        });
        return Convert.ToHexString(SHA256.HashData(canonical)).ToLowerInvariant();
    }

    private static string CreateWorkspaceId(string rootPath)
    {
        string normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        if (OperatingSystem.IsWindows()) normalized = normalized.ToUpperInvariant();
        return "ws_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)).AsSpan(0, 12)).ToLowerInvariant();
    }

    private static (IReadOnlyList<CatalogItemProjection>, IReadOnlyList<ApplicationDiagnostic>) LoadSkills(
        WorkspaceContext workspace,
        int readLimit,
        CancellationToken cancellationToken)
    {
        SkillPackCatalog catalog = SkillPackCatalog.Load(workspace, readLimit);
        cancellationToken.ThrowIfCancellationRequested();
        CatalogItemProjection[] items = catalog.Packs.Select(item =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            SkillPackManifest manifest = item.Manifest;
            SkillPackSafety safety = manifest.Safety ?? new SkillPackSafety();
            return new CatalogItemProjection(
                ApplicationProjection.Safe(manifest.Name, 256),
                ApplicationProjection.Safe(manifest.Name, 256),
                ApplicationProjection.SafeOrNull(manifest.Version, 128),
                ApplicationProjection.Safe(manifest.Description, 2_048),
                ApplicationProjection.Safe(item.Source.Kind, 64),
                ApplicationProjection.SafeOrNull(item.Source.Path),
                ReadOnly: !safety.AllowWrites,
                ApplicationProjection.Safe(safety.ToSummary(), 1_024),
                []);
        }).ToArray();
        ApplicationDiagnostic[] diagnostics = catalog.Diagnostics.Select(diagnostic =>
            new ApplicationDiagnostic(
                diagnostic.ErrorCode,
                ApplicationProjection.CategoryForCode(diagnostic.ErrorCode),
                diagnostic.Summary)).ToArray();
        return (items, diagnostics);
    }

    private static (IReadOnlyList<CatalogItemProjection>, IReadOnlyList<ApplicationDiagnostic>) LoadExperts(
        int readLimit,
        CancellationToken cancellationToken)
    {
        List<CatalogItemProjection> items = [];
        foreach (string name in ExpertProfileCatalog.Names.Take(readLimit))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ExpertProfileCatalog.TryGet(name, out ExpertProfile? profile) || profile is null)
            {
                continue;
            }

            items.Add(new CatalogItemProjection(
                ApplicationProjection.Safe(profile.Name, 256),
                ApplicationProjection.Safe(profile.DisplayName, 256),
                null,
                ApplicationProjection.Safe(profile.ReportFocus, 2_048),
                "built-in",
                null,
                profile.IsReadOnly,
                ApplicationProjection.Safe(profile.ToolBoundary, 2_048),
                []));
        }

        return (items, []);
    }

    private static (IReadOnlyList<CatalogItemProjection>, IReadOnlyList<ApplicationDiagnostic>) LoadAutomations(
        WorkspaceContext workspace,
        int readLimit,
        CancellationToken cancellationToken)
    {
        AutomationCatalog catalog = AutomationCatalog.Load(workspace, readLimit);
        cancellationToken.ThrowIfCancellationRequested();
        CatalogItemProjection[] items = catalog.Items.Select(item =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            AutomationManifest manifest = item.Manifest;
            AutomationSafety safety = manifest.Safety ?? new AutomationSafety();
            return new CatalogItemProjection(
                ApplicationProjection.Safe(manifest.Name, 256),
                ApplicationProjection.Safe(manifest.Name, 256),
                manifest.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ApplicationProjection.Safe(manifest.Description, 2_048),
                ApplicationProjection.Safe(item.Source.Kind, 64),
                ApplicationProjection.SafeOrNull(item.Source.Path),
                ReadOnly: !safety.AllowWrites,
                ApplicationProjection.Safe(safety.ToSummary(), 1_024),
                string.IsNullOrWhiteSpace(manifest.Target?.Type)
                    ? []
                    : [ApplicationProjection.Safe(manifest.Target.Type, 128)]);
        }).ToArray();
        ApplicationDiagnostic[] diagnostics = catalog.Diagnostics.Select(diagnostic =>
            new ApplicationDiagnostic(
                diagnostic.ErrorCode,
                ApplicationProjection.CategoryForCode(diagnostic.ErrorCode),
                diagnostic.Summary)).ToArray();
        return (items, diagnostics);
    }

    private (IReadOnlyList<CatalogItemProjection>, IReadOnlyList<ApplicationDiagnostic>) LoadProjectPacks(
        int readLimit,
        CancellationToken cancellationToken)
    {
        CatalogItemProjection[] items = projectPackRegistry.List()
            .Take(readLimit)
            .Select(pack =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                ProjectPackManifest manifest = pack.Manifest;
                return new CatalogItemProjection(
                    ApplicationProjection.Safe(manifest.Id, 256),
                    ApplicationProjection.Safe(manifest.DisplayName, 256),
                    ApplicationProjection.SafeOrNull(manifest.Version, 128),
                    ApplicationProjection.Safe(manifest.Description, 2_048),
                    "built-in",
                    null,
                    ReadOnly: false,
                    "workspace guard, approval policy, and declared external-tool boundaries apply",
                    manifest.Capabilities.Select(capability =>
                        ApplicationProjection.Safe(capability.Id, 256)).ToArray());
            })
            .ToArray();
        return (items, []);
    }
}
