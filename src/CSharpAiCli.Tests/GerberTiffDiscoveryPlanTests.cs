using System.Security.Cryptography;
using CSharpAiCli.Core;
using CSharpAiCli.ProjectPacks;
using CSharpAiCli.ProjectPacks.GerberTiff;

namespace CSharpAiCli.Tests;

public sealed class GerberTiffDiscoveryPlanTests
{
    [Fact]
    public void Discovery_classifies_mixed_case_inputs_sidecar_and_unknown_with_stable_hash_order()
    {
        using TestWorkspace test = TestWorkspace.Create();
        test.WriteInput("board.GTL", "top-copper");
        test.WriteInput("board.gBl", "bottom-copper");
        test.WriteInput("holes.DrL", "drill");
        test.WriteInput("board.GBRJOB", "sidecar-secret-content");
        test.WriteInput("notes.txt", "unknown-secret-content");

        GerberTiffInputInventory first = new GerberTiffInputDiscovery().Discover(test.Context, "input");
        GerberTiffInputInventory second = new GerberTiffInputDiscovery().Discover(test.Context, "input");

        Assert.True(first.Succeeded, Diagnostics(first.Diagnostics));
        Assert.Equal(4, first.SupportedFileCount);
        Assert.Equal(3, first.ToolInputCount);
        Assert.Equal(1, first.UnknownFileCount);
        Assert.Equal(
            first.Files.Select(file => file.RelativePath).OrderBy(path => path, StringComparer.Ordinal),
            first.Files.Select(file => file.RelativePath));
        Assert.Equal(
            first.Files.Select(file => file.Sha256),
            second.Files.Select(file => file.Sha256));
        Assert.Contains(first.Files, file => file.LayerRole == "top-copper" && file.Sha256 is not null);
        Assert.Contains(first.Files, file => file.LayerRole == "bottom-copper" && file.Sha256 is not null);
        Assert.Contains(first.Files, file => file.Kind == GerberTiffInputKind.Drill && file.PassedToExternalTool);
        Assert.Contains(first.Files, file => file.Kind == GerberTiffInputKind.Sidecar && !file.PassedToExternalTool && file.Sha256 is not null);
        Assert.Contains(first.Files, file => file.Kind == GerberTiffInputKind.Unknown && file.Sha256 is null);
        Assert.Contains(first.Diagnostics, diagnostic => diagnostic.Code == GerberTiffDiagnosticCode.InputUnknownExtension);
    }

    [Fact]
    public void Discovery_blocks_duplicate_ambiguous_and_missing_layer_mappings()
    {
        using TestWorkspace duplicate = TestWorkspace.Create();
        duplicate.WriteInput("first.gtl", "one");
        duplicate.WriteInput("second.GTL", "two");
        GerberTiffInputInventory duplicateResult = new GerberTiffInputDiscovery().Discover(duplicate.Context, "input");

        using TestWorkspace ambiguous = TestWorkspace.Create();
        ambiguous.WriteInput("board-f-cu-b-cu.gbr", "ambiguous");
        GerberTiffInputInventory ambiguousResult = new GerberTiffInputDiscovery().Discover(ambiguous.Context, "input");

        using TestWorkspace missing = TestWorkspace.Create();
        missing.WriteInput("holes.drl", "drill-only");
        GerberTiffInputInventory missingResult = new GerberTiffInputDiscovery().Discover(missing.Context, "input");

        Assert.False(duplicateResult.Succeeded);
        Assert.Contains(duplicateResult.Diagnostics, diagnostic => diagnostic.Code == GerberTiffDiagnosticCode.InputLayerDuplicate);
        Assert.False(ambiguousResult.Succeeded);
        Assert.Contains(ambiguousResult.Diagnostics, diagnostic => diagnostic.Code == GerberTiffDiagnosticCode.InputLayerAmbiguous);
        Assert.False(missingResult.Succeeded);
        Assert.Contains(missingResult.Diagnostics, diagnostic => diagnostic.Code == GerberTiffDiagnosticCode.InputGerberRequired);
    }

    [Fact]
    public void Discovery_blocks_depth_file_count_and_time_limits_without_silent_success()
    {
        using TestWorkspace depth = TestWorkspace.Create();
        string nested = depth.InputPath;
        for (int index = 0; index <= GerberTiffInputEnvelope.MaxRecursionDepth; index++)
        {
            nested = Path.Combine(nested, "d" + index);
            Directory.CreateDirectory(nested);
        }
        File.WriteAllText(Path.Combine(nested, "board.gbr"), "gerber");
        GerberTiffInputInventory depthResult = new GerberTiffInputDiscovery().Discover(depth.Context, "input");

        using TestWorkspace files = TestWorkspace.Create();
        for (int index = 0; index <= GerberTiffInputEnvelope.MaxFileCount; index++)
        {
            files.WriteInput($"unknown-{index:D4}.txt", "x");
        }
        GerberTiffInputInventory fileResult = new GerberTiffInputDiscovery().Discover(files.Context, "input");

        using TestWorkspace timed = TestWorkspace.Create();
        timed.WriteInput("board.gbr", "gerber");
        GerberTiffInputInventory timeResult = new GerberTiffInputDiscovery(
            () => GerberTiffInputEnvelope.ScanTimeoutMilliseconds + 1L).Discover(timed.Context, "input");

        Assert.Contains(depthResult.Diagnostics, diagnostic => diagnostic.Code == GerberTiffDiagnosticCode.InputDepthLimitExceeded);
        Assert.False(depthResult.Succeeded);
        Assert.Contains(fileResult.Diagnostics, diagnostic => diagnostic.Code == GerberTiffDiagnosticCode.InputFileCountLimitExceeded);
        Assert.False(fileResult.Succeeded);
        Assert.Contains(timeResult.Diagnostics, diagnostic => diagnostic.Code == GerberTiffDiagnosticCode.InputScanTimeLimitExceeded);
        Assert.False(timeResult.Succeeded);
    }

    [Fact]
    public void Discovery_blocks_single_and_total_byte_limits_before_hashing()
    {
        using TestWorkspace single = TestWorkspace.Create();
        single.SetInputLength("large.gbr", GerberTiffInputEnvelope.MaxSingleFileBytes + 1);
        GerberTiffInputInventory singleResult = new GerberTiffInputDiscovery().Discover(single.Context, "input");

        using TestWorkspace total = TestWorkspace.Create();
        for (int index = 0; index < 9; index++)
        {
            total.SetInputLength($"layer-{index:D2}.gbr", GerberTiffInputEnvelope.MaxSingleFileBytes);
        }
        GerberTiffInputInventory totalResult = new GerberTiffInputDiscovery().Discover(total.Context, "input");

        Assert.Contains(singleResult.Diagnostics, diagnostic => diagnostic.Code == GerberTiffDiagnosticCode.InputFileSizeLimitExceeded);
        Assert.Null(Assert.Single(singleResult.Files).Sha256);
        Assert.Contains(totalResult.Diagnostics, diagnostic => diagnostic.Code == GerberTiffDiagnosticCode.InputTotalBytesLimitExceeded);
        Assert.All(totalResult.Files, file => Assert.Null(file.Sha256));
    }

    [Fact]
    public void Discovery_blocks_locked_file_and_outside_directory()
    {
        using TestWorkspace locked = TestWorkspace.Create();
        string lockedPath = locked.WriteInput("board.gbr", "locked");
        GerberTiffInputInventory lockedResult;
        using (FileStream stream = new(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            lockedResult = new GerberTiffInputDiscovery().Discover(locked.Context, "input");
        }

        using TestWorkspace outside = TestWorkspace.Create();
        string outsideDirectory = Path.Combine(outside.RootPath, "outside");
        Directory.CreateDirectory(outsideDirectory);
        File.WriteAllText(Path.Combine(outsideDirectory, "board.gbr"), "outside");
        GerberTiffInputInventory outsideResult = new GerberTiffInputDiscovery().Discover(
            outside.Context,
            outsideDirectory);

        if (OperatingSystem.IsWindows())
        {
            Assert.Contains(lockedResult.Diagnostics, diagnostic => diagnostic.Code == GerberTiffDiagnosticCode.InputFileUnreadable);
            Assert.False(lockedResult.Succeeded);
        }

        Assert.Contains(outsideResult.Diagnostics, diagnostic => diagnostic.Code == GerberTiffDiagnosticCode.InputOutsideWorkspace);
        Assert.False(outsideResult.Succeeded);
    }

    [Fact]
    public void Discovery_blocks_reparse_tree_when_symbolic_links_are_available()
    {
        using TestWorkspace test = TestWorkspace.Create();
        string target = Path.Combine(test.WorkspacePath, "target");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "board.gbr"), "target");
        string link = Path.Combine(test.InputPath, "linked");
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        GerberTiffInputInventory result = new GerberTiffInputDiscovery().Discover(test.Context, "input");

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == GerberTiffDiagnosticCode.InputReparsePoint);
        Assert.False(result.Succeeded);
        Directory.Delete(link);
    }

    [Fact]
    public void Discovery_and_plan_reject_reparse_paths_even_when_targets_remain_inside_workspace()
    {
        using TestWorkspace test = TestWorkspace.Create();
        test.WriteInput("board.gbr", "gerber");
        string inputLink = Path.Combine(test.WorkspacePath, "input-link");
        string outputParent = Path.Combine(test.WorkspacePath, "output-parent");
        string outputLink = Path.Combine(test.WorkspacePath, "output-link");
        Directory.CreateDirectory(outputParent);
        try
        {
            Directory.CreateSymbolicLink(inputLink, test.InputPath);
            Directory.CreateSymbolicLink(outputLink, outputParent);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        GerberTiffInputInventory inputResult = new GerberTiffInputDiscovery().Discover(test.Context, "input-link");
        GerberTiffConversionPlan outputResult = new GerberTiffConversionPlanBuilder().Build(
            test.Context,
            "input",
            Path.Combine("output-link", "new-output"),
            test.CreateStaticTools());

        Assert.Contains(inputResult.Diagnostics, diagnostic => diagnostic.Code == GerberTiffDiagnosticCode.InputReparsePoint);
        Assert.Contains(outputResult.Diagnostics, diagnostic => diagnostic.Code == GerberTiffDiagnosticCode.OutputReparsePoint);
        Assert.False(inputResult.Succeeded);
        Assert.False(outputResult.Runnable);
        Directory.Delete(inputLink);
        Directory.Delete(outputLink);
    }

    [Fact]
    public void Plan_is_deterministic_changes_with_tool_hash_and_never_stores_raw_content_or_absolute_paths()
    {
        using TestWorkspace test = TestWorkspace.Create();
        const string rawContent = "PRIVATE-BOARD-CONTENT-DO-NOT-PERSIST";
        test.WriteInput("board.gbr", rawContent);
        Dictionary<string, string> tools = test.CreateStaticTools();
        GerberTiffConversionPlanBuilder builder = new();

        GerberTiffConversionPlan first = builder.Build(test.Context, "input", "output", tools);
        GerberTiffConversionPlan same = builder.Build(test.Context, "input", "output", tools);
        test.WriteInput("000-unknown.txt", "ignored-unknown-content");
        GerberTiffConversionPlan withUnknown = builder.Build(test.Context, "input", "output", tools);
        File.WriteAllText(tools["gerbv"], "changed-static-gerbv");
        GerberTiffConversionPlan changed = builder.Build(test.Context, "input", "output", tools);
        string json = GerberTiffPlanRenderer.RenderJson(first, "--workspace");

        Assert.True(first.Runnable, Diagnostics(first.Diagnostics));
        Assert.Equal(first.Fingerprint, same.Fingerprint);
        Assert.Equal(first.PlanId, same.PlanId);
        Assert.Equal(first.Fingerprint, withUnknown.Fingerprint);
        Assert.NotEqual(first.Fingerprint, changed.Fingerprint);
        Assert.DoesNotContain(rawContent, json, StringComparison.Ordinal);
        Assert.DoesNotContain(test.RootPath, json, StringComparison.OrdinalIgnoreCase);
        Assert.False(first.ConversionExecuted);
        Assert.False(first.ExecutionAuthorized);
        Assert.False(first.ApprovalPersisted);
        Assert.False(Directory.Exists(Path.Combine(test.WorkspacePath, "output")));
    }

    [Fact]
    public void Plan_blocks_existing_inside_outside_and_overlong_output_paths()
    {
        using TestWorkspace test = TestWorkspace.Create();
        test.WriteInput("board.gbr", "gerber");
        Dictionary<string, string> tools = test.CreateStaticTools();
        string existing = Path.Combine(test.WorkspacePath, "existing");
        Directory.CreateDirectory(existing);
        GerberTiffConversionPlanBuilder builder = new();

        GerberTiffConversionPlan existingResult = builder.Build(test.Context, "input", "existing", tools);
        GerberTiffConversionPlan insideResult = builder.Build(test.Context, "input", Path.Combine("input", "new-output"), tools);
        GerberTiffConversionPlan outsideResult = builder.Build(test.Context, "input", Path.Combine(test.RootPath, "outside-output"), tools);
        string longOutput = string.Join(Path.DirectorySeparatorChar, Enumerable.Repeat(new string('a', 60), 9));
        GerberTiffConversionPlan longResult = builder.Build(test.Context, "input", longOutput, tools);

        Assert.Contains(existingResult.Diagnostics, diagnostic => diagnostic.Code == GerberTiffDiagnosticCode.OutputAlreadyExists);
        Assert.Contains(insideResult.Diagnostics, diagnostic => diagnostic.Code == GerberTiffDiagnosticCode.OutputInsideInput);
        Assert.Contains(outsideResult.Diagnostics, diagnostic => diagnostic.Code == GerberTiffDiagnosticCode.OutputOutsideWorkspace);
        Assert.Contains(longResult.Diagnostics, diagnostic => diagnostic.Code == GerberTiffDiagnosticCode.OutputPathLengthLimitExceeded);
        Assert.All([existingResult, insideResult, outsideResult, longResult], plan =>
        {
            Assert.False(plan.Runnable);
            Assert.Null(plan.Fingerprint);
        });
    }

    private static string Diagnostics(IEnumerable<ProjectPackDiagnostic> diagnostics) =>
        string.Join(" | ", diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Summary}"));

    private sealed class TestWorkspace : IDisposable
    {
        private TestWorkspace(string rootPath, string workspacePath, string inputPath)
        {
            RootPath = rootPath;
            WorkspacePath = workspacePath;
            InputPath = inputPath;
            Context = WorkspaceContext.Detect(workspacePath, rootPath);
        }

        public string RootPath { get; }

        public string WorkspacePath { get; }

        public string InputPath { get; }

        public WorkspaceContext Context { get; }

        public static TestWorkspace Create()
        {
            string root = Path.Combine(Path.GetTempPath(), "caicli-gerber-plan-tests-" + Guid.NewGuid().ToString("N"));
            string workspace = Path.Combine(root, "workspace");
            string input = Path.Combine(workspace, "input");
            Directory.CreateDirectory(input);
            return new TestWorkspace(root, workspace, input);
        }

        public string WriteInput(string relativePath, string content)
        {
            string path = Path.Combine(InputPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        public void SetInputLength(string relativePath, long length)
        {
            string path = Path.Combine(InputPath, relativePath);
            using FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            stream.SetLength(length);
        }

        public Dictionary<string, string> CreateStaticTools()
        {
            string tools = Path.Combine(RootPath, "tools");
            Directory.CreateDirectory(tools);
            string gerbv = Path.Combine(tools, "gerbv.exe");
            string magick = Path.Combine(tools, "magick.exe");
            File.WriteAllText(gerbv, "static-gerbv");
            File.WriteAllText(magick, "static-magick");
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["gerbv"] = gerbv,
                ["imagemagick"] = magick
            };
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
