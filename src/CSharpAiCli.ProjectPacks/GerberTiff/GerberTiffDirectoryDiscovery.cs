using System.Collections.ObjectModel;
using CSharpAiCli.Core;

namespace CSharpAiCli.ProjectPacks.GerberTiff;

public static class GerberTiffDiagnosticCode
{
    public const string InputPathRequired = "input-path-required";
    public const string InputPathGlobDenied = "input-path-glob-denied";
    public const string InputPathTraversalDenied = "input-path-traversal-denied";
    public const string InputPathUrlDenied = "input-path-url-denied";
    public const string InputPathNetworkDenied = "input-path-network-denied";
    public const string InputPathDeviceDenied = "input-path-device-denied";
    public const string InputOutsideWorkspace = "input-outside-workspace";
    public const string InputDirectoryNotFound = "input-directory-not-found";
    public const string InputNotDirectory = "input-not-directory";
    public const string InputReparsePoint = "input-reparse-point";
    public const string InputDepthLimitExceeded = "input-depth-limit-exceeded";
    public const string InputFileCountLimitExceeded = "input-file-count-limit-exceeded";
    public const string InputDirectoryCountLimitExceeded = "input-directory-count-limit-exceeded";
    public const string InputPathLengthLimitExceeded = "input-path-length-limit-exceeded";
    public const string InputScanTimeLimitExceeded = "input-scan-time-limit-exceeded";
    public const string InputEnumerationFailed = "input-enumeration-failed";
    public const string InputEntryMetadataFailed = "input-entry-metadata-failed";
    public const string InputDuplicatePath = "input-duplicate-path";
    public const string InputCaseCollision = "input-case-collision";
    public const string InputUnknownExtension = "input-unknown-extension";
    public const string InputLayerAmbiguous = "input-layer-ambiguous";
    public const string InputLayerDuplicate = "input-layer-duplicate";
    public const string InputGerberRequired = "input-gerber-required";
    public const string InputFileEmpty = "input-file-empty";
    public const string InputFileSizeLimitExceeded = "input-file-size-limit-exceeded";
    public const string InputTotalBytesLimitExceeded = "input-total-bytes-limit-exceeded";
    public const string InputFileUnreadable = "input-file-unreadable";
    public const string InputChanged = "input-changed";
    public const string OutputPathRequired = "output-path-required";
    public const string OutputPathGlobDenied = "output-path-glob-denied";
    public const string OutputPathTraversalDenied = "output-path-traversal-denied";
    public const string OutputPathUrlDenied = "output-path-url-denied";
    public const string OutputPathNetworkDenied = "output-path-network-denied";
    public const string OutputPathDeviceDenied = "output-path-device-denied";
    public const string OutputOutsideWorkspace = "output-outside-workspace";
    public const string OutputReparsePoint = "output-reparse-point";
    public const string OutputPathLengthLimitExceeded = "output-path-length-limit-exceeded";
    public const string OutputAlreadyExists = "output-already-exists";
    public const string OutputInsideInput = "output-inside-input";
    public const string OutputParentNotDirectory = "output-parent-not-directory";
}

internal sealed record GerberTiffResolvedWorkspacePath(
    string FullPath,
    string WorkspaceRelativePath);

internal sealed record GerberTiffScannedFile(
    string FullPath,
    string WorkspaceRelativePath,
    int Depth);

internal sealed record GerberTiffDirectoryScanResult(
    GerberTiffResolvedWorkspacePath? Input,
    IReadOnlyList<GerberTiffScannedFile> Files,
    IReadOnlyList<ProjectPackDiagnostic> Diagnostics)
{
    public bool Succeeded =>
        Input is not null &&
        Diagnostics.All(diagnostic => diagnostic.Severity != ProjectPackDiagnosticSeverity.Error);
}

internal sealed class GerberTiffWorkspacePathPolicy
{
    private readonly WorkspaceGuard workspaceGuard = new();

    public GerberTiffResolvedWorkspacePath? ResolveInputDirectory(
        WorkspaceContext workspace,
        string? requestedPath,
        ICollection<ProjectPackDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            diagnostics.Add(Error(GerberTiffDiagnosticCode.InputPathRequired, "An explicit input directory is required."));
            return null;
        }

        string path = requestedPath.Trim();
        if (IsDevicePath(path))
        {
            diagnostics.Add(Error(GerberTiffDiagnosticCode.InputPathDeviceDenied, "Device paths are not supported input."));
            return null;
        }

        if (IsNetworkPath(path) || IsNetworkPath(workspace.RootPath))
        {
            diagnostics.Add(Error(GerberTiffDiagnosticCode.InputPathNetworkDenied, "UNC and network paths are not supported input."));
            return null;
        }

        if (path.Contains("://", StringComparison.Ordinal))
        {
            diagnostics.Add(Error(GerberTiffDiagnosticCode.InputPathUrlDenied, "URL input is not supported."));
            return null;
        }

        if (path.IndexOfAny(['*', '?']) >= 0)
        {
            diagnostics.Add(Error(GerberTiffDiagnosticCode.InputPathGlobDenied, "Glob input is not supported; provide one directory."));
            return null;
        }

        if (ContainsParentTraversal(path))
        {
            diagnostics.Add(Error(GerberTiffDiagnosticCode.InputPathTraversalDenied, "Parent traversal is not supported input."));
            return null;
        }

        string lexicalFullPath;
        try
        {
            lexicalFullPath = Path.GetFullPath(path, workspace.RootPath);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException or PathTooLongException)
        {
            diagnostics.Add(Error(GerberTiffDiagnosticCode.InputDirectoryNotFound, "Input directory could not be resolved safely."));
            return null;
        }

        WorkspaceGuardResult guarded = workspaceGuard.ResolvePath(workspace, path);
        if (!guarded.IsAllowed || guarded.FullPath is null)
        {
            string code = guarded.ErrorCode == ToolErrorCode.WorkspaceBoundaryDenied
                ? GerberTiffDiagnosticCode.InputOutsideWorkspace
                : GerberTiffDiagnosticCode.InputDirectoryNotFound;
            diagnostics.Add(Error(code, "Input directory could not be resolved inside the workspace."));
            return null;
        }

        if (File.Exists(guarded.FullPath))
        {
            diagnostics.Add(Error(GerberTiffDiagnosticCode.InputNotDirectory, "Input must be an explicit directory, not a file."));
            return null;
        }

        if (!Directory.Exists(guarded.FullPath))
        {
            diagnostics.Add(Error(GerberTiffDiagnosticCode.InputDirectoryNotFound, "Input directory does not exist."));
            return null;
        }

        if (HasReparsePointInExistingChain(workspace.RootPath) ||
            HasReparsePointInExistingChain(lexicalFullPath) ||
            HasReparsePointInExistingChain(guarded.FullPath))
        {
            diagnostics.Add(Error(GerberTiffDiagnosticCode.InputReparsePoint, "Input paths containing reparse points are not supported."));
            return null;
        }

        string workspaceRoot = Path.GetFullPath(workspace.RootPath);
        string relativePath = NormalizeRelativePath(Path.GetRelativePath(workspaceRoot, guarded.FullPath));
        if (relativePath.Length > GerberTiffInputEnvelope.MaxRelativePathCharacters)
        {
            diagnostics.Add(Error(GerberTiffDiagnosticCode.InputPathLengthLimitExceeded, "Input directory path exceeds the v1 relative path limit."));
            return null;
        }

        return new GerberTiffResolvedWorkspacePath(Path.GetFullPath(guarded.FullPath), relativePath);
    }

    public GerberTiffResolvedWorkspacePath? ResolveOutputDirectory(
        WorkspaceContext workspace,
        string? requestedPath,
        string inputFullPath,
        ICollection<ProjectPackDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputFullPath);
        ArgumentNullException.ThrowIfNull(diagnostics);
        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            diagnostics.Add(Error(GerberTiffDiagnosticCode.OutputPathRequired, "An explicit output directory is required."));
            return null;
        }

        string path = requestedPath.Trim();
        if (IsDevicePath(path))
        {
            diagnostics.Add(Error(GerberTiffDiagnosticCode.OutputPathDeviceDenied, "Device paths are not supported output."));
            return null;
        }

        if (IsNetworkPath(path) || IsNetworkPath(workspace.RootPath))
        {
            diagnostics.Add(Error(GerberTiffDiagnosticCode.OutputPathNetworkDenied, "UNC and network paths are not supported output."));
            return null;
        }

        if (path.Contains("://", StringComparison.Ordinal))
        {
            diagnostics.Add(Error(GerberTiffDiagnosticCode.OutputPathUrlDenied, "URL output is not supported."));
            return null;
        }

        if (path.IndexOfAny(['*', '?']) >= 0)
        {
            diagnostics.Add(Error(GerberTiffDiagnosticCode.OutputPathGlobDenied, "Glob output is not supported; provide one directory."));
            return null;
        }

        if (ContainsParentTraversal(path))
        {
            diagnostics.Add(Error(GerberTiffDiagnosticCode.OutputPathTraversalDenied, "Parent traversal is not supported output."));
            return null;
        }

        string lexicalFullPath;
        try
        {
            lexicalFullPath = Path.GetFullPath(path, workspace.RootPath);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException or PathTooLongException)
        {
            diagnostics.Add(Error(GerberTiffDiagnosticCode.OutputOutsideWorkspace, "Output directory could not be resolved safely."));
            return null;
        }

        WorkspaceGuardResult guarded = workspaceGuard.ResolvePath(workspace, path);
        if (!guarded.IsAllowed || guarded.FullPath is null)
        {
            diagnostics.Add(Error(
                GerberTiffDiagnosticCode.OutputOutsideWorkspace,
                "Output directory could not be resolved inside the workspace."));
            return null;
        }

        if (Directory.Exists(guarded.FullPath) || File.Exists(guarded.FullPath))
        {
            diagnostics.Add(Error(
                GerberTiffDiagnosticCode.OutputAlreadyExists,
                "Output directory already exists; v1 never overwrites an existing target."));
            return null;
        }

        if (HasReparsePointInExistingChain(lexicalFullPath) ||
            HasReparsePointInExistingChain(guarded.FullPath))
        {
            diagnostics.Add(Error(
                GerberTiffDiagnosticCode.OutputReparsePoint,
                "Output paths containing reparse points are not supported."));
            return null;
        }

        string outputFullPath = Path.GetFullPath(guarded.FullPath);
        string? existingAncestor = Path.GetDirectoryName(lexicalFullPath);
        while (!string.IsNullOrWhiteSpace(existingAncestor) &&
            !Directory.Exists(existingAncestor) &&
            !File.Exists(existingAncestor))
        {
            existingAncestor = Path.GetDirectoryName(existingAncestor);
        }

        if (!string.IsNullOrWhiteSpace(existingAncestor) && File.Exists(existingAncestor))
        {
            diagnostics.Add(Error(
                GerberTiffDiagnosticCode.OutputParentNotDirectory,
                "An existing output path ancestor is not a directory."));
            return null;
        }

        if (IsInsideOrEqual(inputFullPath, outputFullPath))
        {
            diagnostics.Add(Error(
                GerberTiffDiagnosticCode.OutputInsideInput,
                "Output directory must not be inside the input directory."));
            return null;
        }

        string relativePath = NormalizeRelativePath(Path.GetRelativePath(workspace.RootPath, outputFullPath));
        if (relativePath.Length > GerberTiffInputEnvelope.MaxRelativePathCharacters)
        {
            diagnostics.Add(Error(
                GerberTiffDiagnosticCode.OutputPathLengthLimitExceeded,
                "Output directory exceeds the v1 relative path limit."));
            return null;
        }

        return new GerberTiffResolvedWorkspacePath(outputFullPath, relativePath);
    }

    public static bool HasReparsePointInExistingChain(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string? root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            return true;
        }

        string current = root;
        string relative = Path.GetRelativePath(root, fullPath);
        if (relative == ".")
        {
            return HasReparsePoint(current);
        }

        foreach (string segment in relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!Directory.Exists(current) && !File.Exists(current))
            {
                break;
            }

            if (HasReparsePoint(current))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsNetworkPath(string path) =>
        path.StartsWith("\\\\", StringComparison.Ordinal) ||
        path.StartsWith("//", StringComparison.Ordinal);

    public static bool IsDevicePath(string path) =>
        path.StartsWith("\\\\?\\", StringComparison.Ordinal) ||
        path.StartsWith("\\\\.\\", StringComparison.Ordinal) ||
        path.StartsWith("//?/", StringComparison.Ordinal) ||
        path.StartsWith("//./", StringComparison.Ordinal);

    public static string NormalizeRelativePath(string path) =>
        path == "." ? "." : path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

    private static bool ContainsParentTraversal(string path) =>
        path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment == "..");

    private static bool HasReparsePoint(string path)
    {
        try
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static bool IsInsideOrEqual(string rootPath, string candidatePath)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        string candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidatePath));
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(root, candidate, comparison) ||
            candidate.StartsWith(root + Path.DirectorySeparatorChar, comparison);
    }

    private static ProjectPackDiagnostic Error(string code, string summary) =>
        new(code, ProjectPackDiagnosticSeverity.Error, summary);
}

internal sealed class GerberTiffBoundedDirectoryScanner
{
    private readonly GerberTiffWorkspacePathPolicy pathPolicy = new();

    public GerberTiffDirectoryScanResult Scan(
        WorkspaceContext workspace,
        string? requestedPath,
        Func<long> elapsedMilliseconds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(elapsedMilliseconds);
        List<ProjectPackDiagnostic> diagnostics = [];
        GerberTiffResolvedWorkspacePath? input = pathPolicy.ResolveInputDirectory(workspace, requestedPath, diagnostics);
        if (input is null)
        {
            return Result(input, [], diagnostics);
        }

        Queue<(string FullPath, int Depth)> directories = new();
        directories.Enqueue((input.FullPath, 0));
        int directoryCount = 1;
        int fileCount = 0;
        List<GerberTiffScannedFile> files = [];
        HashSet<string> canonicalPaths = new(StringComparer.Ordinal);
        Dictionary<string, string> relativePaths = new(StringComparer.OrdinalIgnoreCase);
        bool stop = false;

        while (directories.Count > 0 && !stop)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsTimedOut(elapsedMilliseconds, diagnostics))
            {
                break;
            }

            (string directory, int depth) = directories.Dequeue();
            IEnumerator<string>? enumerator = null;
            try
            {
                enumerator = Directory.EnumerateFileSystemEntries(directory).GetEnumerator();
                while (enumerator.MoveNext())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (IsTimedOut(elapsedMilliseconds, diagnostics))
                    {
                        stop = true;
                        break;
                    }

                    string entry = enumerator.Current;
                    string relativePath = GerberTiffWorkspacePathPolicy.NormalizeRelativePath(
                        Path.GetRelativePath(workspace.RootPath, entry));
                    if (relativePath.Length > GerberTiffInputEnvelope.MaxRelativePathCharacters)
                    {
                        diagnostics.Add(Error(
                            GerberTiffDiagnosticCode.InputPathLengthLimitExceeded,
                            "An input entry exceeds the v1 relative path limit."));
                        continue;
                    }

                    FileAttributes attributes;
                    try
                    {
                        attributes = File.GetAttributes(entry);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        diagnostics.Add(Error(
                            GerberTiffDiagnosticCode.InputEntryMetadataFailed,
                            "An input entry could not be inspected safely."));
                        continue;
                    }

                    if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        diagnostics.Add(Error(
                            GerberTiffDiagnosticCode.InputReparsePoint,
                            "Reparse points are not supported in the input tree."));
                        continue;
                    }

                    string canonicalPath = Path.GetFullPath(entry);
                    if (!canonicalPaths.Add(canonicalPath))
                    {
                        diagnostics.Add(Error(
                            GerberTiffDiagnosticCode.InputDuplicatePath,
                            "The input tree contains a duplicate canonical path."));
                        continue;
                    }

                    if (relativePaths.TryGetValue(relativePath, out string? existingRelativePath) &&
                        !string.Equals(existingRelativePath, relativePath, StringComparison.Ordinal))
                    {
                        diagnostics.Add(Error(
                            GerberTiffDiagnosticCode.InputCaseCollision,
                            "The input tree contains a case-insensitive path collision."));
                        continue;
                    }

                    relativePaths[relativePath] = relativePath;
                    if (attributes.HasFlag(FileAttributes.Directory))
                    {
                        int childDepth = depth + 1;
                        if (childDepth > GerberTiffInputEnvelope.MaxRecursionDepth)
                        {
                            diagnostics.Add(Error(
                                GerberTiffDiagnosticCode.InputDepthLimitExceeded,
                                "The input tree exceeds the v1 recursion depth limit."));
                            continue;
                        }

                        directoryCount++;
                        if (directoryCount > GerberTiffInputEnvelope.MaxDirectoryCount)
                        {
                            diagnostics.Add(Error(
                                GerberTiffDiagnosticCode.InputDirectoryCountLimitExceeded,
                                "The input tree exceeds the v1 directory count limit."));
                            stop = true;
                            break;
                        }

                        directories.Enqueue((canonicalPath, childDepth));
                        continue;
                    }

                    fileCount++;
                    if (fileCount > GerberTiffInputEnvelope.MaxFileCount)
                    {
                        diagnostics.Add(Error(
                            GerberTiffDiagnosticCode.InputFileCountLimitExceeded,
                            "The input tree exceeds the v1 file count limit."));
                        stop = true;
                        break;
                    }

                    files.Add(new GerberTiffScannedFile(canonicalPath, relativePath, depth));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(Error(
                    GerberTiffDiagnosticCode.InputEnumerationFailed,
                    "An input directory could not be enumerated safely."));
            }
            finally
            {
                enumerator?.Dispose();
            }
        }

        return Result(input, files, diagnostics);
    }

    private static bool IsTimedOut(Func<long> elapsedMilliseconds, ICollection<ProjectPackDiagnostic> diagnostics)
    {
        if (elapsedMilliseconds() <= GerberTiffInputEnvelope.ScanTimeoutMilliseconds)
        {
            return false;
        }

        if (!diagnostics.Any(diagnostic => diagnostic.Code == GerberTiffDiagnosticCode.InputScanTimeLimitExceeded))
        {
            diagnostics.Add(Error(
                GerberTiffDiagnosticCode.InputScanTimeLimitExceeded,
                "Input discovery exceeded the v1 scan time limit."));
        }

        return true;
    }

    private static GerberTiffDirectoryScanResult Result(
        GerberTiffResolvedWorkspacePath? input,
        IEnumerable<GerberTiffScannedFile> files,
        IEnumerable<ProjectPackDiagnostic> diagnostics) =>
        new(
            input,
            new ReadOnlyCollection<GerberTiffScannedFile>(files.ToArray()),
            new ReadOnlyCollection<ProjectPackDiagnostic>(diagnostics.ToArray()));

    private static ProjectPackDiagnostic Error(string code, string summary) =>
        new(code, ProjectPackDiagnosticSeverity.Error, summary);
}
