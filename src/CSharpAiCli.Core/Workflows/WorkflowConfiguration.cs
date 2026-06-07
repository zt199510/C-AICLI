namespace CSharpAiCli.Core;

public sealed record WorkflowConfiguration(IReadOnlyList<WorkflowProfile> Profiles)
{
    public static WorkflowConfiguration Empty { get; } = new([]);
}
