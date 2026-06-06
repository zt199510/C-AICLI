using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class WorkspaceInstructionLoaderTests
{
    [Fact]
    public void Load_returns_empty_when_instruction_file_is_missing()
    {
        string root = CreateTempDirectory();

        try
        {
            WorkspaceContext workspace = WorkspaceContext.Detect(root, root);
            WorkspaceInstructionLoader loader = new();

            InstructionLoadResult result = loader.Load(workspace);

            Assert.False(result.HasInstructions);
            Assert.Null(result.Instructions);
            Assert.Null(result.SourcePath);
            Assert.Empty(result.Warnings);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_reads_and_trims_workspace_instruction_file()
    {
        string root = CreateTempDirectory();

        try
        {
            string instructionPath = Path.Combine(root, "AICLI.md");
            File.WriteAllText(instructionPath, $"{Environment.NewLine}Be concise.{Environment.NewLine}");
            WorkspaceContext workspace = WorkspaceContext.Detect(root, root);
            WorkspaceInstructionLoader loader = new();

            InstructionLoadResult result = loader.Load(workspace);

            Assert.True(result.HasInstructions);
            Assert.Equal("Be concise.", result.Instructions);
            Assert.Equal(instructionPath, result.SourcePath);
            Assert.Empty(result.Warnings);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_ignores_empty_instruction_file()
    {
        string root = CreateTempDirectory();

        try
        {
            File.WriteAllText(Path.Combine(root, "AICLI.md"), "   ");
            WorkspaceContext workspace = WorkspaceContext.Detect(root, root);
            WorkspaceInstructionLoader loader = new();

            InstructionLoadResult result = loader.Load(workspace);

            Assert.False(result.HasInstructions);
            Assert.Empty(result.Warnings);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_ignores_large_instruction_file_and_records_warning_without_content()
    {
        string root = CreateTempDirectory();

        try
        {
            string instructionPath = Path.Combine(root, "AICLI.md");
            File.WriteAllText(instructionPath, "sk-large-secret");
            WorkspaceContext workspace = WorkspaceContext.Detect(root, root);
            WorkspaceInstructionLoader loader = new(maxInstructionBytes: 4);

            InstructionLoadResult result = loader.Load(workspace);

            Assert.False(result.HasInstructions);
            string warning = Assert.Single(result.Warnings);
            Assert.Contains("ignored instruction file", warning);
            Assert.Contains(instructionPath, warning);
            Assert.DoesNotContain("sk-large-secret", warning, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_returns_empty_for_missing_workspace()
    {
        string root = CreateTempDirectory();

        try
        {
            string missingWorkspace = Path.Combine(root, "missing");
            WorkspaceContext workspace = WorkspaceContext.Detect(missingWorkspace, root);
            WorkspaceInstructionLoader loader = new();

            InstructionLoadResult result = loader.Load(workspace);

            Assert.False(result.HasInstructions);
            Assert.Empty(result.Warnings);
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
