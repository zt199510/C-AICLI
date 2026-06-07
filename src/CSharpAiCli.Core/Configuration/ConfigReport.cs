namespace CSharpAiCli.Core;

public sealed record ConfigReport(IReadOnlyList<string> Lines)
{
    public static ConfigReport Create(CliEnvironmentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        EffectiveConfiguration configuration = snapshot.Configuration;
        string apiKeyStatus = configuration.HasApiKey ? "present" : "missing";
        string loadedConfigPaths = configuration.LoadedConfigPaths.Count == 0
            ? "none"
            : string.Join("; ", configuration.LoadedConfigPaths);

        List<string> lines =
        [
            $"{ProductInfo.DisplayName} effective configuration",
            $"workspace: {configuration.WorkspaceRoot}",
            $"workspaceStatus: {FormatWorkspaceStatus(snapshot.WorkspaceStatus)}",
            $"userConfigPath: {configuration.UserConfigPath}",
            $"workspaceConfigPath: {configuration.WorkspaceConfigPath}",
            $"logDirectory: {LogPathResolver.ResolveLogDirectory(snapshot)}",
            $"model: {configuration.Model}",
            $"modelSource: {configuration.ModelSource}",
            $"agentBackend: {configuration.AgentBackend}",
            $"agentBackendSource: {configuration.AgentBackendSource}",
            $"disabledTools: {FormatDisabledTools(configuration.DisabledTools)}",
            $"apiKey: {apiKeyStatus}",
            $"apiKeySource: {configuration.ApiKeySource}",
            $"loadedConfigPaths: {loadedConfigPaths}"
        ];

        foreach (string warning in configuration.Warnings)
        {
            lines.Add($"configWarning: {warning}");
        }

        foreach (string warning in snapshot.Instructions.Warnings)
        {
            lines.Add($"instructionWarning: {warning}");
        }

        return new ConfigReport(lines);
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

    private static string FormatDisabledTools(IReadOnlySet<string> disabledTools)
    {
        return disabledTools.Count == 0
            ? "none"
            : string.Join(", ", disabledTools.Order(StringComparer.Ordinal));
    }
}
