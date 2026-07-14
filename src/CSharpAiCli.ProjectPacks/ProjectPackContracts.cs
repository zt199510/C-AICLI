using System.Collections.ObjectModel;
using CSharpAiCli.Core;

namespace CSharpAiCli.ProjectPacks;

public static class ProjectPackSchema
{
    public const int CurrentVersion = 1;
}

public static class ProjectPackRestartPolicy
{
    public const string SafeReplay = "safe-replay";
    public const string ManualReapproval = "manual-reapproval";
    public const string NotRestartable = "not-restartable";

    public static bool IsKnown(string? value) =>
        value is SafeReplay or ManualReapproval or NotRestartable;
}

public static class ExternalToolTrustStatus
{
    public const string Untrusted = "untrusted";
    public const string Trusted = "trusted";
    public const string HashChanged = "hash-changed";
}

public static class ProjectPackDiagnosticSeverity
{
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Error = "error";

    public static bool IsKnown(string? value) => value is Info or Warning or Error;
}

public sealed record ProjectPackCapability
{
    public ProjectPackCapability(string Id, string Description)
    {
        ProjectPackContractGuard.RequireId(Id, nameof(Id));
        ArgumentException.ThrowIfNullOrWhiteSpace(Description);
        this.Id = Id;
        this.Description = ProjectPackContractGuard.Safe(Description, 2_048);
    }

    public string Id { get; }

    public string Description { get; }
}

public sealed record ExternalToolRequirement
{
    public ExternalToolRequirement(
        string Id,
        string DisplayName,
        string MinimumVersion,
        string License,
        bool RedistributionAllowed,
        string ConfigurationKey,
        IReadOnlyList<string> ExecutableFileNames,
        IReadOnlyList<string> ProbeArguments,
        int ProbeTimeoutMilliseconds = 5_000,
        int MaxProbeOutputCharacters = 16_384,
        long MaxExecutableBytes = 512L * 1024 * 1024,
        bool Required = true,
        string? VersionOutputMarker = null)
    {
        ProjectPackContractGuard.RequireId(Id, nameof(Id));
        ArgumentException.ThrowIfNullOrWhiteSpace(DisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(MinimumVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(License);
        ProjectPackContractGuard.RequireId(ConfigurationKey, nameof(ConfigurationKey));

        string[] executableFileNames = (ExecutableFileNames ?? [])
            .Select(ProjectPackContractGuard.RequireFileName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (executableFileNames.Length == 0)
        {
            throw new ArgumentException("At least one executable file name is required.", nameof(ExecutableFileNames));
        }

        string[] probeArguments = (ProbeArguments ?? [])
            .Select(argument => ProjectPackContractGuard.SafeRequired(argument, 1_024, nameof(ProbeArguments)))
            .ToArray();
        if (probeArguments.Length == 0)
        {
            throw new ArgumentException("At least one typed probe argument is required.", nameof(ProbeArguments));
        }

        if (ProbeTimeoutMilliseconds is < 100 or > 60_000)
        {
            throw new ArgumentOutOfRangeException(nameof(ProbeTimeoutMilliseconds));
        }

        if (MaxProbeOutputCharacters is < 256 or > 1_048_576)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxProbeOutputCharacters));
        }

        if (MaxExecutableBytes is < 1 or > 2L * 1024 * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxExecutableBytes));
        }

        this.Id = Id;
        this.DisplayName = ProjectPackContractGuard.Safe(DisplayName, 256);
        this.MinimumVersion = ProjectPackContractGuard.Safe(MinimumVersion, 128);
        this.License = ProjectPackContractGuard.Safe(License, 256);
        this.RedistributionAllowed = RedistributionAllowed;
        this.ConfigurationKey = ConfigurationKey;
        this.ExecutableFileNames = new ReadOnlyCollection<string>(executableFileNames);
        this.ProbeArguments = new ReadOnlyCollection<string>(probeArguments);
        this.ProbeTimeoutMilliseconds = ProbeTimeoutMilliseconds;
        this.MaxProbeOutputCharacters = MaxProbeOutputCharacters;
        this.MaxExecutableBytes = MaxExecutableBytes;
        this.Required = Required;
        this.VersionOutputMarker = string.IsNullOrWhiteSpace(VersionOutputMarker)
            ? null
            : ProjectPackContractGuard.Safe(VersionOutputMarker, 256);
    }

    public string Id { get; }

    public string DisplayName { get; }

    public string MinimumVersion { get; }

    public string License { get; }

    public bool RedistributionAllowed { get; }

    public string ConfigurationKey { get; }

    public IReadOnlyList<string> ExecutableFileNames { get; }

    public IReadOnlyList<string> ProbeArguments { get; }

    public int ProbeTimeoutMilliseconds { get; }

    public int MaxProbeOutputCharacters { get; }

    public long MaxExecutableBytes { get; }

    public bool Required { get; }

    public string? VersionOutputMarker { get; }
}

public sealed record ExternalToolIdentity(
    string DependencyId,
    string FileName,
    long FileSize,
    DateTimeOffset LastWriteTimeUtc,
    string Sha256,
    string Source,
    string TrustStatus,
    string ProbeStatus,
    string? Version = null);

public sealed record ProjectPackArtifactDeclaration
{
    public ProjectPackArtifactDeclaration(
        string Id,
        string Role,
        string MediaType,
        bool Required,
        bool Managed)
    {
        ProjectPackContractGuard.RequireId(Id, nameof(Id));
        ProjectPackContractGuard.RequireId(Role, nameof(Role));
        ArgumentException.ThrowIfNullOrWhiteSpace(MediaType);
        this.Id = Id;
        this.Role = Role;
        this.MediaType = ProjectPackContractGuard.Safe(MediaType, 256);
        this.Required = Required;
        this.Managed = Managed;
    }

    public string Id { get; }

    public string Role { get; }

    public string MediaType { get; }

    public bool Required { get; }

    public bool Managed { get; }
}

public sealed record ProjectPackStage
{
    public ProjectPackStage(
        string Id,
        string Kind,
        string CapabilityId,
        string? DependencyId,
        bool RequiresApproval,
        string RestartPolicy,
        IReadOnlyList<string>? ArtifactIds = null)
    {
        ProjectPackContractGuard.RequireId(Id, nameof(Id));
        ProjectPackContractGuard.RequireId(Kind, nameof(Kind));
        ProjectPackContractGuard.RequireId(CapabilityId, nameof(CapabilityId));
        if (DependencyId is not null)
        {
            ProjectPackContractGuard.RequireId(DependencyId, nameof(DependencyId));
        }

        if (!ProjectPackRestartPolicy.IsKnown(RestartPolicy))
        {
            throw new ArgumentException("Unknown restart policy.", nameof(RestartPolicy));
        }

        string[] artifactIds = (ArtifactIds ?? [])
            .Select(id => ProjectPackContractGuard.RequireId(id, nameof(ArtifactIds)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        this.Id = Id;
        this.Kind = Kind;
        this.CapabilityId = CapabilityId;
        this.DependencyId = DependencyId;
        this.RequiresApproval = RequiresApproval;
        this.RestartPolicy = RestartPolicy;
        this.ArtifactIds = new ReadOnlyCollection<string>(artifactIds);
    }

    public string Id { get; }

    public string Kind { get; }

    public string CapabilityId { get; }

    public string? DependencyId { get; }

    public bool RequiresApproval { get; }

    public string RestartPolicy { get; }

    public IReadOnlyList<string> ArtifactIds { get; }
}

public sealed record ProjectPackDiagnostic
{
    public ProjectPackDiagnostic(
        string Code,
        string Severity,
        string Summary,
        string? DependencyId = null,
        string? StageId = null)
    {
        ProjectPackContractGuard.RequireId(Code, nameof(Code));
        if (!ProjectPackDiagnosticSeverity.IsKnown(Severity))
        {
            throw new ArgumentException("Unknown diagnostic severity.", nameof(Severity));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(Summary);
        this.Code = Code;
        this.Severity = Severity;
        this.Summary = ProjectPackContractGuard.Safe(Summary, 4_096);
        this.DependencyId = ProjectPackContractGuard.SafeOptionalId(DependencyId, nameof(DependencyId));
        this.StageId = ProjectPackContractGuard.SafeOptionalId(StageId, nameof(StageId));
    }

    public string Code { get; }

    public string Severity { get; }

    public string Summary { get; }

    public string? DependencyId { get; }

    public string? StageId { get; }
}

public sealed record ProjectPackInputIdentity
{
    public ProjectPackInputIdentity(string Id, long Size, string Sha256)
    {
        ProjectPackContractGuard.RequireId(Id, nameof(Id));
        if (Size < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Size));
        }

        ProjectPackContractGuard.RequireSha256(Sha256, nameof(Sha256));
        this.Id = Id;
        this.Size = Size;
        this.Sha256 = Sha256.ToUpperInvariant();
    }

    public string Id { get; }

    public long Size { get; }

    public string Sha256 { get; }
}

public sealed record ProjectPackPlan
{
    public ProjectPackPlan(
        string PackId,
        string PlanId,
        IReadOnlyList<ProjectPackInputIdentity>? Inputs,
        IReadOnlyList<ProjectPackStage>? Stages,
        IReadOnlyList<ProjectPackArtifactDeclaration>? Artifacts,
        IReadOnlyList<ProjectPackDiagnostic>? Diagnostics)
    {
        ProjectPackContractGuard.RequireId(PackId, nameof(PackId));
        ProjectPackContractGuard.RequireId(PlanId, nameof(PlanId));
        SchemaVersion = ProjectPackSchema.CurrentVersion;
        this.PackId = PackId;
        this.PlanId = PlanId;
        this.Inputs = new ReadOnlyCollection<ProjectPackInputIdentity>((Inputs ?? []).ToArray());
        this.Stages = new ReadOnlyCollection<ProjectPackStage>((Stages ?? []).ToArray());
        this.Artifacts = new ReadOnlyCollection<ProjectPackArtifactDeclaration>((Artifacts ?? []).ToArray());
        this.Diagnostics = new ReadOnlyCollection<ProjectPackDiagnostic>((Diagnostics ?? []).ToArray());
    }

    public int SchemaVersion { get; }

    public string PackId { get; }

    public string PlanId { get; }

    public IReadOnlyList<ProjectPackInputIdentity> Inputs { get; }

    public IReadOnlyList<ProjectPackStage> Stages { get; }

    public IReadOnlyList<ProjectPackArtifactDeclaration> Artifacts { get; }

    public IReadOnlyList<ProjectPackDiagnostic> Diagnostics { get; }
}

public sealed record ProjectPackManifest
{
    public ProjectPackManifest(
        int SchemaVersion,
        string Id,
        string DisplayName,
        string Version,
        string Description,
        IReadOnlyList<ProjectPackCapability>? Capabilities,
        IReadOnlyList<ExternalToolRequirement>? Dependencies,
        IReadOnlyList<ProjectPackStage>? Stages,
        IReadOnlyList<ProjectPackArtifactDeclaration>? Artifacts)
    {
        ProjectPackContractGuard.RequireId(Id, nameof(Id));
        ArgumentException.ThrowIfNullOrWhiteSpace(DisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(Version);
        ArgumentException.ThrowIfNullOrWhiteSpace(Description);
        this.SchemaVersion = SchemaVersion;
        this.Id = Id;
        this.DisplayName = ProjectPackContractGuard.Safe(DisplayName, 256);
        this.Version = ProjectPackContractGuard.Safe(Version, 64);
        this.Description = ProjectPackContractGuard.Safe(Description, 2_048);
        this.Capabilities = new ReadOnlyCollection<ProjectPackCapability>((Capabilities ?? []).ToArray());
        this.Dependencies = new ReadOnlyCollection<ExternalToolRequirement>((Dependencies ?? []).ToArray());
        this.Stages = new ReadOnlyCollection<ProjectPackStage>((Stages ?? []).ToArray());
        this.Artifacts = new ReadOnlyCollection<ProjectPackArtifactDeclaration>((Artifacts ?? []).ToArray());
    }

    public int SchemaVersion { get; }

    public string Id { get; }

    public string DisplayName { get; }

    public string Version { get; }

    public string Description { get; }

    public IReadOnlyList<ProjectPackCapability> Capabilities { get; }

    public IReadOnlyList<ExternalToolRequirement> Dependencies { get; }

    public IReadOnlyList<ProjectPackStage> Stages { get; }

    public IReadOnlyList<ProjectPackArtifactDeclaration> Artifacts { get; }
}

public interface IProjectPack
{
    ProjectPackManifest Manifest { get; }
}

internal static class ProjectPackContractGuard
{
    public static string RequireId(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > 128 || !char.IsAsciiLetter(value[0]) ||
            value.Any(character => char.IsUpper(character) ||
                !(char.IsAsciiLetterOrDigit(character) || character is '-' or '.')))
        {
            throw new ArgumentException("Identifier must use lowercase ASCII letters, digits, hyphens, or dots.", parameterName);
        }

        return value;
    }

    public static string RequireFileName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!string.Equals(value, Path.GetFileName(value), StringComparison.Ordinal) ||
            value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("Executable names must be file names without a path.", nameof(value));
        }

        return value;
    }

    public static void RequireSha256(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("SHA256 must contain 64 hexadecimal characters.", parameterName);
        }
    }

    public static string Safe(string value, int maxLength)
    {
        string safe = DiagnosticSecretRedactor.Redact(value);
        return safe.Length <= maxLength ? safe : safe[..maxLength];
    }

    public static string SafeRequired(string value, int maxLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return Safe(value, maxLength);
    }

    public static string? SafeOptionalId(string? value, string parameterName)
    {
        if (value is null)
        {
            return null;
        }

        RequireId(value, parameterName);
        return value;
    }
}
