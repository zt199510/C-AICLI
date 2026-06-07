namespace CSharpAiCli.Core;

public sealed record PatchOperation(
    string Path,
    string Find,
    string Replace);
