using System.Diagnostics;
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
    public void Load_reads_and_trims_agents_instruction_file()
    {
        string root = CreateTempDirectory();

        try
        {
            string instructionPath = Path.Combine(root, "AGENTS.md");
            File.WriteAllText(instructionPath, $"{Environment.NewLine}Use project conventions.{Environment.NewLine}");
            WorkspaceContext workspace = WorkspaceContext.Detect(root, root);
            WorkspaceInstructionLoader loader = new();

            InstructionLoadResult result = loader.Load(workspace);

            Assert.True(result.HasInstructions);
            Assert.Equal("Use project conventions.", result.Instructions);
            Assert.Equal(instructionPath, result.SourcePath);
            Assert.Empty(result.Warnings);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_prefers_agents_instruction_file_over_aicli_instruction_file()
    {
        string root = CreateTempDirectory();

        try
        {
            string agentsPath = Path.Combine(root, "AGENTS.md");
            string aicliPath = Path.Combine(root, "AICLI.md");
            File.WriteAllText(agentsPath, "Follow AGENTS.");
            File.WriteAllText(aicliPath, "Follow AICLI.");
            WorkspaceContext workspace = WorkspaceContext.Detect(root, root);
            WorkspaceInstructionLoader loader = new();

            InstructionLoadResult result = loader.Load(workspace);

            Assert.True(result.HasInstructions);
            Assert.Equal("Follow AGENTS.", result.Instructions);
            Assert.Equal(agentsPath, result.SourcePath);
            Assert.Empty(result.Warnings);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_for_target_directory_combines_instruction_files_from_root_to_leaf()
    {
        string root = CreateTempDirectory();

        try
        {
            string sourceDirectory = Path.Combine(root, "src");
            string featureDirectory = Path.Combine(sourceDirectory, "feature");
            Directory.CreateDirectory(featureDirectory);

            File.WriteAllText(Path.Combine(root, "AGENTS.md"), "Root instructions.");
            File.WriteAllText(Path.Combine(sourceDirectory, "AICLI.md"), "Source instructions.");
            File.WriteAllText(Path.Combine(featureDirectory, "AGENTS.md"), "Feature instructions.");
            File.WriteAllText(Path.Combine(featureDirectory, "AICLI.md"), "Legacy feature instructions.");

            WorkspaceContext workspace = WorkspaceContext.Detect(root, root);
            WorkspaceInstructionLoader loader = new();

            InstructionLoadResult result = loader.Load(workspace, featureDirectory);

            Assert.True(result.HasInstructions);
            Assert.Equal(
                string.Join(
                    $"{Environment.NewLine}{Environment.NewLine}",
                    "Root instructions.",
                    "Source instructions.",
                    "Feature instructions."),
                result.Instructions);
            Assert.Empty(result.Warnings);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_for_existing_target_file_combines_through_containing_directory()
    {
        string root = CreateTempDirectory();

        try
        {
            string sourceDirectory = Path.Combine(root, "src");
            Directory.CreateDirectory(sourceDirectory);
            string targetFile = Path.Combine(sourceDirectory, "Program.cs");

            File.WriteAllText(Path.Combine(root, "AGENTS.md"), "Root instructions.");
            File.WriteAllText(Path.Combine(sourceDirectory, "AGENTS.md"), "Source instructions.");
            File.WriteAllText(targetFile, "Console.WriteLine();");

            WorkspaceContext workspace = WorkspaceContext.Detect(root, root);
            WorkspaceInstructionLoader loader = new();

            InstructionLoadResult result = loader.Load(workspace, targetFile);

            Assert.True(result.HasInstructions);
            Assert.Equal(
                string.Join(
                    $"{Environment.NewLine}{Environment.NewLine}",
                    "Root instructions.",
                    "Source instructions."),
                result.Instructions);
            Assert.Empty(result.Warnings);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_for_outside_target_path_returns_empty_and_records_warning()
    {
        string root = CreateTempDirectory();
        string outsideRoot = CreateTempDirectory();

        try
        {
            string outsideInstructionPath = Path.Combine(outsideRoot, "AGENTS.md");
            File.WriteAllText(Path.Combine(root, "AGENTS.md"), "Workspace instructions.");
            File.WriteAllText(outsideInstructionPath, "outside-secret");

            WorkspaceContext workspace = WorkspaceContext.Detect(root, root);
            WorkspaceInstructionLoader loader = new();

            InstructionLoadResult result = loader.Load(workspace, outsideRoot);

            Assert.False(result.HasInstructions);
            Assert.Null(result.Instructions);
            Assert.Null(result.SourcePath);
            string warning = Assert.Single(result.Warnings);
            Assert.Contains("outside the workspace", warning);
            Assert.Contains(outsideRoot, warning);
            Assert.DoesNotContain("outside-secret", warning, StringComparison.Ordinal);
            Assert.DoesNotContain(outsideInstructionPath, warning, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(outsideRoot, recursive: true);
        }
    }

    [Fact]
    public void Load_for_target_directory_link_outside_workspace_returns_empty_and_records_warning_when_available()
    {
        string root = CreateTempDirectory();
        string outsideRoot = CreateTempDirectory();
        string linkPath = Path.Combine(root, "linked");

        try
        {
            string outsideInstructionPath = Path.Combine(outsideRoot, "AGENTS.md");
            File.WriteAllText(Path.Combine(root, "AGENTS.md"), "Workspace instructions.");
            File.WriteAllText(outsideInstructionPath, "outside-secret");
            if (!TryCreateDirectoryLink(linkPath, outsideRoot))
            {
                return;
            }

            WorkspaceContext workspace = WorkspaceContext.Detect(root, root);
            WorkspaceInstructionLoader loader = new();

            InstructionLoadResult result = loader.Load(workspace, linkPath);

            Assert.False(result.HasInstructions);
            Assert.Null(result.Instructions);
            Assert.Null(result.SourcePath);
            string warning = Assert.Single(result.Warnings);
            Assert.Contains("outside the workspace", warning);
            Assert.DoesNotContain("outside-secret", warning, StringComparison.Ordinal);
            Assert.DoesNotContain(outsideInstructionPath, warning, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectoryLinkIfExists(linkPath);
            Directory.Delete(root, recursive: true);
            Directory.Delete(outsideRoot, recursive: true);
        }
    }

    [Fact]
    public void Load_skips_instruction_file_link_outside_workspace_when_available()
    {
        string root = CreateTempDirectory();
        string outsideRoot = CreateTempDirectory();

        try
        {
            string sourceDirectory = Path.Combine(root, "src");
            Directory.CreateDirectory(sourceDirectory);
            string outsideInstructionPath = Path.Combine(outsideRoot, "AGENTS.md");
            string linkedInstructionPath = Path.Combine(sourceDirectory, "AGENTS.md");
            File.WriteAllText(Path.Combine(root, "AGENTS.md"), "Root instructions.");
            File.WriteAllText(Path.Combine(sourceDirectory, "AICLI.md"), "Fallback instructions.");
            File.WriteAllText(outsideInstructionPath, "outside-secret");
            if (!TryCreateFileLink(linkedInstructionPath, outsideInstructionPath))
            {
                return;
            }

            WorkspaceContext workspace = WorkspaceContext.Detect(root, root);
            WorkspaceInstructionLoader loader = new();

            InstructionLoadResult result = loader.Load(workspace, sourceDirectory);

            Assert.True(result.HasInstructions);
            Assert.Equal("Root instructions.", result.Instructions);
            string warning = Assert.Single(result.Warnings);
            Assert.Contains("outside the workspace", warning);
            Assert.DoesNotContain("outside-secret", warning, StringComparison.Ordinal);
            Assert.DoesNotContain(outsideInstructionPath, warning, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(outsideRoot, recursive: true);
        }
    }

    [Fact]
    public void Load_without_target_path_keeps_root_only_behavior()
    {
        string root = CreateTempDirectory();

        try
        {
            string childDirectory = Path.Combine(root, "child");
            Directory.CreateDirectory(childDirectory);
            string rootInstructionPath = Path.Combine(root, "AGENTS.md");
            File.WriteAllText(rootInstructionPath, "Root only.");
            File.WriteAllText(Path.Combine(childDirectory, "AGENTS.md"), "Child instructions.");

            WorkspaceContext workspace = WorkspaceContext.Detect(root, root);
            WorkspaceInstructionLoader loader = new();

            InstructionLoadResult result = loader.Load(workspace);

            Assert.True(result.HasInstructions);
            Assert.Equal("Root only.", result.Instructions);
            Assert.Equal(rootInstructionPath, result.SourcePath);
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

    private static bool TryCreateDirectoryLink(string linkPath, string targetPath)
    {
        try
        {
            new DirectoryInfo(linkPath).CreateAsSymbolicLink(targetPath);
            return true;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or PlatformNotSupportedException
            or NotSupportedException)
        {
            return OperatingSystem.IsWindows() && TryCreateWindowsLink(linkPath, targetPath, isDirectory: true);
        }
    }

    private static bool TryCreateFileLink(string linkPath, string targetPath)
    {
        try
        {
            new FileInfo(linkPath).CreateAsSymbolicLink(targetPath);
            return true;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or PlatformNotSupportedException
            or NotSupportedException)
        {
            return OperatingSystem.IsWindows() && TryCreateWindowsLink(linkPath, targetPath, isDirectory: false);
        }
    }

    private static bool TryCreateWindowsLink(string linkPath, string targetPath, bool isDirectory)
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo("cmd.exe")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            }
        };

        process.StartInfo.ArgumentList.Add("/c");
        process.StartInfo.ArgumentList.Add("mklink");
        if (isDirectory)
        {
            process.StartInfo.ArgumentList.Add("/J");
        }

        process.StartInfo.ArgumentList.Add(linkPath);
        process.StartInfo.ArgumentList.Add(targetPath);

        try
        {
            process.Start();
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static void DeleteDirectoryLinkIfExists(string linkPath)
    {
        if (!Directory.Exists(linkPath))
        {
            return;
        }

        try
        {
            Directory.Delete(linkPath);
        }
        catch (IOException)
        {
        }
    }
}
