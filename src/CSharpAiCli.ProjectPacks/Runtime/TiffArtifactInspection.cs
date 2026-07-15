using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Security.Cryptography;
using CSharpAiCli.Core;
using CSharpAiCli.ProjectPacks.GerberTiff;
using ImageMagick;

namespace CSharpAiCli.ProjectPacks.Runtime;

public sealed record TiffInventoryItem(
    string ArtifactId,
    string RelativePath,
    string FullPath,
    long Size,
    string Sha256,
    string ByteOrder);

public sealed record TiffInventoryResult(
    bool Succeeded,
    IReadOnlyList<TiffInventoryItem> Items,
    IReadOnlyList<TiffVerificationDiagnostic> Diagnostics);

public sealed class TiffArtifactInventoryService
{
    public TiffInventoryResult Revalidate(
        ProjectPackRunRecord record,
        GerberTiffRunPlanSnapshot plan,
        ProjectPackInputManifest manifest,
        ManagedProjectPackRunLayout layout,
        WorkspaceContext workspace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(workspace);
        List<TiffVerificationDiagnostic> diagnostics = [];
        List<TiffInventoryItem> items = [];
        try
        {
            if (record.State is not ProjectPackRunState.Verifying and not ProjectPackRunState.AwaitingAcceptance ||
                record.PlanId != plan.PlanId ||
                !record.PlanFingerprint.Equals(plan.Fingerprint, StringComparison.OrdinalIgnoreCase) ||
                manifest.RunId != record.RunId || manifest.PlanId != plan.PlanId ||
                !manifest.PlanFingerprint.Equals(plan.Fingerprint, StringComparison.OrdinalIgnoreCase))
            {
                return Failure(TiffVerificationErrorCode.RunStateInvalid,
                    "TIFF verification requires one matching verifying or awaiting-acceptance run.");
            }

            ProjectPackStagedInput[] inputs = manifest.Inputs
                .Where(input => input.PassedToExternalTool)
                .OrderBy(input => input.SourceRelativePath, StringComparer.Ordinal)
                .ToArray();
            ProjectPackRunArtifactPointer[] pointers = record.Artifacts
                .Where(artifact => artifact.Kind == "tiff-output")
                .OrderBy(artifact => artifact.Id, StringComparer.Ordinal)
                .ToArray();
            if (inputs.Length == 0 || pointers.Length != inputs.Length ||
                pointers.Select(pointer => pointer.Id).Distinct(StringComparer.Ordinal).Count() != pointers.Length)
            {
                return Failure(TiffVerificationErrorCode.OutputDuplicate,
                    "Declared TIFF output pointers are missing, duplicate, or do not match the frozen input count.");
            }

            WorkspaceGuardResult outputGuard = new WorkspaceGuard().ResolvePath(workspace, plan.OutputDirectory);
            if (!outputGuard.IsAllowed || outputGuard.FullPath is null || !Directory.Exists(outputGuard.FullPath))
            {
                return Failure(TiffVerificationErrorCode.OutputMissing,
                    "Declared TIFF output directory is missing or outside the workspace.");
            }

            ManagedProjectPackRunStore.EnsureNoReparseInExistingChain(outputGuard.FullPath);
            HashSet<string> expectedFullPaths = new(PathComparer);
            for (int index = 0; index < inputs.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ProjectPackStagedInput input = inputs[index];
                ProjectPackRunArtifactPointer pointer = pointers[index];
                string expectedId = $"tiff-{index + 1:D4}";
                string fileName = GerberTiffArtifactNaming.TiffFileName(index + 1, input.SourceRelativePath);
                string expectedRelative = NormalizeRelative(Path.Combine(plan.OutputDirectory, fileName));
                if (pointer.Id != expectedId || pointer.Scope != "workspace-output" || pointer.Path != expectedRelative ||
                    !pointer.Exists || pointer.Size is null || pointer.Sha256 is null)
                {
                    diagnostics.Add(Error(TiffVerificationErrorCode.InventoryInvalid,
                        "TIFF output pointer does not match the deterministic declared inventory.", pointer.Id));
                    continue;
                }

                string fullPath = Path.GetFullPath(Path.Combine(workspace.RootPath,
                    pointer.Path.Replace('/', Path.DirectorySeparatorChar)));
                if (!IsWithin(outputGuard.FullPath, fullPath) || !expectedFullPaths.Add(fullPath))
                {
                    diagnostics.Add(Error(TiffVerificationErrorCode.OutputDuplicate,
                        "TIFF output path is duplicate or escaped its declared output directory.", pointer.Id));
                    continue;
                }

                TiffVerificationDiagnostic? sourceDiagnostic = VerifySourceAndStaging(input, layout, workspace);
                if (sourceDiagnostic is not null)
                {
                    diagnostics.Add(sourceDiagnostic);
                    continue;
                }

                TiffFileIdentityResult identity = TiffFileIdentityReader.Read(
                    fullPath, pointer.Size.Value, pointer.Sha256, cancellationToken);
                if (!identity.Succeeded)
                {
                    diagnostics.Add(identity.Diagnostic! with { ArtifactId = pointer.Id });
                    continue;
                }

                items.Add(new TiffInventoryItem(
                    pointer.Id,
                    pointer.Path,
                    fullPath,
                    identity.Size,
                    identity.Sha256!,
                    identity.ByteOrder!));
            }

            foreach (string entry in Directory.EnumerateFileSystemEntries(
                outputGuard.FullPath, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string fullEntry = Path.GetFullPath(entry);
                if (Directory.Exists(fullEntry) || !expectedFullPaths.Contains(fullEntry))
                {
                    diagnostics.Add(Error(TiffVerificationErrorCode.OutputUnexpected,
                        "Declared TIFF output directory contains an unexpected file or directory."));
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            diagnostics.Add(Error(TiffVerificationErrorCode.DecodeTimedOut, "TIFF output inventory was canceled."));
        }
        catch (ProjectPackContractException exception)
        {
            diagnostics.Add(Error(TiffVerificationErrorCode.InventoryInvalid, exception.Message));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            diagnostics.Add(Error(TiffVerificationErrorCode.InventoryInvalid,
                "TIFF output inventory could not be read within its boundary."));
        }

        bool succeeded = diagnostics.All(diagnostic => diagnostic.Severity != "error") && items.Count > 0;
        return new TiffInventoryResult(
            succeeded,
            new ReadOnlyCollection<TiffInventoryItem>(items.OrderBy(item => item.ArtifactId, StringComparer.Ordinal).ToArray()),
            new ReadOnlyCollection<TiffVerificationDiagnostic>(diagnostics.Take(TiffVerificationLimits.MaxDiagnosticCount).ToArray()));
    }

    private static TiffVerificationDiagnostic? VerifySourceAndStaging(
        ProjectPackStagedInput input,
        ManagedProjectPackRunLayout layout,
        WorkspaceContext workspace)
    {
        WorkspaceGuardResult sourceGuard = new WorkspaceGuard().ResolvePath(workspace, input.SourceRelativePath);
        string staged = Path.GetFullPath(Path.Combine(layout.RunRoot,
            input.StagedRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!sourceGuard.IsAllowed || sourceGuard.FullPath is null ||
            !IsWithin(layout.StagingPath, staged) ||
            !SameFileIdentity(sourceGuard.FullPath, input.Size, input.Sha256) ||
            !SameFileIdentity(staged, input.Size, input.Sha256))
        {
            return Error(TiffVerificationErrorCode.OutputChanged,
                "Source or staged input changed after controlled conversion.", input.Id);
        }

        return null;
    }

    private static bool SameFileIdentity(string path, long expectedSize, string expectedSha256)
    {
        try
        {
            ManagedProjectPackRunStore.EnsureNoReparseInExistingChain(path);
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
            return stream.Length == expectedSize &&
                Convert.ToHexString(SHA256.HashData(stream)).Equals(expectedSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private static TiffInventoryResult Failure(string code, string summary) =>
        new(false, [], [Error(code, summary)]);

    private static TiffVerificationDiagnostic Error(string code, string summary, string? artifactId = null) =>
        new(code, "error", summary, artifactId);

    private static string NormalizeRelative(string value) => value.Replace(Path.DirectorySeparatorChar, '/');

    internal static bool IsWithin(string root, string candidate)
    {
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string normalizedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        return normalizedCandidate.StartsWith(
            normalizedRoot + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}

internal sealed record TiffFileIdentityResult(
    bool Succeeded,
    long Size,
    string? Sha256,
    string? ByteOrder,
    TiffVerificationDiagnostic? Diagnostic);

internal static class TiffFileIdentityReader
{
    public static TiffFileIdentityResult Read(
        string path,
        long expectedSize,
        string expectedSha256,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ManagedProjectPackRunStore.EnsureNoReparseInExistingChain(path);
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.SequentialScan);
            if (stream.Length <= 0 || stream.Length > TiffVerificationLimits.MaxFileBytes)
            {
                return Failure(TiffVerificationErrorCode.FileTooLarge,
                    "TIFF file is empty or exceeds the frozen file-size limit.", stream.Length);
            }

            if (stream.Length != expectedSize)
            {
                return Failure(TiffVerificationErrorCode.OutputChanged,
                    "TIFF file size changed after controlled conversion.", stream.Length);
            }

            Span<byte> signature = stackalloc byte[4];
            stream.ReadExactly(signature);
            string? byteOrder = signature switch
            {
                [0x49, 0x49, 0x2A, 0x00] => "little-endian",
                [0x4D, 0x4D, 0x00, 0x2A] => "big-endian",
                [0x49, 0x49, 0x2B, 0x00] or [0x4D, 0x4D, 0x00, 0x2B] => null,
                _ => string.Empty
            };
            if (byteOrder is null)
            {
                return Failure(TiffVerificationErrorCode.BigTiffUnsupported,
                    "BigTIFF is outside the frozen v1 supported format set.", stream.Length);
            }

            if (byteOrder.Length == 0)
            {
                return Failure(TiffVerificationErrorCode.SignatureInvalid,
                    "TIFF signature is invalid or unsupported.", stream.Length);
            }

            stream.Position = 0;
            string sha256 = Convert.ToHexString(SHA256.HashData(stream));
            cancellationToken.ThrowIfCancellationRequested();
            if (!sha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                return Failure(TiffVerificationErrorCode.OutputChanged,
                    "TIFF SHA256 changed after controlled conversion.", stream.Length);
            }

            return new TiffFileIdentityResult(true, stream.Length, sha256, byteOrder, null);
        }
        catch (OperationCanceledException)
        {
            return Failure(TiffVerificationErrorCode.DecodeTimedOut, "TIFF identity read was canceled.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return Failure(TiffVerificationErrorCode.OutputMissing, "TIFF file could not be read safely.");
        }
    }

    private static TiffFileIdentityResult Failure(string code, string summary, long size = 0) =>
        new(false, size, null, null, new TiffVerificationDiagnostic(code, "error", summary));
}

public sealed record TiffDecodedFrame(TiffFrameMetadata Metadata, byte[]? RgbaPixels);

public sealed record TiffDecodeResult(
    bool Succeeded,
    TiffArtifactMetadata? Metadata,
    IReadOnlyList<TiffDecodedFrame> Frames,
    TiffVerificationDiagnostic? Diagnostic);

public interface ITiffArtifactDecoder
{
    TiffDecodeResult Decode(
        TiffInventoryItem item,
        string managedTemporaryDirectory,
        bool includePixels,
        CancellationToken cancellationToken = default);

    TiffPreviewWriteResult WritePreview(
        TiffInventoryItem item,
        string managedTemporaryDirectory,
        string destinationPath,
        CancellationToken cancellationToken = default);
}

public sealed record TiffPreviewWriteResult(
    bool Succeeded,
    int Width,
    int Height,
    int SourceFrameCount,
    TiffVerificationDiagnostic? Diagnostic);

public sealed class MagickTiffArtifactDecoder : ITiffArtifactDecoder
{
    private static readonly object ResourceLock = new();

    public TiffDecodeResult Decode(
        TiffInventoryItem item,
        string managedTemporaryDirectory,
        bool includePixels,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        lock (ResourceLock)
        {
            Stopwatch clock = Stopwatch.StartNew();
            try
            {
                ConfigureResources(managedTemporaryDirectory);
                using FileStream stream = OpenFrozen(item);
                using MagickImageCollection ping = new();
                MagickReadSettings settings = new() { Format = MagickFormat.Tiff };
                ping.Ping(stream, settings);
                TiffVerificationDiagnostic? pingDiagnostic = ValidateCollection(
                    ping, item.ArtifactId, requireDecodedChannels: false);
                if (pingDiagnostic is not null)
                {
                    return Failure(pingDiagnostic);
                }

                cancellationToken.ThrowIfCancellationRequested();
                stream.Position = 0;
                using MagickImageCollection images = new();
                images.Read(stream, settings);
                TiffVerificationDiagnostic? decodedDiagnostic = ValidateCollection(
                    images, item.ArtifactId, requireDecodedChannels: true);
                if (decodedDiagnostic is not null)
                {
                    return Failure(decodedDiagnostic);
                }

                if (clock.ElapsedMilliseconds > TiffVerificationLimits.DecodeTimeoutMilliseconds)
                {
                    return Failure(Error(TiffVerificationErrorCode.DecodeTimedOut,
                        "TIFF decode exceeded the frozen timeout.", item.ArtifactId));
                }

                List<TiffDecodedFrame> frames = [];
                for (int index = 0; index < images.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    IMagickImage<byte> image = images[index];
                    TiffFrameMetadata metadata = Metadata(image, index);
                    byte[]? pixels = null;
                    if (includePixels)
                    {
                        using IMagickImage<byte> normalized = image.Clone();
                        normalized.AutoOrient();
                        normalized.ColorSpace = ColorSpace.sRGB;
                        normalized.Alpha(AlphaOption.On);
                        using IUnsafePixelCollection<byte> pixelCollection = normalized.GetPixelsUnsafe();
                        pixels = pixelCollection.ToByteArray(PixelMapping.RGBA);
                        if (pixels is null || pixels.LongLength != checked((long)normalized.Width * normalized.Height * 4))
                        {
                            return Failure(Error(TiffVerificationErrorCode.DecodeFailed,
                                "TIFF decoder returned an incomplete normalized pixel buffer.", item.ArtifactId, index));
                        }
                    }

                    frames.Add(new TiffDecodedFrame(metadata, pixels));
                }

                stream.Position = 0;
                string postDecodeHash = Convert.ToHexString(SHA256.HashData(stream));
                if (!postDecodeHash.Equals(item.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return Failure(Error(TiffVerificationErrorCode.OutputChanged,
                        "TIFF changed while it was decoded.", item.ArtifactId));
                }

                TiffArtifactMetadata artifactMetadata = new(
                    item.ArtifactId,
                    Path.GetFileName(item.FullPath),
                    item.Size,
                    item.Sha256,
                    item.ByteOrder,
                    frames.Count,
                    frames.Select(frame => frame.Metadata).ToArray());
                return new TiffDecodeResult(true, artifactMetadata, frames, null);
            }
            catch (OperationCanceledException)
            {
                return Failure(Error(TiffVerificationErrorCode.DecodeTimedOut,
                    "TIFF decode was canceled.", item.ArtifactId));
            }
            catch (MagickResourceLimitErrorException)
            {
                string code = clock.ElapsedMilliseconds >= TiffVerificationLimits.DecodeTimeoutMilliseconds
                    ? TiffVerificationErrorCode.DecodeTimedOut
                    : TiffVerificationErrorCode.MemoryLimit;
                return Failure(Error(code, "TIFF decode exceeded a frozen ImageMagick resource limit.", item.ArtifactId));
            }
            catch (MagickException)
            {
                return Failure(Error(TiffVerificationErrorCode.DecodeFailed,
                    "TIFF decoder rejected corrupt, truncated, or unsupported content.", item.ArtifactId));
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or OverflowException)
            {
                return Failure(Error(TiffVerificationErrorCode.DecodeFailed,
                    "TIFF could not be decoded safely.", item.ArtifactId));
            }
        }
    }

    public TiffPreviewWriteResult WritePreview(
        TiffInventoryItem item,
        string managedTemporaryDirectory,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        lock (ResourceLock)
        {
            try
            {
                ConfigureResources(managedTemporaryDirectory);
                if (File.Exists(destinationPath) || Directory.Exists(destinationPath))
                {
                    return PreviewFailure(TiffVerificationErrorCode.PreviewConflict,
                        "Managed preview path already exists; overwrite is denied.", item.ArtifactId);
                }

                using FileStream stream = OpenFrozen(item);
                using MagickImageCollection images = new();
                images.Read(stream, new MagickReadSettings { Format = MagickFormat.Tiff });
                TiffVerificationDiagnostic? diagnostic = ValidateCollection(
                    images, item.ArtifactId, requireDecodedChannels: true);
                if (diagnostic is not null)
                {
                    return new TiffPreviewWriteResult(false, 0, 0, 0, diagnostic);
                }

                using MagickImageCollection previews = new();
                foreach (IMagickImage<byte> source in images)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    IMagickImage<byte> preview = source.Clone();
                    preview.AutoOrient();
                    preview.ColorSpace = ColorSpace.sRGB;
                    preview.BackgroundColor = MagickColors.White;
                    preview.Alpha(AlphaOption.Remove);
                    ResizeWithin(preview, TiffVerificationLimits.MaxPreviewDimension, TiffVerificationLimits.MaxPreviewDimension);
                    previews.Add(preview);
                }

                using IMagickImage<byte> contactSheet = previews.Count == 1
                    ? previews[0].Clone()
                    : previews.AppendVertically();
                ResizeWithin(contactSheet, TiffVerificationLimits.MaxPreviewDimension, TiffVerificationLimits.MaxPreviewDimension);
                contactSheet.Strip();
                contactSheet.Format = MagickFormat.Png;
                string directory = Path.GetDirectoryName(destinationPath) ?? throw new IOException();
                Directory.CreateDirectory(directory);
                string temporary = Path.Combine(directory, Path.GetFileName(destinationPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    contactSheet.Write(temporary, MagickFormat.Png);
                    File.Move(temporary, destinationPath, overwrite: false);
                }
                finally
                {
                    File.Delete(temporary);
                }

                stream.Position = 0;
                if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(item.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(destinationPath);
                    return PreviewFailure(TiffVerificationErrorCode.OutputChanged,
                        "TIFF changed while its preview was generated.", item.ArtifactId);
                }

                return new TiffPreviewWriteResult(
                    true,
                    checked((int)contactSheet.Width),
                    checked((int)contactSheet.Height),
                    images.Count,
                    null);
            }
            catch (OperationCanceledException)
            {
                return PreviewFailure(TiffVerificationErrorCode.DecodeTimedOut,
                    "TIFF preview generation was canceled.", item.ArtifactId);
            }
            catch (MagickException)
            {
                return PreviewFailure(TiffVerificationErrorCode.PreviewFailed,
                    "ImageMagick could not generate a bounded PNG preview.", item.ArtifactId);
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or OverflowException)
            {
                return PreviewFailure(TiffVerificationErrorCode.PreviewFailed,
                    "Managed PNG preview could not be written safely.", item.ArtifactId);
            }
        }
    }

    private static FileStream OpenFrozen(TiffInventoryItem item)
    {
        ManagedProjectPackRunStore.EnsureNoReparseInExistingChain(item.FullPath);
        FileStream stream = new(
            item.FullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.SequentialScan);
        string sha256 = Convert.ToHexString(SHA256.HashData(stream));
        if (stream.Length != item.Size || !sha256.Equals(item.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            stream.Dispose();
            throw new IOException("TIFF identity changed before decode.");
        }

        stream.Position = 0;
        return stream;
    }

    private static void ConfigureResources(string managedTemporaryDirectory)
    {
        ManagedProjectPackRunStore.EnsureNoReparseInExistingChain(managedTemporaryDirectory);
        Directory.CreateDirectory(managedTemporaryDirectory);
        ManagedProjectPackRunStore.EnsureNoReparseInExistingChain(managedTemporaryDirectory);
        MagickNET.SetTempDirectory(managedTemporaryDirectory);
        ResourceLimits.Width = TiffVerificationLimits.MaxDimension;
        ResourceLimits.Height = TiffVerificationLimits.MaxDimension;
        ResourceLimits.ListLength = TiffVerificationLimits.MaxFrameCount;
        ResourceLimits.Area = TiffVerificationLimits.MaxDecodedMemoryBytes;
        ResourceLimits.Memory = TiffVerificationLimits.MaxDecodedMemoryBytes;
        ResourceLimits.MaxMemoryRequest = TiffVerificationLimits.MaxDecodedMemoryBytes;
        ResourceLimits.Disk = 0;
        ResourceLimits.Thread = 1;
        ResourceLimits.Time = TiffVerificationLimits.DecodeTimeoutMilliseconds / 1_000;
    }

    private static TiffVerificationDiagnostic? ValidateCollection(
        MagickImageCollection images,
        string artifactId,
        bool requireDecodedChannels)
    {
        if (images.Count is 0 or > TiffVerificationLimits.MaxFrameCount)
        {
            return Error(TiffVerificationErrorCode.FrameLimit,
                "TIFF frame count is empty or exceeds the frozen limit.", artifactId);
        }

        long totalPixels = 0;
        long decodedBytes = 0;
        for (int index = 0; index < images.Count; index++)
        {
            IMagickImage<byte> image = images[index];
            long pixels;
            try
            {
                pixels = checked((long)image.Width * image.Height);
                totalPixels = checked(totalPixels + pixels);
                decodedBytes = checked(decodedBytes + pixels * 4);
            }
            catch (OverflowException)
            {
                return Error(TiffVerificationErrorCode.PixelLimit,
                    "TIFF pixel dimensions overflow the frozen limit.", artifactId, index);
            }

            if (image.Width is 0 or > TiffVerificationLimits.MaxDimension ||
                image.Height is 0 or > TiffVerificationLimits.MaxDimension)
            {
                return Error(TiffVerificationErrorCode.DimensionLimit,
                    "TIFF dimensions exceed the frozen per-axis limit.", artifactId, index);
            }

            if (pixels > TiffVerificationLimits.MaxPixelsPerFrame || totalPixels > TiffVerificationLimits.MaxTotalPixels)
            {
                return Error(TiffVerificationErrorCode.PixelLimit,
                    "TIFF pixel count exceeds the frozen frame or aggregate limit.", artifactId, index);
            }

            if (decodedBytes > TiffVerificationLimits.MaxDecodedMemoryBytes)
            {
                return Error(TiffVerificationErrorCode.MemoryLimit,
                    "TIFF decoded RGBA memory exceeds the frozen limit.", artifactId, index);
            }

            if (image.Format != MagickFormat.Tiff || image.Depth != 8 || image.ColorType != ColorType.TrueColor ||
                image.HasAlpha || requireDecodedChannels && image.ChannelCount != 3)
            {
                return Error(TiffVerificationErrorCode.FormatUnsupported,
                    $"TIFF pixel format is outside the frozen RGB8/no-alpha v1 set " +
                    $"(format={image.Format}, depth={image.Depth}, colorType={image.ColorType}, " +
                    $"channels={image.ChannelCount}, alpha={image.HasAlpha}).", artifactId, index);
            }

            if (image.Compression != CompressionMethod.LZW)
            {
                return Error(TiffVerificationErrorCode.CompressionUnsupported,
                    "TIFF compression is outside the frozen LZW v1 set.", artifactId, index);
            }

            if (image.Density.Units != DensityUnit.PixelsPerInch ||
                image.Orientation is not OrientationType.Undefined and not OrientationType.TopLeft)
            {
                return Error(TiffVerificationErrorCode.FormatUnsupported,
                    "TIFF density unit or orientation is outside the frozen v1 set.", artifactId, index);
            }
        }

        return null;
    }

    private static TiffFrameMetadata Metadata(IMagickImage<byte> image, int index)
    {
        long pixels = checked((long)image.Width * image.Height);
        return new TiffFrameMetadata(
            index,
            checked((int)image.Width),
            checked((int)image.Height),
            image.Density.X,
            image.Density.Y,
            checked((int)image.Depth),
            checked((int)image.ChannelCount),
            "rgb8",
            "lzw",
            image.Orientation == OrientationType.Undefined ? "unspecified" : "top-left",
            image.HasAlpha,
            pixels,
            checked(pixels * 4));
    }

    private static void ResizeWithin(IMagickImage<byte> image, int maxWidth, int maxHeight)
    {
        if (image.Width <= maxWidth && image.Height <= maxHeight)
        {
            return;
        }

        double scale = Math.Min(maxWidth / (double)image.Width, maxHeight / (double)image.Height);
        image.Resize(
            Math.Max(1u, checked((uint)Math.Floor(image.Width * scale))),
            Math.Max(1u, checked((uint)Math.Floor(image.Height * scale))));
    }

    private static TiffDecodeResult Failure(TiffVerificationDiagnostic diagnostic) => new(false, null, [], diagnostic);

    private static TiffPreviewWriteResult PreviewFailure(string code, string summary, string artifactId) =>
        new(false, 0, 0, 0, Error(code, summary, artifactId));

    private static TiffVerificationDiagnostic Error(
        string code,
        string summary,
        string artifactId,
        int? frameIndex = null) =>
        new(code, "error", summary, artifactId, frameIndex);
}
