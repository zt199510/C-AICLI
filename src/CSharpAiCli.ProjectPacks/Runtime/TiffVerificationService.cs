using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CSharpAiCli.Core;

namespace CSharpAiCli.ProjectPacks.Runtime;

public sealed record TiffVerificationRunResult(
    TiffVerificationResult Result,
    ProjectPackRunMutationResult? Mutation,
    TiffVerificationDiagnostic? PersistenceDiagnostic)
{
    public bool Succeeded => Result.HardVerificationPassed &&
        Mutation?.Succeeded == true && Mutation.Record?.State == ProjectPackRunState.AwaitingAcceptance &&
        PersistenceDiagnostic is null;
}

public sealed record TiffPreviewRunResult(
    TiffPreviewResult Result,
    ProjectPackRunMutationResult? Mutation,
    TiffVerificationDiagnostic? PersistenceDiagnostic)
{
    public bool Succeeded => Result.Succeeded && Mutation?.Succeeded == true && PersistenceDiagnostic is null;
}

public sealed class TiffVerificationService
{
    private readonly ManagedProjectPackRunStore store;
    private readonly ProjectPackRunService runService;
    private readonly TiffArtifactInventoryService inventoryService;
    private readonly ITiffArtifactDecoder decoder;

    public TiffVerificationService(
        ManagedProjectPackRunStore store,
        ITiffArtifactDecoder? decoder = null,
        TiffArtifactInventoryService? inventoryService = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        runService = new ProjectPackRunService(store);
        this.decoder = decoder ?? new MagickTiffArtifactDecoder();
        this.inventoryService = inventoryService ?? new TiffArtifactInventoryService();
    }

    public TiffVerificationRunResult Verify(
        string runId,
        WorkspaceContext workspace,
        string? baselinePath,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        RunVerificationContext? context = LoadContext(runId, ProjectPackRunState.Verifying, out TiffVerificationDiagnostic? loadDiagnostic);
        if (context is null)
        {
            TiffVerificationResult failed = FailureResult(runId, loadDiagnostic!);
            return new TiffVerificationRunResult(failed, null, loadDiagnostic);
        }

        List<TiffVerificationDiagnostic> diagnostics = [];
        List<TiffArtifactMetadata> metadata = [];
        List<TiffPixelComparisonResult> pixelComparisons = [];
        BaselineContext? baseline = null;
        bool baselineRequested = !string.IsNullOrWhiteSpace(baselinePath);
        if (baselineRequested)
        {
            baseline = LoadBaseline(
                baselinePath!,
                workspace,
                context,
                diagnostics);
        }

        TiffInventoryResult inventory = inventoryService.Revalidate(
            context.Record,
            context.Plan,
            context.Manifest,
            context.Layout,
            workspace,
            cancellationToken);
        diagnostics.AddRange(inventory.Diagnostics);
        bool fileValid = inventory.Succeeded;
        bool metadataValid = false;
        bool contentRequested = baseline?.Manifest.Outputs.Any(output =>
            output.ExactSha256 is not null || output.PixelComparison is not null) ?? baselineRequested;
        bool contentCompared = !contentRequested;

        if (fileValid)
        {
            foreach (TiffInventoryItem item in inventory.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                TiffBaselineOutput? expected = baseline?.Manifest.Outputs
                    .SingleOrDefault(output => output.ArtifactId == item.ArtifactId);
                bool includePixels = expected?.PixelComparison is not null;
                TiffDecodeResult decoded = decoder.Decode(
                    item,
                    Path.Combine(context.Layout.WorkingPath, "tiff-decode-temp"),
                    includePixels,
                    cancellationToken);
                if (!decoded.Succeeded || decoded.Metadata is null)
                {
                    diagnostics.Add(decoded.Diagnostic ?? new TiffVerificationDiagnostic(
                        TiffVerificationErrorCode.DecodeFailed,
                        "error",
                        "TIFF decoder failed without structured evidence.",
                        item.ArtifactId));
                    fileValid = false;
                    continue;
                }

                metadata.Add(decoded.Metadata);
                if (baseline is null)
                {
                    continue;
                }

                if (expected is null)
                {
                    diagnostics.Add(Error(TiffVerificationErrorCode.BaselineInvalid,
                        "Baseline output inventory does not match the run output inventory.", item.ArtifactId));
                    continue;
                }

                CompareMetadata(decoded.Metadata, expected.Metadata, diagnostics);
                if (expected.ExactSha256 is not null &&
                    !item.Sha256.Equals(expected.ExactSha256, StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add(Error(TiffVerificationErrorCode.ExactHashMismatch,
                        "Byte-deterministic TIFF SHA256 does not match the baseline.", item.ArtifactId));
                }

                if (expected.PixelComparison is not null)
                {
                    TiffPixelComparisonResult? pixel = ComparePixels(
                        item,
                        decoded,
                        expected.PixelComparison,
                        workspace,
                        context.Layout,
                        diagnostics,
                        cancellationToken);
                    if (pixel is not null)
                    {
                        pixelComparisons.Add(pixel);
                    }
                }
            }

            if (baseline is not null &&
                baseline.Manifest.Outputs.Select(output => output.ArtifactId).OrderBy(id => id, StringComparer.Ordinal)
                    .SequenceEqual(inventory.Items.Select(item => item.ArtifactId).OrderBy(id => id, StringComparer.Ordinal), StringComparer.Ordinal) == false)
            {
                diagnostics.Add(Error(TiffVerificationErrorCode.BaselineInvalid,
                    "Baseline output ids do not exactly match the declared TIFF inventory."));
            }

            metadataValid = fileValid && (!baselineRequested || baseline is not null) &&
                !diagnostics.Any(diagnostic => diagnostic.Severity == "error" &&
                diagnostic.Code is TiffVerificationErrorCode.FormatUnsupported
                    or TiffVerificationErrorCode.CompressionUnsupported
                    or TiffVerificationErrorCode.MetadataMismatch
                    or TiffVerificationErrorCode.BaselineInvalid);
            contentCompared = !contentRequested || baseline is not null && metadataValid &&
                !diagnostics.Any(diagnostic => diagnostic.Severity == "error" &&
                    diagnostic.Code is TiffVerificationErrorCode.ExactHashMismatch
                        or TiffVerificationErrorCode.PixelMismatch
                        or TiffVerificationErrorCode.BaselineChanged) &&
                pixelComparisons.All(comparison => comparison.Passed);

            TiffInventoryResult postInventory = inventoryService.Revalidate(
                context.Record,
                context.Plan,
                context.Manifest,
                context.Layout,
                workspace,
                cancellationToken);
            if (!postInventory.Succeeded || !SameInventory(inventory.Items, postInventory.Items))
            {
                diagnostics.AddRange(postInventory.Diagnostics);
                diagnostics.Add(Error(TiffVerificationErrorCode.OutputChanged,
                    "TIFF/source inventory changed during verification."));
                fileValid = false;
                metadataValid = false;
                contentCompared = false;
            }

            if (baseline is not null)
            {
                try
                {
                    ManagedProjectPackRunStore.EnsureNoReparseInExistingChain(baseline.FullPath);
                    TiffBaselineLoadResult postBaseline = TiffVerificationBaselineLoader.Load(baseline.FullPath);
                    if (!postBaseline.Succeeded || postBaseline.Sha256 != baseline.Sha256)
                    {
                        throw new IOException();
                    }
                }
                catch (Exception exception) when (exception is IOException
                    or UnauthorizedAccessException
                    or NotSupportedException
                    or ProjectPackContractException)
                {
                    diagnostics.Add(Error(TiffVerificationErrorCode.BaselineChanged,
                        "TIFF verification baseline changed while verification was running."));
                    metadataValid = false;
                    contentCompared = false;
                }
            }
        }

        bool hardPassed = fileValid && metadataValid && (!contentRequested || contentCompared) &&
            diagnostics.All(diagnostic => diagnostic.Severity != "error");
        IReadOnlyList<TiffVerificationLevelResult> levels = BuildLevels(
            fileValid,
            metadataValid,
            contentRequested,
            contentCompared,
            hardPassed);
        string summary = hardPassed
            ? "TIFF hard verification passed; the run now requires explicit human review and is not accepted."
            : "TIFF hard verification failed; lower-level evidence does not establish content correctness.";
        TiffVerificationResult result = new(
            runId,
            hardPassed,
            hardPassed,
            baseline?.RelativePath,
            baseline?.Sha256,
            levels,
            metadata.OrderBy(item => item.ArtifactId, StringComparer.Ordinal).ToArray(),
            pixelComparisons.OrderBy(item => item.ArtifactId, StringComparer.Ordinal).ToArray(),
            BoundDiagnostics(diagnostics),
            [],
            summary);

        ReportPersistenceResult reports = PersistVerificationReports(context, result);
        if (!reports.Succeeded)
        {
            return new TiffVerificationRunResult(result, null, reports.Diagnostic);
        }

        string? errorCode = result.Diagnostics.FirstOrDefault(diagnostic => diagnostic.Severity == "error")?.Code;
        ProjectPackRunMutationResult mutation = runService.CompleteVerification(
            runId,
            hardPassed,
            reports.Pointers,
            nowUtc,
            errorCode,
            summary);
        TiffVerificationDiagnostic? mutationDiagnostic = mutation.Succeeded
            ? null
            : Error(
                mutation.Diagnostic?.ErrorCode ?? ProjectPackRunErrorCode.RecordWriteFailed,
                mutation.Diagnostic?.Summary ?? "TIFF verification evidence could not be attached to the run.");
        return new TiffVerificationRunResult(result, mutation, mutationDiagnostic);
    }

    public TiffPreviewRunResult Preview(
        string runId,
        WorkspaceContext workspace,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        RunVerificationContext? context = LoadContext(
            runId,
            ProjectPackRunState.AwaitingAcceptance,
            out TiffVerificationDiagnostic? loadDiagnostic);
        if (context is null)
        {
            TiffPreviewResult failed = new(runId, false, [], [loadDiagnostic!], loadDiagnostic!.Summary);
            return new TiffPreviewRunResult(failed, null, loadDiagnostic);
        }

        TiffInventoryResult inventory = inventoryService.Revalidate(
            context.Record,
            context.Plan,
            context.Manifest,
            context.Layout,
            workspace,
            cancellationToken);
        List<TiffVerificationDiagnostic> diagnostics = [.. inventory.Diagnostics];
        List<TiffPreviewArtifact> previews = [];
        string previewRoot = Path.Combine(context.Layout.ArtifactsPath, "previews");
        if (inventory.Succeeded)
        {
            try
            {
                ManagedProjectPackRunStore.EnsureNoReparseInExistingChain(context.Layout.ArtifactsPath);
                ManagedProjectPackRunStore.EnsureNoReparseInExistingChain(previewRoot);
                Directory.CreateDirectory(previewRoot);
                ManagedProjectPackRunStore.EnsureNoReparseInExistingChain(previewRoot);
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or ProjectPackContractException)
            {
                diagnostics.Add(Error(TiffVerificationErrorCode.PreviewFailed,
                    "Managed preview directory failed containment or reparse validation."));
            }

            foreach (TiffInventoryItem item in inventory.Items)
            {
                if (diagnostics.Any(diagnostic => diagnostic.Severity == "error"))
                {
                    break;
                }

                string fileName = $"preview-{item.ArtifactId}.png";
                string destination = Path.Combine(previewRoot, fileName);
                TiffPreviewWriteResult written = decoder.WritePreview(
                    item,
                    Path.Combine(context.Layout.WorkingPath, "tiff-preview-temp"),
                    destination,
                    cancellationToken);
                if (!written.Succeeded)
                {
                    diagnostics.Add(written.Diagnostic ?? Error(
                        TiffVerificationErrorCode.PreviewFailed,
                        "PNG preview failed without structured evidence.",
                        item.ArtifactId));
                    continue;
                }

                try
                {
                    ManagedProjectPackRunStore.EnsureNoReparseInExistingChain(destination);
                    using FileStream stream = new(destination, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
                    string sha256 = Convert.ToHexString(SHA256.HashData(stream));
                    previews.Add(new TiffPreviewArtifact(
                        item.ArtifactId,
                        written.SourceFrameCount > 1 ? "tiff-contact-sheet" : "tiff-preview",
                        "artifacts/previews/" + fileName,
                        stream.Length,
                        sha256,
                        written.Width,
                        written.Height,
                        written.SourceFrameCount));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
                {
                    diagnostics.Add(Error(TiffVerificationErrorCode.PreviewFailed,
                        "Generated PNG preview could not be inventoried safely.", item.ArtifactId));
                }
            }
        }

        bool succeeded = inventory.Succeeded && previews.Count == inventory.Items.Count &&
            diagnostics.All(diagnostic => diagnostic.Severity != "error");
        string summary = succeeded
            ? "Managed PNG preview/contact sheet evidence was generated for human review; it is not a correctness proof."
            : "Managed PNG preview generation was incomplete; hard verification state was not upgraded or accepted.";
        TiffPreviewResult result = new(
            runId,
            succeeded,
            previews.OrderBy(preview => preview.ArtifactId, StringComparer.Ordinal).ToArray(),
            BoundDiagnostics(diagnostics),
            summary);
        ReportPersistenceResult report = PersistPreviewReport(context, result);
        if (!report.Succeeded)
        {
            return new TiffPreviewRunResult(result, null, report.Diagnostic);
        }

        List<ProjectPackRunArtifactPointer> pointers = [.. report.Pointers];
        pointers.AddRange(previews.Select(preview => new ProjectPackRunArtifactPointer(
            "preview-" + preview.ArtifactId,
            preview.Kind,
            "managed-run",
            preview.Path,
            true,
            preview.Size,
            preview.Sha256)));
        ProjectPackRunMutationResult mutation = runService.AttachVerificationEvidence(
            runId,
            pointers,
            nowUtc,
            summary);
        TiffVerificationDiagnostic? mutationDiagnostic = mutation.Succeeded
            ? null
            : Error(
                mutation.Diagnostic?.ErrorCode ?? ProjectPackRunErrorCode.RecordWriteFailed,
                mutation.Diagnostic?.Summary ?? "Preview evidence could not be attached to the run.");
        return new TiffPreviewRunResult(result, mutation, mutationDiagnostic);
    }

    private RunVerificationContext? LoadContext(
        string runId,
        string requiredState,
        out TiffVerificationDiagnostic? diagnostic)
    {
        ProjectPackRunReadResult read = store.Read(runId);
        if (!read.Succeeded || read.Record is null || read.Checkpoint is null)
        {
            diagnostic = Error(
                read.Diagnostic?.ErrorCode ?? ProjectPackRunErrorCode.NotFound,
                read.Diagnostic?.Summary ?? "Project pack run could not be loaded.");
            return null;
        }

        if (read.Record.State != requiredState || read.Record.PackId != "gerber-tiff")
        {
            diagnostic = Error(
                TiffVerificationErrorCode.RunStateInvalid,
                $"Command requires a gerber-tiff run in '{requiredState}' state.");
            return null;
        }

        try
        {
            ManagedProjectPackRunLayout layout = store.GetLayout(runId);
            GerberTiffRunPlanSnapshot plan = GerberTiffRunPlanLoader.Load(
                ManagedProjectPackRunStore.ReadTextBounded(layout.PlanPath, 2 * 1024 * 1024));
            ProjectPackInputManifest manifest = ProjectPackStagingService.LoadManifest(
                ManagedProjectPackRunStore.ReadTextBounded(layout.InputManifestPath, 2 * 1024 * 1024));
            diagnostic = null;
            return new RunVerificationContext(read.Record, read.Checkpoint, layout, plan, manifest);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ProjectPackContractException
            or JsonException)
        {
            diagnostic = Error(ProjectPackRunErrorCode.RecordCorrupt,
                "Managed project pack plan or input manifest is corrupt.");
            return null;
        }
    }

    private static BaselineContext? LoadBaseline(
        string baselinePath,
        WorkspaceContext workspace,
        RunVerificationContext context,
        List<TiffVerificationDiagnostic> diagnostics)
    {
        try
        {
            WorkspaceGuardResult guard = new WorkspaceGuard().ResolvePath(workspace, baselinePath);
            if (!guard.IsAllowed || guard.FullPath is null || !File.Exists(guard.FullPath))
            {
                diagnostics.Add(Error(TiffVerificationErrorCode.BaselineOutsideWorkspace,
                    "TIFF baseline must be an explicit regular file inside the workspace."));
                return null;
            }

            ManagedProjectPackRunStore.EnsureNoReparseInExistingChain(guard.FullPath);
            TiffBaselineLoadResult loaded = TiffVerificationBaselineLoader.Load(guard.FullPath);
            if (!loaded.Succeeded || loaded.Baseline is null || loaded.Sha256 is null)
            {
                diagnostics.Add(loaded.Diagnostic ?? Error(
                    TiffVerificationErrorCode.BaselineInvalid,
                    "TIFF baseline failed strict validation."));
                return null;
            }

            string inputFingerprint = TiffInputFingerprint.Compute(context.Manifest);
            if (loaded.Baseline.PackVersion != context.Record.PackVersion ||
                !loaded.Baseline.InputFingerprint.Equals(inputFingerprint, StringComparison.OrdinalIgnoreCase) ||
                !ExecutionToolMatches(context, loaded.Baseline))
            {
                diagnostics.Add(Error(TiffVerificationErrorCode.BaselineInvalid,
                    "Baseline pack/tool/input identity does not match the controlled conversion evidence."));
                return null;
            }

            return new BaselineContext(
                loaded.Baseline,
                guard.FullPath,
                NormalizeRelative(Path.GetRelativePath(workspace.RootPath, guard.FullPath)),
                loaded.Sha256);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ProjectPackContractException
            or ArgumentException)
        {
            diagnostics.Add(Error(TiffVerificationErrorCode.BaselineOutsideWorkspace,
                "TIFF baseline path could not be resolved safely inside the workspace."));
            return null;
        }
    }

    private static bool ExecutionToolMatches(RunVerificationContext context, TiffVerificationBaseline baseline)
    {
        try
        {
            ProjectPackRunArtifactPointer pointer = context.Record.Artifacts.Single(artifact =>
                artifact.Kind == "conversion-execution-log" && artifact.Scope == "managed-run");
            if (pointer.Size is null || pointer.Sha256 is null ||
                !pointer.Path.StartsWith("logs/", StringComparison.Ordinal) || pointer.Path.Contains("..", StringComparison.Ordinal))
            {
                return false;
            }

            string path = Path.GetFullPath(Path.Combine(context.Layout.RunRoot,
                pointer.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (!TiffArtifactInventoryService.IsWithin(context.Layout.LogsPath, path))
            {
                return false;
            }

            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.LongLength != pointer.Size ||
                !Convert.ToHexString(SHA256.HashData(bytes)).Equals(pointer.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            JsonElement root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
                root.GetProperty("type").GetString() != "gerber-tiff.conversion-execution")
            {
                return false;
            }

            JsonElement[] encodes = root.GetProperty("operations").EnumerateArray()
                .Where(operation => operation.GetProperty("operation").GetString() == "tiff.encode")
                .ToArray();
            return encodes.Length > 0 && encodes.All(operation =>
            {
                JsonElement tool = operation.GetProperty("tool");
                return tool.GetProperty("fileName").GetString() == baseline.ToolName &&
                    tool.GetProperty("version").GetString() == baseline.ToolVersion;
            });
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or JsonException)
        {
            return false;
        }
    }

    private TiffPixelComparisonResult? ComparePixels(
        TiffInventoryItem actualItem,
        TiffDecodeResult actual,
        TiffBaselinePixelComparison comparison,
        WorkspaceContext workspace,
        ManagedProjectPackRunLayout layout,
        List<TiffVerificationDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        WorkspaceGuardResult guard = new WorkspaceGuard().ResolvePath(workspace, comparison.BaselineTiffPath);
        if (!guard.IsAllowed || guard.FullPath is null || !File.Exists(guard.FullPath))
        {
            diagnostics.Add(Error(TiffVerificationErrorCode.BaselineOutsideWorkspace,
                "Pixel baseline TIFF is missing or outside the workspace.", actualItem.ArtifactId));
            return null;
        }

        FileInfo info = new(guard.FullPath);
        TiffFileIdentityResult identity = TiffFileIdentityReader.Read(
            guard.FullPath,
            info.Length,
            comparison.BaselineSha256,
            cancellationToken);
        if (!identity.Succeeded)
        {
            diagnostics.Add((identity.Diagnostic ?? Error(
                TiffVerificationErrorCode.BaselineChanged,
                "Pixel baseline TIFF identity is invalid.")) with { ArtifactId = actualItem.ArtifactId });
            return null;
        }

        TiffInventoryItem baselineItem = new(
            "baseline-" + actualItem.ArtifactId,
            comparison.BaselineTiffPath,
            guard.FullPath,
            info.Length,
            comparison.BaselineSha256,
            identity.ByteOrder!);
        TiffDecodeResult expected = decoder.Decode(
            baselineItem,
            Path.Combine(layout.WorkingPath, "tiff-baseline-decode-temp"),
            includePixels: true,
            cancellationToken);
        if (!expected.Succeeded)
        {
            diagnostics.Add((expected.Diagnostic ?? Error(
                TiffVerificationErrorCode.DecodeFailed,
                "Pixel baseline TIFF could not be decoded.")) with { ArtifactId = actualItem.ArtifactId });
            return null;
        }

        if (actual.Frames.Count != expected.Frames.Count || actual.Frames.Zip(expected.Frames).Any(pair =>
            pair.First.Metadata.Width != pair.Second.Metadata.Width ||
            pair.First.Metadata.Height != pair.Second.Metadata.Height ||
            pair.First.RgbaPixels is null || pair.Second.RgbaPixels is null ||
            pair.First.RgbaPixels.Length != pair.Second.RgbaPixels.Length))
        {
            diagnostics.Add(Error(TiffVerificationErrorCode.PixelMismatch,
                "Normalized TIFF pixel dimensions or frame counts do not match the baseline.", actualItem.ArtifactId));
            return null;
        }

        long differentPixels = 0;
        int observedMax = 0;
        using IncrementalHash actualHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using IncrementalHash baselineHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach ((TiffDecodedFrame actualFrame, TiffDecodedFrame baselineFrame) in actual.Frames.Zip(expected.Frames))
        {
            byte[] actualPixels = actualFrame.RgbaPixels!;
            byte[] baselinePixels = baselineFrame.RgbaPixels!;
            actualHash.AppendData(actualPixels);
            baselineHash.AppendData(baselinePixels);
            for (int index = 0; index < actualPixels.Length; index += 4)
            {
                int pixelMax = 0;
                for (int channel = 0; channel < 4; channel++)
                {
                    int delta = Math.Abs(actualPixels[index + channel] - baselinePixels[index + channel]);
                    pixelMax = Math.Max(pixelMax, delta);
                    observedMax = Math.Max(observedMax, delta);
                }

                if (pixelMax > comparison.MaxChannelDelta)
                {
                    differentPixels++;
                }
            }
        }

        bool passed = differentPixels <= comparison.MaxDifferentPixels;
        if (!passed)
        {
            diagnostics.Add(Error(TiffVerificationErrorCode.PixelMismatch,
                "Normalized TIFF pixels exceed the explicit channel/count tolerance.", actualItem.ArtifactId));
        }

        return new TiffPixelComparisonResult(
            actualItem.ArtifactId,
            comparison.Algorithm,
            comparison.ColorSpace,
            comparison.Orientation,
            comparison.Alpha,
            comparison.MaxChannelDelta,
            comparison.MaxDifferentPixels,
            differentPixels,
            observedMax,
            Convert.ToHexString(actualHash.GetHashAndReset()),
            Convert.ToHexString(baselineHash.GetHashAndReset()),
            passed);
    }

    private static void CompareMetadata(
        TiffArtifactMetadata actual,
        TiffBaselineMetadata expected,
        List<TiffVerificationDiagnostic> diagnostics)
    {
        if (actual.FrameCount != expected.FrameCount)
        {
            diagnostics.Add(Error(TiffVerificationErrorCode.MetadataMismatch,
                "TIFF frame count does not match the baseline.", actual.ArtifactId));
            return;
        }

        foreach (TiffFrameMetadata frame in actual.Frames)
        {
            if (frame.Width != expected.Width || frame.Height != expected.Height ||
                Math.Abs(frame.DpiX - expected.DpiX) > expected.DpiTolerance ||
                Math.Abs(frame.DpiY - expected.DpiY) > expected.DpiTolerance ||
                frame.BitsPerSample != expected.BitsPerSample ||
                frame.SamplesPerPixel != expected.SamplesPerPixel ||
                frame.PixelFormat != expected.PixelFormat || frame.Compression != expected.Compression ||
                frame.Orientation != expected.Orientation || frame.HasAlpha != expected.HasAlpha)
            {
                diagnostics.Add(Error(TiffVerificationErrorCode.MetadataMismatch,
                    "TIFF frame metadata does not match the explicit baseline fields/tolerance " +
                    $"(actual={frame.Width}x{frame.Height},dpi={frame.DpiX:0.###}x{frame.DpiY:0.###}," +
                    $"bits={frame.BitsPerSample},samples={frame.SamplesPerPixel},format={frame.PixelFormat}," +
                    $"compression={frame.Compression},orientation={frame.Orientation},alpha={frame.HasAlpha};" +
                    $"expected={expected.Width}x{expected.Height},dpi={expected.DpiX:0.###}x{expected.DpiY:0.###}," +
                    $"bits={expected.BitsPerSample},samples={expected.SamplesPerPixel},format={expected.PixelFormat}," +
                    $"compression={expected.Compression},orientation={expected.Orientation},alpha={expected.HasAlpha}).",
                    actual.ArtifactId,
                    frame.Index));
            }
        }
    }

    private static ReportPersistenceResult PersistVerificationReports(
        RunVerificationContext context,
        TiffVerificationResult result)
    {
        string suffix = $"r{context.Record.Revision:D4}";
        string jsonName = $"verification-{suffix}.json";
        string markdownName = $"verification-{suffix}.md";
        string json = TiffVerificationRenderer.RenderJson(result) + Environment.NewLine;
        string markdown = TiffVerificationRenderer.RenderMarkdown(result);
        if (Encoding.UTF8.GetByteCount(json) > TiffVerificationLimits.MaxReportBytes ||
            Encoding.UTF8.GetByteCount(markdown) > TiffVerificationLimits.MaxReportBytes)
        {
            return new ReportPersistenceResult(false, [], Error(
                TiffVerificationErrorCode.ReportWriteFailed,
                "TIFF verification report exceeds the frozen report-size limit."));
        }

        try
        {
            ManagedProjectPackRunStore.EnsureNoReparseInExistingChain(context.Layout.ReportsPath);
            string jsonPath = Path.Combine(context.Layout.ReportsPath, jsonName);
            string markdownPath = Path.Combine(context.Layout.ReportsPath, markdownName);
            ManagedProjectPackRunStore.WriteTextAtomically(jsonPath, json, overwrite: false);
            ManagedProjectPackRunStore.WriteTextAtomically(markdownPath, markdown, overwrite: false);
            return new ReportPersistenceResult(true,
            [
                Pointer("inspection-report", "tiff-verification-json", "reports/" + jsonName, json),
                Pointer("inspection-report-markdown", "tiff-verification-markdown", "reports/" + markdownName, markdown)
            ], null);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ProjectPackContractException)
        {
            return new ReportPersistenceResult(false, [], Error(
                TiffVerificationErrorCode.ReportWriteFailed,
                "Stable TIFF verification reports could not be written with no-overwrite semantics."));
        }
    }

    private static ReportPersistenceResult PersistPreviewReport(
        RunVerificationContext context,
        TiffPreviewResult result)
    {
        string name = $"preview-r{context.Record.Revision:D4}.json";
        string json = TiffVerificationRenderer.RenderPreviewJson(result) + Environment.NewLine;
        if (Encoding.UTF8.GetByteCount(json) > TiffVerificationLimits.MaxReportBytes)
        {
            return new ReportPersistenceResult(false, [], Error(
                TiffVerificationErrorCode.ReportWriteFailed,
                "TIFF preview report exceeds the frozen report-size limit."));
        }

        try
        {
            ManagedProjectPackRunStore.EnsureNoReparseInExistingChain(context.Layout.ReportsPath);
            ManagedProjectPackRunStore.WriteTextAtomically(
                Path.Combine(context.Layout.ReportsPath, name),
                json,
                overwrite: false);
            return new ReportPersistenceResult(true,
                [Pointer("preview-report", "tiff-preview-report", "reports/" + name, json)], null);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ProjectPackContractException)
        {
            return new ReportPersistenceResult(false, [], Error(
                TiffVerificationErrorCode.ReportWriteFailed,
                "Stable TIFF preview report could not be written with no-overwrite semantics."));
        }
    }

    private static ProjectPackRunArtifactPointer Pointer(
        string id,
        string kind,
        string relativePath,
        string content)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        return new ProjectPackRunArtifactPointer(
            id,
            kind,
            "managed-run",
            relativePath,
            true,
            bytes.Length,
            Convert.ToHexString(SHA256.HashData(bytes)));
    }

    private static IReadOnlyList<TiffVerificationLevelResult> BuildLevels(
        bool fileValid,
        bool metadataValid,
        bool contentRequested,
        bool contentCompared,
        bool hardPassed) =>
    [
        new(TiffVerificationLevel.FileValid,
            fileValid ? TiffVerificationStatus.Passed : TiffVerificationStatus.Failed,
            fileValid ? "Signature, bounded decoder, inventory, and mutation checks passed." : "File-level validation failed."),
        new(TiffVerificationLevel.MetadataValid,
            metadataValid ? TiffVerificationStatus.Passed : TiffVerificationStatus.Failed,
            metadataValid ? "Frozen TIFF format fields and requested baseline metadata passed." : "Metadata validation failed or was blocked by file validation."),
        new(TiffVerificationLevel.ContentCompared,
            contentRequested
                ? contentCompared ? TiffVerificationStatus.Passed : TiffVerificationStatus.Failed
                : TiffVerificationStatus.NotRequested,
            contentRequested
                ? contentCompared ? "Requested exact hash/pixel comparisons passed." : "Requested content comparison failed."
                : "No exact hash or pixel baseline was requested."),
        new(TiffVerificationLevel.HumanReviewRequired,
            hardPassed ? TiffVerificationStatus.Required : TiffVerificationStatus.NotRequested,
            hardPassed ? "Hard verification passed; explicit human accept/reject remains required." : "Human gate was not reached because hard verification failed.")
    ];

    private static bool SameInventory(
        IReadOnlyList<TiffInventoryItem> first,
        IReadOnlyList<TiffInventoryItem> second) =>
        first.Count == second.Count && first.Zip(second).All(pair =>
            pair.First.ArtifactId == pair.Second.ArtifactId &&
            pair.First.RelativePath == pair.Second.RelativePath &&
            pair.First.Size == pair.Second.Size &&
            pair.First.Sha256 == pair.Second.Sha256);

    private static TiffVerificationResult FailureResult(string runId, TiffVerificationDiagnostic diagnostic) =>
        new(
            runId,
            false,
            false,
            null,
            null,
            BuildLevels(false, false, false, false, false),
            [],
            [],
            [diagnostic],
            [],
            diagnostic.Summary);

    private static IReadOnlyList<TiffVerificationDiagnostic> BoundDiagnostics(
        IEnumerable<TiffVerificationDiagnostic> diagnostics) =>
        new ReadOnlyCollection<TiffVerificationDiagnostic>(diagnostics
            .Distinct()
            .OrderBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.ArtifactId, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.FrameIndex)
            .Take(TiffVerificationLimits.MaxDiagnosticCount)
            .ToArray());

    private static TiffVerificationDiagnostic Error(
        string code,
        string summary,
        string? artifactId = null,
        int? frameIndex = null) =>
        new(code, "error", DiagnosticSecretRedactor.Redact(summary), artifactId, frameIndex);

    private static string NormalizeRelative(string value) => value.Replace(Path.DirectorySeparatorChar, '/');

    private sealed record RunVerificationContext(
        ProjectPackRunRecord Record,
        ProjectPackRunCheckpoint Checkpoint,
        ManagedProjectPackRunLayout Layout,
        GerberTiffRunPlanSnapshot Plan,
        ProjectPackInputManifest Manifest);

    private sealed record BaselineContext(
        TiffVerificationBaseline Manifest,
        string FullPath,
        string RelativePath,
        string Sha256);

    private sealed record ReportPersistenceResult(
        bool Succeeded,
        IReadOnlyList<ProjectPackRunArtifactPointer> Pointers,
        TiffVerificationDiagnostic? Diagnostic);
}

public static class TiffInputFingerprint
{
    public static string Compute(ProjectPackInputManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        StringBuilder canonical = new("gerber-tiff.input-fingerprint.v1\n");
        foreach (ProjectPackStagedInput input in manifest.Inputs.OrderBy(item => item.SourceRelativePath, StringComparer.Ordinal))
        {
            canonical.Append(input.Id).Append('|')
                .Append(input.SourceRelativePath).Append('|')
                .Append(input.Kind).Append('|')
                .Append(input.LayerRole).Append('|')
                .Append(input.PassedToExternalTool ? "tool" : "sidecar").Append('|')
                .Append(input.Size).Append('|')
                .Append(input.Sha256).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }
}
