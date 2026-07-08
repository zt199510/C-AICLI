using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class CliEnvironmentSnapshotTests
{
    [Fact]
    public void Create_builds_workspace_paths_config_paths_and_runtime_status()
    {
        string root = CreateTempDirectory();

        try
        {
            string userProfile = Path.Combine(root, "home");
            string workspaceRoot = Path.Combine(root, "workspace");
            Directory.CreateDirectory(userProfile);
            Directory.CreateDirectory(workspaceRoot);

            CliEnvironmentSnapshot snapshot = CliEnvironmentSnapshot.Create(
                workspacePath: workspaceRoot,
                currentDirectory: root,
                userProfile: userProfile,
                dotnetSdkVersion: "9.0.308",
                dotnetRuntime: ".NET 9.0.0",
                openAiApiKey: "",
                hasGlobalJson: false);

            Assert.Equal(Path.GetFullPath(workspaceRoot), snapshot.CurrentDirectory);
            Assert.Equal(Path.Combine(userProfile, ".caicli", "config.json"), snapshot.UserConfigPath);
            Assert.Equal(Path.Combine(Path.GetFullPath(workspaceRoot), ".caicli", "config.json"), snapshot.WorkspaceConfigPath);
            Assert.Equal(WorkspaceStatus.Ready, snapshot.WorkspaceStatus);
            Assert.False(snapshot.HasOpenAiApiKey);
            Assert.Equal("9.0.308", snapshot.DotnetSdkVersion);
            Assert.Equal(".NET 9.0.0", snapshot.DotnetRuntime);
            Assert.Equal("net9.0", snapshot.TargetFramework);
            Assert.False(snapshot.HasGlobalJson);
            Assert.False(snapshot.Instructions.HasInstructions);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Create_loads_effective_configuration_and_redacts_api_key_in_ToString()
    {
        string root = CreateTempDirectory();

        try
        {
            string userProfile = Path.Combine(root, "home");
            string workspaceRoot = Path.Combine(root, "workspace");
            Directory.CreateDirectory(userProfile);
            Directory.CreateDirectory(workspaceRoot);

            string workspaceConfigPath = Path.Combine(workspaceRoot, ".caicli", "config.json");
            Directory.CreateDirectory(Path.GetDirectoryName(workspaceConfigPath)!);
            File.WriteAllText(workspaceConfigPath, @"{
  ""model"": ""gpt-workspace"",
  ""apiKey"": ""sk-workspace-secret""
}");

            CliEnvironmentSnapshot snapshot = CliEnvironmentSnapshot.Create(
                workspacePath: workspaceRoot,
                currentDirectory: root,
                userProfile: userProfile,
                dotnetSdkVersion: "9.0.308",
                dotnetRuntime: ".NET 9.0.0",
                openAiApiKey: "sk-env-secret",
                openAiModel: "gpt-env",
                hasGlobalJson: true);

            Assert.Equal("gpt-env", snapshot.Configuration.Model);
            Assert.Equal("OPENAI_MODEL", snapshot.Configuration.ModelSource);
            Assert.True(snapshot.HasOpenAiApiKey);
            Assert.Equal("OPENAI_API_KEY", snapshot.Configuration.ApiKeySource);
            Assert.Equal("sk-env-secret", snapshot.Configuration.ApiKey!.Value);
            Assert.True(snapshot.HasGlobalJson);
            Assert.DoesNotContain("sk-env-secret", snapshot.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("sk-workspace-secret", snapshot.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Create_passes_openai_base_url_injection_to_effective_configuration()
    {
        string root = CreateTempDirectory();

        try
        {
            string userProfile = Path.Combine(root, "home");
            string workspaceRoot = Path.Combine(root, "workspace");
            Directory.CreateDirectory(userProfile);
            Directory.CreateDirectory(workspaceRoot);

            CliEnvironmentSnapshot snapshot = CliEnvironmentSnapshot.Create(
                workspacePath: workspaceRoot,
                currentDirectory: root,
                userProfile: userProfile,
                dotnetSdkVersion: "9.0.308",
                dotnetRuntime: ".NET 9.0.0",
                openAiApiKey: "",
                openAiBaseUrl: "https://env.example.test/v1",
                hasGlobalJson: false);

            Assert.Equal("https://env.example.test/v1", snapshot.Configuration.BaseUrl);
            Assert.Equal("OPENAI_BASE_URL", snapshot.Configuration.BaseUrlSource);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Create_loads_workspace_instructions_from_AICLI_md()
    {
        string root = CreateTempDirectory();

        try
        {
            string userProfile = Path.Combine(root, "home");
            string workspaceRoot = Path.Combine(root, "workspace");
            Directory.CreateDirectory(userProfile);
            Directory.CreateDirectory(workspaceRoot);
            File.WriteAllText(Path.Combine(workspaceRoot, "AICLI.md"), "Prefer short answers.");

            CliEnvironmentSnapshot snapshot = CliEnvironmentSnapshot.Create(
                workspacePath: workspaceRoot,
                currentDirectory: root,
                userProfile: userProfile,
                dotnetSdkVersion: "9.0.308",
                dotnetRuntime: ".NET 9.0.0",
                openAiApiKey: "",
                hasGlobalJson: false);

            Assert.True(snapshot.Instructions.HasInstructions);
            Assert.Equal("Prefer short answers.", snapshot.Instructions.Instructions);
            Assert.Equal(Path.Combine(workspaceRoot, "AICLI.md"), snapshot.Instructions.SourcePath);
            InstructionSource source = Assert.Single(snapshot.Instructions.Sources);
            Assert.Equal(Path.Combine(workspaceRoot, "AICLI.md"), source.SourcePath);
            Assert.Equal(0, source.Order);
            Assert.Empty(snapshot.Instructions.Warnings);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Create_loads_hierarchical_instructions_for_instruction_target_path()
    {
        string root = CreateTempDirectory();

        try
        {
            string userProfile = Path.Combine(root, "home");
            string workspaceRoot = Path.Combine(root, "workspace");
            string sourceDirectory = Path.Combine(workspaceRoot, "src");
            Directory.CreateDirectory(userProfile);
            Directory.CreateDirectory(sourceDirectory);

            string rootInstructionPath = Path.Combine(workspaceRoot, "AICLI.md");
            string sourceInstructionPath = Path.Combine(sourceDirectory, "AGENTS.md");
            string targetPath = Path.Combine(sourceDirectory, "Program.cs");
            File.WriteAllText(rootInstructionPath, "Root instructions.");
            File.WriteAllText(sourceInstructionPath, "Source instructions.");
            File.WriteAllText(targetPath, "Console.WriteLine();");

            CliEnvironmentSnapshot snapshot = CliEnvironmentSnapshot.Create(
                workspacePath: workspaceRoot,
                currentDirectory: root,
                userProfile: userProfile,
                dotnetSdkVersion: "9.0.308",
                dotnetRuntime: ".NET 9.0.0",
                openAiApiKey: "",
                hasGlobalJson: false,
                instructionTargetPath: targetPath);

            Assert.True(snapshot.Instructions.HasInstructions);
            Assert.Equal(
                string.Join(
                    $"{Environment.NewLine}{Environment.NewLine}",
                    "Root instructions.",
                    "Source instructions."),
                snapshot.Instructions.Instructions);
            Assert.Equal(rootInstructionPath, snapshot.Instructions.SourcePath);
            Assert.Collection(
                snapshot.Instructions.Sources,
                source =>
                {
                    Assert.Equal(rootInstructionPath, source.SourcePath);
                    Assert.Equal(0, source.Order);
                },
                source =>
                {
                    Assert.Equal(sourceInstructionPath, source.SourcePath);
                    Assert.Equal(1, source.Order);
                });
            Assert.Empty(snapshot.Instructions.Warnings);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Create_with_instruction_target_path_outside_workspace_carries_empty_safe_instruction_result()
    {
        string root = CreateTempDirectory();
        string outsideRoot = CreateTempDirectory();

        try
        {
            string userProfile = Path.Combine(root, "home");
            string workspaceRoot = Path.Combine(root, "workspace");
            Directory.CreateDirectory(userProfile);
            Directory.CreateDirectory(workspaceRoot);

            string outsideInstructionPath = Path.Combine(outsideRoot, "AGENTS.md");
            File.WriteAllText(Path.Combine(workspaceRoot, "AICLI.md"), "Workspace instructions.");
            File.WriteAllText(outsideInstructionPath, "outside-secret");

            CliEnvironmentSnapshot snapshot = CliEnvironmentSnapshot.Create(
                workspacePath: workspaceRoot,
                currentDirectory: root,
                userProfile: userProfile,
                dotnetSdkVersion: "9.0.308",
                dotnetRuntime: ".NET 9.0.0",
                openAiApiKey: "",
                hasGlobalJson: false,
                instructionTargetPath: outsideRoot);

            Assert.False(snapshot.Instructions.HasInstructions);
            Assert.Null(snapshot.Instructions.Instructions);
            Assert.Null(snapshot.Instructions.SourcePath);
            Assert.Empty(snapshot.Instructions.Sources);
            string warning = Assert.Single(snapshot.Instructions.Warnings);
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
    public void Create_reports_missing_workspace_and_skips_workspace_config_loading()
    {
        string root = CreateTempDirectory();

        try
        {
            string userProfile = Path.Combine(root, "home");
            string missingWorkspace = Path.Combine(root, "missing-workspace");
            Directory.CreateDirectory(userProfile);

            CliEnvironmentSnapshot snapshot = CliEnvironmentSnapshot.Create(
                workspacePath: missingWorkspace,
                currentDirectory: root,
                userProfile: userProfile,
                dotnetSdkVersion: "9.0.308",
                dotnetRuntime: ".NET 9.0.0",
                openAiApiKey: "",
                hasGlobalJson: false);

            Assert.Equal(Path.GetFullPath(missingWorkspace), snapshot.CurrentDirectory);
            Assert.Equal(WorkspaceStatus.Missing, snapshot.WorkspaceStatus);
            Assert.Equal("not configured", snapshot.Configuration.Model);
            Assert.Empty(snapshot.Configuration.LoadedConfigPaths);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Create_uses_caicli_user_profile_environment_override()
    {
        string root = CreateTempDirectory();
        string? previous = Environment.GetEnvironmentVariable("CAICLI_USER_PROFILE");

        try
        {
            string userProfile = Path.Combine(root, "portable-home");
            string workspaceRoot = Path.Combine(root, "workspace");
            Directory.CreateDirectory(userProfile);
            Directory.CreateDirectory(workspaceRoot);
            Environment.SetEnvironmentVariable("CAICLI_USER_PROFILE", userProfile);

            CliEnvironmentSnapshot snapshot = CliEnvironmentSnapshot.Create(
                workspacePath: workspaceRoot,
                currentDirectory: root,
                dotnetSdkVersion: "9.0.308",
                dotnetRuntime: ".NET 9.0.0",
                openAiApiKey: "",
                hasGlobalJson: false);

            Assert.Equal(Path.Combine(userProfile, ".caicli", "config.json"), snapshot.UserConfigPath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CAICLI_USER_PROFILE", previous);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Create_detects_global_json_from_effective_workspace_root()
    {
        string root = CreateTempDirectory();

        try
        {
            string userProfile = Path.Combine(root, "home");
            string workspaceRoot = Path.Combine(root, "workspace");
            Directory.CreateDirectory(userProfile);
            Directory.CreateDirectory(workspaceRoot);
            File.WriteAllText(Path.Combine(root, "global.json"), "{}");

            CliEnvironmentSnapshot currentDirectoryOnlySnapshot = CliEnvironmentSnapshot.Create(
                workspacePath: workspaceRoot,
                currentDirectory: root,
                userProfile: userProfile,
                dotnetSdkVersion: "9.0.308",
                dotnetRuntime: ".NET 9.0.0",
                openAiApiKey: "");

            Assert.False(currentDirectoryOnlySnapshot.HasGlobalJson);

            File.WriteAllText(Path.Combine(workspaceRoot, "global.json"), "{}");

            CliEnvironmentSnapshot workspaceSnapshot = CliEnvironmentSnapshot.Create(
                workspacePath: workspaceRoot,
                currentDirectory: root,
                userProfile: userProfile,
                dotnetSdkVersion: "9.0.308",
                dotnetRuntime: ".NET 9.0.0",
                openAiApiKey: "");

            Assert.True(workspaceSnapshot.HasGlobalJson);
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
