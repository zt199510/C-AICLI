namespace CSharpAiCli.ProjectPacks;

public static class ProjectPackErrorCode
{
    public const string SchemaUnsupported = "pack-schema-unsupported";
    public const string DuplicatePack = "pack-duplicate";
    public const string DuplicateContractId = "pack-contract-id-duplicate";
    public const string UnknownCapability = "pack-capability-unknown";
    public const string UnknownDependency = "pack-dependency-unknown";
    public const string UnknownArtifact = "pack-artifact-unknown";
    public const string InvalidStageSafety = "pack-stage-safety-invalid";
}

public sealed class ProjectPackContractException(string errorCode, string message) : Exception(message)
{
    public string ErrorCode { get; } = errorCode;
}

public sealed class ProjectPackRegistry
{
    private readonly Dictionary<string, IProjectPack> packs = new(StringComparer.Ordinal);

    public ProjectPackRegistry(IEnumerable<IProjectPack>? initialPacks = null)
    {
        foreach (IProjectPack pack in initialPacks ?? [])
        {
            Register(pack);
        }
    }

    public void Register(IProjectPack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        Validate(pack.Manifest);
        if (!packs.TryAdd(pack.Manifest.Id, pack))
        {
            throw new ProjectPackContractException(
                ProjectPackErrorCode.DuplicatePack,
                $"Project pack '{pack.Manifest.Id}' is already registered.");
        }
    }

    public IReadOnlyList<IProjectPack> List() => packs.Values
        .OrderBy(pack => pack.Manifest.Id, StringComparer.Ordinal)
        .ToArray();

    public bool TryGet(string id, out IProjectPack? pack)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return packs.TryGetValue(id, out pack);
    }

    private static void Validate(ProjectPackManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.SchemaVersion != ProjectPackSchema.CurrentVersion)
        {
            throw new ProjectPackContractException(
                ProjectPackErrorCode.SchemaUnsupported,
                $"Project pack schema version {manifest.SchemaVersion} is not supported.");
        }

        HashSet<string> capabilities = UniqueIds(manifest.Capabilities.Select(item => item.Id), "capability");
        HashSet<string> dependencies = UniqueIds(manifest.Dependencies.Select(item => item.Id), "dependency");
        HashSet<string> stages = UniqueIds(manifest.Stages.Select(item => item.Id), "stage");
        HashSet<string> artifacts = UniqueIds(manifest.Artifacts.Select(item => item.Id), "artifact");
        _ = stages;

        foreach (ProjectPackStage stage in manifest.Stages)
        {
            if (!capabilities.Contains(stage.CapabilityId))
            {
                throw new ProjectPackContractException(
                    ProjectPackErrorCode.UnknownCapability,
                    $"Stage '{stage.Id}' references unknown capability '{stage.CapabilityId}'.");
            }

            if (stage.DependencyId is not null && !dependencies.Contains(stage.DependencyId))
            {
                throw new ProjectPackContractException(
                    ProjectPackErrorCode.UnknownDependency,
                    $"Stage '{stage.Id}' references unknown dependency '{stage.DependencyId}'.");
            }

            foreach (string artifactId in stage.ArtifactIds)
            {
                if (!artifacts.Contains(artifactId))
                {
                    throw new ProjectPackContractException(
                        ProjectPackErrorCode.UnknownArtifact,
                        $"Stage '{stage.Id}' references unknown artifact '{artifactId}'.");
                }
            }

            if (stage.DependencyId is not null &&
                (!stage.RequiresApproval || stage.RestartPolicy != ProjectPackRestartPolicy.ManualReapproval))
            {
                throw new ProjectPackContractException(
                    ProjectPackErrorCode.InvalidStageSafety,
                    $"External stage '{stage.Id}' must require approval and manual reapproval on restart.");
            }
        }
    }

    private static HashSet<string> UniqueIds(IEnumerable<string> ids, string kind)
    {
        HashSet<string> result = new(StringComparer.Ordinal);
        foreach (string id in ids)
        {
            if (!result.Add(id))
            {
                throw new ProjectPackContractException(
                    ProjectPackErrorCode.DuplicateContractId,
                    $"Project pack contains duplicate {kind} id '{id}'.");
            }
        }

        return result;
    }
}
