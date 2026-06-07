namespace CSharpAiCli.Core;

public sealed record PatchPreview(
    PatchOperation Operation,
    string FullPath,
    string OriginalContentHash,
    int Replacements,
    string Summary,
    string Diff,
    DirtyWorkspaceStatus DirtyWorkspace);
