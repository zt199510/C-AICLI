using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CSharpAiCli.Core;
using CSharpAiCli.ProjectPacks.GerberTiff;

namespace CSharpAiCli.ProjectPacks.Runtime;

public sealed record ProjectPackStagedInput(
    string Id,
    string SourceRelativePath,
    string StagedRelativePath,
    string Kind,
    string? LayerRole,
    bool PassedToExternalTool,
    long Size,
    string Sha256);

public sealed record ProjectPackInputManifest
{
    public ProjectPackInputManifest(
        string runId,
        string planId,
        string planFingerprint,
        IReadOnlyList<ProjectPackStagedInput>? inputs)
    {
        SchemaVersion = ProjectPackRunRecord.CurrentSchemaVersion;
        RunId = runId;
        PlanId = planId;
        PlanFingerprint = planFingerprint;
        Inputs = new ReadOnlyCollection<ProjectPackStagedInput>((inputs ?? []).ToArray());
        SourceInputReadOnly = true;
        UnknownFilesCopied = false;
    }

    public int SchemaVersion { get; }
    public string RunId { get; }
    public string PlanId { get; }
    public string PlanFingerprint { get; }
    public IReadOnlyList<ProjectPackStagedInput> Inputs { get; }
    public bool SourceInputReadOnly { get; }
    public bool UnknownFilesCopied { get; }
}

public sealed record ProjectPackRunValidationResult(
    bool Succeeded,
    GerberTiffConversionPlan? CurrentPlan,
    ProjectPackRunDiagnostic? Diagnostic)
{
    public static ProjectPackRunValidationResult Success(GerberTiffConversionPlan plan) => new(true, plan, null);
    public static ProjectPackRunValidationResult Failure(string code, string summary) =>
        new(false, null, new ProjectPackRunDiagnostic(code, summary));
}

public static class ProjectPackRunPolicyFingerprint
{
    public static string Compute(string policyDescriptor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyDescriptor);
        string canonical = "project-pack-run-policy.v1\n" + policyDescriptor.Trim().ToLowerInvariant() +
            "\nsource-read-only=true\noutput-overwrite=false\napproval-persisted=false";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}

public sealed class GerberTiffRunRevalidator
{
    public ProjectPackRunValidationResult Revalidate(
        GerberTiffRunPlanSnapshot snapshot,
        WorkspaceContext workspace,
        IReadOnlyDictionary<string, string>? toolPaths,
        string expectedPolicyFingerprint,
        string currentPolicyFingerprint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(workspace);
        if (!string.Equals(expectedPolicyFingerprint, currentPolicyFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            return ProjectPackRunValidationResult.Failure(
                ProjectPackRunErrorCode.PolicyChanged,
                "Current project pack policy does not match the checkpoint.");
        }

        GerberTiffConversionPlan current = new GerberTiffConversionPlanBuilder().Build(
            workspace,
            snapshot.InputDirectory,
            snapshot.OutputDirectory,
            toolPaths,
            trustedHashes: null,
            cancellationToken);
        if (!current.ReadyForStaging || current.Fingerprint is null || current.PlanId is null)
        {
            string code = current.Diagnostics.Any(diagnostic => diagnostic.Code == GerberTiffDiagnosticCode.OutputAlreadyExists)
                ? ProjectPackRunErrorCode.OutputConflict
                : current.Diagnostics.Any(diagnostic => diagnostic.Code.StartsWith("input-", StringComparison.Ordinal))
                    ? ProjectPackRunErrorCode.InputChanged
                    : ProjectPackRunErrorCode.PlanFingerprintChanged;
            return ProjectPackRunValidationResult.Failure(code, "Project pack plan no longer passes input/output pre-run validation.");
        }

        if (!SameInputs(snapshot, current))
        {
            return ProjectPackRunValidationResult.Failure(
                ProjectPackRunErrorCode.InputChanged,
                "Project pack input inventory changed after planning.");
        }

        if (!SameTools(snapshot, current))
        {
            return ProjectPackRunValidationResult.Failure(
                ProjectPackRunErrorCode.ToolChanged,
                "Project pack tool identity changed after planning.");
        }

        if (!string.Equals(snapshot.Fingerprint, current.Fingerprint, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(snapshot.PlanId, current.PlanId, StringComparison.Ordinal))
        {
            return ProjectPackRunValidationResult.Failure(
                ProjectPackRunErrorCode.PlanFingerprintChanged,
                "Project pack plan fingerprint changed after planning.");
        }

        return ProjectPackRunValidationResult.Success(current);
    }

    private static bool SameInputs(GerberTiffRunPlanSnapshot snapshot, GerberTiffConversionPlan current)
    {
        GerberTiffInputFile[] currentInputs = current.Inventory.Files
            .Where(file => file.Supported)
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();
        if (snapshot.Inputs.Count != currentInputs.Length)
        {
            return false;
        }

        return snapshot.Inputs.Zip(currentInputs).All(pair =>
            pair.First.Id == pair.Second.Id &&
            pair.First.RelativePath == pair.Second.RelativePath &&
            pair.First.Extension.Equals(pair.Second.Extension, StringComparison.OrdinalIgnoreCase) &&
            pair.First.Kind == pair.Second.Kind &&
            pair.First.LayerRole == pair.Second.LayerRole &&
            pair.First.PassedToExternalTool == pair.Second.PassedToExternalTool &&
            pair.First.Size == pair.Second.Size &&
            pair.First.Sha256.Equals(pair.Second.Sha256, StringComparison.OrdinalIgnoreCase));
    }

    private static bool SameTools(GerberTiffRunPlanSnapshot snapshot, GerberTiffConversionPlan current)
    {
        GerberTiffToolPlanIdentity[] currentTools = current.Tools.OrderBy(tool => tool.DependencyId, StringComparer.Ordinal).ToArray();
        if (snapshot.Tools.Count != currentTools.Length)
        {
            return false;
        }

        return snapshot.Tools.Zip(currentTools).All(pair =>
            pair.First.DependencyId == pair.Second.DependencyId &&
            pair.First.Required == pair.Second.Required &&
            pair.First.Status == pair.Second.Status &&
            pair.First.FileName == pair.Second.FileName &&
            pair.First.FileSize == pair.Second.FileSize &&
            string.Equals(pair.First.Sha256, pair.Second.Sha256, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed record ProjectPackStagingResult(
    bool Succeeded,
    ProjectPackInputManifest Manifest,
    ProjectPackRunDiagnostic? Diagnostic);

public sealed class ProjectPackStagingService
{
    private const int BufferBytes = 128 * 1024;

    public ProjectPackInputManifest CreateManifest(GerberTiffRunPlanSnapshot plan, string runId)
    {
        ArgumentNullException.ThrowIfNull(plan);
        List<ProjectPackStagedInput> inputs = [];
        int sequence = 0;
        foreach (GerberTiffRunPlanInput input in plan.Inputs.OrderBy(item => item.RelativePath, StringComparer.Ordinal))
        {
            sequence++;
            string stagedName = $"{sequence:D4}-{input.Id}{input.Extension.ToLowerInvariant()}";
            inputs.Add(new ProjectPackStagedInput(
                input.Id,
                input.RelativePath,
                "staging/" + stagedName,
                input.Kind,
                input.LayerRole,
                input.PassedToExternalTool,
                input.Size,
                input.Sha256));
        }

        return new ProjectPackInputManifest(runId, plan.PlanId, plan.Fingerprint, inputs);
    }

    public ProjectPackStagingResult Stage(
        ProjectPackInputManifest manifest,
        WorkspaceContext workspace,
        ManagedProjectPackRunLayout layout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(layout);
        if (manifest.Inputs.Count is 0 or > GerberTiffInputEnvelope.MaxFileCount ||
            manifest.Inputs.Sum(input => input.Size) > GerberTiffInputEnvelope.MaxTotalBytes)
        {
            return Failure(ProjectPackRunErrorCode.StagingLimitExceeded, "Staging input exceeds the frozen v1 bounds.", manifest);
        }

        try
        {
            ManagedProjectPackRunStore.EnsureNoReparseInExistingChain(layout.RunRoot);
            ManagedProjectPackRunStore.EnsureNoReparseInExistingChain(layout.StagingPath);
            foreach (ProjectPackStagedInput input in manifest.Inputs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string source = ResolveSource(workspace, input);
                string destination = ResolveStaging(layout, input.StagedRelativePath);
                VerifyFile(source, input.Size, input.Sha256, ProjectPackRunErrorCode.InputChanged);
                CopyBounded(source, destination, input.Size, cancellationToken);
                VerifyFile(destination, input.Size, input.Sha256, ProjectPackRunErrorCode.StagingHashMismatch);
                VerifyFile(source, input.Size, input.Sha256, ProjectPackRunErrorCode.InputChanged);
            }

            return new ProjectPackStagingResult(true, manifest, null);
        }
        catch (ProjectPackContractException exception)
        {
            return Failure(exception.ErrorCode, exception.Message, manifest);
        }
        catch (OperationCanceledException)
        {
            return Failure(ProjectPackRunErrorCode.DriverCanceled, "Staging was canceled.", manifest);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return Failure(ProjectPackRunErrorCode.StagingCopyFailed, "A bounded staging copy could not be completed.", manifest);
        }
    }

    public ProjectPackRunDiagnostic? VerifyStaged(
        ProjectPackInputManifest manifest,
        ManagedProjectPackRunLayout layout)
    {
        try
        {
            ManagedProjectPackRunStore.EnsureNoReparseInExistingChain(layout.StagingPath);
            foreach (ProjectPackStagedInput input in manifest.Inputs)
            {
                VerifyFile(ResolveStaging(layout, input.StagedRelativePath), input.Size, input.Sha256,
                    ProjectPackRunErrorCode.StagingHashMismatch);
            }

            return null;
        }
        catch (ProjectPackContractException exception)
        {
            return new ProjectPackRunDiagnostic(exception.ErrorCode, exception.Message, layout.StagingPath, manifest.RunId);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new ProjectPackRunDiagnostic(
                ProjectPackRunErrorCode.StagingHashMismatch,
                "Staged input could not be verified.",
                layout.StagingPath,
                manifest.RunId);
        }
    }

    public static string RenderManifest(ProjectPackInputManifest manifest) =>
        JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });

    public static ProjectPackInputManifest LoadManifest(string json)
    {
        ProjectPackInputManifest? manifest = JsonSerializer.Deserialize<ProjectPackInputManifest>(json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                MaxDepth = 32
            });
        if (manifest is null || manifest.SchemaVersion != ProjectPackRunRecord.CurrentSchemaVersion ||
            !ProjectPackRunId.IsValid(manifest.RunId) || manifest.Inputs.Count == 0)
        {
            throw new ProjectPackContractException(ProjectPackRunErrorCode.RecordCorrupt, "Input manifest is corrupt.");
        }

        return manifest;
    }

    private static string ResolveSource(WorkspaceContext workspace, ProjectPackStagedInput input)
    {
        string relativeOsPath = input.SourceRelativePath.Replace('/', Path.DirectorySeparatorChar);
        WorkspaceGuardResult guard = new WorkspaceGuard().ResolvePath(workspace, relativeOsPath);
        if (!guard.IsAllowed || guard.FullPath is null || !File.Exists(guard.FullPath))
        {
            throw new ProjectPackContractException(ProjectPackRunErrorCode.InputOutsideWorkspace, "Staging source is outside the workspace or missing.");
        }

        if (GerberTiffWorkspacePathPolicy.HasReparsePointInExistingChain(guard.FullPath))
        {
            throw new ProjectPackContractException(ProjectPackRunErrorCode.InputReparsePoint, "Staging source contains a reparse point.");
        }

        return guard.FullPath;
    }

    private static string ResolveStaging(ManagedProjectPackRunLayout layout, string stagedRelativePath)
    {
        string fileName = Path.GetFileName(stagedRelativePath);
        if (!string.Equals(stagedRelativePath.Replace('\\', '/'), "staging/" + fileName, StringComparison.Ordinal) ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ProjectPackContractException(ProjectPackRunErrorCode.PathUnsafe, "Staging mapping is invalid.");
        }

        string destination = Path.GetFullPath(Path.Combine(layout.StagingPath, fileName));
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(layout.StagingPath)) + Path.DirectorySeparatorChar;
        if (!destination.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new ProjectPackContractException(ProjectPackRunErrorCode.PathUnsafe, "Staging mapping escaped the managed run.");
        }

        return destination;
    }

    private static void VerifyFile(string path, long expectedSize, string expectedSha256, string errorCode)
    {
        FileInfo before = new(path);
        before.Refresh();
        if (!before.Exists || before.Length != expectedSize || before.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new ProjectPackContractException(errorCode, "Input identity changed during staging.");
        }

        string hash;
        using (FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferBytes, FileOptions.SequentialScan))
        {
            hash = Convert.ToHexString(SHA256.HashData(stream));
        }

        FileInfo after = new(path);
        after.Refresh();
        if (!after.Exists || after.Length != before.Length || after.LastWriteTimeUtc != before.LastWriteTimeUtc ||
            !hash.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new ProjectPackContractException(errorCode, "Input identity changed during staging.");
        }
    }

    private static void CopyBounded(string source, string destination, long expectedSize, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[BufferBytes];
        long copied = 0;
        using FileStream input = new(source, FileMode.Open, FileAccess.Read, FileShare.Read, BufferBytes, FileOptions.SequentialScan);
        using FileStream output = new(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferBytes, FileOptions.WriteThrough);
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            copied = checked(copied + read);
            if (copied > expectedSize)
            {
                throw new ProjectPackContractException(ProjectPackRunErrorCode.InputChanged, "Input grew during staging.");
            }

            output.Write(buffer, 0, read);
        }

        output.Flush(flushToDisk: true);
        if (copied != expectedSize)
        {
            throw new ProjectPackContractException(ProjectPackRunErrorCode.InputChanged, "Input size changed during staging.");
        }
    }

    private static ProjectPackStagingResult Failure(string code, string summary, ProjectPackInputManifest manifest) =>
        new(false, manifest, new ProjectPackRunDiagnostic(code, summary, RunId: manifest.RunId));
}
