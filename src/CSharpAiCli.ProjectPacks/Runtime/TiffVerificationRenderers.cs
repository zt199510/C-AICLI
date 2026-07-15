using System.Globalization;
using System.Text;
using System.Text.Json;
using CSharpAiCli.Core;

namespace CSharpAiCli.ProjectPacks.Runtime;

public static class TiffVerificationRenderer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static string RenderJson(TiffVerificationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return JsonSerializer.Serialize(new
        {
            type = result.Type,
            schemaVersion = result.SchemaVersion,
            runId = Safe(result.RunId),
            status = result.HardVerificationPassed ? "passed" : "failed",
            hardVerificationPassed = result.HardVerificationPassed,
            humanReviewRequired = result.HumanReviewRequired,
            correctnessProof = false,
            baseline = result.BaselinePath is null ? null : new
            {
                path = Safe(result.BaselinePath),
                sha256 = result.BaselineSha256
            },
            limits = TiffResourceLimits.FrozenV1,
            levels = result.Levels
                .OrderBy(LevelOrder)
                .Select(level => new
                {
                    level = level.Level,
                    status = level.Status,
                    summary = Safe(level.Summary)
                }),
            artifacts = result.Artifacts
                .OrderBy(artifact => artifact.ArtifactId, StringComparer.Ordinal)
                .Select(artifact => new
                {
                    artifactId = artifact.ArtifactId,
                    fileName = Safe(artifact.FileName),
                    artifact.FileSize,
                    artifact.Sha256,
                    artifact.ByteOrder,
                    artifact.FrameCount,
                    frames = artifact.Frames.OrderBy(frame => frame.Index)
                }),
            pixelComparisons = result.PixelComparisons
                .OrderBy(comparison => comparison.ArtifactId, StringComparer.Ordinal),
            diagnostics = result.Diagnostics
                .OrderBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.ArtifactId, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.FrameIndex)
                .Select(diagnostic => new
                {
                    diagnostic.Code,
                    diagnostic.Severity,
                    summary = Safe(diagnostic.Summary),
                    diagnostic.ArtifactId,
                    diagnostic.FrameIndex
                }),
            summary = Safe(result.Summary)
        }, JsonOptions);
    }

    public static string RenderMarkdown(TiffVerificationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        StringBuilder builder = new();
        builder.AppendLine("# Gerber/TIFF Verification");
        builder.AppendLine();
        builder.AppendLine($"- Run: `{Safe(result.RunId)}`");
        builder.AppendLine($"- Hard verification: `{(result.HardVerificationPassed ? "passed" : "failed")}`");
        builder.AppendLine($"- Human review required: `{result.HumanReviewRequired.ToString().ToLowerInvariant()}`");
        builder.AppendLine("- Correctness proof: `false`");
        if (result.BaselinePath is not null)
        {
            builder.AppendLine($"- Baseline: `{Safe(result.BaselinePath)}` (`{result.BaselineSha256}`)");
        }

        builder.AppendLine();
        builder.AppendLine("## Verification Levels");
        builder.AppendLine();
        builder.AppendLine("| Level | Status | Summary |");
        builder.AppendLine("|---|---|---|");
        foreach (TiffVerificationLevelResult level in result.Levels.OrderBy(LevelOrder))
        {
            builder.AppendLine($"| `{level.Level}` | `{level.Status}` | {Cell(level.Summary)} |");
        }

        builder.AppendLine();
        builder.AppendLine("## TIFF Metadata");
        builder.AppendLine();
        builder.AppendLine("| Artifact | Frame | Dimensions | DPI | Format | Compression | Orientation | SHA256 |");
        builder.AppendLine("|---|---:|---:|---:|---|---|---|---|");
        foreach (TiffArtifactMetadata artifact in result.Artifacts.OrderBy(item => item.ArtifactId, StringComparer.Ordinal))
        {
            foreach (TiffFrameMetadata frame in artifact.Frames.OrderBy(item => item.Index))
            {
                builder.AppendLine(
                    $"| `{artifact.ArtifactId}` | {frame.Index} | {frame.Width} x {frame.Height} | " +
                    $"{frame.DpiX.ToString("0.###", CultureInfo.InvariantCulture)} x " +
                    $"{frame.DpiY.ToString("0.###", CultureInfo.InvariantCulture)} | " +
                    $"{frame.PixelFormat}; {frame.BitsPerSample}-bit; {frame.SamplesPerPixel} samples | " +
                    $"{frame.Compression} | {frame.Orientation} | `{artifact.Sha256}` |");
            }
        }

        if (result.PixelComparisons.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Pixel Comparison");
            builder.AppendLine();
            builder.AppendLine("| Artifact | Algorithm | Tolerance | Observed | Result |");
            builder.AppendLine("|---|---|---|---|---|");
            foreach (TiffPixelComparisonResult comparison in result.PixelComparisons.OrderBy(item => item.ArtifactId, StringComparer.Ordinal))
            {
                builder.AppendLine(
                    $"| `{comparison.ArtifactId}` | `{comparison.Algorithm}`; {comparison.ColorSpace}; " +
                    $"{comparison.Orientation}; {comparison.Alpha} | max delta {comparison.MaxChannelDelta}; " +
                    $"max different pixels {comparison.MaxDifferentPixels} | max delta {comparison.ObservedMaxChannelDelta}; " +
                    $"different pixels {comparison.DifferentPixels} | `{(comparison.Passed ? "passed" : "failed")}` |");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Diagnostics");
        builder.AppendLine();
        if (result.Diagnostics.Count == 0)
        {
            builder.AppendLine("None.");
        }
        else
        {
            foreach (TiffVerificationDiagnostic diagnostic in result.Diagnostics
                .OrderBy(item => item.Code, StringComparer.Ordinal)
                .ThenBy(item => item.ArtifactId, StringComparer.Ordinal)
                .ThenBy(item => item.FrameIndex))
            {
                builder.Append("- `").Append(diagnostic.Code).Append("` (").Append(diagnostic.Severity).Append(")");
                if (diagnostic.ArtifactId is not null)
                {
                    builder.Append(" artifact `").Append(diagnostic.ArtifactId).Append('`');
                }

                if (diagnostic.FrameIndex is not null)
                {
                    builder.Append(" frame ").Append(diagnostic.FrameIndex.Value);
                }

                builder.Append(": ").AppendLine(Safe(diagnostic.Summary));
            }
        }

        builder.AppendLine();
        builder.AppendLine("Preview images are human-review aids only. They are not a correctness proof and are not opened or uploaded automatically.");
        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    public static string RenderText(TiffVerificationResult result)
    {
        StringBuilder builder = new();
        builder.AppendLine($"runId: {Safe(result.RunId)}");
        builder.AppendLine($"status: {(result.HardVerificationPassed ? "passed" : "failed")}");
        builder.AppendLine($"hardVerificationPassed: {result.HardVerificationPassed.ToString().ToLowerInvariant()}");
        builder.AppendLine($"humanReviewRequired: {result.HumanReviewRequired.ToString().ToLowerInvariant()}");
        builder.AppendLine("correctnessProof: false");
        foreach (TiffVerificationLevelResult level in result.Levels.OrderBy(LevelOrder))
        {
            builder.AppendLine($"level: {level.Level} {level.Status}");
        }

        foreach (TiffArtifactMetadata artifact in result.Artifacts.OrderBy(item => item.ArtifactId, StringComparer.Ordinal))
        {
            builder.AppendLine($"artifact: {artifact.ArtifactId} frames={artifact.FrameCount} bytes={artifact.FileSize} sha256={artifact.Sha256}");
        }

        foreach (TiffVerificationDiagnostic diagnostic in result.Diagnostics.OrderBy(item => item.Code, StringComparer.Ordinal))
        {
            builder.AppendLine($"diagnostic: {diagnostic.Code} {Safe(diagnostic.Summary)}");
        }

        builder.AppendLine($"summary: {Safe(result.Summary)}");
        return builder.ToString().TrimEnd();
    }

    public static string RenderPreviewJson(TiffPreviewResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return JsonSerializer.Serialize(new
        {
            type = result.Type,
            schemaVersion = result.SchemaVersion,
            runId = Safe(result.RunId),
            status = result.Succeeded ? "succeeded" : "failed",
            result.CorrectnessProof,
            result.AutomaticallyOpened,
            result.AutomaticallyUploaded,
            previews = result.Previews
                .OrderBy(preview => preview.ArtifactId, StringComparer.Ordinal)
                .Select(preview => new
                {
                    artifactId = Safe(preview.ArtifactId),
                    kind = Safe(preview.Kind),
                    path = Safe(preview.Path),
                    preview.Size,
                    preview.Sha256,
                    preview.Width,
                    preview.Height,
                    preview.SourceFrameCount
                }),
            diagnostics = result.Diagnostics
                .OrderBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.ArtifactId, StringComparer.Ordinal)
                .Select(diagnostic => new
                {
                    code = Safe(diagnostic.Code),
                    severity = Safe(diagnostic.Severity),
                    summary = Safe(diagnostic.Summary),
                    artifactId = diagnostic.ArtifactId is null ? null : Safe(diagnostic.ArtifactId),
                    diagnostic.FrameIndex
                }),
            summary = Safe(result.Summary)
        }, JsonOptions);
    }

    public static string RenderPreviewText(TiffPreviewResult result)
    {
        StringBuilder builder = new();
        builder.AppendLine($"runId: {Safe(result.RunId)}");
        builder.AppendLine($"status: {(result.Succeeded ? "succeeded" : "failed")}");
        builder.AppendLine("correctnessProof: false");
        builder.AppendLine("automaticallyOpened: false");
        builder.AppendLine("automaticallyUploaded: false");
        foreach (TiffPreviewArtifact preview in result.Previews.OrderBy(item => item.ArtifactId, StringComparer.Ordinal))
        {
            builder.AppendLine($"preview: {Safe(preview.Path)} ({preview.Width}x{preview.Height}, frames={preview.SourceFrameCount}, sha256={preview.Sha256})");
        }

        foreach (TiffVerificationDiagnostic diagnostic in result.Diagnostics.OrderBy(item => item.Code, StringComparer.Ordinal))
        {
            builder.AppendLine($"diagnostic: {diagnostic.Code} {Safe(diagnostic.Summary)}");
        }

        builder.AppendLine($"summary: {Safe(result.Summary)}");
        return builder.ToString().TrimEnd();
    }

    private static int LevelOrder(TiffVerificationLevelResult level) => level.Level switch
    {
        TiffVerificationLevel.FileValid => 0,
        TiffVerificationLevel.MetadataValid => 1,
        TiffVerificationLevel.ContentCompared => 2,
        TiffVerificationLevel.HumanReviewRequired => 3,
        _ => 4
    };

    private static string Safe(string value) => DiagnosticSecretRedactor.Redact(value ?? string.Empty);

    private static string Cell(string value) => Safe(value).Replace("|", "\\|", StringComparison.Ordinal)
        .Replace("\r", " ", StringComparison.Ordinal)
        .Replace("\n", " ", StringComparison.Ordinal);
}
