namespace CSharpAiCli.Core;

public static class BuiltInPipelineCatalog
{
    private static readonly IReadOnlyDictionary<string, PipelineManifest> Pipelines = CreatePipelines()
        .ToDictionary(pipeline => pipeline.Name, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<PipelineManifest> List() =>
        Pipelines.Values.OrderBy(pipeline => pipeline.Name, StringComparer.Ordinal).ToArray();

    public static bool TryGet(string? name, out PipelineManifest? pipeline)
    {
        if (!string.IsNullOrWhiteSpace(name) && Pipelines.TryGetValue(name, out PipelineManifest? found))
        {
            pipeline = found;
            return true;
        }

        pipeline = null;
        return false;
    }

    private static IReadOnlyList<PipelineManifest> CreatePipelines()
    {
        PipelineRoleStep implementer = Step(
            "implement",
            "implementer",
            PipelineCommandFamily.Exec,
            "bugfix",
            "Implement the smallest focused change that addresses the task, then report changed files and verification evidence.");
        PipelineRoleStep reviewer = Step(
            "review",
            "reviewer",
            PipelineCommandFamily.Skill,
            "reviewer",
            "Review the implementation and report findings, assumptions, and remaining risks without changing the workspace.",
            skill: "review-only");
        PipelineRoleStep tester = Step(
            "test",
            "tester",
            PipelineCommandFamily.Exec,
            "tester",
            "Validate the requested behavior, run only policy-allowed verification, and report uncovered risks.");
        PipelineRoleStep security = Step(
            "security",
            "security",
            PipelineCommandFamily.Exec,
            "security",
            "Audit the requested scope for security risks and secret exposure without changing the workspace or running shell commands.");

        return
        [
            new PipelineManifest(
                "fix-review-test",
                "Implement a focused fix, review it read-only, then validate it.",
                [implementer, reviewer, tester]),
            new PipelineManifest(
                "review-test",
                "Review the requested scope read-only, then validate it.",
                [reviewer, tester]),
            new PipelineManifest(
                "security-review",
                "Run a read-only security audit followed by an independent read-only review.",
                [security, reviewer])
        ];
    }

    private static PipelineRoleStep Step(
        string stepId,
        string role,
        string commandFamily,
        string expertName,
        string instructions,
        string? skill = null)
    {
        ExpertProfile expert = ExpertProfileCatalog.GetOrNull(expertName) ??
            throw new InvalidOperationException($"Built-in expert profile '{expertName}' is missing.");
        return new PipelineRoleStep(
            stepId,
            role,
            commandFamily,
            expert.Name,
            instructions,
            PipelineRoleBoundary.FromExpert(expert),
            skill);
    }
}
