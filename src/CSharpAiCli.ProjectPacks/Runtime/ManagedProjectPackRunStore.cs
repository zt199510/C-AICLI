using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CSharpAiCli.Core;

namespace CSharpAiCli.ProjectPacks.Runtime;

public sealed record ManagedProjectPackRunLayout(
    string RunsRoot,
    string RunRoot,
    string RunRecordPath,
    string CheckpointPath,
    string ArtifactManifestPath,
    string InputManifestPath,
    string PlanPath,
    string StagingPath,
    string WorkingPath,
    string LogsPath,
    string ArtifactsPath,
    string ReportsPath,
    string LockPath);

public sealed class ManagedProjectPackRunStore
{
    private const int MaxRunRecordBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 64
    };

    private readonly string runsRoot;
    private readonly int defaultArtifactMinimumAgeDays;

    public ManagedProjectPackRunStore(
        string runsRoot,
        int defaultArtifactMinimumAgeDays = ArtifactRetentionConfiguration.DefaultDays)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runsRoot);
        if (defaultArtifactMinimumAgeDays < ArtifactRetentionConfiguration.MinimumDays ||
            defaultArtifactMinimumAgeDays > ArtifactRetentionConfiguration.MaximumDays)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultArtifactMinimumAgeDays));
        }

        this.runsRoot = Path.GetFullPath(runsRoot);
        this.defaultArtifactMinimumAgeDays = defaultArtifactMinimumAgeDays;
    }

    public string RunsRoot => runsRoot;

    public static ManagedProjectPackRunStore Create(CliEnvironmentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        string? stateRoot = Path.GetDirectoryName(snapshot.UserConfigPath);
        stateRoot = string.IsNullOrWhiteSpace(stateRoot)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".caicli")
            : stateRoot;
        return new ManagedProjectPackRunStore(
            Path.Combine(stateRoot, "runs"),
            snapshot.Configuration.ArtifactRetention.DefaultMinimumAgeDays);
    }

    public ManagedProjectPackRunLayout GetLayout(string runId)
    {
        if (!ProjectPackRunId.IsValid(runId))
        {
            throw new ProjectPackContractException(ProjectPackRunErrorCode.IdInvalid, "Project pack run id is invalid.");
        }

        string root = Path.GetFullPath(runsRoot);
        string runRoot = Path.GetFullPath(Path.Combine(root, runId));
        EnsureContained(root, runRoot);
        return new ManagedProjectPackRunLayout(
            root,
            runRoot,
            Path.Combine(runRoot, "run.json"),
            Path.Combine(runRoot, "checkpoint.json"),
            Path.Combine(runRoot, "artifact-manifest.json"),
            Path.Combine(runRoot, "input-manifest.json"),
            Path.Combine(runRoot, "plan.json"),
            Path.Combine(runRoot, "staging"),
            Path.Combine(runRoot, "working"),
            Path.Combine(runRoot, "logs"),
            Path.Combine(runRoot, "artifacts"),
            Path.Combine(runRoot, "reports"),
            Path.Combine(runRoot, ".run.lock"));
    }

    public ProjectPackRunMutationResult CreateRun(
        ProjectPackRunRecord record,
        ProjectPackRunCheckpoint checkpoint,
        string planJson,
        string inputManifestJson)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(planJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputManifestJson);
        ManagedProjectPackRunLayout layout;
        try
        {
            layout = GetLayout(record.RunId);
            EnsureNoReparseInExistingChain(layout.RunsRoot);
            Directory.CreateDirectory(layout.RunsRoot);
            EnsureNoReparseInExistingChain(layout.RunsRoot);
            if (Directory.Exists(layout.RunRoot) || File.Exists(layout.RunRoot))
            {
                return ProjectPackRunMutationResult.Failure(
                    ProjectPackRunErrorCode.AlreadyExists,
                    "Project pack run directory already exists.",
                    layout.RunRoot,
                    record.RunId);
            }

            Directory.CreateDirectory(layout.RunRoot);
            EnsureNoReparseInExistingChain(layout.RunRoot);
            foreach (string directory in Subdirectories(layout))
            {
                Directory.CreateDirectory(directory);
                EnsureNoReparseInExistingChain(directory);
            }
        }
        catch (ProjectPackContractException exception)
        {
            return ProjectPackRunMutationResult.Failure(exception.ErrorCode, exception.Message, runId: record.RunId);
        }
        catch (Exception exception) when (IsStoreException(exception))
        {
            return ProjectPackRunMutationResult.Failure(
                Directory.Exists(Path.Combine(runsRoot, record.RunId))
                    ? ProjectPackRunErrorCode.AlreadyExists
                    : ProjectPackRunErrorCode.RecordWriteFailed,
                "Managed project pack run directory could not be created.",
                runId: record.RunId);
        }

        try
        {
            using FileStream runLock = AcquireLock(layout, createRun: true);
            if (File.Exists(layout.RunRecordPath) || File.Exists(layout.CheckpointPath))
            {
                return ProjectPackRunMutationResult.Failure(
                    ProjectPackRunErrorCode.AlreadyExists,
                    "Project pack run already exists.",
                    layout.RunRoot,
                    record.RunId);
            }

            WriteTextAtomically(layout.PlanPath, planJson, overwrite: false);
            WriteTextAtomically(layout.InputManifestPath, inputManifestJson, overwrite: false);
            WriteJsonAtomically(layout.CheckpointPath, checkpoint, overwrite: false);
            WriteJsonAtomically(layout.RunRecordPath, record, overwrite: false);
            ManagedArtifactStore.WriteManifest(layout, record, defaultArtifactMinimumAgeDays);
            return ProjectPackRunMutationResult.Success(record, checkpoint);
        }
        catch (ProjectPackContractException exception)
        {
            return ProjectPackRunMutationResult.Failure(exception.ErrorCode, exception.Message, layout.RunRoot, record.RunId);
        }
        catch (IOException)
        {
            return ProjectPackRunMutationResult.Failure(
                ProjectPackRunErrorCode.ConcurrentConflict,
                "Project pack run is already being modified.",
                layout.RunRoot,
                record.RunId);
        }
        catch (Exception exception) when (IsStoreException(exception))
        {
            return ProjectPackRunMutationResult.Failure(
                ProjectPackRunErrorCode.RecordWriteFailed,
                "Project pack run record could not be written.",
                layout.RunRoot,
                record.RunId);
        }
    }

    public ProjectPackRunReadResult Read(string runId)
    {
        ManagedProjectPackRunLayout layout;
        try
        {
            layout = GetLayout(runId);
            if (!Directory.Exists(layout.RunRoot))
            {
                return ProjectPackRunReadResult.Failure(ProjectPackRunErrorCode.NotFound, "Project pack run was not found.", runId: runId);
            }

            EnsureNoReparseInExistingChain(layout.RunRoot);
            if (!File.Exists(layout.RunRecordPath) || !File.Exists(layout.CheckpointPath))
            {
                return ProjectPackRunReadResult.Failure(
                    ProjectPackRunErrorCode.RecordCorrupt,
                    "Project pack run record or checkpoint is missing.",
                    layout.RunRoot,
                    runId);
            }

            string runJson = ReadTextBounded(layout.RunRecordPath, MaxRunRecordBytes);
            string checkpointJson = ReadTextBounded(layout.CheckpointPath, MaxRunRecordBytes);
            int runSchema = ReadSchemaVersion(runJson);
            int checkpointSchema = ReadSchemaVersion(checkpointJson);
            if (runSchema != ProjectPackRunRecord.CurrentSchemaVersion ||
                checkpointSchema != ProjectPackRunCheckpoint.CurrentSchemaVersion)
            {
                return ProjectPackRunReadResult.Failure(
                    ProjectPackRunErrorCode.SchemaUnsupported,
                    "Project pack run record uses an unsupported schema.",
                    layout.RunRoot,
                    runId);
            }

            ProjectPackRunRecord? record = JsonSerializer.Deserialize<ProjectPackRunRecord>(runJson, JsonOptions);
            ProjectPackRunCheckpoint? checkpoint = JsonSerializer.Deserialize<ProjectPackRunCheckpoint>(checkpointJson, JsonOptions);
            if (record is null || checkpoint is null || record.RunId != runId || checkpoint.RunId != runId ||
                record.Revision != checkpoint.Revision || record.State != checkpoint.State ||
                record.PlanFingerprint != checkpoint.PlanFingerprint ||
                record.PolicyFingerprint != checkpoint.PolicyFingerprint)
            {
                return ProjectPackRunReadResult.Failure(
                    ProjectPackRunErrorCode.RecordCorrupt,
                    "Project pack run record and checkpoint are inconsistent.",
                    layout.RunRoot,
                    runId);
            }

            return ProjectPackRunReadResult.Success(record, checkpoint);
        }
        catch (ProjectPackContractException exception)
        {
            return ProjectPackRunReadResult.Failure(exception.ErrorCode, exception.Message, runId: runId);
        }
        catch (JsonException)
        {
            return ProjectPackRunReadResult.Failure(
                ProjectPackRunErrorCode.RecordCorrupt,
                "Project pack run record is corrupt.",
                runId: runId);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return ProjectPackRunReadResult.Failure(
                ProjectPackRunErrorCode.RecordCorrupt,
                "Project pack run record is invalid.",
                runId: runId);
        }
        catch (Exception exception) when (IsStoreException(exception))
        {
            return ProjectPackRunReadResult.Failure(
                ProjectPackRunErrorCode.RecordUnreadable,
                "Project pack run record could not be read.",
                runId: runId);
        }
    }

    public ProjectPackRunMutationResult Update(
        ProjectPackRunRecord record,
        ProjectPackRunCheckpoint checkpoint,
        long expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(checkpoint);
        ManagedProjectPackRunLayout layout;
        try
        {
            layout = GetLayout(record.RunId);
            EnsureNoReparseInExistingChain(layout.RunRoot);
            using FileStream runLock = AcquireLock(layout, createRun: false);
            ProjectPackRunReadResult current = Read(record.RunId);
            if (!current.Succeeded || current.Record is null || current.Checkpoint is null)
            {
                return ProjectPackRunMutationResult.Failure(
                    current.Diagnostic?.ErrorCode ?? ProjectPackRunErrorCode.RecordCorrupt,
                    current.Diagnostic?.Summary ?? "Project pack run record could not be loaded.",
                    layout.RunRoot,
                    record.RunId);
            }

            if (current.Record.Revision != expectedRevision || record.Revision != expectedRevision + 1 ||
                checkpoint.Revision != record.Revision || checkpoint.State != record.State)
            {
                return ProjectPackRunMutationResult.Failure(
                    ProjectPackRunErrorCode.RevisionConflict,
                    "Project pack run revision changed before update.",
                    layout.RunRoot,
                    record.RunId);
            }

            WriteJsonAtomically(layout.CheckpointPath, checkpoint, overwrite: true);
            WriteJsonAtomically(layout.RunRecordPath, record, overwrite: true);
            ManagedArtifactStore.WriteManifest(layout, record, defaultArtifactMinimumAgeDays);
            return ProjectPackRunMutationResult.Success(record, checkpoint);
        }
        catch (ProjectPackContractException exception)
        {
            return ProjectPackRunMutationResult.Failure(exception.ErrorCode, exception.Message, runId: record.RunId);
        }
        catch (IOException)
        {
            return ProjectPackRunMutationResult.Failure(
                ProjectPackRunErrorCode.ConcurrentConflict,
                "Project pack run is already being modified.",
                runId: record.RunId);
        }
        catch (Exception exception) when (IsStoreException(exception))
        {
            return ProjectPackRunMutationResult.Failure(
                ProjectPackRunErrorCode.RecordWriteFailed,
                "Project pack run record could not be updated.",
                runId: record.RunId);
        }
    }

    public static void EnsureNoReparseInExistingChain(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string? root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ProjectPackContractException(ProjectPackRunErrorCode.PathUnsafe, "Managed run path could not be rooted.");
        }

        string current = root;
        string relative = Path.GetRelativePath(root, fullPath);
        foreach (string segment in relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!Directory.Exists(current) && !File.Exists(current))
            {
                continue;
            }

            if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
            {
                throw new ProjectPackContractException(
                    ProjectPackRunErrorCode.ReparsePoint,
                    "Managed run paths containing reparse points are not supported.");
            }
        }
    }

    private static IEnumerable<string> Subdirectories(ManagedProjectPackRunLayout layout) =>
        [layout.StagingPath, layout.WorkingPath, layout.LogsPath, layout.ArtifactsPath, layout.ReportsPath];

    private static FileStream AcquireLock(ManagedProjectPackRunLayout layout, bool createRun)
    {
        EnsureNoReparseInExistingChain(layout.RunRoot);
        FileMode mode = createRun ? FileMode.OpenOrCreate : FileMode.OpenOrCreate;
        return new FileStream(layout.LockPath, mode, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.WriteThrough);
    }

    private static int ReadSchemaVersion(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
        return document.RootElement.TryGetProperty("schemaVersion", out JsonElement element) &&
            element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out int version)
                ? version
                : throw new JsonException("schemaVersion is missing.");
    }

    private static void WriteJsonAtomically<T>(string path, T value, bool overwrite) =>
        WriteTextAtomically(path, JsonSerializer.Serialize(value, JsonOptions), overwrite);

    internal static void WriteTextAtomically(string path, string value, bool overwrite)
    {
        string directory = Path.GetDirectoryName(path) ?? throw new IOException("Destination directory is unavailable.");
        string temporaryPath = Path.Combine(directory, Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            using (StreamWriter writer = new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(value);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, path, overwrite);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception exception) when (IsStoreException(exception))
            {
            }
        }
    }

    public static string ReadTextBounded(string path, int maxBytes)
    {
        if (maxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        }

        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.SequentialScan);
        if (stream.Length <= 0 || stream.Length > maxBytes)
        {
            throw new IOException("Managed run JSON exceeds its read bound.");
        }

        using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string value = reader.ReadToEnd();
        if (stream.Length > maxBytes)
        {
            throw new IOException("Managed run JSON changed beyond its read bound.");
        }

        return value;
    }

    private static void EnsureContained(string root, string candidate)
    {
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string normalizedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        string rooted = normalizedRoot + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!normalizedCandidate.StartsWith(rooted, comparison))
        {
            throw new ProjectPackContractException(ProjectPackRunErrorCode.PathUnsafe, "Managed run path escaped the run root.");
        }
    }

    private static bool IsStoreException(Exception exception) => exception is IOException
        or UnauthorizedAccessException
        or DirectoryNotFoundException
        or FileNotFoundException
        or NotSupportedException
        or PathTooLongException;
}
