using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CSharpAiCli.Core;

public sealed record CliEnvironmentSnapshot(
    WorkspaceContext Workspace,
    EffectiveConfiguration Configuration,
    string DotnetSdkVersion,
    string DotnetRuntime,
    string TargetFramework,
    bool HasGlobalJson)
{
    public string CurrentDirectory => Workspace.RootPath;
    public string UserConfigPath => Configuration.UserConfigPath;
    public string WorkspaceConfigPath => Configuration.WorkspaceConfigPath;
    public WorkspaceStatus WorkspaceStatus => Workspace.Status;
    public bool HasOpenAiApiKey => Configuration.HasApiKey;
    public InstructionLoadResult Instructions { get; init; } = InstructionLoadResult.Empty();

    public static CliEnvironmentSnapshot Create(
        string? workspacePath = null,
        string? currentDirectory = null,
        string? userProfile = null,
        string? dotnetSdkVersion = null,
        string? dotnetRuntime = null,
        string? openAiApiKey = null,
        string? openAiModel = null,
        bool? hasGlobalJson = null)
    {
        currentDirectory ??= Environment.CurrentDirectory;
        userProfile ??= Environment.GetEnvironmentVariable("CAICLI_USER_PROFILE");
        dotnetSdkVersion ??= ReadDotnetSdkVersion();
        dotnetRuntime ??= RuntimeInformation.FrameworkDescription;

        WorkspaceContext workspace = WorkspaceContext.Detect(workspacePath, currentDirectory);
        EffectiveConfiguration configuration = ConfigLoader.Load(workspace, userProfile, openAiApiKey, openAiModel);
        InstructionLoadResult instructions = new WorkspaceInstructionLoader().Load(workspace);
        hasGlobalJson ??= File.Exists(Path.Combine(workspace.RootPath, "global.json"));

        return new CliEnvironmentSnapshot(
            Workspace: workspace,
            Configuration: configuration,
            DotnetSdkVersion: dotnetSdkVersion,
            DotnetRuntime: dotnetRuntime,
            TargetFramework: ProductInfo.TargetFramework,
            HasGlobalJson: hasGlobalJson.Value)
        {
            Instructions = instructions
        };
    }

    private static string ReadDotnetSdkVersion()
    {
        try
        {
            using Process process = new()
            {
                StartInfo = new ProcessStartInfo("dotnet", "--version")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();

            if (!process.WaitForExit(milliseconds: 2000))
            {
                TryKill(process);
                return "unavailable";
            }

            string version = process.StandardOutput.ReadToEnd().Trim();
            return string.IsNullOrWhiteSpace(version) ? "unavailable" : version;
        }
        catch
        {
            return "unavailable";
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }
}
