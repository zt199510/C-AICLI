using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class WorkspaceContextTests
{
    [Fact]
    public void Detect_uses_current_directory_when_workspace_path_is_empty()
    {
        string root = CreateTempDirectory();

        try
        {
            WorkspaceContext context = WorkspaceContext.Detect(
                workspacePath: null,
                currentDirectory: root);

            Assert.Equal(Path.GetFullPath(root), context.RootPath);
            Assert.Equal(Path.Combine(Path.GetFullPath(root), ".caicli", "config.json"), context.ConfigPath);
            Assert.Equal(WorkspaceStatus.Ready, context.Status);
            Assert.True(context.IsUsable);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_resolves_relative_workspace_path_against_current_directory()
    {
        string root = CreateTempDirectory();

        try
        {
            string workspace = Path.Combine(root, "project");
            Directory.CreateDirectory(workspace);

            WorkspaceContext context = WorkspaceContext.Detect(
                workspacePath: "project",
                currentDirectory: root);

            Assert.Equal(Path.GetFullPath(workspace), context.RootPath);
            Assert.Equal(WorkspaceStatus.Ready, context.Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_marks_missing_workspace_without_throwing()
    {
        string root = CreateTempDirectory();

        try
        {
            string missing = Path.Combine(root, "missing");

            WorkspaceContext context = WorkspaceContext.Detect(
                workspacePath: missing,
                currentDirectory: root);

            Assert.Equal(Path.GetFullPath(missing), context.RootPath);
            Assert.Equal(WorkspaceStatus.Missing, context.Status);
            Assert.False(context.IsUsable);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_marks_file_path_as_not_directory()
    {
        string root = CreateTempDirectory();

        try
        {
            string filePath = Path.Combine(root, "workspace.txt");
            File.WriteAllText(filePath, "not a directory");

            WorkspaceContext context = WorkspaceContext.Detect(
                workspacePath: filePath,
                currentDirectory: root);

            Assert.Equal(WorkspaceStatus.NotDirectory, context.Status);
            Assert.False(context.IsUsable);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "caicli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
