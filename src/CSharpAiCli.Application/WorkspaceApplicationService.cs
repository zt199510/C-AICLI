using System.Security.Cryptography;
using System.Text;
using CSharpAiCli.Core;

namespace CSharpAiCli.Application;

public sealed record WorkspaceOpenRequest(string? Path, string? CurrentDirectory = null);

public sealed record WorkspaceOpenApplicationResult(
    bool Success,
    string? WorkspaceId,
    string? RootPath,
    string Status,
    string? ErrorCode,
    string SafeMessage);

public sealed record WorkspaceCapabilityProjection(
    bool ReadOnlyQueries,
    bool GitQueries,
    bool LocalCatalogs,
    bool ManagedArtifacts,
    bool ControlledContext);

public sealed record WorkspaceConfigurationProjection(
    bool HasApiKey,
    string ApiKeySource,
    string EffectiveModel,
    string ModelSource,
    string AgentBackendSource,
    string ApprovalMode,
    string ApprovalModeSource,
    int LoadedSourceCount);

public sealed record WorkspaceSnapshotProjection(
    string WorkspaceId,
    string RootPath,
    string Status,
    WorkspaceCapabilityProjection Capabilities,
    WorkspaceConfigurationProjection Configuration);

public sealed class WorkspaceApplicationService
{
    private readonly IWorkspaceGuard workspaceGuard;

    public WorkspaceApplicationService()
        : this(new WorkspaceGuard())
    {
    }

    public WorkspaceApplicationService(IWorkspaceGuard workspaceGuard)
    {
        this.workspaceGuard = workspaceGuard ?? throw new ArgumentNullException(nameof(workspaceGuard));
    }

    public WorkspaceOpenApplicationResult Open(WorkspaceOpenRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        WorkspaceContext workspace;
        try
        {
            workspace = WorkspaceContext.Detect(request.Path, request.CurrentDirectory);
        }
        catch (Exception exception) when (exception is ArgumentException
            or IOException
            or NotSupportedException
            or PathTooLongException)
        {
            return Failure(ToolErrorCode.InvalidWorkspacePath, "Workspace path could not be resolved safely.");
        }

        if (!workspace.IsUsable)
        {
            string message = workspace.Status == WorkspaceStatus.NotDirectory
                ? "Workspace path must be a directory."
                : "Workspace directory does not exist.";
            return Failure(ToolErrorCode.WorkspaceUnavailable, message, ToStatus(workspace.Status));
        }

        WorkspaceGuardResult guarded = workspaceGuard.ResolvePath(workspace, ".");
        if (!guarded.IsAllowed || string.IsNullOrWhiteSpace(guarded.FullPath))
        {
            return Failure(
                guarded.ErrorCode ?? ToolErrorCode.InvalidWorkspacePath,
                guarded.SafeMessage,
                ToStatus(workspace.Status));
        }

        string rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(guarded.FullPath));
        return new WorkspaceOpenApplicationResult(
            Success: true,
            WorkspaceId: CreateWorkspaceId(rootPath),
            RootPath: rootPath,
            Status: ToStatus(workspace.Status),
            ErrorCode: null,
            SafeMessage: string.Empty);
    }

    public ApplicationResult<WorkspaceSnapshotProjection> Snapshot(
        CliEnvironmentSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();

        WorkspaceOpenApplicationResult opened = Open(new WorkspaceOpenRequest(snapshot.Workspace.RootPath));
        cancellationToken.ThrowIfCancellationRequested();
        if (!opened.Success || opened.WorkspaceId is null || opened.RootPath is null)
        {
            return ApplicationResult<WorkspaceSnapshotProjection>.Failure(new ApplicationError(
                opened.ErrorCode ?? ToolErrorCode.WorkspaceUnavailable,
                ApplicationErrorCategory.Workspace,
                opened.SafeMessage,
                Retryable: false));
        }

        EffectiveConfiguration configuration = snapshot.Configuration;
        WorkspaceSnapshotProjection projection = new(
            opened.WorkspaceId,
            opened.RootPath,
            opened.Status,
            new WorkspaceCapabilityProjection(
                ReadOnlyQueries: true,
                GitQueries: true,
                LocalCatalogs: true,
                ManagedArtifacts: true,
                ControlledContext: true),
            new WorkspaceConfigurationProjection(
                configuration.HasApiKey,
                ApplicationProjection.Safe(configuration.ApiKeySource, 256),
                ApplicationProjection.Safe(configuration.Model, 256),
                ApplicationProjection.Safe(configuration.ModelSource, 256),
                ApplicationProjection.Safe(configuration.AgentBackendSource, 256),
                configuration.ApprovalMode.ToString(),
                ApplicationProjection.Safe(configuration.ApprovalModeSource, 256),
                configuration.LoadedConfigPaths.Count));

        ApplicationDiagnostic[] diagnostics = configuration.Warnings
            .Take(ApplicationLimits.MaxDiagnostics + 1)
            .Select(warning => new ApplicationDiagnostic(
                "configuration-warning",
                ApplicationErrorCategory.Validation,
                warning))
            .ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        return ApplicationResult<WorkspaceSnapshotProjection>.Success(
            projection,
            diagnostics,
            diagnostics.Length > ApplicationLimits.MaxDiagnostics);
    }

    private static WorkspaceOpenApplicationResult Failure(
        string errorCode,
        string safeMessage,
        string status = "invalid") =>
        new(false, null, null, status, errorCode, safeMessage ?? string.Empty);

    private static string CreateWorkspaceId(string rootPath)
    {
        string identityPath = OperatingSystem.IsWindows()
            ? rootPath.ToUpperInvariant()
            : rootPath;
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(identityPath));
        return "ws_" + Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant();
    }

    private static string ToStatus(WorkspaceStatus status) => status switch
    {
        WorkspaceStatus.Ready => "ready",
        WorkspaceStatus.Missing => "missing",
        WorkspaceStatus.NotDirectory => "not-directory",
        _ => "unknown"
    };
}
