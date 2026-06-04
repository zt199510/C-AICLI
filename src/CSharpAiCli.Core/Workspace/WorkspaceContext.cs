namespace CSharpAiCli.Core;

public sealed record WorkspaceContext(
    string RootPath,
    string ConfigPath,
    WorkspaceStatus Status)
{
    public bool IsUsable => Status == WorkspaceStatus.Ready;

    public static WorkspaceContext Detect(string? workspacePath = null, string? currentDirectory = null)
    {
        currentDirectory ??= Environment.CurrentDirectory;

        string requestedPath = string.IsNullOrWhiteSpace(workspacePath)
            ? currentDirectory
            : workspacePath;

        string rootPath = Path.GetFullPath(requestedPath, currentDirectory);
        WorkspaceStatus status = Directory.Exists(rootPath)
            ? WorkspaceStatus.Ready
            : File.Exists(rootPath)
                ? WorkspaceStatus.NotDirectory
                : WorkspaceStatus.Missing;

        return new WorkspaceContext(
            RootPath: rootPath,
            ConfigPath: Path.Combine(rootPath, ".caicli", "config.json"),
            Status: status);
    }
}
