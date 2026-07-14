using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Security.Cryptography;
using CSharpAiCli.Core;

namespace CSharpAiCli.ProjectPacks.GerberTiff;

public sealed record GerberTiffInventoryLimits(
    int MaxRecursionDepth,
    int MaxFileCount,
    int MaxDirectoryCount,
    long MaxSingleFileBytes,
    long MaxTotalBytes,
    int MaxRelativePathCharacters,
    int ScanTimeoutMilliseconds);

public sealed record GerberTiffInputFile(
    string Id,
    string RelativePath,
    string Extension,
    string Kind,
    string? LayerRole,
    bool Supported,
    bool PassedToExternalTool,
    long Size,
    string? Sha256);

public sealed record GerberTiffInputInventory
{
    public GerberTiffInputInventory(
        string? inputDirectory,
        IReadOnlyList<GerberTiffInputFile>? files,
        long totalSupportedBytes,
        IReadOnlyList<ProjectPackDiagnostic>? diagnostics)
    {
        SchemaVersion = ProjectPackSchema.CurrentVersion;
        InputDirectory = inputDirectory;
        Files = new ReadOnlyCollection<GerberTiffInputFile>((files ?? []).ToArray());
        TotalSupportedBytes = totalSupportedBytes;
        Diagnostics = new ReadOnlyCollection<ProjectPackDiagnostic>((diagnostics ?? []).ToArray());
        Limits = new GerberTiffInventoryLimits(
            GerberTiffInputEnvelope.MaxRecursionDepth,
            GerberTiffInputEnvelope.MaxFileCount,
            GerberTiffInputEnvelope.MaxDirectoryCount,
            GerberTiffInputEnvelope.MaxSingleFileBytes,
            GerberTiffInputEnvelope.MaxTotalBytes,
            GerberTiffInputEnvelope.MaxRelativePathCharacters,
            GerberTiffInputEnvelope.ScanTimeoutMilliseconds);
    }

    public int SchemaVersion { get; }

    public string? InputDirectory { get; }

    public GerberTiffInventoryLimits Limits { get; }

    public IReadOnlyList<GerberTiffInputFile> Files { get; }

    public long TotalSupportedBytes { get; }

    public IReadOnlyList<ProjectPackDiagnostic> Diagnostics { get; }

    public int SupportedFileCount => Files.Count(file => file.Supported);

    public int ToolInputCount => Files.Count(file => file.PassedToExternalTool);

    public int UnknownFileCount => Files.Count(file => !file.Supported);

    public bool Succeeded =>
        InputDirectory is not null &&
        Diagnostics.All(diagnostic => diagnostic.Severity != ProjectPackDiagnosticSeverity.Error);
}

public sealed class GerberTiffInputDiscovery
{
    private const int HashBufferBytes = 128 * 1024;
    private readonly GerberTiffBoundedDirectoryScanner scanner = new();
    private readonly GerberTiffLayerClassifier classifier = new();
    private readonly Func<long>? elapsedMillisecondsProvider;

    public GerberTiffInputDiscovery(Func<long>? elapsedMillisecondsProvider = null)
    {
        this.elapsedMillisecondsProvider = elapsedMillisecondsProvider;
    }

    public GerberTiffInputInventory Discover(
        WorkspaceContext workspace,
        string? inputDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        Stopwatch clock = Stopwatch.StartNew();
        Func<long> elapsedMilliseconds = elapsedMillisecondsProvider ?? (() => clock.ElapsedMilliseconds);
        GerberTiffDirectoryScanResult scan = scanner.Scan(
            workspace,
            inputDirectory,
            elapsedMilliseconds,
            cancellationToken);
        List<ProjectPackDiagnostic> diagnostics = [.. scan.Diagnostics];
        if (scan.Input is null)
        {
            return Inventory(null, [], 0, diagnostics);
        }

        List<Candidate> candidates = [];
        foreach (GerberTiffScannedFile scannedFile in scan.Files
            .OrderBy(file => file.WorkspaceRelativePath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsTimedOut(elapsedMilliseconds, diagnostics))
            {
                break;
            }

            GerberTiffLayerClassification classification = classifier.Classify(scannedFile.WorkspaceRelativePath);
            diagnostics.AddRange(classification.Diagnostics);
            try
            {
                FileInfo info = new(scannedFile.FullPath);
                info.Refresh();
                if (!info.Exists)
                {
                    diagnostics.Add(Error(
                        GerberTiffDiagnosticCode.InputEntryMetadataFailed,
                        "An input file disappeared during discovery."));
                    continue;
                }

                candidates.Add(new Candidate(
                    scannedFile,
                    classification,
                    info.Length,
                    info.LastWriteTimeUtc));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(Error(
                    GerberTiffDiagnosticCode.InputEntryMetadataFailed,
                    "Input file metadata could not be read safely."));
            }
        }

        diagnostics.AddRange(classifier.ValidateMappings(candidates.Select(candidate => candidate.Classification).ToArray()));
        long totalSupportedBytes = 0;
        foreach (Candidate candidate in candidates.Where(candidate => candidate.Classification.Supported))
        {
            if (candidate.Size == 0)
            {
                diagnostics.Add(Error(
                    GerberTiffDiagnosticCode.InputFileEmpty,
                    "A supported input file is empty."));
            }

            if (candidate.Size > GerberTiffInputEnvelope.MaxSingleFileBytes)
            {
                diagnostics.Add(Error(
                    GerberTiffDiagnosticCode.InputFileSizeLimitExceeded,
                    "A supported input file exceeds the v1 single-file byte limit."));
            }

            try
            {
                totalSupportedBytes = checked(totalSupportedBytes + candidate.Size);
            }
            catch (OverflowException)
            {
                totalSupportedBytes = long.MaxValue;
            }
        }

        if (totalSupportedBytes > GerberTiffInputEnvelope.MaxTotalBytes)
        {
            diagnostics.Add(Error(
                GerberTiffDiagnosticCode.InputTotalBytesLimitExceeded,
                "Supported inputs exceed the v1 total byte limit."));
        }

        List<GerberTiffInputFile> files = [];
        int supportedSequence = 0;
        int unknownSequence = 0;
        foreach (Candidate candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string id = candidate.Classification.Supported
                ? $"input-{++supportedSequence:D4}"
                : $"unknown-{++unknownSequence:D4}";
            string? sha256 = null;
            bool shouldHash = candidate.Classification.Supported &&
                candidate.Size > 0 &&
                candidate.Size <= GerberTiffInputEnvelope.MaxSingleFileBytes &&
                totalSupportedBytes <= GerberTiffInputEnvelope.MaxTotalBytes &&
                (candidate.Classification.PassedToExternalTool ||
                    candidate.Classification.Kind == GerberTiffInputKind.Sidecar);
            if (shouldHash && !IsTimedOut(elapsedMilliseconds, diagnostics))
            {
                sha256 = TryHash(candidate, elapsedMilliseconds, diagnostics, cancellationToken);
            }

            files.Add(new GerberTiffInputFile(
                id,
                candidate.ScannedFile.WorkspaceRelativePath,
                candidate.Classification.Extension,
                candidate.Classification.Kind,
                candidate.Classification.LayerRole,
                candidate.Classification.Supported,
                candidate.Classification.PassedToExternalTool,
                candidate.Size,
                sha256));
        }

        ProjectPackDiagnostic[] stableDiagnostics = diagnostics
            .OrderBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Severity, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Summary, StringComparer.Ordinal)
            .ToArray();
        return Inventory(scan.Input.WorkspaceRelativePath, files, totalSupportedBytes, stableDiagnostics);
    }

    private static string? TryHash(
        Candidate candidate,
        Func<long> elapsedMilliseconds,
        ICollection<ProjectPackDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        try
        {
            using FileStream stream = new(
                candidate.ScannedFile.FullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                HashBufferBytes,
                FileOptions.SequentialScan);
            if (stream.Length != candidate.Size)
            {
                diagnostics.Add(Error(
                    GerberTiffDiagnosticCode.InputChanged,
                    "A supported input file changed during discovery."));
                return null;
            }

            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[HashBufferBytes];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsTimedOut(elapsedMilliseconds, diagnostics))
                {
                    return null;
                }

                hash.AppendData(buffer, 0, read);
            }

            FileInfo after = new(candidate.ScannedFile.FullPath);
            after.Refresh();
            if (!after.Exists ||
                after.Length != candidate.Size ||
                after.LastWriteTimeUtc != candidate.LastWriteTimeUtc)
            {
                diagnostics.Add(Error(
                    GerberTiffDiagnosticCode.InputChanged,
                    "A supported input file changed during discovery."));
                return null;
            }

            return Convert.ToHexString(hash.GetHashAndReset());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(Error(
                GerberTiffDiagnosticCode.InputFileUnreadable,
                "A supported input file could not be read for hashing."));
            return null;
        }
    }

    private static bool IsTimedOut(
        Func<long> elapsedMilliseconds,
        ICollection<ProjectPackDiagnostic> diagnostics)
    {
        if (elapsedMilliseconds() <= GerberTiffInputEnvelope.ScanTimeoutMilliseconds)
        {
            return false;
        }

        if (!diagnostics.Any(diagnostic => diagnostic.Code == GerberTiffDiagnosticCode.InputScanTimeLimitExceeded))
        {
            diagnostics.Add(Error(
                GerberTiffDiagnosticCode.InputScanTimeLimitExceeded,
                "Input discovery exceeded the v1 scan and hash time limit."));
        }

        return true;
    }

    private static GerberTiffInputInventory Inventory(
        string? inputDirectory,
        IEnumerable<GerberTiffInputFile> files,
        long totalSupportedBytes,
        IEnumerable<ProjectPackDiagnostic> diagnostics) =>
        new(
            inputDirectory,
            new ReadOnlyCollection<GerberTiffInputFile>(files.ToArray()),
            totalSupportedBytes,
            new ReadOnlyCollection<ProjectPackDiagnostic>(diagnostics.ToArray()));

    private static ProjectPackDiagnostic Error(string code, string summary) =>
        new(code, ProjectPackDiagnosticSeverity.Error, summary);

    private sealed record Candidate(
        GerberTiffScannedFile ScannedFile,
        GerberTiffLayerClassification Classification,
        long Size,
        DateTime LastWriteTimeUtc);
}
