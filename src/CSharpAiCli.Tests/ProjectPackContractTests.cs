using System.Text.Json;
using CSharpAiCli.ProjectPacks;
using CSharpAiCli.ProjectPacks.GerberTiff;

namespace CSharpAiCli.Tests;

public sealed class ProjectPackContractTests
{
    [Fact]
    public void Gerber_tiff_manifest_registers_with_external_stage_safety()
    {
        GerberTiffWorkflowPack pack = new();
        ProjectPackRegistry registry = new([pack]);

        IProjectPack registered = Assert.Single(registry.List());
        Assert.Equal(ProjectPackSchema.CurrentVersion, registered.Manifest.SchemaVersion);
        Assert.Equal("gerber-tiff", registered.Manifest.Id);
        Assert.Equal(["gerbv", "imagemagick"], registered.Manifest.Dependencies.Select(item => item.Id).ToArray());
        Assert.All(
            registered.Manifest.Stages.Where(stage => stage.DependencyId is not null),
            stage =>
            {
                Assert.True(stage.RequiresApproval);
                Assert.Equal(ProjectPackRestartPolicy.ManualReapproval, stage.RestartPolicy);
            });
    }

    [Fact]
    public void Common_contract_dtos_do_not_expose_machine_path_or_domain_fields()
    {
        Type[] types =
        [
            typeof(ProjectPackManifest),
            typeof(ProjectPackCapability),
            typeof(ExternalToolRequirement),
            typeof(ExternalToolIdentity),
            typeof(ProjectPackPlan),
            typeof(ProjectPackStage),
            typeof(ProjectPackArtifactDeclaration),
            typeof(ProjectPackDiagnostic)
        ];

        foreach (Type type in types)
        {
            string[] properties = type.GetProperties().Select(property => property.Name).ToArray();
            Assert.DoesNotContain(properties, name => name.Contains("Path", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(properties, name => name.Contains("Gerber", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(properties, name => name.Contains("Tiff", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(properties, name => name.Contains("Dpi", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(properties, name => name.Contains("Compression", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(properties, name => name.Contains("Pixel", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Manifest_json_contains_no_user_machine_absolute_path()
    {
        string json = JsonSerializer.Serialize(new GerberTiffWorkflowPack().Manifest);

        Assert.DoesNotContain(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Directory.GetCurrentDirectory(), json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Registry_rejects_duplicate_pack()
    {
        ProjectPackManifest manifest = CreateManifest();
        ProjectPackRegistry registry = new([new TestPack(manifest)]);

        ProjectPackContractException exception = Assert.Throws<ProjectPackContractException>(
            () => registry.Register(new TestPack(manifest)));

        Assert.Equal(ProjectPackErrorCode.DuplicatePack, exception.ErrorCode);
    }

    [Fact]
    public void Registry_rejects_unsupported_schema()
    {
        ProjectPackManifest manifest = CreateManifest(schemaVersion: 2);

        ProjectPackContractException exception = Assert.Throws<ProjectPackContractException>(
            () => new ProjectPackRegistry([new TestPack(manifest)]));

        Assert.Equal(ProjectPackErrorCode.SchemaUnsupported, exception.ErrorCode);
    }

    [Fact]
    public void Registry_rejects_duplicate_contract_id()
    {
        ProjectPackManifest manifest = CreateManifest(
            capabilities: [new("read", "Read."), new("read", "Read again.")]);

        ProjectPackContractException exception = Assert.Throws<ProjectPackContractException>(
            () => new ProjectPackRegistry([new TestPack(manifest)]));

        Assert.Equal(ProjectPackErrorCode.DuplicateContractId, exception.ErrorCode);
    }

    [Fact]
    public void Registry_rejects_unknown_stage_capability()
    {
        ProjectPackManifest manifest = CreateManifest(
            stages:
            [
                new ProjectPackStage(
                    "inspect", "read", "missing", null, false,
                    ProjectPackRestartPolicy.SafeReplay)
            ]);

        ProjectPackContractException exception = Assert.Throws<ProjectPackContractException>(
            () => new ProjectPackRegistry([new TestPack(manifest)]));

        Assert.Equal(ProjectPackErrorCode.UnknownCapability, exception.ErrorCode);
    }

    [Fact]
    public void Registry_rejects_unknown_dependency()
    {
        ProjectPackManifest manifest = CreateManifest(
            stages:
            [
                new ProjectPackStage(
                    "convert", "external-tool", "read", "missing", true,
                    ProjectPackRestartPolicy.ManualReapproval)
            ]);

        ProjectPackContractException exception = Assert.Throws<ProjectPackContractException>(
            () => new ProjectPackRegistry([new TestPack(manifest)]));

        Assert.Equal(ProjectPackErrorCode.UnknownDependency, exception.ErrorCode);
    }

    [Fact]
    public void Registry_rejects_external_stage_that_can_replay_or_skip_approval()
    {
        ExternalToolRequirement requirement = CreateRequirement();
        ProjectPackManifest manifest = CreateManifest(
            dependencies: [requirement],
            stages:
            [
                new ProjectPackStage(
                    "convert", "external-tool", "read", requirement.Id, false,
                    ProjectPackRestartPolicy.SafeReplay)
            ]);

        ProjectPackContractException exception = Assert.Throws<ProjectPackContractException>(
            () => new ProjectPackRegistry([new TestPack(manifest)]));

        Assert.Equal(ProjectPackErrorCode.InvalidStageSafety, exception.ErrorCode);
    }

    private static ProjectPackManifest CreateManifest(
        int schemaVersion = ProjectPackSchema.CurrentVersion,
        IReadOnlyList<ProjectPackCapability>? capabilities = null,
        IReadOnlyList<ExternalToolRequirement>? dependencies = null,
        IReadOnlyList<ProjectPackStage>? stages = null)
    {
        return new ProjectPackManifest(
            schemaVersion,
            "test-pack",
            "Test Pack",
            "1.0.0",
            "Test project pack.",
            capabilities ?? [new("read", "Read inputs.")],
            dependencies ?? [],
            stages ?? [],
            []);
    }

    private static ExternalToolRequirement CreateRequirement()
    {
        return new ExternalToolRequirement(
            "test-tool",
            "Test Tool",
            "1.0.0",
            "Test license",
            false,
            "test-tool",
            ["test-tool.exe"],
            ["--version"]);
    }

    private sealed class TestPack(ProjectPackManifest manifest) : IProjectPack
    {
        public ProjectPackManifest Manifest { get; } = manifest;
    }
}
