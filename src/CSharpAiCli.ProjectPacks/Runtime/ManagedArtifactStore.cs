using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using CSharpAiCli.Core;

namespace CSharpAiCli.ProjectPacks.Runtime;

public sealed class ManagedArtifactStore
{
    private const int MaxManifestBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 64
    };

    private readonly ManagedProjectPackRunStore runStore;
    private readonly Action<string>? beforePruneMove;

    public ManagedArtifactStore(ManagedProjectPackRunStore runStore)
        : this(runStore, null)
    {
    }

    internal ManagedArtifactStore(
        ManagedProjectPackRunStore runStore,
        Action<string>? beforePruneMove)
    {
        this.runStore = runStore ?? throw new ArgumentNullException(nameof(runStore));
        this.beforePruneMove = beforePruneMove;
    }

    public static ManagedArtifactStore Create(CliEnvironmentSnapshot snapshot) =>
        new(ManagedProjectPackRunStore.Create(snapshot));

    public ManagedArtifactReadResult ReadManifest(string runId)
    {
        ProjectPackRunReadResult run = runStore.Read(runId);
        if (!run.Succeeded || run.Record is null)
        {
            return Failure(
                run.Diagnostic?.ErrorCode ?? ManagedArtifactErrorCode.ManifestCorrupt,
                run.Diagnostic?.Summary ?? "Managed artifact owner run could not be read.",
                runId: runId,
                path: run.Diagnostic?.Path);
        }

        ManagedProjectPackRunLayout layout;
        try
        {
            layout = runStore.GetLayout(runId);
            ManagedProjectPackRunStore.EnsureNoReparseInExistingChain(layout.RunRoot);
            if (!File.Exists(layout.ArtifactManifestPath))
            {
                return Failure(
                    ManagedArtifactErrorCode.ManifestMissing,
                    "Managed artifact manifest is missing.",
                    runId: runId,
                    path: layout.ArtifactManifestPath);
            }

            string json = ManagedProjectPackRunStore.ReadTextBounded(layout.ArtifactManifestPath, MaxManifestBytes);
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
            if (!document.RootElement.TryGetProperty("schemaVersion", out JsonElement schema) ||
                !schema.TryGetInt32(out int schemaVersion) ||
                schemaVersion != ManagedArtifactManifest.CurrentSchemaVersion)
            {
                return Failure(
                    ManagedArtifactErrorCode.SchemaUnsupported,
                    "Managed artifact manifest uses an unsupported schema.",
                    runId: runId,
                    path: layout.ArtifactManifestPath);
            }

            ManagedArtifactManifest? manifest = JsonSerializer.Deserialize<ManagedArtifactManifest>(json, JsonOptions);
            if (manifest is null || manifest.RunId != runId || manifest.RunRevision != run.Record.Revision ||
                manifest.RunState != run.Record.State || manifest.Owner.JobId != run.Record.Correlation.JobId ||
                manifest.Owner.QueueId != run.Record.Correlation.QueueId ||
                manifest.Acceptance != run.Record.Acceptance ||
                !ManifestMatchesRecord(manifest, run.Record))
            {
                return Failure(
                    ManagedArtifactErrorCode.RecordMismatch,
                    "Managed artifact manifest does not match its owner run revision.",
                    runId: runId,
                    path: layout.ArtifactManifestPath);
            }

            return new ManagedArtifactReadResult(true, manifest, null, null);
        }
        catch (JsonException)
        {
            return Failure(
                ManagedArtifactErrorCode.ManifestCorrupt,
                "Managed artifact manifest is corrupt.",
                runId: runId);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException
            or InvalidOperationException
            or ProjectPackContractException)
        {
            return Failure(
                exception is ProjectPackContractException contract && contract.ErrorCode == ProjectPackRunErrorCode.ReparsePoint
                    ? ManagedArtifactErrorCode.ReparsePoint
                    : ManagedArtifactErrorCode.ManifestCorrupt,
                "Managed artifact manifest could not be read safely.",
                runId: runId);
        }
    }

    public ManagedArtifactReadResult Read(string artifactId)
    {
        if (!ManagedArtifactId.IsValid(artifactId))
        {
            return Failure(ManagedArtifactErrorCode.NotFound, "Managed artifact was not found.", artifactId);
        }

        ManagedArtifactListResult list = List();
        (ManagedArtifactManifest Manifest, ManagedArtifactEntry Artifact)? match = list.Artifacts
            .Select(item => ((ManagedArtifactManifest Manifest, ManagedArtifactEntry Artifact)?)item)
            .SingleOrDefault(item => item?.Artifact.ArtifactId == artifactId);
        if (match is null)
        {
            return new ManagedArtifactReadResult(
                false,
                null,
                null,
                new ManagedArtifactDiagnostic(
                    ManagedArtifactErrorCode.NotFound,
                    "Managed artifact was not found.",
                    artifactId));
        }

        return new ManagedArtifactReadResult(true, match.Value.Manifest, match.Value.Artifact, null);
    }

    public ManagedArtifactListResult List(string? runId = null, string? runState = null)
    {
        List<(ManagedArtifactManifest Manifest, ManagedArtifactEntry Artifact)> artifacts = [];
        List<ManagedArtifactDiagnostic> diagnostics = [];
        IEnumerable<string> runIds;
        if (!string.IsNullOrWhiteSpace(runId))
        {
            runIds = [runId];
        }
        else
        {
            runIds = EnumerateRunIds(diagnostics);
        }

        foreach (string candidateRunId in runIds)
        {
            ManagedArtifactReadResult read = ReadManifest(candidateRunId);
            if (!read.Succeeded || read.Manifest is null)
            {
                if (read.Diagnostic is not null)
                {
                    diagnostics.Add(read.Diagnostic);
                }

                continue;
            }

            if (!string.IsNullOrWhiteSpace(runState) && read.Manifest.RunState != runState)
            {
                continue;
            }

            artifacts.AddRange(read.Manifest.Artifacts.Select(artifact => (read.Manifest, artifact)));
        }

        return new ManagedArtifactListResult(
            artifacts
                .OrderByDescending(item => item.Manifest.UpdatedAtUtc)
                .ThenBy(item => item.Artifact.ArtifactId, StringComparer.Ordinal)
                .ToArray(),
            diagnostics
                .OrderBy(diagnostic => diagnostic.RunId, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.ErrorCode, StringComparer.Ordinal)
                .ToArray());
    }

    public ManagedArtifactVerificationResult Verify(string artifactId, WorkspaceContext workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ManagedArtifactReadResult read = Read(artifactId);
        if (!read.Succeeded || read.Manifest is null || read.Artifact is null)
        {
            ManagedArtifactDiagnostic diagnostic = read.Diagnostic ?? new ManagedArtifactDiagnostic(
                ManagedArtifactErrorCode.NotFound,
                "Managed artifact was not found.",
                artifactId);
            return new ManagedArtifactVerificationResult(
                artifactId,
                false,
                ManagedArtifactAvailability.Missing,
                null,
                null,
                diagnostic);
        }

        ManagedArtifactEntry artifact = read.Artifact;
        if (artifact.Tombstone is not null)
        {
            return new ManagedArtifactVerificationResult(
                artifactId,
                true,
                ManagedArtifactAvailability.Pruned,
                null,
                null,
                null);
        }

        if (artifact.Ownership == ManagedArtifactOwnership.External)
        {
            return VerificationFailure(
                artifact,
                ManagedArtifactAvailability.External,
                ManagedArtifactErrorCode.External,
                "External artifact pointers are not verified or managed by C-AICLI.");
        }

        string? path = ResolveArtifactPath(read.Manifest, artifact, workspace, out ManagedArtifactDiagnostic? pathDiagnostic);
        if (path is null)
        {
            return new ManagedArtifactVerificationResult(
                artifactId,
                false,
                ManagedArtifactAvailability.Missing,
                null,
                null,
                pathDiagnostic);
        }

        try
        {
            ManagedProjectPackRunStore.EnsureNoReparseInExistingChain(path);
            using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                128 * 1024,
                FileOptions.SequentialScan);
            long length = stream.Length;
            string sha256 = Convert.ToHexString(SHA256.HashData(stream));
            if (artifact.Size is null || artifact.Sha256 is null || length != artifact.Size ||
                !sha256.Equals(artifact.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return new ManagedArtifactVerificationResult(
                    artifactId,
                    false,
                    ManagedArtifactAvailability.Changed,
                    length,
                    sha256,
                    new ManagedArtifactDiagnostic(
                        ManagedArtifactErrorCode.Changed,
                        "Artifact size or SHA256 does not match the retained manifest identity.",
                        artifactId,
                        read.Manifest.RunId,
                        artifact.Path));
            }

            return new ManagedArtifactVerificationResult(
                artifactId,
                true,
                ManagedArtifactAvailability.Available,
                length,
                sha256,
                null);
        }
        catch (FileNotFoundException)
        {
            return VerificationFailure(
                artifact,
                ManagedArtifactAvailability.Missing,
                ManagedArtifactErrorCode.Missing,
                "Artifact is missing.",
                read.Manifest.RunId);
        }
        catch (ProjectPackContractException)
        {
            return VerificationFailure(
                artifact,
                ManagedArtifactAvailability.Changed,
                ManagedArtifactErrorCode.ReparsePoint,
                "Artifact path contains a reparse point.",
                read.Manifest.RunId);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException)
        {
            return VerificationFailure(
                artifact,
                ManagedArtifactAvailability.Changed,
                ManagedArtifactErrorCode.Changed,
                "Artifact could not be read with a stable identity.",
                read.Manifest.RunId);
        }
    }

    public ManagedArtifactPruneResult Prune(
        ManagedArtifactPruneFilter filter,
        bool apply,
        WorkspaceContext workspace,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(workspace);
        List<ManagedArtifactPruneItem> items = [];
        List<ManagedArtifactDiagnostic> diagnostics = [];
        ManagedArtifactListResult list = List();
        diagnostics.AddRange(list.Diagnostics);
        foreach ((ManagedArtifactManifest manifest, ManagedArtifactEntry artifact) in list.Artifacts)
        {
            if (!IsPruneCandidate(manifest, artifact, filter))
            {
                continue;
            }

            ManagedArtifactVerificationResult verification = Verify(artifact.ArtifactId, workspace);
            if (!verification.Succeeded || verification.Availability != ManagedArtifactAvailability.Available)
            {
                if (verification.Diagnostic is not null)
                {
                    diagnostics.Add(verification.Diagnostic);
                }

                continue;
            }

            string reason = $"terminal {manifest.RunState}; declared before {filter.OlderThanUtc:O}; retention={artifact.Retention.Class}";
            if (!apply)
            {
                items.Add(new ManagedArtifactPruneItem(
                    artifact.ArtifactId,
                    manifest.RunId,
                    manifest.RunState,
                    artifact.Path,
                    artifact.Size!.Value,
                    artifact.DeclaredAtUtc,
                    reason,
                    Deleted: false));
                continue;
            }

            ManagedArtifactPruneItem applied = ApplyPrune(manifest, artifact, reason, nowUtc);
            items.Add(applied);
            if (applied.ErrorCode is not null)
            {
                diagnostics.Add(new ManagedArtifactDiagnostic(
                    applied.ErrorCode,
                    applied.Summary ?? "Managed artifact could not be pruned safely.",
                    applied.ArtifactId,
                    applied.RunId,
                    applied.Path));
            }
        }

        return new ManagedArtifactPruneResult(
            apply,
            items.OrderBy(item => item.ArtifactId, StringComparer.Ordinal).ToArray(),
            diagnostics
                .OrderBy(diagnostic => diagnostic.RunId, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.ArtifactId, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.ErrorCode, StringComparer.Ordinal)
                .ToArray());
    }

    internal static void WriteManifest(
        ManagedProjectPackRunLayout layout,
        ProjectPackRunRecord record,
        int defaultMinimumAgeDays)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(record);
        Dictionary<string, ManagedArtifactTombstone> tombstones = ReadExistingTombstones(layout, record.RunId);
        Dictionary<string, DateTimeOffset> declaredTimes = ReadExistingDeclaredTimes(layout, record.RunId);
        ManagedArtifactEntry[] artifacts = record.Artifacts.Select(pointer =>
        {
            string ownership = ManagedArtifactOwnership.FromScope(pointer.Scope);
            bool owned = ownership == ManagedArtifactOwnership.Managed;
            bool prunable = owned && (pointer.Path.StartsWith("artifacts/", StringComparison.Ordinal) ||
                pointer.Path.StartsWith("reports/", StringComparison.Ordinal) ||
                pointer.Path.StartsWith("logs/", StringComparison.Ordinal));
            string retentionClass = !owned
                ? ManagedArtifactRetentionClass.ExternalOwner
                : prunable
                    ? ManagedArtifactRetentionClass.TerminalPrunable
                    : ManagedArtifactRetentionClass.RetainMetadata;
            string artifactId = ManagedArtifactId.Create(record.RunId, pointer.Id);
            tombstones.TryGetValue(artifactId, out ManagedArtifactTombstone? tombstone);
            bool hardVerificationState = record.State is ProjectPackRunState.AwaitingAcceptance
                or ProjectPackRunState.Accepted
                or ProjectPackRunState.Rejected;
            bool hardVerificationEvidence = record.Artifacts.Any(artifact =>
                artifact.Kind == "tiff-verification-json" && artifact.Scope == "managed-run");
            string verification = pointer.Kind is "tiff-output" or "tiff-verification-json" or "tiff-verification-markdown"
                ? hardVerificationState && hardVerificationEvidence
                    ? ManagedArtifactVerificationStatus.HardPassed
                    : record.State == ProjectPackRunState.Failed
                        ? ManagedArtifactVerificationStatus.Failed
                        : ManagedArtifactVerificationStatus.Declared
                : ownership is ManagedArtifactOwnership.WorkspaceOutput or ManagedArtifactOwnership.Source
                    ? ManagedArtifactVerificationStatus.NotApplicable
                    : ManagedArtifactVerificationStatus.Declared;
            return new ManagedArtifactEntry(
                artifactId,
                pointer.Id,
                pointer.Kind,
                ownership,
                pointer.Path,
                pointer.Size,
                pointer.Sha256,
                verification,
                pointer.Exists ? ManagedArtifactAvailability.Available : ManagedArtifactAvailability.Missing,
                new ManagedArtifactRetention(retentionClass, owned, prunable, defaultMinimumAgeDays),
                declaredTimes.TryGetValue(artifactId, out DateTimeOffset declaredAtUtc)
                    ? declaredAtUtc
                    : record.UpdatedAtUtc,
                tombstone);
        }).ToArray();
        ManagedArtifactManifest manifest = new(
            ManagedArtifactManifest.CurrentSchemaVersion,
            ManagedArtifactManifest.ManifestType,
            record.RunId,
            record.Revision,
            record.State,
            new ManagedArtifactOwner(
                record.RunId,
                record.Correlation.JobId,
                record.Correlation.QueueId,
                record.Correlation.RootRunId,
                record.Correlation.ParentRunId,
                record.Correlation.Attempt),
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            artifacts,
            record.Acceptance);
        ManagedProjectPackRunStore.WriteTextAtomically(
            layout.ArtifactManifestPath,
            JsonSerializer.Serialize(manifest, JsonOptions),
            overwrite: File.Exists(layout.ArtifactManifestPath));
    }

    internal static void WriteManifest(ManagedProjectPackRunLayout layout, ManagedArtifactManifest manifest) =>
        ManagedProjectPackRunStore.WriteTextAtomically(
            layout.ArtifactManifestPath,
            JsonSerializer.Serialize(manifest, JsonOptions),
            overwrite: true);

    private ManagedArtifactPruneItem ApplyPrune(
        ManagedArtifactManifest selectedManifest,
        ManagedArtifactEntry selectedArtifact,
        string reason,
        DateTimeOffset nowUtc)
    {
        string runId = selectedManifest.RunId;
        ManagedArtifactReadResult currentRead = ReadManifest(runId);
        ManagedArtifactEntry? currentArtifact = currentRead.Manifest?.Artifacts.SingleOrDefault(
            artifact => artifact.ArtifactId == selectedArtifact.ArtifactId);
        if (!currentRead.Succeeded || currentRead.Manifest is null || currentArtifact is null ||
            currentRead.Manifest.RunRevision != selectedManifest.RunRevision ||
            currentRead.Manifest.RunState != selectedManifest.RunState ||
            currentArtifact != selectedArtifact ||
            !ProjectPackRunState.IsTerminal(currentRead.Manifest.RunState) ||
            currentArtifact.Tombstone is not null)
        {
            return PruneFailure(
                selectedManifest,
                selectedArtifact,
                reason,
                ManagedArtifactErrorCode.PruneRace,
                "Run or artifact identity changed after prune selection.");
        }

        ManagedProjectPackRunLayout layout;
        string path;
        string quarantinePath;
        try
        {
            layout = runStore.GetLayout(runId);
            path = ResolveOwnedPrunePath(layout, currentArtifact);
            ManagedProjectPackRunStore.EnsureNoReparseInExistingChain(layout.RunRoot);
            ManagedProjectPackRunStore.EnsureNoReparseInExistingChain(path);
            if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            {
                throw new ProjectPackContractException(
                    ProjectPackRunErrorCode.ReparsePoint,
                    "Artifact is a reparse point.");
            }

            string quarantineRoot = Path.Combine(layout.WorkingPath, "prune-quarantine");
            Directory.CreateDirectory(quarantineRoot);
            ManagedProjectPackRunStore.EnsureNoReparseInExistingChain(quarantineRoot);
            quarantinePath = Path.Combine(
                quarantineRoot,
                currentArtifact.ArtifactId + "." + Guid.NewGuid().ToString("N") + ".prune");
            beforePruneMove?.Invoke(path);
            ManagedProjectPackRunStore.EnsureNoReparseInExistingChain(path);
            File.Move(path, quarantinePath, overwrite: false);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ProjectPackContractException)
        {
            return PruneFailure(
                selectedManifest,
                selectedArtifact,
                reason,
                exception is ProjectPackContractException
                    ? ManagedArtifactErrorCode.ReparsePoint
                    : ManagedArtifactErrorCode.PruneFailed,
                "Artifact could not be moved to the bounded prune quarantine; it was not deleted.");
        }

        try
        {
            ManagedProjectPackRunStore.EnsureNoReparseInExistingChain(quarantinePath);
            (long size, string sha256) = ReadIdentity(quarantinePath);
            if (size != currentArtifact.Size ||
                !sha256.Equals(currentArtifact.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                TryRestoreQuarantine(quarantinePath, path);
                return PruneFailure(
                    selectedManifest,
                    selectedArtifact,
                    reason,
                    ManagedArtifactErrorCode.PruneRace,
                    "Artifact identity changed during prune; changed content was not deleted.");
            }

            ManagedArtifactTombstone tombstone = new(
                nowUtc,
                reason,
                currentArtifact.Size,
                currentArtifact.Sha256,
                currentRead.Manifest.RunState,
                currentRead.Manifest.Owner.JobId,
                currentRead.Manifest.Owner.QueueId);
            ManagedArtifactEntry updatedArtifact = new(
                currentArtifact.ArtifactId,
                currentArtifact.PointerId,
                currentArtifact.Kind,
                currentArtifact.Ownership,
                currentArtifact.Path,
                currentArtifact.Size,
                currentArtifact.Sha256,
                currentArtifact.Verification,
                ManagedArtifactAvailability.Pruned,
                currentArtifact.Retention,
                currentArtifact.DeclaredAtUtc,
                tombstone);
            ManagedArtifactManifest updatedManifest = new(
                currentRead.Manifest.SchemaVersion,
                currentRead.Manifest.Type,
                currentRead.Manifest.RunId,
                currentRead.Manifest.RunRevision,
                currentRead.Manifest.RunState,
                currentRead.Manifest.Owner,
                currentRead.Manifest.CreatedAtUtc,
                nowUtc,
                currentRead.Manifest.Artifacts
                    .Select(artifact => artifact.ArtifactId == updatedArtifact.ArtifactId ? updatedArtifact : artifact)
                    .ToArray(),
                currentRead.Manifest.Acceptance);
            WriteManifest(layout, updatedManifest);
            try
            {
                (long finalSize, string finalSha256) = ReadIdentity(quarantinePath);
                if (finalSize != currentArtifact.Size ||
                    !finalSha256.Equals(currentArtifact.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    TryRestoreQuarantine(quarantinePath, path);
                    WriteManifest(layout, currentRead.Manifest);
                    return PruneFailure(
                        selectedManifest,
                        selectedArtifact,
                        reason,
                        ManagedArtifactErrorCode.PruneRace,
                        "Quarantined artifact identity changed after tombstone persistence; changed content was retained.");
                }

                File.Delete(quarantinePath);
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or NotSupportedException)
            {
                TryRestoreQuarantine(quarantinePath, path);
                WriteManifest(layout, currentRead.Manifest);
                return PruneFailure(
                    selectedManifest,
                    selectedArtifact,
                    reason,
                    ManagedArtifactErrorCode.PruneFailed,
                    "Quarantined artifact could not be deleted; its tombstone was rolled back.");
            }

            return new ManagedArtifactPruneItem(
                currentArtifact.ArtifactId,
                currentRead.Manifest.RunId,
                currentRead.Manifest.RunState,
                currentArtifact.Path,
                currentArtifact.Size!.Value,
                currentArtifact.DeclaredAtUtc,
                reason,
                Deleted: true);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException
            or InvalidOperationException)
        {
            TryRestoreQuarantine(quarantinePath, path);
            return PruneFailure(
                selectedManifest,
                selectedArtifact,
                reason,
                ManagedArtifactErrorCode.PruneFailed,
                "Artifact quarantine identity or tombstone could not be persisted; content was not deleted.");
        }
    }

    private static bool IsPruneCandidate(
        ManagedArtifactManifest manifest,
        ManagedArtifactEntry artifact,
        ManagedArtifactPruneFilter filter) =>
        ProjectPackRunState.IsTerminal(manifest.RunState) &&
        (filter.Statuses is null || filter.Statuses.Contains(manifest.RunState)) &&
        artifact.Ownership == ManagedArtifactOwnership.Managed &&
        artifact.Retention.Owned &&
        artifact.Retention.Prunable &&
        artifact.Tombstone is null &&
        artifact.Availability == ManagedArtifactAvailability.Available &&
        artifact.Size is not null &&
        artifact.DeclaredAtUtc <= filter.OlderThanUtc &&
        (filter.MinimumSize is null || artifact.Size >= filter.MinimumSize) &&
        (filter.MaximumSize is null || artifact.Size <= filter.MaximumSize);

    private static string ResolveOwnedPrunePath(
        ManagedProjectPackRunLayout layout,
        ManagedArtifactEntry artifact)
    {
        if (artifact.Ownership != ManagedArtifactOwnership.Managed ||
            !artifact.Retention.Owned ||
            !artifact.Retention.Prunable ||
            Path.IsPathRooted(artifact.Path) ||
            artifact.Path.Contains("..", StringComparison.Ordinal) ||
            artifact.Path is "run.json" or "checkpoint.json" or "artifact-manifest.json" or "plan.json" or "input-manifest.json")
        {
            throw new IOException("Artifact is not an owned prune target.");
        }

        string path = Path.GetFullPath(Path.Combine(
            layout.RunRoot,
            artifact.Path.Replace('/', Path.DirectorySeparatorChar)));
        string[] allowedRoots = [layout.ArtifactsPath, layout.ReportsPath, layout.LogsPath];
        if (!allowedRoots.Any(root => IsWithin(root, path)))
        {
            throw new IOException("Artifact escaped its prune allowlist root.");
        }

        return path;
    }

    private static (long Size, string Sha256) ReadIdentity(string path)
    {
        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.SequentialScan);
        long size = stream.Length;
        string sha256 = Convert.ToHexString(SHA256.HashData(stream));
        if (stream.Length != size)
        {
            throw new IOException("Artifact changed during identity read.");
        }

        return (size, sha256);
    }

    private static void TryRestoreQuarantine(string quarantinePath, string originalPath)
    {
        try
        {
            if (File.Exists(quarantinePath) && !File.Exists(originalPath) && !Directory.Exists(originalPath))
            {
                File.Move(quarantinePath, originalPath, overwrite: false);
            }
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException)
        {
        }
    }

    private static ManagedArtifactPruneItem PruneFailure(
        ManagedArtifactManifest manifest,
        ManagedArtifactEntry artifact,
        string reason,
        string errorCode,
        string summary) =>
        new(
            artifact.ArtifactId,
            manifest.RunId,
            manifest.RunState,
            artifact.Path,
            artifact.Size ?? 0,
            artifact.DeclaredAtUtc,
            reason,
            Deleted: false,
            errorCode,
            summary);

    private static bool ManifestMatchesRecord(
        ManagedArtifactManifest manifest,
        ProjectPackRunRecord record)
    {
        if (manifest.Artifacts.Count != record.Artifacts.Count)
        {
            return false;
        }

        Dictionary<string, ProjectPackRunArtifactPointer> pointers = record.Artifacts.ToDictionary(
            pointer => pointer.Id,
            StringComparer.Ordinal);
        foreach (ManagedArtifactEntry artifact in manifest.Artifacts)
        {
            if (!pointers.TryGetValue(artifact.PointerId, out ProjectPackRunArtifactPointer? pointer) ||
                artifact.ArtifactId != ManagedArtifactId.Create(record.RunId, pointer.Id) ||
                artifact.Kind != pointer.Kind ||
                artifact.Ownership != ManagedArtifactOwnership.FromScope(pointer.Scope) ||
                artifact.Path != pointer.Path ||
                artifact.Size != pointer.Size ||
                artifact.Sha256 != pointer.Sha256)
            {
                return false;
            }

            bool expectedOwned = pointer.Scope == "managed-run";
            bool expectedPrunable = expectedOwned && (pointer.Path.StartsWith("artifacts/", StringComparison.Ordinal) ||
                pointer.Path.StartsWith("reports/", StringComparison.Ordinal) ||
                pointer.Path.StartsWith("logs/", StringComparison.Ordinal));
            if (artifact.Retention.Owned != expectedOwned || artifact.Retention.Prunable != expectedPrunable)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsWithin(string root, string path)
    {
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, PathComparison);
    }

    private IEnumerable<string> EnumerateRunIds(List<ManagedArtifactDiagnostic> diagnostics)
    {
        if (!Directory.Exists(runStore.RunsRoot))
        {
            return [];
        }

        List<string> runIds = [];
        try
        {
            ManagedProjectPackRunStore.EnsureNoReparseInExistingChain(runStore.RunsRoot);
            foreach (string directory in Directory.EnumerateDirectories(runStore.RunsRoot, "run_*", SearchOption.TopDirectoryOnly))
            {
                string runId = Path.GetFileName(directory);
                if (!ProjectPackRunId.IsValid(runId))
                {
                    diagnostics.Add(new ManagedArtifactDiagnostic(
                        ManagedArtifactErrorCode.ManifestCorrupt,
                        "Managed run directory name is invalid.",
                        RunId: runId,
                        Path: directory));
                    continue;
                }

                try
                {
                    ManagedProjectPackRunStore.EnsureNoReparseInExistingChain(directory);
                    runIds.Add(runId);
                }
                catch (ProjectPackContractException)
                {
                    diagnostics.Add(new ManagedArtifactDiagnostic(
                        ManagedArtifactErrorCode.ReparsePoint,
                        "Managed run directory contains a reparse point.",
                        RunId: runId,
                        Path: directory));
                }
            }
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ProjectPackContractException)
        {
            diagnostics.Add(new ManagedArtifactDiagnostic(
                ManagedArtifactErrorCode.ManifestCorrupt,
                "Managed run index could not be enumerated safely.",
                Path: runStore.RunsRoot));
        }

        return runIds.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    private string? ResolveArtifactPath(
        ManagedArtifactManifest manifest,
        ManagedArtifactEntry artifact,
        WorkspaceContext workspace,
        out ManagedArtifactDiagnostic? diagnostic)
    {
        diagnostic = null;
        try
        {
            if (artifact.Ownership == ManagedArtifactOwnership.Managed)
            {
                if (Path.IsPathRooted(artifact.Path) || artifact.Path.Contains("..", StringComparison.Ordinal))
                {
                    throw new IOException();
                }

                ManagedProjectPackRunLayout layout = runStore.GetLayout(manifest.RunId);
                string path = Path.GetFullPath(Path.Combine(
                    layout.RunRoot,
                    artifact.Path.Replace('/', Path.DirectorySeparatorChar)));
                string root = Path.TrimEndingDirectorySeparator(layout.RunRoot) + Path.DirectorySeparatorChar;
                if (!path.StartsWith(root, PathComparison))
                {
                    throw new IOException();
                }

                return path;
            }

            WorkspaceGuardResult guard = new WorkspaceGuard().ResolvePath(workspace, artifact.Path);
            if (!guard.IsAllowed || guard.FullPath is null)
            {
                throw new IOException();
            }

            return guard.FullPath;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException
            or ProjectPackContractException)
        {
            diagnostic = new ManagedArtifactDiagnostic(
                ManagedArtifactErrorCode.PathUnsafe,
                "Artifact path is outside its declared ownership boundary.",
                artifact.ArtifactId,
                manifest.RunId,
                artifact.Path);
            return null;
        }
    }

    private static Dictionary<string, ManagedArtifactTombstone> ReadExistingTombstones(
        ManagedProjectPackRunLayout layout,
        string runId)
    {
        ManagedArtifactManifest? existing = TryReadExisting(layout, runId);
        return existing?.Artifacts
            .Where(artifact => artifact.Tombstone is not null)
            .ToDictionary(artifact => artifact.ArtifactId, artifact => artifact.Tombstone!, StringComparer.Ordinal)
            ?? new Dictionary<string, ManagedArtifactTombstone>(StringComparer.Ordinal);
    }

    private static Dictionary<string, DateTimeOffset> ReadExistingDeclaredTimes(
        ManagedProjectPackRunLayout layout,
        string runId)
    {
        ManagedArtifactManifest? existing = TryReadExisting(layout, runId);
        return existing?.Artifacts.ToDictionary(
            artifact => artifact.ArtifactId,
            artifact => artifact.DeclaredAtUtc,
            StringComparer.Ordinal) ?? new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
    }

    private static ManagedArtifactManifest? TryReadExisting(ManagedProjectPackRunLayout layout, string runId)
    {
        try
        {
            if (!File.Exists(layout.ArtifactManifestPath))
            {
                return null;
            }

            string json = ManagedProjectPackRunStore.ReadTextBounded(layout.ArtifactManifestPath, MaxManifestBytes);
            ManagedArtifactManifest? manifest = JsonSerializer.Deserialize<ManagedArtifactManifest>(json, JsonOptions);
            return manifest?.RunId == runId ? manifest : null;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or JsonException
            or ArgumentException
            or InvalidOperationException)
        {
            return null;
        }
    }

    private static ManagedArtifactReadResult Failure(
        string code,
        string summary,
        string? artifactId = null,
        string? runId = null,
        string? path = null) =>
        new(false, null, null, new ManagedArtifactDiagnostic(code, summary, artifactId, runId, path));

    private static ManagedArtifactVerificationResult VerificationFailure(
        ManagedArtifactEntry artifact,
        string availability,
        string errorCode,
        string summary,
        string? runId = null) =>
        new(
            artifact.ArtifactId,
            false,
            availability,
            null,
            null,
            new ManagedArtifactDiagnostic(errorCode, summary, artifact.ArtifactId, runId, artifact.Path));

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
