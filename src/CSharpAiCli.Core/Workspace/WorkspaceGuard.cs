namespace CSharpAiCli.Core;

public sealed class WorkspaceGuard : IWorkspaceGuard
{
    public WorkspaceGuardResult ResolvePath(WorkspaceContext workspace, string requestedPath)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        if (!workspace.IsUsable)
        {
            return WorkspaceGuardResult.Deny(
                "workspace-unavailable",
                "Workspace is not available.");
        }

        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            return WorkspaceGuardResult.Deny(
                "invalid-workspace-path",
                "A workspace path is required.");
        }

        try
        {
            string workspaceRoot = ResolveExistingPath(Path.GetFullPath(workspace.RootPath));
            string requestedFullPath = Path.GetFullPath(requestedPath, workspaceRoot);
            string resolvedPath = ResolveExistingPath(requestedFullPath);

            if (!IsInsideOrEqual(workspaceRoot, resolvedPath))
            {
                return WorkspaceGuardResult.Deny(
                    "workspace-boundary-denied",
                    "Path must remain inside the workspace.");
            }

            return WorkspaceGuardResult.Allow(resolvedPath);
        }
        catch (Exception exception) when (exception is ArgumentException
            or IOException
            or NotSupportedException
            or PathTooLongException
            or UnauthorizedAccessException)
        {
            return WorkspaceGuardResult.Deny(
                "invalid-workspace-path",
                "Workspace path could not be resolved safely.");
        }
    }

    private static bool IsInsideOrEqual(string workspaceRoot, string candidatePath)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        string candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidatePath));
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (string.Equals(root, candidate, comparison))
        {
            return true;
        }

        string rootWithSeparator = root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(rootWithSeparator, comparison);
    }

    private static string ResolveExistingPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string? pathRoot = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(pathRoot))
        {
            return fullPath;
        }

        string relativePath = Path.GetRelativePath(pathRoot, fullPath);
        if (relativePath == ".")
        {
            return fullPath;
        }

        string current = pathRoot;
        foreach (string segment in relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            current = ResolveLinkTarget(current);
        }

        return Path.GetFullPath(current);
    }

    private static string ResolveLinkTarget(string path)
    {
        if (Directory.Exists(path))
        {
            DirectoryInfo directoryInfo = new(path);
            if (directoryInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                FileSystemInfo? target = directoryInfo.ResolveLinkTarget(returnFinalTarget: true);
                if (target is not null)
                {
                    return target.FullName;
                }
            }
        }
        else if (File.Exists(path))
        {
            FileInfo fileInfo = new(path);
            if (fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                FileSystemInfo? target = fileInfo.ResolveLinkTarget(returnFinalTarget: true);
                if (target is not null)
                {
                    return target.FullName;
                }
            }
        }

        return path;
    }
}
