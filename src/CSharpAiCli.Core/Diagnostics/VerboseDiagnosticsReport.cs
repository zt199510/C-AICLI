using System.Globalization;
using System.Text;

namespace CSharpAiCli.Core;

public sealed record VerboseDiagnosticsReport(IReadOnlyList<string> Lines)
{
    public static VerboseDiagnosticsReport Create(
        string commandName,
        CliEnvironmentSnapshot snapshot,
        DiagnosticContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(context);

        EffectiveConfiguration configuration = snapshot.Configuration;
        string apiKeyStatus = configuration.HasApiKey ? "present" : "missing";

        List<string> lines =
        [
            $"{ProductInfo.DisplayName} verbose diagnostics",
            $"commandName: {Sanitize(commandName)}",
            $"commandId: {Sanitize(context.CommandId)}",
            $"sessionId: {Sanitize(context.SessionId)}",
            $"timestampUtc: {context.TimestampUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)}",
            $"workspace: {Sanitize(context.Workspace)}",
            $"workspaceStatus: {FormatWorkspaceStatus(snapshot.WorkspaceStatus)}",
            $"logDirectory: {Sanitize(LogPathResolver.ResolveLogDirectory(snapshot))}",
            $"userConfigPath: {Sanitize(snapshot.UserConfigPath)}",
            $"workspaceConfigPath: {Sanitize(snapshot.WorkspaceConfigPath)}",
            $"model: {Sanitize(configuration.Model)}",
            $"modelSource: {Sanitize(configuration.ModelSource)}",
            $"baseUrl: {Sanitize(configuration.BaseUrl)}",
            $"baseUrlSource: {Sanitize(configuration.BaseUrlSource)}",
            $"agentBackend: {Sanitize(configuration.AgentBackend)}",
            $"agentBackendSource: {Sanitize(configuration.AgentBackendSource)}",
            $"agentBackendStatus: {Sanitize(FormatAgentBackendStatus(configuration.AgentBackend))}",
            $"apiKey: {apiKeyStatus}",
            $"apiKeySource: {Sanitize(configuration.ApiKeySource)}",
            $"configWarningCount: {configuration.Warnings.Count.ToString(CultureInfo.InvariantCulture)}",
            $"instructionWarningCount: {snapshot.Instructions.Warnings.Count.ToString(CultureInfo.InvariantCulture)}"
        ];

        return new VerboseDiagnosticsReport(lines);
    }

    public string ToDisplayText()
    {
        return string.Join(Environment.NewLine, Lines);
    }

    private static string Sanitize(string value)
    {
        StringBuilder builder = new(value.Length);
        foreach (char character in value)
        {
            builder.Append(character switch
            {
                '|' => '/',
                _ when char.IsControl(character) => ' ',
                _ => character
            });
        }

        string singleLine = string.Join(
            " ",
            builder.ToString().Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return DiagnosticSecretRedactor.Redact(singleLine);
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
