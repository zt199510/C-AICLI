using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CSharpAiCli.ProjectPacks.Runtime;

public static class ManagedArtifactErrorCode
{
    public const string NotFound = "artifact-not-found";
    public const string ManifestMissing = "artifact-manifest-missing";
    public const string ManifestCorrupt = "artifact-manifest-corrupt";
    public const string SchemaUnsupported = "artifact-manifest-schema-unsupported";
    public const string RecordMismatch = "artifact-manifest-record-mismatch";
    public const string PathUnsafe = "artifact-path-unsafe";
    public const string ReparsePoint = "artifact-reparse-point";
    public const string Missing = "artifact-missing";
    public const string Changed = "artifact-identity-changed";
    public const string External = "artifact-external-owner";
    public const string WriteFailed = "artifact-manifest-write-failed";
    public const string PruneIneligible = "artifact-prune-ineligible";
    public const string PruneRace = "artifact-prune-race";
    public const string PruneFailed = "artifact-prune-failed";
}

public static partial class ManagedArtifactId
{
    [GeneratedRegex("^artifact_[a-f0-9]{24}$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public static string Create(string runId, string pointerId)
    {
        if (!ProjectPackRunId.IsValid(runId))
        {
            throw new ArgumentException("Managed artifact run id is invalid.", nameof(runId));
        }

        ProjectPackContractGuard.RequireId(pointerId, nameof(pointerId));
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes($"managed-artifact.v1\n{runId}\n{pointerId}"));
        return "artifact_" + Convert.ToHexString(digest)[..24].ToLowerInvariant();
    }

    public static bool IsValid(string? artifactId) =>
        !string.IsNullOrWhiteSpace(artifactId) && Pattern().IsMatch(artifactId);
}

public static class ManagedArtifactOwnership
{
    public const string Managed = "managed";
    public const string WorkspaceOutput = "workspace-output";
    public const string Source = "source";
    public const string External = "external";

    public static bool IsKnown(string? value) =>
        value is Managed or WorkspaceOutput or Source or External;

    public static string FromScope(string scope) => scope switch
    {
        "managed-run" => Managed,
        "workspace-output" => WorkspaceOutput,
        "source" => Source,
        _ => External
    };
}

public static class ManagedArtifactRetentionClass
{
    public const string TerminalPrunable = "terminal-prunable";
    public const string RetainMetadata = "retain-metadata";
    public const string ExternalOwner = "external-owner";
}

public static class ManagedArtifactVerificationStatus
{
    public const string Declared = "declared";
    public const string HardPassed = "hard-passed";
    public const string Failed = "failed";
    public const string NotApplicable = "not-applicable";
}

public static class ManagedArtifactAvailability
{
    public const string Available = "available";
    public const string Missing = "missing";
    public const string Changed = "changed";
    public const string Pruned = "pruned";
    public const string External = "external";
}

public static class ManagedArtifactRetentionParser
{
    public static bool TryParseAge(string? value, out TimeSpan age)
    {
        age = default;
        if (string.IsNullOrWhiteSpace(value) || value.Length < 2)
        {
            return false;
        }

        char suffix = char.ToLowerInvariant(value[^1]);
        if (!double.TryParse(
            value[..^1],
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out double amount) || amount <= 0)
        {
            return false;
        }

        try
        {
            age = suffix switch
            {
                'd' => TimeSpan.FromDays(amount),
                'h' => TimeSpan.FromHours(amount),
                'm' => TimeSpan.FromMinutes(amount),
                _ => default
            };
            return age > TimeSpan.Zero;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}

public sealed record ManagedArtifactOwner(
    string RunId,
    string? JobId,
    string? QueueId,
    string? RootRunId,
    string? ParentRunId,
    int Attempt);

public sealed record ManagedArtifactRetention(
    string Class,
    bool Owned,
    bool Prunable,
    int DefaultMinimumAgeDays);

public sealed record ManagedArtifactTombstone(
    DateTimeOffset RemovedAtUtc,
    string Reason,
    long? OriginalSize,
    string? OriginalSha256,
    string RunState,
    string? JobId,
    string? QueueId);

public sealed record ManagedArtifactEntry
{
    public ManagedArtifactEntry(
        string artifactId,
        string pointerId,
        string kind,
        string ownership,
        string path,
        long? size,
        string? sha256,
        string verification,
        string availability,
        ManagedArtifactRetention retention,
        DateTimeOffset declaredAtUtc,
        ManagedArtifactTombstone? tombstone = null)
    {
        if (!ManagedArtifactId.IsValid(artifactId) || !ManagedArtifactOwnership.IsKnown(ownership) || size < 0)
        {
            throw new ArgumentException("Managed artifact entry is invalid.");
        }

        ProjectPackContractGuard.RequireId(pointerId, nameof(pointerId));
        ProjectPackContractGuard.RequireId(kind, nameof(kind));
        if (sha256 is not null)
        {
            ProjectPackContractGuard.RequireSha256(sha256, nameof(sha256));
        }

        ArtifactId = artifactId;
        PointerId = pointerId;
        Kind = kind;
        Ownership = ownership;
        Path = path;
        Size = size;
        Sha256 = sha256?.ToUpperInvariant();
        Verification = verification;
        Availability = tombstone is null ? availability : ManagedArtifactAvailability.Pruned;
        Retention = retention;
        DeclaredAtUtc = declaredAtUtc;
        Tombstone = tombstone;
    }

    public string ArtifactId { get; }
    public string PointerId { get; }
    public string Kind { get; }
    public string Ownership { get; }
    public string Path { get; }
    public long? Size { get; }
    public string? Sha256 { get; }
    public string Verification { get; }
    public string Availability { get; }
    public ManagedArtifactRetention Retention { get; }
    public DateTimeOffset DeclaredAtUtc { get; }
    public ManagedArtifactTombstone? Tombstone { get; }
}

public sealed record ManagedArtifactManifest
{
    public const int CurrentSchemaVersion = 1;
    public const string ManifestType = "managed-artifact-manifest";

    public ManagedArtifactManifest(
        int schemaVersion,
        string type,
        string runId,
        long runRevision,
        string runState,
        ManagedArtifactOwner owner,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        IReadOnlyList<ManagedArtifactEntry>? artifacts,
        ProjectPackAcceptanceDecision? acceptance = null)
    {
        if (schemaVersion != CurrentSchemaVersion || type != ManifestType ||
            !ProjectPackRunId.IsValid(runId) || runRevision < 0 || !ProjectPackRunState.IsKnown(runState) ||
            owner.RunId != runId || owner.Attempt <= 0)
        {
            throw new ArgumentException("Managed artifact manifest is invalid.");
        }

        ManagedArtifactEntry[] entries = (artifacts ?? []).ToArray();
        if (entries.Select(entry => entry.ArtifactId).Distinct(StringComparer.Ordinal).Count() != entries.Length ||
            entries.Select(entry => entry.PointerId).Distinct(StringComparer.Ordinal).Count() != entries.Length)
        {
            throw new ArgumentException("Managed artifact manifest entries are duplicate.");
        }

        SchemaVersion = schemaVersion;
        Type = type;
        RunId = runId;
        RunRevision = runRevision;
        RunState = runState;
        Owner = owner;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
        Artifacts = new ReadOnlyCollection<ManagedArtifactEntry>(entries
            .OrderBy(entry => entry.ArtifactId, StringComparer.Ordinal)
            .ToArray());
        Acceptance = acceptance;
    }

    public int SchemaVersion { get; }
    public string Type { get; }
    public string RunId { get; }
    public long RunRevision { get; }
    public string RunState { get; }
    public ManagedArtifactOwner Owner { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset UpdatedAtUtc { get; }
    public IReadOnlyList<ManagedArtifactEntry> Artifacts { get; }
    public ProjectPackAcceptanceDecision? Acceptance { get; }
}

public sealed record ManagedArtifactDiagnostic(
    string ErrorCode,
    string Summary,
    string? ArtifactId = null,
    string? RunId = null,
    string? Path = null);

public sealed record ManagedArtifactReadResult(
    bool Succeeded,
    ManagedArtifactManifest? Manifest,
    ManagedArtifactEntry? Artifact,
    ManagedArtifactDiagnostic? Diagnostic);

public sealed record ManagedArtifactListResult(
    IReadOnlyList<(ManagedArtifactManifest Manifest, ManagedArtifactEntry Artifact)> Artifacts,
    IReadOnlyList<ManagedArtifactDiagnostic> Diagnostics);

public sealed record ManagedArtifactVerificationResult(
    string ArtifactId,
    bool Succeeded,
    string Availability,
    long? ObservedSize,
    string? ObservedSha256,
    ManagedArtifactDiagnostic? Diagnostic);

public sealed record ManagedArtifactPruneFilter(
    DateTimeOffset OlderThanUtc,
    IReadOnlySet<string>? Statuses = null,
    long? MinimumSize = null,
    long? MaximumSize = null);

public sealed record ManagedArtifactPruneItem(
    string ArtifactId,
    string RunId,
    string RunState,
    string Path,
    long Size,
    DateTimeOffset DeclaredAtUtc,
    string Reason,
    bool Deleted,
    string? ErrorCode = null,
    string? Summary = null);

public sealed record ManagedArtifactPruneResult(
    bool Apply,
    IReadOnlyList<ManagedArtifactPruneItem> Items,
    IReadOnlyList<ManagedArtifactDiagnostic> Diagnostics)
{
    public long TotalBytes => Items.Sum(item => item.Size);
    public int DeletedCount => Items.Count(item => item.Deleted);
}
