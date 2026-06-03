using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CSharpAiCli.Core;

public sealed record CliEnvironmentSnapshot(
    string CurrentDirectory,
    string UserConfigPath,
    string WorkspaceConfigPath,
    string DotnetSdkVersion,
    string DotnetRuntime,
    string TargetFramework,
    bool HasGlobalJson,
    bool HasOpenAiApiKey)
{
    public static CliEnvironmentSnapshot Create(
        string? currentDirectory = null,
        string? userProfile = null,
        string? dotnetSdkVersion = null,
        string? dotnetRuntime = null,
        string? openAiApiKey = null,
        bool? hasGlobalJson = null)
    {
        currentDirectory ??= Environment.CurrentDirectory;
        userProfile ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        dotnetSdkVersion ??= ReadDotnetSdkVersion();
        dotnetRuntime ??= RuntimeInformation.FrameworkDescription;
        openAiApiKey ??= Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        hasGlobalJson ??= File.Exists(Path.Combine(currentDirectory, "global.json"));

        return new CliEnvironmentSnapshot(
            CurrentDirectory: currentDirectory,
            UserConfigPath: Path.Combine(userProfile, ".caicli", "config.json"),
            WorkspaceConfigPath: Path.Combine(currentDirectory, ".caicli", "config.json"),
            DotnetSdkVersion: dotnetSdkVersion,
            DotnetRuntime: dotnetRuntime,
            TargetFramework: ProductInfo.TargetFramework,
            HasGlobalJson: hasGlobalJson.Value,
            HasOpenAiApiKey: !string.IsNullOrWhiteSpace(openAiApiKey));
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
