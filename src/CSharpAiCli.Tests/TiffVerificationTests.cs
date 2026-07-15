using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CSharpAiCli.Cli;
using CSharpAiCli.Core;
using CSharpAiCli.ProjectPacks.GerberTiff;
using CSharpAiCli.ProjectPacks.Runtime;
using ImageMagick;

namespace CSharpAiCli.Tests;

public sealed class TiffVerificationTests
{
    [Fact]
    public void Decoder_reads_bounded_single_and_multipage_lzw_tiff()
    {
        using TestRun test = TestRun.Create(createRun: false);
        string single = Path.Combine(test.Workspace, "single.tiff");
        string multiple = Path.Combine(test.Workspace, "multiple.tiff");
        WriteTiff(single, 16, 12, frameCount: 1);
        WriteTiff(multiple, 10, 8, frameCount: 2);
        MagickTiffArtifactDecoder decoder = new();

        TiffDecodeResult one = decoder.Decode(Item("tiff-0001", single), test.Temp, includePixels: true);
        TiffDecodeResult two = decoder.Decode(Item("tiff-0002", multiple), test.Temp, includePixels: true);

        Assert.True(one.Succeeded, one.Diagnostic?.Summary);
        Assert.Equal(1, one.Metadata?.FrameCount);
        Assert.Equal("rgb8", one.Metadata?.Frames[0].PixelFormat);
        Assert.NotNull(one.Frames[0].RgbaPixels);
        Assert.True(two.Succeeded, two.Diagnostic?.Summary);
        Assert.Equal(2, two.Metadata?.FrameCount);
        Assert.All(two.Metadata!.Frames, frame => Assert.Equal("lzw", frame.Compression));
    }

    [Fact]
    public void Decoder_rejects_truncated_corrupt_and_unsupported_tiff()
    {
        using TestRun test = TestRun.Create(createRun: false);
        string truncated = Path.Combine(test.Workspace, "truncated.tiff");
        File.WriteAllBytes(truncated, [0x49, 0x49, 0x2A, 0x00, 0x08, 0x00]);
        string unsupported = Path.Combine(test.Workspace, "unsupported.tiff");
        WriteTiff(unsupported, 8, 8, frameCount: 1, grayscale: true, lzw: false);
        MagickTiffArtifactDecoder decoder = new();

        TiffDecodeResult corrupt = decoder.Decode(Item("tiff-0001", truncated), test.Temp, includePixels: false);
        TiffDecodeResult format = decoder.Decode(Item("tiff-0002", unsupported), test.Temp, includePixels: false);

        Assert.False(corrupt.Succeeded);
        Assert.Equal(TiffVerificationErrorCode.DecodeFailed, corrupt.Diagnostic?.Code);
        Assert.False(format.Succeeded);
        Assert.Contains(format.Diagnostic?.Code, new string[]
        {
            TiffVerificationErrorCode.FormatUnsupported,
            TiffVerificationErrorCode.CompressionUnsupported
        });
    }

    [Fact]
    public void Identity_reader_rejects_huge_and_bigtiff_before_decode()
    {
        using TestRun test = TestRun.Create(createRun: false);
        string huge = Path.Combine(test.Workspace, "huge.tiff");
        using (FileStream stream = new(huge, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.Write([0x49, 0x49, 0x2A, 0x00]);
            stream.SetLength(TiffVerificationLimits.MaxFileBytes + 1);
        }

        string bigTiff = Path.Combine(test.Workspace, "big.tiff");
        File.WriteAllBytes(bigTiff, [0x49, 0x49, 0x2B, 0x00, 0x08, 0x00, 0x00, 0x00]);

        TiffFileIdentityResult hugeResult = TiffFileIdentityReader.Read(
            huge, new FileInfo(huge).Length, Hash(huge));
        TiffFileIdentityResult bigResult = TiffFileIdentityReader.Read(
            bigTiff, new FileInfo(bigTiff).Length, Hash(bigTiff));

        Assert.Equal(TiffVerificationErrorCode.FileTooLarge, hugeResult.Diagnostic?.Code);
        Assert.Equal(TiffVerificationErrorCode.BigTiffUnsupported, bigResult.Diagnostic?.Code);
    }

    [Fact]
    public void Baseline_schema_rejects_unknown_fields_and_unproven_exact_hash()
    {
        using TestRun test = TestRun.Create(createRun: false);
        string baseline = Path.Combine(test.Workspace, "baseline.json");
        File.WriteAllText(baseline, """
            {
              "schemaVersion": 1,
              "type": "gerber-tiff.verification-baseline",
              "packId": "gerber-tiff",
              "packVersion": "1.0.0-preview.1",
              "toolName": "magick.exe",
              "toolVersion": "test-version",
              "inputFingerprint": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
              "unknown": true,
              "outputs": []
            }
            """);
        Assert.False(TiffVerificationBaselineLoader.Load(baseline).Succeeded);

        File.WriteAllText(baseline, BaselineJson(
            inputFingerprint: new string('A', 64),
            exactHash: new string('B', 64),
            byteDeterministic: false));
        TiffBaselineLoadResult exact = TiffVerificationBaselineLoader.Load(baseline);
        Assert.False(exact.Succeeded);
        Assert.Equal(TiffVerificationErrorCode.ExactHashNotAllowed, exact.Diagnostic?.Code);
    }

    [Fact]
    public void Identity_and_verifier_reject_invalid_signature_unexpected_output_and_outside_baseline()
    {
        using TestRun signatureTest = TestRun.Create(createRun: false);
        string invalid = Path.Combine(signatureTest.Workspace, "invalid.tiff");
        File.WriteAllBytes(invalid, [0x50, 0x4E, 0x47, 0x00, 0x01]);
        TiffFileIdentityResult signature = TiffFileIdentityReader.Read(
            invalid, new FileInfo(invalid).Length, Hash(invalid));
        Assert.Equal(TiffVerificationErrorCode.SignatureInvalid, signature.Diagnostic?.Code);

        using TestRun unexpectedRun = TestRun.Create();
        File.WriteAllText(Path.Combine(unexpectedRun.Workspace, "output", "unexpected.txt"), "unexpected");
        TiffVerificationRunResult unexpected = new TiffVerificationService(unexpectedRun.Store).Verify(
            unexpectedRun.RunId, unexpectedRun.Snapshot.Workspace, null, unexpectedRun.Now.AddMinutes(1));
        Assert.Contains(unexpected.Result.Diagnostics, diagnostic => diagnostic.Code == TiffVerificationErrorCode.OutputUnexpected);

        using TestRun outsideRun = TestRun.Create();
        string outside = Path.Combine(outsideRun.Root, "outside-baseline.json");
        File.WriteAllText(outside, "{}");
        TiffVerificationRunResult outsideResult = new TiffVerificationService(outsideRun.Store).Verify(
            outsideRun.RunId, outsideRun.Snapshot.Workspace, outside, outsideRun.Now.AddMinutes(1));
        Assert.Contains(outsideResult.Result.Diagnostics, diagnostic => diagnostic.Code == TiffVerificationErrorCode.BaselineOutsideWorkspace);
        Assert.Contains(outsideResult.Result.Levels, level =>
            level.Level == TiffVerificationLevel.FileValid && level.Status == TiffVerificationStatus.Passed);
        Assert.Contains(outsideResult.Result.Levels, level =>
            level.Level == TiffVerificationLevel.ContentCompared && level.Status == TiffVerificationStatus.Failed);
    }

    [Fact]
    public void Verification_renderers_are_deterministic_redacted_and_keep_levels_separate()
    {
        TiffVerificationResult result = new(
            "run_20260715T040000000Z_abcdef12",
            false,
            false,
            null,
            null,
            [
                new(TiffVerificationLevel.FileValid, TiffVerificationStatus.Passed, "file passed"),
                new(TiffVerificationLevel.MetadataValid, TiffVerificationStatus.Failed, "metadata failed"),
                new(TiffVerificationLevel.ContentCompared, TiffVerificationStatus.NotRequested, "not requested"),
                new(TiffVerificationLevel.HumanReviewRequired, TiffVerificationStatus.NotRequested, "not reached")
            ],
            [],
            [],
            [new("test", "error", "apiKey=sk-renderer-secret")],
            [],
            "apiKey=sk-renderer-secret");

        string first = TiffVerificationRenderer.RenderJson(result);
        string second = TiffVerificationRenderer.RenderJson(result);
        string markdown = TiffVerificationRenderer.RenderMarkdown(result);

        Assert.Equal(first, second);
        Assert.DoesNotContain("sk-renderer-secret", first, StringComparison.Ordinal);
        Assert.Contains("apiKey=[redacted]", first, StringComparison.Ordinal);
        Assert.Contains("| `file-valid` | `passed` |", markdown, StringComparison.Ordinal);
        Assert.Contains("| `metadata-valid` | `failed` |", markdown, StringComparison.Ordinal);
        Assert.Contains("Correctness proof: `false`", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_passes_metadata_then_preview_generates_no_overwrite_managed_evidence()
    {
        using TestRun test = TestRun.Create(frameCount: 2);
        TiffVerificationService service = new(test.Store);

        TiffVerificationRunResult verified = service.Verify(
            test.RunId, test.Snapshot.Workspace, baselinePath: null, test.Now.AddMinutes(1));
        TiffPreviewRunResult preview = service.Preview(
            test.RunId, test.Snapshot.Workspace, test.Now.AddMinutes(2));
        TiffPreviewRunResult repeated = service.Preview(
            test.RunId, test.Snapshot.Workspace, test.Now.AddMinutes(3));

        Assert.True(verified.Succeeded, verified.Result.Summary);
        Assert.Equal(ProjectPackRunState.AwaitingAcceptance, verified.Mutation?.Record?.State);
        Assert.Contains(verified.Result.Levels, level =>
            level.Level == TiffVerificationLevel.ContentCompared && level.Status == TiffVerificationStatus.NotRequested);
        Assert.Single(Directory.EnumerateFiles(test.Layout.ReportsPath, "verification-r*.json"));
        Assert.Single(Directory.EnumerateFiles(test.Layout.ReportsPath, "verification-r*.md"));
        Assert.True(preview.Succeeded, preview.Result.Summary);
        Assert.Single(preview.Result.Previews);
        Assert.Equal("tiff-contact-sheet", preview.Result.Previews[0].Kind);
        Assert.False(preview.Result.CorrectnessProof);
        Assert.False(repeated.Succeeded);
        Assert.Contains(repeated.Result.Diagnostics, diagnostic => diagnostic.Code == TiffVerificationErrorCode.PreviewConflict);
        Assert.Equal(ProjectPackRunState.AwaitingAcceptance, test.Store.Read(test.RunId).Record?.State);
    }

    [Fact]
    public void Verify_exact_hash_mismatch_fails_without_reaching_human_gate()
    {
        using TestRun test = TestRun.Create(includeExecutionLog: true);
        string baseline = test.WriteBaseline(exactHash: new string('A', 64), byteDeterministic: true);

        TiffVerificationRunResult result = new TiffVerificationService(test.Store).Verify(
            test.RunId, test.Snapshot.Workspace, Path.GetFileName(baseline), test.Now.AddMinutes(1));

        Assert.False(result.Succeeded);
        Assert.False(result.Result.HardVerificationPassed);
        Assert.Contains(result.Result.Diagnostics, diagnostic => diagnostic.Code == TiffVerificationErrorCode.ExactHashMismatch);
        Assert.Equal(ProjectPackRunState.Failed, result.Mutation?.Record?.State);
        Assert.DoesNotContain(result.Result.Levels, level =>
            level.Level == TiffVerificationLevel.HumanReviewRequired && level.Status == TiffVerificationStatus.Required);
    }

    [Fact]
    public void Verify_pixel_comparison_records_explicit_algorithm_and_tolerance()
    {
        using TestRun test = TestRun.Create(includeExecutionLog: true);
        string pixelBaseline = Path.Combine(test.Workspace, "pixel-baseline.tiff");
        File.Copy(test.TiffPath, pixelBaseline);
        string baseline = test.WriteBaseline(
            exactHash: null,
            byteDeterministic: false,
            pixelBaseline: Path.GetFileName(pixelBaseline),
            pixelBaselineHash: Hash(pixelBaseline));

        TiffVerificationRunResult result = new TiffVerificationService(test.Store).Verify(
            test.RunId, test.Snapshot.Workspace, Path.GetFileName(baseline), test.Now.AddMinutes(1));

        Assert.True(result.Succeeded, string.Join("; ", result.Result.Diagnostics.Select(item => item.Summary)));
        TiffPixelComparisonResult comparison = Assert.Single(result.Result.PixelComparisons);
        Assert.Equal(TiffVerificationSchema.PixelAlgorithm, comparison.Algorithm);
        Assert.Equal(0, comparison.MaxChannelDelta);
        Assert.Equal(0, comparison.DifferentPixels);
        Assert.True(comparison.Passed);
        Assert.Contains(result.Result.Levels, level =>
            level.Level == TiffVerificationLevel.ContentCompared && level.Status == TiffVerificationStatus.Passed);
    }

    [Fact]
    public void Verify_pixel_mismatch_reports_observed_tolerance_failure()
    {
        using TestRun test = TestRun.Create(includeExecutionLog: true);
        string pixelBaseline = Path.Combine(test.Workspace, "different-pixel-baseline.tiff");
        WriteTiff(pixelBaseline, 20, 12, frameCount: 1, firstColor: MagickColors.Green);
        string baseline = test.WriteBaseline(
            exactHash: null,
            byteDeterministic: false,
            pixelBaseline: Path.GetFileName(pixelBaseline),
            pixelBaselineHash: Hash(pixelBaseline));

        TiffVerificationRunResult result = new TiffVerificationService(test.Store).Verify(
            test.RunId, test.Snapshot.Workspace, Path.GetFileName(baseline), test.Now.AddMinutes(1));

        TiffPixelComparisonResult comparison = Assert.Single(result.Result.PixelComparisons);
        Assert.False(result.Succeeded);
        Assert.False(comparison.Passed);
        Assert.True(comparison.ObservedMaxChannelDelta > 0);
        Assert.True(comparison.DifferentPixels > 0);
        Assert.Contains(result.Result.Diagnostics, diagnostic => diagnostic.Code == TiffVerificationErrorCode.PixelMismatch);
    }

    [Fact]
    public void Verify_reports_timeout_and_post_conversion_mutation_stably()
    {
        using TestRun timeoutRun = TestRun.Create();
        TiffVerificationRunResult timedOut = new TiffVerificationService(
            timeoutRun.Store,
            new TimeoutDecoder()).Verify(
                timeoutRun.RunId,
                timeoutRun.Snapshot.Workspace,
                null,
                timeoutRun.Now.AddMinutes(1));
        Assert.Contains(timedOut.Result.Diagnostics, diagnostic => diagnostic.Code == TiffVerificationErrorCode.DecodeTimedOut);

        using TestRun changedRun = TestRun.Create();
        File.AppendAllText(changedRun.TiffPath, "mutation");
        TiffVerificationRunResult changed = new TiffVerificationService(changedRun.Store).Verify(
            changedRun.RunId,
            changedRun.Snapshot.Workspace,
            null,
            changedRun.Now.AddMinutes(1));
        Assert.Contains(changed.Result.Diagnostics, diagnostic => diagnostic.Code == TiffVerificationErrorCode.OutputChanged);
        Assert.Equal(ProjectPackRunState.Failed, changed.Mutation?.Record?.State);
    }

    [Fact]
    public void Packs_verify_and_preview_json_update_run_job_and_report_pointers()
    {
        using TestRun test = TestRun.Create(createJob: true);
        using StringWriter verifyOutput = new();
        int verifyExit = CliCommandFactory.Create(verifyOutput, _ => test.Snapshot)
            .Parse(["packs", "verify", test.RunId, "--output", "json", "--workspace", test.Workspace])
            .Invoke();
        JsonObject verifyJson = Assert.IsType<JsonObject>(JsonNode.Parse(verifyOutput.ToString()));

        using StringWriter previewOutput = new();
        int previewExit = CliCommandFactory.Create(previewOutput, _ => test.Snapshot)
            .Parse(["packs", "preview", test.RunId, "--output", "json", "--workspace", test.Workspace])
            .Invoke();
        JsonObject previewJson = Assert.IsType<JsonObject>(JsonNode.Parse(previewOutput.ToString()));
        JobRecord job = Assert.IsType<JobRecord>(JobRecordStore.Create(test.Snapshot).Read(test.JobId!).Record);

        Assert.Equal(0, verifyExit);
        Assert.Equal("packs.verify", verifyJson["type"]?.GetValue<string>());
        Assert.True(verifyJson["hardVerificationPassed"]?.GetValue<bool>());
        Assert.Equal(0, previewExit);
        Assert.Equal("packs.preview", previewJson["type"]?.GetValue<string>());
        Assert.False(previewJson["correctnessProof"]?.GetValue<bool>());
        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Null(job.TaskReport);
        Assert.Contains(job.Artifacts, artifact => artifact.Kind == JobArtifactKind.ProjectPackVerificationReport);
        Assert.Contains(job.Artifacts, artifact => artifact.Kind == JobArtifactKind.ProjectPackPreview);
        Assert.Equal(ProjectPackRunState.AwaitingAcceptance, test.Store.Read(test.RunId).Record?.State);
    }

    private static TiffInventoryItem Item(string id, string path) =>
        new(id, Path.GetFileName(path), path, new FileInfo(path).Length, Hash(path), "little-endian");

    private static string Hash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void WriteTiff(
        string path,
        int width,
        int height,
        int frameCount,
        bool grayscale = false,
        bool lzw = true,
        MagickColor? firstColor = null)
    {
        using MagickImageCollection images = new();
        for (int index = 0; index < frameCount; index++)
        {
            MagickColor color = grayscale
                ? MagickColors.Gray
                : index % 2 == 0 ? firstColor ?? MagickColors.Red : MagickColors.Blue;
            MagickImage image = new(color, (uint)width, (uint)height)
            {
                ColorType = grayscale ? ColorType.Grayscale : ColorType.TrueColor,
                Depth = 8,
                Density = new Density(300, 300, DensityUnit.PixelsPerInch),
                Format = MagickFormat.Tiff
            };
            image.Alpha(AlphaOption.Off);
            image.Settings.Compression = lzw ? CompressionMethod.LZW : CompressionMethod.NoCompression;
            images.Add(image);
        }

        images.Write(path);
    }

    private static string BaselineJson(
        string inputFingerprint,
        string? exactHash,
        bool byteDeterministic,
        string? pixelBaseline = null,
        string? pixelBaselineHash = null,
        int frameCount = 1,
        int width = 20,
        int height = 12,
        string orientation = "top-left") => JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            type = TiffVerificationSchema.BaselineType,
            packId = "gerber-tiff",
            packVersion = "1.0.0-preview.1",
            toolName = "magick.exe",
            toolVersion = "test-version",
            inputFingerprint,
            outputs = new[]
            {
                new
                {
                    artifactId = "tiff-0001",
                    byteDeterministic,
                    exactSha256 = exactHash,
                    metadata = new
                    {
                        frameCount,
                        width,
                        height,
                        dpiX = 300,
                        dpiY = 300,
                        dpiTolerance = 0.01,
                        bitsPerSample = 8,
                        samplesPerPixel = 3,
                        pixelFormat = "rgb8",
                        compression = "lzw",
                        orientation,
                        hasAlpha = false
                    },
                    pixelComparison = pixelBaseline is null ? null : new
                    {
                        baselineTiffPath = pixelBaseline,
                        baselineSha256 = pixelBaselineHash,
                        algorithm = TiffVerificationSchema.PixelAlgorithm,
                        colorSpace = TiffVerificationSchema.PixelColorSpace,
                        orientation = TiffVerificationSchema.PixelOrientation,
                        alpha = TiffVerificationSchema.PixelAlpha,
                        maxChannelDelta = 0,
                        maxDifferentPixels = 0
                    }
                }
            }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });

    private sealed class TimeoutDecoder : ITiffArtifactDecoder
    {
        public TiffDecodeResult Decode(
            TiffInventoryItem item,
            string managedTemporaryDirectory,
            bool includePixels,
            CancellationToken cancellationToken = default) =>
            new(false, null, [], new TiffVerificationDiagnostic(
                TiffVerificationErrorCode.DecodeTimedOut,
                "error",
                "Controlled decode timeout fixture.",
                item.ArtifactId));

        public TiffPreviewWriteResult WritePreview(
            TiffInventoryItem item,
            string managedTemporaryDirectory,
            string destinationPath,
            CancellationToken cancellationToken = default) =>
            new(false, 0, 0, 0, new TiffVerificationDiagnostic(
                TiffVerificationErrorCode.PreviewFailed,
                "error",
                "Not used."));
    }

    private sealed class TestRun : IDisposable
    {
        private TestRun(string root, string workspace, CliEnvironmentSnapshot snapshot)
        {
            Root = root;
            Workspace = workspace;
            Snapshot = snapshot;
            Store = ManagedProjectPackRunStore.Create(snapshot);
            Temp = Path.Combine(root, "temp");
            Directory.CreateDirectory(Temp);
        }

        public string Root { get; }
        public string Workspace { get; }
        public string Temp { get; }
        public CliEnvironmentSnapshot Snapshot { get; }
        public ManagedProjectPackRunStore Store { get; }
        public DateTimeOffset Now { get; } = DateTimeOffset.Parse("2026-07-15T04:00:00Z");
        public string RunId { get; private set; } = string.Empty;
        public string? JobId { get; private set; }
        public string TiffPath { get; private set; } = string.Empty;
        public ManagedProjectPackRunLayout Layout => Store.GetLayout(RunId);

        public static TestRun Create(
            bool createRun = true,
            int frameCount = 1,
            bool includeExecutionLog = false,
            bool createJob = false)
        {
            string root = Path.Combine(Path.GetTempPath(), "caicli-tiff-verification-tests-" + Guid.NewGuid().ToString("N"));
            string workspace = Path.Combine(root, "workspace");
            Directory.CreateDirectory(workspace);
            CliEnvironmentSnapshot snapshot = CliEnvironmentSnapshot.Create(
                workspacePath: workspace,
                currentDirectory: workspace,
                userProfile: Path.Combine(root, "profile"),
                dotnetSdkVersion: "9.0.308",
                dotnetRuntime: ".NET 9",
                openAiApiKey: null,
                openAiModel: null,
                hasGlobalJson: false);
            TestRun test = new(root, workspace, snapshot);
            if (createRun)
            {
                test.Initialize(frameCount, includeExecutionLog, createJob);
            }

            return test;
        }

        public string WriteBaseline(
            string? exactHash,
            bool byteDeterministic,
            string? pixelBaseline = null,
            string? pixelBaselineHash = null)
        {
            ProjectPackInputManifest manifest = ProjectPackStagingService.LoadManifest(
                ManagedProjectPackRunStore.ReadTextBounded(Layout.InputManifestPath, 2 * 1024 * 1024));
            TiffDecodeResult decoded = new MagickTiffArtifactDecoder().Decode(
                Item("tiff-0001", TiffPath), Temp, includePixels: false);
            TiffFrameMetadata frame = decoded.Metadata!.Frames[0];
            string path = Path.Combine(Workspace, "verification-baseline.json");
            File.WriteAllText(path, BaselineJson(
                TiffInputFingerprint.Compute(manifest),
                exactHash,
                byteDeterministic,
                pixelBaseline,
                pixelBaselineHash,
                decoded.Metadata.FrameCount,
                frame.Width,
                frame.Height,
                frame.Orientation));
            return path;
        }

        private void Initialize(int frameCount, bool includeExecutionLog, bool createJob)
        {
            string input = Path.Combine(Workspace, "input");
            Directory.CreateDirectory(input);
            File.WriteAllText(Path.Combine(input, "board.gbr"), "bounded-gerber-fixture");
            GerberTiffConversionPlan built = new GerberTiffConversionPlanBuilder().Build(
                Snapshot.Workspace, "input", "output", toolPaths: null);
            string planJson = GerberTiffPlanRenderer.RenderJson(built, "--workspace");
            GerberTiffRunPlanSnapshot plan = GerberTiffRunPlanLoader.Load(planJson);
            RunId = ProjectPackRunId.Create(Now);
            ProjectPackRunCorrelation correlation = new();
            if (createJob)
            {
                JobId = JobIdGenerator.Create(Now);
                correlation = new ProjectPackRunCorrelation(jobId: JobId);
                JobRecord job = JobRecord.CreateRunning(
                    JobId,
                    Now,
                    new JobCommandSummary("packs run", plan.PlanId, WorkspaceRoot: Workspace),
                    RunId).WithStatus(
                        JobStatus.Succeeded,
                        Now,
                        0,
                        "conversion-executed-verification-pending",
                        summary: "Conversion fixture completed; verification pending.",
                        taskReport: null);
                JobRecordStore.Create(Snapshot).Create(job);
            }

            ProjectPackRunMutationResult ready = new ProjectPackRunService(Store).CreateAndStage(
                plan,
                Snapshot.Workspace,
                toolPaths: null,
                ProjectPackRunPolicyFingerprint.Compute("approval=never;pack=gerber-tiff"),
                Now,
                correlation,
                RunId);
            Assert.True(ready.Succeeded, ready.Diagnostic?.Summary);
            string output = Path.Combine(Workspace, "output");
            Directory.CreateDirectory(output);
            TiffPath = Path.Combine(output, GerberTiffArtifactNaming.TiffFileName(1, "board.gbr"));
            WriteTiff(TiffPath, 20, 12, frameCount);
            List<ProjectPackRunArtifactPointer> artifacts = [.. ready.Record!.Artifacts];
            artifacts.Add(new ProjectPackRunArtifactPointer(
                "tiff-0001",
                "tiff-output",
                "workspace-output",
                "output/" + Path.GetFileName(TiffPath),
                true,
                new FileInfo(TiffPath).Length,
                Hash(TiffPath)));
            if (includeExecutionLog)
            {
                string log = JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    type = "gerber-tiff.conversion-execution",
                    conversionExecuted = true,
                    tiffVerificationPassed = false,
                    operations = new[]
                    {
                        new
                        {
                            operation = "tiff.encode",
                            tool = new
                            {
                                fileName = "magick.exe",
                                fileSize = 1,
                                sha256 = new string('C', 64),
                                version = "test-version"
                            }
                        }
                    }
                }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
                string logPath = Path.Combine(Layout.LogsPath, "conversion-execution.json");
                File.WriteAllText(logPath, log);
                artifacts.Add(new ProjectPackRunArtifactPointer(
                    "execution-log",
                    "conversion-execution-log",
                    "managed-run",
                    "logs/conversion-execution.json",
                    true,
                    Encoding.UTF8.GetByteCount(log),
                    Hash(logPath)));
            }

            ProjectPackRunRecord runningRecord = ready.Record.Transition(ProjectPackRunState.Running, Now.AddSeconds(1));
            ProjectPackRunCheckpoint runningCheckpoint = Checkpoint(ready.Checkpoint!, runningRecord, ProjectPackRunState.Running);
            ProjectPackRunMutationResult running = Store.Update(runningRecord, runningCheckpoint, ready.Record.Revision);
            Assert.True(running.Succeeded);
            ProjectPackRunRecord verifyingRecord = running.Record!.Transition(
                ProjectPackRunState.Verifying,
                Now.AddSeconds(2),
                summary: "Controlled conversion fixture completed; verification pending.",
                artifacts: artifacts);
            ProjectPackRunCheckpoint verifyingCheckpoint = Checkpoint(
                running.Checkpoint!, verifyingRecord, ProjectPackRunState.Verifying);
            ProjectPackRunMutationResult verifying = Store.Update(
                verifyingRecord, verifyingCheckpoint, running.Record.Revision);
            Assert.True(verifying.Succeeded);
        }

        private static ProjectPackRunCheckpoint Checkpoint(
            ProjectPackRunCheckpoint previous,
            ProjectPackRunRecord record,
            string state) =>
            new(
                previous.SchemaVersion,
                previous.RunId,
                record.Revision,
                state,
                previous.PlanFingerprint,
                previous.PolicyFingerprint,
                record.UpdatedAtUtc,
                previous.Stages,
                approvalPersisted: false);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
