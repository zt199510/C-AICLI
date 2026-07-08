namespace CSharpAiCli.Core;

public sealed record StatusReport(IReadOnlyList<string> Lines)
{
    public static StatusReport Create(CliEnvironmentSnapshot snapshot, ToolExecutionResult gitStatus)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(gitStatus);

        EffectiveConfiguration configuration = snapshot.Configuration;
        string apiKeyStatus = configuration.HasApiKey ? "present" : "missing";

        List<string> lines =
        [
            $"{ProductInfo.DisplayName} status",
            $"workspace: {snapshot.CurrentDirectory}",
            $"workspaceStatus: {FormatWorkspaceStatus(snapshot.WorkspaceStatus)}"
        ];

        AddGitStatus(lines, gitStatus);

        lines.Add($"configurationStatus: {FormatConfigurationStatus(configuration)}");
        lines.Add($"model: {configuration.Model} ({configuration.ModelSource})");
        lines.Add($"baseUrl: {configuration.BaseUrl} ({configuration.BaseUrlSource})");
        lines.Add($"apiKey: {apiKeyStatus} ({configuration.ApiKeySource})");
        lines.Add($"agentBackend: {configuration.AgentBackend} ({configuration.AgentBackendSource})");
        lines.Add($"approvalMode: {FormatApprovalMode(configuration.ApprovalMode)} ({configuration.ApprovalModeSource})");
        lines.Add($"disabledTools: {FormatDisabledTools(configuration.DisabledTools)}");

        foreach (string warning in configuration.Warnings)
        {
            lines.Add($"configWarning: {warning}");
        }

        return new StatusReport(lines);
    }

    public string ToDisplayText()
    {
        return string.Join(Environment.NewLine, Lines);
    }

    private static void AddGitStatus(List<string> lines, ToolExecutionResult gitStatus)
    {
        if (!gitStatus.Succeeded)
        {
            lines.Add($"gitStatus: {FormatGitFailure(gitStatus)}");
            return;
        }

        string[] gitLines = SplitLines(gitStatus.Summary);
        if (gitLines.Length == 0)
        {
            lines.Add("gitStatus: working tree clean");
            return;
        }

        if (gitLines.Length == 1 && string.Equals(gitLines[0], "working tree clean", StringComparison.Ordinal))
        {
            lines.Add("gitStatus: working tree clean");
            return;
        }

        lines.Add($"gitStatus: changed ({gitLines.Length})");
        foreach (string gitLine in gitLines)
        {
            lines.Add($"gitChange: {gitLine}");
        }
    }

    private static string[] SplitLines(string text)
    {
        return text
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r'))
            .ToArray();
    }

    private static string FormatConfigurationStatus(EffectiveConfiguration configuration)
    {
        bool modelConfigured = !string.IsNullOrWhiteSpace(configuration.Model) &&
            !string.Equals(configuration.Model, "not configured", StringComparison.OrdinalIgnoreCase);

        if (!modelConfigured || !configuration.HasApiKey)
        {
            return "incomplete";
        }

        return configuration.Warnings.Count == 0 ? "ready" : "warning";
    }

    private static string FormatGitFailure(ToolExecutionResult gitStatus)
    {
        return gitStatus.ErrorCode switch
        {
            "git-not-repository" => "not a git repository",
            "workspace-unavailable" => "unavailable (workspace unavailable)",
            "invalid-workspace-path" => "unavailable (invalid workspace path)",
            null or "" => "unavailable",
            _ => $"unavailable ({gitStatus.ErrorCode})"
        };
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
}
