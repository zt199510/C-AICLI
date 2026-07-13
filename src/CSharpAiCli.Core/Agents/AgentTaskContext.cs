namespace CSharpAiCli.Core;

public sealed record AgentTaskContext(
    string CurrentDirectory,
    string WorkspaceRoot,
    string WorkspaceStatus,
    string? CurrentDirectoryErrorCode,
    string? Instructions,
    IReadOnlyList<InstructionSource> InstructionSources,
    IReadOnlyList<string> InstructionWarnings,
    string? SessionName,
    bool HasTranscriptContext,
    AgentGitContextSummary Git,
    WorkflowReferenceResolution? References = null);
