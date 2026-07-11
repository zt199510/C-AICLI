using System.Text.Json;

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
            $"baseUrl: {configuration.BaseUrl}",
            $"baseUrlSource: {configuration.BaseUrlSource}",
            $"agentBackend: {configuration.AgentBackend}",
            $"agentBackendSource: {configuration.AgentBackendSource}",
            $"approvalMode: {FormatApprovalMode(configuration.ApprovalMode)}",
            $"approvalModeSource: {configuration.ApprovalModeSource}",
            $"agentRunMaxSteps: {configuration.AgentRunLimits.MaxSteps.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            $"agentRunMaxStepsSource: {configuration.AgentRunMaxStepsSource}",
            $"agentRunMaxToolCalls: {configuration.AgentRunLimits.MaxToolCalls.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            $"agentRunMaxToolCallsSource: {configuration.AgentRunMaxToolCallsSource}",
            $"agentRunMaxRetries: {configuration.AgentRunLimits.MaxRetries.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            $"agentRunMaxRetriesSource: {configuration.AgentRunMaxRetriesSource}",
            $"agentRunTimeoutSeconds: {FormatSeconds(configuration.AgentRunLimits.OverallTimeout)}",
            $"agentRunTimeoutSource: {configuration.AgentRunTimeoutSource}",
            $"shellPolicyAllowedCommandsConfigured: {FormatBoolean(configuration.ShellPolicy.AllowedCommandsConfigured)}",
            $"shellPolicyAllowedCommandsSource: {configuration.ShellPolicy.AllowedCommandsSource}",
            $"shellPolicyAllowedCommands: {FormatShellPolicyCommands(configuration.ShellPolicy.AllowedCommands)}",
            $"shellPolicyDeniedCommands: {FormatShellPolicyCommands(configuration.ShellPolicy.DeniedCommands)}",
            $"shellPolicyMaxTimeoutMilliseconds: {FormatShellPolicyMaxTimeout(configuration.ShellPolicy.MaxTimeoutMilliseconds)}",
            $"shellPolicyMaxTimeoutMillisecondsSource: {configuration.ShellPolicy.MaxTimeoutMillisecondsSource}",
            $"disabledTools: {FormatDisabledTools(configuration.DisabledTools)}",
            $"apiKey: {apiKeyStatus}",
            $"apiKeySource: {configuration.ApiKeySource}",
            $"loadedConfigPaths: {loadedConfigPaths}"
        ];

        AddInstructionSources(lines, snapshot.Instructions.Sources);

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

    private static string FormatApprovalMode(ApprovalMode approvalMode)
    {
        return approvalMode switch
        {
            ApprovalMode.Never => "never",
            ApprovalMode.OnRequest => "on-request",
            ApprovalMode.OnFailure => "on-failure",
            ApprovalMode.Always => "always",
            _ => "unknown"
        };
    }

    private static string FormatDisabledTools(IReadOnlySet<string> disabledTools)
    {
        return disabledTools.Count == 0
            ? "none"
            : string.Join(", ", disabledTools.Order(StringComparer.Ordinal));
    }

    private static string FormatShellPolicyCommands(IReadOnlyList<string> commands)
    {
        return JsonSerializer.Serialize(commands);
    }

    private static string FormatShellPolicyMaxTimeout(int? maxTimeoutMilliseconds)
    {
        return maxTimeoutMilliseconds.HasValue
            ? maxTimeoutMilliseconds.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "none";
    }

    private static string FormatSeconds(TimeSpan timeout)
    {
        return ((int)timeout.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string FormatBoolean(bool value)
    {
        return value ? "true" : "false";
    }

    private static void AddInstructionSources(List<string> lines, IReadOnlyList<InstructionSource> sources)
    {
        if (sources.Count == 0)
        {
            lines.Add("instructionSources: none");
            return;
        }

        foreach (InstructionSource source in sources)
        {
            lines.Add($"instructionSource: {source.Order}: {source.SourcePath}");
        }
    }
}
