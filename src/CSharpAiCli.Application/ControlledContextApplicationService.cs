using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using CSharpAiCli.Core;

namespace CSharpAiCli.Application;

public sealed record ControlledContextDescriptor(
    string SelectionId,
    string RelativePath,
    string Kind,
    long ByteCount,
    int FileCount,
    string Availability);

public sealed record ControlledContextSearchProjection(
    IReadOnlyList<ControlledContextDescriptor> Items,
    bool Truncated,
    int ScannedEntries);

internal sealed record ControlledContextAuthority(
    string WorkspaceId,
    string WorkspaceRoot,
    string FullPath,
    ComposerContextReferenceRecord Reference,
    bool ExternalSnapshot = false);

public sealed class ControlledContextApplicationService
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp"
    };
    private static readonly HashSet<string> ExcludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".caicli", "node_modules", "test-results", "playwright-report"
    };

    private readonly ConcurrentDictionary<string, ControlledContextAuthority> selections = new(StringComparer.Ordinal);
    private readonly string? externalStorageRoot;

    public ControlledContextApplicationService(string? externalStorageRoot = null) => this.externalStorageRoot = externalStorageRoot;

    public ApplicationResult<ControlledContextSearchProjection> Search(
        WorkspaceSnapshotProjection workspace,
        string query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        query = query?.Trim() ?? string.Empty;
        if (Encoding.UTF8.GetByteCount(query) > ComposerIntentLimits.MaxSearchQueryBytes)
            return Failure<ControlledContextSearchProjection>("context-query-limit", ApplicationErrorCategory.LimitExceeded, "Context search query is too long.");

        try
        {
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace.RootPath));
            EnsureNoReparse(root, root);
            var results = new List<ControlledContextDescriptor>();
            var pending = new Queue<string>();
            pending.Enqueue(root);
            int scanned = 0;
            bool truncated = false;
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string directory = pending.Dequeue();
                foreach (string entry in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.OrdinalIgnoreCase))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++scanned > ComposerIntentLimits.MaxScannedEntries) { truncated = true; pending.Clear(); break; }
                    FileAttributes attributes;
                    try { attributes = File.GetAttributes(entry); }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { continue; }
                    if (attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                    bool isDirectory = attributes.HasFlag(FileAttributes.Directory);
                    if (isDirectory && ExcludedDirectoryNames.Contains(Path.GetFileName(entry))) continue;
                    string relative = NormalizeRelative(root, entry);
                    if (isDirectory) pending.Enqueue(entry);
                    if (query.Length > 0 && !relative.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
                    ApplicationResult<ControlledContextDescriptor> resolved = ResolveCore(workspace, entry, cancellationToken);
                    if (resolved.Succeeded && resolved.Data is not null) results.Add(resolved.Data);
                    if (results.Count >= ComposerIntentLimits.MaxSearchResults) { truncated = true; pending.Clear(); break; }
                }
            }
            return ApplicationResult<ControlledContextSearchProjection>.Success(
                new ControlledContextSearchProjection(results, truncated, Math.Min(scanned, ComposerIntentLimits.MaxScannedEntries)),
                truncated: truncated);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Failure<ControlledContextSearchProjection>("context-search-unavailable", ApplicationErrorCategory.Unavailable, "Workspace context could not be searched.");
        }
    }

    public ApplicationResult<ControlledContextDescriptor> ResolveNativePath(
        WorkspaceSnapshotProjection workspace,
        string nativePath,
        string expectedKind,
        string? threadId = null,
        CancellationToken cancellationToken = default)
    {
        if (!ComposerContextKind.IsKnown(expectedKind))
            return Failure<ControlledContextDescriptor>("context-kind-invalid", ApplicationErrorCategory.Validation, "Context kind is invalid.");
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace.RootPath));
        string full = Path.GetFullPath(nativePath);
        bool contained = string.Equals(root, Path.TrimEndingDirectorySeparator(full), PathComparison()) || full.StartsWith(root + Path.DirectorySeparatorChar, PathComparison());
        ApplicationResult<ControlledContextDescriptor> result = contained
            ? ResolveCore(workspace, nativePath, cancellationToken)
            : ResolveExternal(workspace, nativePath, expectedKind, threadId, cancellationToken);
        if (result.Succeeded && result.Data?.Kind != expectedKind)
            return Failure<ControlledContextDescriptor>("context-kind-mismatch", ApplicationErrorCategory.Validation, "Selected context has the wrong kind.");
        return result;
    }

    public ApplicationResult<ComposerContextReferenceRecord> Revalidate(
        WorkspaceSnapshotProjection workspace,
        string selectionId,
        CancellationToken cancellationToken = default)
    {
        if (!selections.TryGetValue(selectionId, out ControlledContextAuthority? authority) ||
            authority.WorkspaceId != workspace.WorkspaceId ||
            !string.Equals(authority.WorkspaceRoot, Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace.RootPath)), PathComparison()))
        {
            return Failure<ComposerContextReferenceRecord>("context-selection-stale", ApplicationErrorCategory.Conflict, "Context selection is stale; select it again.");
        }
        ApplicationResult<ControlledContextDescriptor> resolved = authority.ExternalSnapshot
            ? RevalidateExternal(workspace, authority, cancellationToken)
            : ResolveCore(workspace, authority.FullPath, cancellationToken, selectionId);
        if (!resolved.Succeeded || resolved.Data is null)
            return Failure<ComposerContextReferenceRecord>(resolved.Error?.Code ?? "context-selection-stale", ApplicationErrorCategory.Conflict, "Context selection changed; select it again.");
        ControlledContextAuthority refreshed = selections[selectionId];
        if (refreshed.Reference != authority.Reference)
        {
            selections.TryRemove(selectionId, out _);
            return Failure<ComposerContextReferenceRecord>("context-selection-stale", ApplicationErrorCategory.Conflict, "Context selection changed; select it again.");
        }
        return ApplicationResult<ComposerContextReferenceRecord>.Success(authority.Reference);
    }

    public void Clear() => selections.Clear();

    private ApplicationResult<ControlledContextDescriptor> ResolveExternal(WorkspaceSnapshotProjection workspace, string nativePath, string expectedKind, string? threadId, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (expectedKind != ComposerContextKind.File) throw new ContextException("context-external-folder-denied", ApplicationErrorCategory.Denied, "External folders cannot be attached.");
            DesktopContextSnapshot snapshot = DesktopContextSnapshotStore.Create(workspace, threadId ?? "", nativePath, externalStorageRoot);
            ValidateFileType(snapshot.FullPath, cancellationToken);
            string selectionId = "ctx_" + Guid.NewGuid().ToString("N");
            ComposerContextReferenceRecord reference = new() { SelectionId = selectionId, RelativePath = snapshot.RelativePath, Kind = ComposerContextKind.File, ByteCount = snapshot.ByteCount, FileCount = 1, ObservedIdentity = snapshot.ContentSha256 };
            selections[selectionId] = new ControlledContextAuthority(workspace.WorkspaceId, Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace.RootPath)), snapshot.FullPath, reference, true);
            return ApplicationResult<ControlledContextDescriptor>.Success(new ControlledContextDescriptor(selectionId, snapshot.RelativePath, ComposerContextKind.File, snapshot.ByteCount, 1, "available"));
        }
        catch (OperationCanceledException) { throw; }
        catch (ContextException exception) { return Failure<ControlledContextDescriptor>(exception.Code, exception.Category, exception.Message); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidDataException)
        {
            return Failure<ControlledContextDescriptor>("context-external-denied", ApplicationErrorCategory.Denied, exception.Message);
        }
    }

    private ApplicationResult<ControlledContextDescriptor> RevalidateExternal(WorkspaceSnapshotProjection workspace, ControlledContextAuthority authority, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            string expected = DesktopContextSnapshotStore.Resolve(workspace, authority.Reference.RelativePath, externalStorageRoot);
            if (!string.Equals(expected, authority.FullPath, PathComparison()) || !File.Exists(expected) || File.GetAttributes(expected).HasFlag(FileAttributes.ReparsePoint))
                throw new ContextException("context-selection-stale", ApplicationErrorCategory.Conflict, "External attachment snapshot is unavailable.");
            using FileStream stream = new(expected, FileMode.Open, FileAccess.Read, FileShare.Read);
            string hash = Convert.ToHexStringLower(SHA256.HashData(stream));
            if (!string.Equals(hash, authority.Reference.ObservedIdentity, StringComparison.Ordinal))
                throw new ContextException("context-selection-stale", ApplicationErrorCategory.Conflict, "External attachment snapshot changed.");
            return ApplicationResult<ControlledContextDescriptor>.Success(new ControlledContextDescriptor(authority.Reference.SelectionId, authority.Reference.RelativePath, authority.Reference.Kind, authority.Reference.ByteCount, authority.Reference.FileCount, "available"));
        }
        catch (OperationCanceledException) { throw; }
        catch (ContextException exception) { return Failure<ControlledContextDescriptor>(exception.Code, exception.Category, exception.Message); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return Failure<ControlledContextDescriptor>("context-selection-stale", ApplicationErrorCategory.Conflict, "External attachment snapshot is unavailable."); }
    }

    private ApplicationResult<ControlledContextDescriptor> ResolveCore(
        WorkspaceSnapshotProjection workspace,
        string path,
        CancellationToken cancellationToken,
        string? stableSelectionId = null)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace.RootPath));
            string full = Path.GetFullPath(path, root);
            EnsureContained(root, full);
            EnsureNoReparse(root, full);
            string relative = NormalizeRelative(root, full);
            EnsureAllowedRelative(relative);
            FileAttributes beforeAttributes = File.GetAttributes(full);
            bool isDirectory = beforeAttributes.HasFlag(FileAttributes.Directory);
            string kind = isDirectory ? ComposerContextKind.Folder : ComposerContextKind.File;
            (long bytes, int files, string identity) = isDirectory
                ? InspectFolder(root, full, cancellationToken)
                : InspectFile(full, cancellationToken);
            EnsureNoReparse(root, full);
            string selectionId = stableSelectionId ?? "ctx_" + Guid.NewGuid().ToString("N");
            ComposerContextReferenceRecord reference = new()
            {
                SelectionId = selectionId,
                RelativePath = relative,
                Kind = kind,
                ByteCount = bytes,
                FileCount = files,
                ObservedIdentity = identity
            };
            selections[selectionId] = new ControlledContextAuthority(workspace.WorkspaceId, root, full, reference);
            return ApplicationResult<ControlledContextDescriptor>.Success(
                new ControlledContextDescriptor(selectionId, relative, kind, bytes, files, "available"));
        }
        catch (OperationCanceledException) { throw; }
        catch (ContextException exception) { return Failure<ControlledContextDescriptor>(exception.Code, exception.Category, exception.Message); }
        catch (FileNotFoundException) { return Failure<ControlledContextDescriptor>("context-not-found", ApplicationErrorCategory.NotFound, "Context item was not found."); }
        catch (DirectoryNotFoundException) { return Failure<ControlledContextDescriptor>("context-not-found", ApplicationErrorCategory.NotFound, "Context item was not found."); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Failure<ControlledContextDescriptor>("context-unavailable", ApplicationErrorCategory.Unavailable, "Context item could not be inspected safely.");
        }
    }

    private static (long Bytes, int Files, string Identity) InspectFile(string full, CancellationToken cancellationToken)
    {
        FileInfo before = new(full);
        before.Refresh();
        if (!before.Exists || before.Attributes.HasFlag(FileAttributes.Directory) || before.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new ContextException("context-file-invalid", ApplicationErrorCategory.Denied, "Context must be a regular file.");
        if (before.Length > ComposerIntentLimits.MaxSingleFileBytes)
            throw new ContextException("context-file-limit", ApplicationErrorCategory.LimitExceeded, "Context file is larger than 10 MiB.");
        ValidateFileType(full, cancellationToken);
        FileInfo after = new(full);
        after.Refresh();
        if (!after.Exists || before.Length != after.Length || before.LastWriteTimeUtc != after.LastWriteTimeUtc || after.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new ContextException("context-selection-stale", ApplicationErrorCategory.Conflict, "Context file changed while it was inspected.");
        return (after.Length, 1, HashIdentity($"file\0{after.Length}\0{after.LastWriteTimeUtc.Ticks}"));
    }

    private static (long Bytes, int Files, string Identity) InspectFolder(string root, string folder, CancellationToken cancellationToken)
    {
        var entries = new List<string>();
        var pending = new Queue<string>();
        pending.Enqueue(folder);
        long bytes = 0;
        int files = 0;
        while (pending.Count > 0)
        {
            string directory = pending.Dequeue();
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileAttributes attributes = File.GetAttributes(entry);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    throw new ContextException("context-reparse-denied", ApplicationErrorCategory.Denied, "Folders containing reparse points cannot be attached.");
                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    if (ExcludedDirectoryNames.Contains(Path.GetFileName(entry))) continue;
                    pending.Enqueue(entry);
                    continue;
                }
                FileInfo file = new(entry);
                ValidateFileType(entry, cancellationToken);
                files++;
                bytes += file.Length;
                if (files > ComposerIntentLimits.MaxFolderFiles || bytes > ComposerIntentLimits.MaxFolderBytes)
                    throw new ContextException("context-folder-limit", ApplicationErrorCategory.LimitExceeded, "Context folder exceeds the file-count or 64 MiB envelope.");
                entries.Add($"{NormalizeRelative(root, entry)}\0{file.Length}\0{file.LastWriteTimeUtc.Ticks}");
            }
        }
        if (files == 0) throw new ContextException("context-folder-empty", ApplicationErrorCategory.Validation, "Context folder contains no supported files.");
        return (bytes, files, HashIdentity("folder\0" + string.Join("\n", entries)));
    }

    private static void ValidateFileType(string path, CancellationToken cancellationToken)
    {
        string extension = Path.GetExtension(path);
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[] header = new byte[Math.Min(4096, checked((int)Math.Min(stream.Length, 4096)))];
        int read = stream.Read(header);
        cancellationToken.ThrowIfCancellationRequested();
        ReadOnlySpan<byte> bytes = header.AsSpan(0, read);
        if (ImageExtensions.Contains(extension))
        {
            bool valid = extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ? bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) :
                extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ? bytes.StartsWith(new byte[] { 255, 216, 255 }) :
                extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) ? bytes.StartsWith("GIF8"u8) :
                extension.Equals(".webp", StringComparison.OrdinalIgnoreCase) && bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8);
            if (!valid) throw new ContextException("context-type-denied", ApplicationErrorCategory.Denied, "Image signature does not match its supported extension.");
            return;
        }
        if (bytes.Contains((byte)0)) throw new ContextException("context-type-denied", ApplicationErrorCategory.Denied, "Binary files are not supported context.");
        try { _ = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { throw new ContextException("context-type-denied", ApplicationErrorCategory.Denied, "Text context must be UTF-8."); }
    }

    private static void EnsureContained(string root, string full)
    {
        string prefix = root + Path.DirectorySeparatorChar;
        if (!string.Equals(root, Path.TrimEndingDirectorySeparator(full), PathComparison()) && !full.StartsWith(prefix, PathComparison()))
            throw new ContextException("context-boundary-denied", ApplicationErrorCategory.Denied, "Context must remain inside the workspace.");
    }

    private static void EnsureNoReparse(string root, string full)
    {
        EnsureContained(root, full);
        string current = root;
        if (File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint))
            throw new ContextException("context-reparse-denied", ApplicationErrorCategory.Denied, "Workspace reparse roots are not supported.");
        string relative = Path.GetRelativePath(root, full);
        if (relative == ".") return;
        foreach (string segment in relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                throw new ContextException("context-reparse-denied", ApplicationErrorCategory.Denied, "Reparse points cannot be attached as context.");
        }
    }

    private static void EnsureAllowedRelative(string relative)
    {
        if (!ComposerIntentContractValidator.IsSafeRelativePath(relative))
            throw new ContextException("context-path-invalid", ApplicationErrorCategory.Validation, "Context relative path is invalid.");
        string first = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)[0];
        if (ExcludedDirectoryNames.Contains(first))
            throw new ContextException("context-private-denied", ApplicationErrorCategory.Denied, "Private or managed workspace data cannot be attached.");
    }

    private static string NormalizeRelative(string root, string path) => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
    private static string HashIdentity(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static StringComparison PathComparison() => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static ApplicationResult<T> Failure<T>(string code, string category, string message) =>
        ApplicationResult<T>.Failure(new ApplicationError(code, category, message, Retryable: category is ApplicationErrorCategory.Unavailable));

    private sealed class ContextException(string code, string category, string message) : Exception(message)
    {
        public string Code { get; } = code;
        public string Category { get; } = category;
    }
}
