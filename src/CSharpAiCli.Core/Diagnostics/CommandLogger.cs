using System.Globalization;

namespace CSharpAiCli.Core;

public static class CommandLogger
{
    public static void Append(
        string commandName,
        CliEnvironmentSnapshot snapshot,
        DateTimeOffset? timestampUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);
        ArgumentNullException.ThrowIfNull(snapshot);

        DateTimeOffset timestamp = timestampUtc ?? DateTimeOffset.UtcNow;
        string logDirectory = LogPathResolver.ResolveLogDirectory(snapshot);
        Directory.CreateDirectory(logDirectory);

        string fileName = timestamp.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".log";
        string logPath = Path.Combine(logDirectory, fileName);
        string apiKeyStatus = snapshot.Configuration.HasApiKey ? "present" : "missing";
        string warnings = snapshot.Configuration.Warnings.Count == 0
            ? "none"
            : string.Join("; ", snapshot.Configuration.Warnings.Select(Sanitize));
        string instructionWarnings = snapshot.Instructions.Warnings.Count == 0
            ? "none"
            : string.Join("; ", snapshot.Instructions.Warnings.Select(Sanitize));

        string line = string.Join(" | ",
        [
            $"timestampUtc={timestamp.UtcDateTime:O}",
            $"command={Sanitize(commandName)}",
            $"workspace={Sanitize(snapshot.CurrentDirectory)}",
            $"workspaceStatus={FormatWorkspaceStatus(snapshot.WorkspaceStatus)}",
            $"model={Sanitize(snapshot.Configuration.Model)}",
            $"modelSource={Sanitize(snapshot.Configuration.ModelSource)}",
            $"baseUrl={Sanitize(snapshot.Configuration.BaseUrl)}",
            $"baseUrlSource={Sanitize(snapshot.Configuration.BaseUrlSource)}",
            $"agentBackend={Sanitize(snapshot.Configuration.AgentBackend)}",
            $"agentBackendSource={Sanitize(snapshot.Configuration.AgentBackendSource)}",
            $"agentBackendStatus={Sanitize(FormatAgentBackendStatus(snapshot.Configuration.AgentBackend))}",
            $"apiKey={apiKeyStatus}",
            $"apiKeySource={Sanitize(snapshot.Configuration.ApiKeySource)}",
            $"warnings={warnings}",
            $"instructionWarnings={instructionWarnings}"
        ]);

        File.AppendAllText(logPath, line + Environment.NewLine);
    }

    private static string Sanitize(string value)
    {
        string withoutPipes = value.Replace("|", "/", StringComparison.Ordinal);
        return string.Join(
            " ",
            withoutPipes.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
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
