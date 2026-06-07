namespace CSharpAiCli.Core;

public sealed record WorkflowValidationSuggestion(
    string ProfileName,
    string WorkspacePath,
    string WorkspacePathSource,
    string? ValidationCommand,
    string Source,
    bool RequiresApproval);
