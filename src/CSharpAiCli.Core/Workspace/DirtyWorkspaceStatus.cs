namespace CSharpAiCli.Core;

public sealed record DirtyWorkspaceStatus(
    bool IsDirty,
    string Summary);
