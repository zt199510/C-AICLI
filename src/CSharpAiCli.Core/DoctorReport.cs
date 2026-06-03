namespace CSharpAiCli.Core;

public sealed record DoctorReport(IReadOnlyList<string> Lines)
{
    public static DoctorReport Create(CliEnvironmentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        string sdkLock = snapshot.HasGlobalJson ? "global.json found" : "not locked";
        string apiKeyStatus = snapshot.HasOpenAiApiKey ? "present" : "missing";

        return new DoctorReport(
        [
            $"{ProductInfo.DisplayName} doctor",
            $"command: {ProductInfo.CommandName}",
            $"target framework: {snapshot.TargetFramework}",
            $"dotnet SDK: {snapshot.DotnetSdkVersion}",
            $"dotnet runtime: {snapshot.DotnetRuntime}",
            $"sdk lock: {sdkLock}",
            $"workspace: {snapshot.CurrentDirectory}",
            $"user config: {snapshot.UserConfigPath}",
            $"workspace config: {snapshot.WorkspaceConfigPath}",
            $"api key: {apiKeyStatus}"
        ]);
    }

    public string ToDisplayText()
    {
        return string.Join(Environment.NewLine, Lines);
    }
}
