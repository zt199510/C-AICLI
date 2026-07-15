using System.Collections.ObjectModel;

namespace CSharpAiCli.ProjectPacks.Runtime;

public static class TiffVerificationSchema
{
    public const int CurrentVersion = 1;
    public const string BaselineType = "gerber-tiff.verification-baseline";
    public const string ResultType = "packs.verify";
    public const string PreviewType = "packs.preview";
    public const string PixelAlgorithm = "rgba8-absolute-v1";
    public const string PixelColorSpace = "srgb";
    public const string PixelOrientation = "top-left";
    public const string PixelAlpha = "straight";
}

public static class TiffVerificationLimits
{
    public const long MaxFileBytes = 256L * 1024 * 1024;
    public const int MaxDimension = 32_768;
    public const long MaxPixelsPerFrame = 100_000_000;
    public const long MaxTotalPixels = 128_000_000;
    public const int MaxFrameCount = 32;
    public const long MaxDecodedMemoryBytes = 512L * 1024 * 1024;
    public const int DecodeTimeoutMilliseconds = 15_000;
    public const int MaxPreviewDimension = 2_048;
    public const int MaxDiagnosticCount = 256;
    public const int MaxReportBytes = 4 * 1024 * 1024;
    public const int MaxBaselineBytes = 2 * 1024 * 1024;
}

public static class TiffVerificationLevel
{
    public const string FileValid = "file-valid";
    public const string MetadataValid = "metadata-valid";
    public const string ContentCompared = "content-compared";
    public const string HumanReviewRequired = "human-review-required";
}

public static class TiffVerificationStatus
{
    public const string Passed = "passed";
    public const string Failed = "failed";
    public const string NotRequested = "not-requested";
    public const string Required = "required";
}

public static class TiffVerificationErrorCode
{
    public const string RunStateInvalid = "pack-tiff-run-state-invalid";
    public const string InventoryInvalid = "pack-tiff-output-inventory-invalid";
    public const string OutputMissing = "pack-tiff-output-missing";
    public const string OutputUnexpected = "pack-tiff-output-unexpected";
    public const string OutputDuplicate = "pack-tiff-output-duplicate";
    public const string OutputChanged = "pack-tiff-output-changed";
    public const string FileTooLarge = "pack-tiff-file-too-large";
    public const string SignatureInvalid = "pack-tiff-signature-invalid";
    public const string BigTiffUnsupported = "pack-tiff-bigtiff-unsupported";
    public const string DecodeFailed = "pack-tiff-decode-failed";
    public const string DecodeTimedOut = "pack-tiff-decode-timeout";
    public const string DimensionLimit = "pack-tiff-dimension-limit";
    public const string PixelLimit = "pack-tiff-pixel-limit";
    public const string FrameLimit = "pack-tiff-frame-limit";
    public const string MemoryLimit = "pack-tiff-memory-limit";
    public const string FormatUnsupported = "pack-tiff-format-unsupported";
    public const string CompressionUnsupported = "pack-tiff-compression-unsupported";
    public const string MetadataMismatch = "pack-tiff-metadata-mismatch";
    public const string BaselineInvalid = "pack-tiff-baseline-invalid";
    public const string BaselineOutsideWorkspace = "pack-tiff-baseline-outside-workspace";
    public const string BaselineChanged = "pack-tiff-baseline-changed";
    public const string ExactHashNotAllowed = "pack-tiff-exact-hash-not-allowed";
    public const string ExactHashMismatch = "pack-tiff-exact-hash-mismatch";
    public const string PixelMismatch = "pack-tiff-pixel-mismatch";
    public const string PreviewConflict = "pack-tiff-preview-conflict";
    public const string PreviewFailed = "pack-tiff-preview-failed";
    public const string ReportWriteFailed = "pack-tiff-report-write-failed";
}

public sealed record TiffResourceLimits(
    long MaxFileBytes,
    int MaxDimension,
    long MaxPixelsPerFrame,
    long MaxTotalPixels,
    int MaxFrameCount,
    long MaxDecodedMemoryBytes,
    int DecodeTimeoutMilliseconds,
    int MaxPreviewDimension)
{
    public static TiffResourceLimits FrozenV1 { get; } = new(
        TiffVerificationLimits.MaxFileBytes,
        TiffVerificationLimits.MaxDimension,
        TiffVerificationLimits.MaxPixelsPerFrame,
        TiffVerificationLimits.MaxTotalPixels,
        TiffVerificationLimits.MaxFrameCount,
        TiffVerificationLimits.MaxDecodedMemoryBytes,
        TiffVerificationLimits.DecodeTimeoutMilliseconds,
        TiffVerificationLimits.MaxPreviewDimension);
}

public sealed record TiffFrameMetadata(
    int Index,
    int Width,
    int Height,
    double DpiX,
    double DpiY,
    int BitsPerSample,
    int SamplesPerPixel,
    string PixelFormat,
    string Compression,
    string Orientation,
    bool HasAlpha,
    long PixelCount,
    long DecodedBytes);

public sealed record TiffArtifactMetadata(
    string ArtifactId,
    string FileName,
    long FileSize,
    string Sha256,
    string ByteOrder,
    int FrameCount,
    IReadOnlyList<TiffFrameMetadata> Frames)
{
    public IReadOnlyList<TiffFrameMetadata> Frames { get; } =
        new ReadOnlyCollection<TiffFrameMetadata>((Frames ?? []).OrderBy(frame => frame.Index).ToArray());
}

public sealed record TiffVerificationDiagnostic(
    string Code,
    string Severity,
    string Summary,
    string? ArtifactId = null,
    int? FrameIndex = null);

public sealed record TiffVerificationLevelResult(string Level, string Status, string Summary);

public sealed record TiffPixelComparisonResult(
    string ArtifactId,
    string Algorithm,
    string ColorSpace,
    string Orientation,
    string Alpha,
    int MaxChannelDelta,
    long MaxDifferentPixels,
    long DifferentPixels,
    int ObservedMaxChannelDelta,
    string ActualPixelSha256,
    string BaselinePixelSha256,
    bool Passed);

public sealed record TiffVerificationResult(
    string RunId,
    bool HardVerificationPassed,
    bool HumanReviewRequired,
    string? BaselinePath,
    string? BaselineSha256,
    IReadOnlyList<TiffVerificationLevelResult> Levels,
    IReadOnlyList<TiffArtifactMetadata> Artifacts,
    IReadOnlyList<TiffPixelComparisonResult> PixelComparisons,
    IReadOnlyList<TiffVerificationDiagnostic> Diagnostics,
    IReadOnlyList<ProjectPackRunArtifactPointer> Evidence,
    string Summary)
{
    public int SchemaVersion => TiffVerificationSchema.CurrentVersion;
    public string Type => TiffVerificationSchema.ResultType;
}

public sealed record TiffPreviewArtifact(
    string ArtifactId,
    string Kind,
    string Path,
    long Size,
    string Sha256,
    int Width,
    int Height,
    int SourceFrameCount);

public sealed record TiffPreviewResult(
    string RunId,
    bool Succeeded,
    IReadOnlyList<TiffPreviewArtifact> Previews,
    IReadOnlyList<TiffVerificationDiagnostic> Diagnostics,
    string Summary)
{
    public int SchemaVersion => TiffVerificationSchema.CurrentVersion;
    public string Type => TiffVerificationSchema.PreviewType;
    public bool CorrectnessProof => false;
    public bool AutomaticallyOpened => false;
    public bool AutomaticallyUploaded => false;
}
