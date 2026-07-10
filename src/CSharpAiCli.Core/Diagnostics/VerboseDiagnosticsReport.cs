using System.Globalization;
using System.Text.RegularExpressions;

namespace CSharpAiCli.Core;

public sealed record VerboseDiagnosticsReport(IReadOnlyList<string> Lines)
{
    private const string SecretKeyNamePattern =
        @"(?:[A-Za-z0-9]+[_-]+)*(?:api[_-]?key|access[_-]?token|refresh[_-]?token|token|client[_-]?secret|secret[_-]?access[_-]?key|password|secret)" +
        "|apiKey|accessToken|refreshToken|clientSecret|awsSecretAccessKey";

    private static readonly Regex KeyValueSecretPattern = new(
        $$"""\b(?:{{SecretKeyNamePattern}})\b(\s*[:=]\s*)(?:"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|Bearer\s+[A-Za-z0-9._~+/=-]+|[^\s,;]+)""",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex BearerTokenPattern = new(
        @"\bBearer\s+[A-Za-z0-9._~+/=-]+",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex OpenAiKeyPattern = new(
        @"\bsk-[A-Za-z0-9._-]+",
        RegexOptions.CultureInvariant);
    private static readonly Regex GitHubTokenPattern = new(
        @"\bgh[pousr]_[A-Za-z0-9_]+",
        RegexOptions.CultureInvariant);

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
        string withoutPipes = value.Replace("|", "/", StringComparison.Ordinal);
        string singleLine = string.Join(
            " ",
            withoutPipes.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return RedactSecrets(singleLine);
    }

    private static string RedactSecrets(string value)
    {
        string redacted = RedactKeyValueSecrets(value);
        redacted = BearerTokenPattern.Replace(redacted, "Bearer [redacted]");
        redacted = OpenAiKeyPattern.Replace(redacted, "[redacted]");
        redacted = GitHubTokenPattern.Replace(redacted, "[redacted]");
        return redacted;
    }

    private static string RedactKeyValueSecrets(string value)
    {
        return KeyValueSecretPattern.Replace(value, match =>
        {
            Group separator = match.Groups[1];
            if (!separator.Success)
            {
                return match.Value;
            }

            int prefixLength = separator.Index - match.Index;
            return match.Value[..prefixLength] + separator.Value + "[redacted]";
        });
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
