using System.Reflection;
using System.Text.Json.Nodes;
using CSharpAiCli.ProjectPacks;
using CSharpAiCli.ProjectPacks.Runtime;

namespace CSharpAiCli.Tests;

public sealed class VerticalWorkflowSchemaContractTests
{
    [Fact]
    public void Schema_versions_types_states_and_error_code_sets_are_frozen()
    {
        Assert.Equal(1, ProjectPackSchema.CurrentVersion);
        Assert.Equal(1, ProjectPackRunRecord.CurrentSchemaVersion);
        Assert.Equal(1, ProjectPackRunCheckpoint.CurrentSchemaVersion);
        Assert.Equal(1, ManagedArtifactManifest.CurrentSchemaVersion);
        Assert.Equal(1, TiffVerificationSchema.CurrentVersion);
        Assert.Equal("managed-artifact-manifest", ManagedArtifactManifest.ManifestType);
        Assert.Equal("packs.verify", TiffVerificationSchema.ResultType);
        Assert.Equal("packs.preview", TiffVerificationSchema.PreviewType);
        Assert.Equal("gerber-tiff.verification-baseline", TiffVerificationSchema.BaselineType);

        Assert.Equal(12, Constants(typeof(ProjectPackRunState)).Count);
        Assert.Equal(4, Constants(typeof(TiffVerificationLevel)).Count);
        AssertCodes(typeof(ProjectPackRunErrorCode), 46, "pack-");
        AssertCodes(typeof(TiffVerificationErrorCode), 27, "pack-tiff-");
        AssertCodes(typeof(ManagedArtifactErrorCode), 14, "artifact-");
    }

    [Fact]
    public void Run_artifact_and_verification_json_top_level_shapes_are_frozen()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-15T10:00:00Z");
        string runId = "run_20260715T100000000Z_abcdef12";
        string fingerprint = new('A', 64);
        ProjectPackRunRecord record = new(
            1,
            runId,
            0,
            "gerber-tiff",
            "1.0.0-preview.1",
            "plan-1",
            fingerprint,
            fingerprint,
            ProjectPackRunState.Failed,
            now,
            now);
        ProjectPackRunCheckpoint checkpoint = new(
            1,
            runId,
            0,
            ProjectPackRunState.Failed,
            fingerprint,
            fingerprint,
            now);
        JsonObject run = Assert.IsType<JsonObject>(JsonNode.Parse(ProjectPackRunRenderer.RenderJson(record, checkpoint)));
        AssertKeys(run, "checkpoint", "redaction", "run", "schemaVersion", "status", "type");
        Assert.Equal("packs.run", run["type"]?.GetValue<string>());

        ManagedArtifactListResult artifacts = new([], []);
        JsonObject artifactList = Assert.IsType<JsonObject>(JsonNode.Parse(ManagedArtifactRenderer.RenderListJson(artifacts)));
        AssertKeys(artifactList, "artifacts", "diagnostics", "schemaVersion", "type");
        Assert.Equal("artifacts.list", artifactList["type"]?.GetValue<string>());

        TiffVerificationResult verification = new(
            runId,
            false,
            false,
            null,
            null,
            [],
            [],
            [],
            [],
            [],
            "not verified");
        JsonObject verify = Assert.IsType<JsonObject>(JsonNode.Parse(TiffVerificationRenderer.RenderJson(verification)));
        AssertKeys(
            verify,
            "artifacts",
            "baseline",
            "correctnessProof",
            "diagnostics",
            "hardVerificationPassed",
            "humanReviewRequired",
            "levels",
            "limits",
            "pixelComparisons",
            "runId",
            "schemaVersion",
            "status",
            "summary",
            "type");
        Assert.Equal("packs.verify", verify["type"]?.GetValue<string>());
    }

    private static IReadOnlyList<string> Constants(Type type) => type
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
        .Select(field => Assert.IsType<string>(field.GetRawConstantValue()))
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();

    private static void AssertCodes(Type type, int expectedCount, string prefix)
    {
        IReadOnlyList<string> codes = Constants(type);
        Assert.Equal(expectedCount, codes.Count);
        Assert.Equal(codes.Count, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(codes, code =>
        {
            Assert.StartsWith(prefix, code, StringComparison.Ordinal);
            Assert.Matches("^[a-z0-9]+(?:-[a-z0-9]+)*$", code);
        });
    }

    private static void AssertKeys(JsonObject value, params string[] expected) => Assert.Equal(
        expected.OrderBy(key => key, StringComparer.Ordinal),
        value.Select(property => property.Key).OrderBy(key => key, StringComparer.Ordinal));
}
