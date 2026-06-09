namespace CSharpAiCli.Core;

public sealed record DoctorReport(IReadOnlyList<string> Lines)
{
    public static DoctorReport Create(CliEnvironmentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        string sdkLock = snapshot.HasGlobalJson ? "global.json found" : "not locked";
        string apiKeyStatus = snapshot.Configuration.HasApiKey
            ? $"present ({snapshot.Configuration.ApiKeySource})"
            : $"missing ({snapshot.Configuration.ApiKeySource})";

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
            $"log directory: {LogPathResolver.ResolveLogDirectory(snapshot)}",
            $"model: {snapshot.Configuration.Model} ({snapshot.Configuration.ModelSource})",
            $"base URL: {snapshot.Configuration.BaseUrl} ({snapshot.Configuration.BaseUrlSource})",
            $"api key: {apiKeyStatus}",
            $"agent backend: {snapshot.Configuration.AgentBackend} ({snapshot.Configuration.AgentBackendSource})",
            $"agent backend status: {FormatAgentBackendStatus(snapshot.Configuration.AgentBackend)}"
        ];

        foreach (string warning in snapshot.Configuration.Warnings)
        {
            lines.Add($"config warning: {warning}");
        }

        foreach (string warning in snapshot.Instructions.Warnings)
        {
            lines.Add($"instruction warning: {warning}");
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

    private static string FormatAgentBackendStatus(string backend)
    {
        return backend switch
        {
            "direct" => "available",
            "framework" => "unavailable: Microsoft Agent Framework adapter is an experimental stub and no framework package is enabled.",
            _ => "unknown"
        };
    }
}
