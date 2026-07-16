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
