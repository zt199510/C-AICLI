using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CSharpAiCli.ProjectPacks.Runtime;

public sealed record TiffBaselineMetadata(
    int FrameCount,
    int Width,
    int Height,
    double DpiX,
    double DpiY,
    double DpiTolerance,
    int BitsPerSample,
    int SamplesPerPixel,
    string PixelFormat,
    string Compression,
    string Orientation,
    bool HasAlpha);

public sealed record TiffBaselinePixelComparison(
    string BaselineTiffPath,
    string BaselineSha256,
    string Algorithm,
    string ColorSpace,
    string Orientation,
    string Alpha,
    int MaxChannelDelta,
    long MaxDifferentPixels);

public sealed record TiffBaselineOutput(
    string ArtifactId,
    bool ByteDeterministic,
    string? ExactSha256,
    TiffBaselineMetadata Metadata,
    TiffBaselinePixelComparison? PixelComparison);

public sealed record TiffVerificationBaseline(
    int SchemaVersion,
    string Type,
    string PackId,
    string PackVersion,
    string ToolName,
    string ToolVersion,
    string InputFingerprint,
    IReadOnlyList<TiffBaselineOutput> Outputs)
{
    public IReadOnlyList<TiffBaselineOutput> Outputs { get; } =
        new ReadOnlyCollection<TiffBaselineOutput>((Outputs ?? []).OrderBy(output => output.ArtifactId, StringComparer.Ordinal).ToArray());
}

public sealed record TiffBaselineLoadResult(
    bool Succeeded,
    TiffVerificationBaseline? Baseline,
    string? Sha256,
    TiffVerificationDiagnostic? Diagnostic)
{
    public static TiffBaselineLoadResult Failure(string code, string summary) =>
        new(false, null, null, new TiffVerificationDiagnostic(code, "error", summary));
}

public static class TiffVerificationBaselineLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32
    };

    public static TiffBaselineLoadResult Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            FileInfo info = new(path);
            if (!info.Exists || info.Length <= 0 || info.Length > TiffVerificationLimits.MaxBaselineBytes)
            {
                return TiffBaselineLoadResult.Failure(
                    TiffVerificationErrorCode.BaselineInvalid,
                    "TIFF verification baseline is missing, empty, or exceeds its size limit.");
            }

            byte[] bytes;
            using (FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.SequentialScan))
            {
                bytes = new byte[checked((int)stream.Length)];
                stream.ReadExactly(bytes);
                if (stream.Length != bytes.Length)
                {
                    return TiffBaselineLoadResult.Failure(
                        TiffVerificationErrorCode.BaselineChanged,
                        "TIFF verification baseline changed while it was read.");
                }
            }

            string sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
            TiffVerificationBaseline? baseline = JsonSerializer.Deserialize<TiffVerificationBaseline>(bytes, JsonOptions);
            TiffVerificationDiagnostic? diagnostic = Validate(baseline);
            return diagnostic is null
                ? new TiffBaselineLoadResult(true, baseline, sha256, null)
                : new TiffBaselineLoadResult(false, null, sha256, diagnostic);
        }
        catch (JsonException)
        {
            return TiffBaselineLoadResult.Failure(
                TiffVerificationErrorCode.BaselineInvalid,
                "TIFF verification baseline is not valid strict schema-v1 JSON.");
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException
            or OverflowException)
        {
            return TiffBaselineLoadResult.Failure(
                TiffVerificationErrorCode.BaselineInvalid,
                "TIFF verification baseline could not be read safely.");
        }
    }

    private static TiffVerificationDiagnostic? Validate(TiffVerificationBaseline? baseline)
    {
        if (baseline is null || baseline.SchemaVersion != TiffVerificationSchema.CurrentVersion ||
            baseline.Type != TiffVerificationSchema.BaselineType || baseline.PackId != "gerber-tiff" ||
            !SafeText(baseline.PackVersion, 64) || !SafeText(baseline.ToolName, 64) ||
            !SafeText(baseline.ToolVersion, 128) || !IsSha256(baseline.InputFingerprint) ||
            baseline.Outputs.Count is 0 or > TiffVerificationLimits.MaxFrameCount ||
            baseline.Outputs.Select(output => output.ArtifactId).Distinct(StringComparer.Ordinal).Count() != baseline.Outputs.Count)
        {
            return Invalid("TIFF verification baseline identity, output count, or input fingerprint is invalid.");
        }

        foreach (TiffBaselineOutput output in baseline.Outputs)
        {
            if (!SafeId(output.ArtifactId) || output.Metadata is null || !ValidMetadata(output.Metadata))
            {
                return Invalid("TIFF verification baseline contains invalid output metadata.", output.ArtifactId);
            }

            if (output.ByteDeterministic != (output.ExactSha256 is not null) ||
                output.ExactSha256 is not null && !IsSha256(output.ExactSha256))
            {
                return new TiffVerificationDiagnostic(
                    TiffVerificationErrorCode.ExactHashNotAllowed,
                    "error",
                    "Exact TIFF hash requires byteDeterministic=true and one valid SHA256.",
                    output.ArtifactId);
            }

            if (output.PixelComparison is not null && !ValidPixelComparison(output.PixelComparison))
            {
                return Invalid("TIFF pixel comparison contract is invalid.", output.ArtifactId);
            }
        }

        return null;
    }

    private static bool ValidMetadata(TiffBaselineMetadata metadata) =>
        metadata.FrameCount is > 0 and <= TiffVerificationLimits.MaxFrameCount &&
        metadata.Width is > 0 and <= TiffVerificationLimits.MaxDimension &&
        metadata.Height is > 0 and <= TiffVerificationLimits.MaxDimension &&
        double.IsFinite(metadata.DpiX) && metadata.DpiX > 0 &&
        double.IsFinite(metadata.DpiY) && metadata.DpiY > 0 &&
        double.IsFinite(metadata.DpiTolerance) && metadata.DpiTolerance is >= 0 and <= 10 &&
        metadata.BitsPerSample == 8 && metadata.SamplesPerPixel == 3 &&
        metadata.PixelFormat == "rgb8" && metadata.Compression == "lzw" &&
        SafeText(metadata.Orientation, 32) && !metadata.HasAlpha;

    private static bool ValidPixelComparison(TiffBaselinePixelComparison comparison) =>
        SafeRelativePath(comparison.BaselineTiffPath) && IsSha256(comparison.BaselineSha256) &&
        comparison.Algorithm == TiffVerificationSchema.PixelAlgorithm &&
        comparison.ColorSpace == TiffVerificationSchema.PixelColorSpace &&
        comparison.Orientation == TiffVerificationSchema.PixelOrientation &&
        comparison.Alpha == TiffVerificationSchema.PixelAlpha &&
        comparison.MaxChannelDelta is >= 0 and <= byte.MaxValue &&
        comparison.MaxDifferentPixels is >= 0 and <= TiffVerificationLimits.MaxTotalPixels;

    private static TiffVerificationDiagnostic Invalid(string summary, string? artifactId = null) =>
        new(TiffVerificationErrorCode.BaselineInvalid, "error", summary, artifactId);

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static bool SafeId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 64 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    private static bool SafeText(string? value, int maxLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maxLength &&
        !value.Any(character => char.IsControl(character));

    private static bool SafeRelativePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 1_024 || Path.IsPathFullyQualified(value) ||
            value.StartsWith("\\\\", StringComparison.Ordinal) || Uri.TryCreate(value, UriKind.Absolute, out _))
        {
            return false;
        }

        return value.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar])
            .All(segment => segment.Length > 0 && segment is not "." and not "..");
    }
}
