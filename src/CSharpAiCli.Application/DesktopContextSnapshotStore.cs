using System.Security.Cryptography;
using CSharpAiCli.Core;

namespace CSharpAiCli.Application;

public sealed record DesktopContextSnapshot(string FullPath, string RelativePath, string ContentSha256, long ByteCount);

public static class DesktopContextSnapshotStore
{
    public const string RelativePrefix = ".caicli-external/";
    private static readonly HashSet<string> DeniedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".com", ".msi", ".msix", ".bat", ".cmd", ".ps1", ".scr", ".zip", ".7z", ".rar", ".tar", ".gz"
    };

    public static DesktopContextSnapshot Create(WorkspaceSnapshotProjection workspace, string threadId, string nativePath, string? storageRoot = null)
    {
        if (!ThreadIdentity.IsThreadId(threadId)) throw new InvalidDataException("An existing task is required for an external attachment.");
        if (nativePath.StartsWith("\\\\", StringComparison.Ordinal) || nativePath.StartsWith("\\\\.\\", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("UNC and device paths cannot be attached.");
        string source = Path.GetFullPath(nativePath);
        FileInfo info = new(source);
        info.Refresh();
        if (!info.Exists || info.Attributes.HasFlag(FileAttributes.Directory) || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException("External context must be a regular file.");
        if (DeniedExtensions.Contains(info.Extension)) throw new InvalidDataException("Executable and archive attachments are not supported.");
        if (info.Length > ComposerIntentLimits.MaxSingleFileBytes) throw new InvalidDataException("External context is larger than 10 MiB.");

        string hash;
        using (FileStream input = new(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            hash = Convert.ToHexStringLower(SHA256.HashData(input));
        string extension = info.Extension.Length <= 16 && info.Extension.All(value => char.IsLetterOrDigit(value) || value == '.') ? info.Extension.ToLowerInvariant() : ".txt";
        string relative = $"{RelativePrefix}{threadId}/{hash}/attachment{extension}";
        string destination = Resolve(workspace, relative, storageRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (!File.Exists(destination))
        {
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using FileStream input = new(source, FileMode.Open, FileAccess.Read, FileShare.Read);
                using FileStream output = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                input.CopyTo(output);
                output.Flush(true);
                output.Close();
                string copiedHash;
                using (FileStream verify = new(temporary, FileMode.Open, FileAccess.Read, FileShare.Read))
                    copiedHash = Convert.ToHexStringLower(SHA256.HashData(verify));
                if (!string.Equals(copiedHash, hash, StringComparison.Ordinal))
                    throw new IOException("External attachment changed while it was copied.");
                File.Move(temporary, destination, overwrite: false);
                File.SetAttributes(destination, File.GetAttributes(destination) | FileAttributes.ReadOnly);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
        return new DesktopContextSnapshot(destination, relative, hash, info.Length);
    }

    public static string Resolve(WorkspaceSnapshotProjection workspace, string relativePath, string? storageRoot = null)
    {
        if (!relativePath.StartsWith(RelativePrefix, StringComparison.Ordinal)) throw new InvalidDataException("Attachment snapshot identity is invalid.");
        string tail = relativePath[RelativePrefix.Length..];
        string[] segments = tail.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 3 || !ThreadIdentity.IsThreadId(segments[0]) || segments[1].Length != 64 || segments[1].Any(value => !Uri.IsHexDigit(value)) || Path.GetFileName(segments[2]) != segments[2])
            throw new InvalidDataException("Attachment snapshot identity is invalid.");
        string root = Path.GetFullPath(Path.Combine(storageRoot ?? DefaultStorageRoot(), workspace.WorkspaceId));
        string result = Path.GetFullPath(Path.Combine(root, segments[0], segments[1], segments[2]));
        if (!result.StartsWith(root + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("Attachment snapshot escaped its managed root.");
        return result;
    }

    public static bool IsExternal(string relativePath) => relativePath.StartsWith(RelativePrefix, StringComparison.Ordinal);

    private static string DefaultStorageRoot()
    {
        string? profile = Environment.GetEnvironmentVariable("CAICLI_USER_PROFILE");
        return profile is null
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "C-AICLI", "desktop-attachments")
            : Path.Combine(profile, ".caicli", "desktop-attachments");
    }
}
