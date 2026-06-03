namespace CSharpAiCli.Core;

public sealed record DoctorReport(IReadOnlyList<string> Lines)
{
    public static DoctorReport Create(CliEnvironmentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        string sdkLock = snapshot.HasGlobalJson ? "global.json found" : "not locked";
        string apiKeyStatus = snapshot.Configuration.HasApiKey
            ? $"present ({snapshot.Configuration.ApiKeySource})"
            : "missing";

        List<string> lines =
        [
            $"{ProductInfo.DisplayName} doctor",
            $"command: {ProductInfo.CommandName}",
            $"target framework: {snapshot.TargetFramework}",
            $"dotnet SDK: {snapshot.DotnetSdkVersion}",
            $"dotnet runtime: {snapshot.DotnetRuntime}",
            $"sdk lock: {sdkLock}",
            $"workspace: {snapshot.CurrentDirectory}",
            $"workspace status: {FormatWorkspaceStatus(snapshot.WorkspaceStatus)}",
            $"user config: {snapshot.UserConfigPath}",
            $"workspace config: {snapshot.WorkspaceConfigPath}",
            $"api key: {apiKeyStatus}"
        ];

        foreach (string warning in snapshot.Configuration.Warnings)
        {
            lines.Add($"config warning: {warning}");
        }

        return new DoctorReport(lines);
    }

    public string ToDisplayText()
    {
        return string.Join(Environment.NewLine, Lines);
    }

    private static string FormatWorkspaceStatus(WorkspaceStatus status)
    {
        return status switch
        {
            WorkspaceStatus.Ready => "ready",
            WorkspaceStatus.Missing => "missing",
            WorkspaceStatus.NotDirectory => "not directory",
            _ => "unknown"
        };
    }
}
