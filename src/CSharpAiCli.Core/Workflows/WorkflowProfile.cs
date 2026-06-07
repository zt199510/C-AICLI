namespace CSharpAiCli.Core;

public sealed record WorkflowProfile(
    string Name,
    string? WorkspacePath,
    string? ValidationCommand,
    string Description,
    string Source);
