namespace CSharpAiCli.Core;

public sealed class ReportPathResolver
{
    private readonly IWorkspaceGuard workspaceGuard;

    public ReportPathResolver()
        : this(new WorkspaceGuard())
    {
    }

    public ReportPathResolver(IWorkspaceGuard workspaceGuard)
    {
        ArgumentNullException.ThrowIfNull(workspaceGuard);
        this.workspaceGuard = workspaceGuard;
    }

    public ReportWriteResult ResolveForWrite(WorkspaceContext workspace, string? requestedPath)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            return ReportWriteResult.Failure(
                "report-path-required",
                "Report path is required.");
        }

        WorkspaceGuardResult guardResult = workspaceGuard.ResolvePath(workspace, requestedPath);
        if (!guardResult.IsAllowed)
        {
            return ReportWriteResult.Failure(
                guardResult.ErrorCode ?? ToolErrorCode.WorkspaceBoundaryDenied,
                guardResult.SafeMessage ?? "Report path must remain inside the workspace.");
        }

        string resolvedPath = guardResult.FullPath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(resolvedPath))
        {
            return ReportWriteResult.Failure(
                "invalid-report-path",
                "Report path could not be resolved safely.");
        }

        if (Directory.Exists(resolvedPath))
        {
            return ReportWriteResult.Failure(
                "report-path-is-directory",
                "Report path must point to a file.",
                resolvedPath);
        }

        if (File.Exists(resolvedPath))
        {
            return ReportWriteResult.Failure(
                "report-path-exists",
                "Report path already exists.",
                resolvedPath);
        }

        return ReportWriteResult.Success(resolvedPath);
    }

    public ReportWriteResult WriteMarkdown(
        WorkspaceContext workspace,
        string? requestedPath,
        string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        ReportWriteResult resolved = ResolveForWrite(workspace, requestedPath);
        if (!resolved.Succeeded || string.IsNullOrWhiteSpace(resolved.Path))
        {
            return resolved;
        }

        try
        {
            string? directory = Path.GetDirectoryName(resolved.Path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using FileStream stream = new(
                resolved.Path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);
            using StreamWriter writer = new(stream);
            writer.Write(markdown);
            return ReportWriteResult.Success(resolved.Path);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException
            or PathTooLongException)
        {
            if (File.Exists(resolved.Path))
            {
                return ReportWriteResult.Failure(
                    "report-path-exists",
                    "Report path already exists.",
                    resolved.Path);
            }

            return ReportWriteResult.Failure(
                "report-write-failed",
                "Markdown report could not be written.",
                resolved.Path);
        }
    }
}
