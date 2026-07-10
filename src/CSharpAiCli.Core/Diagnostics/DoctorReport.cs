using System.Globalization;
using System.Text.Json;

namespace CSharpAiCli.Core;

public sealed record DoctorReport(IReadOnlyList<string> Lines)
{
    private const string WorkspaceApplyPatchToolName = "workspace.apply_patch";
    private const string TrustedMcpRegistrySource = "user config";

    public static DoctorReport Create(CliEnvironmentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        EffectiveConfiguration configuration = snapshot.Configuration;
        string sdkLock = snapshot.HasGlobalJson ? "global.json found" : "not locked";
        string apiKeyStatus = configuration.HasApiKey
            ? $"present ({configuration.ApiKeySource})"
            : $"missing ({configuration.ApiKeySource})";

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
            $"model: {configuration.Model} ({configuration.ModelSource})",
            $"base URL: {configuration.BaseUrl} ({configuration.BaseUrlSource})",
            $"api key: {apiKeyStatus}",
            $"agent backend: {configuration.AgentBackend} ({configuration.AgentBackendSource})",
            $"approval mode: {FormatApprovalMode(configuration.ApprovalMode)} ({configuration.ApprovalModeSource})",
            $"agent backend status: {FormatAgentBackendStatus(configuration.AgentBackend)}"
        ];

        AddShellPolicy(lines, configuration.ShellPolicy);
        AddPatchPolicy(lines, configuration.DisabledTools);
        AddMcpExecutionPolicy(lines, configuration);
        AddInstructionSources(lines, snapshot.Instructions.Sources);

        foreach (string warning in configuration.Warnings)
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

    private static string FormatAgentBackendStatus(string backend)
    {
        return backend switch
        {
            "direct" => "available",
            "framework" => "unavailable: Microsoft Agent Framework adapter is an experimental stub and no framework package is enabled.",
            _ => "unknown"
        };
    }

    private static void AddShellPolicy(List<string> lines, ShellPolicyConfiguration policy)
    {
        lines.Add($"shell policy allowed commands configured: {FormatBoolean(policy.AllowedCommandsConfigured)} ({policy.AllowedCommandsSource})");
        lines.Add($"shell policy allowed commands: {FormatShellPolicyCommands(policy.AllowedCommands)}");
        lines.Add($"shell policy denied commands: {FormatShellPolicyCommands(policy.DeniedCommands)}");
        lines.Add($"shell policy max timeout milliseconds: {FormatShellPolicyMaxTimeout(policy.MaxTimeoutMilliseconds)} ({policy.MaxTimeoutMillisecondsSource})");
        lines.Add("shell policy dangerous command detector: enabled");
    }

    private static void AddPatchPolicy(List<string> lines, IReadOnlySet<string> disabledTools)
    {
        string toolStatus = disabledTools.Contains(WorkspaceApplyPatchToolName) ? "disabled" : "enabled";
        lines.Add($"patch policy tool status: {toolStatus}");
        lines.Add("patch policy approval: required for write operations");
        lines.Add("patch policy dry-run preview: enabled");
        lines.Add("patch policy dirty workspace reporting: enabled");
    }

    private static void AddMcpExecutionPolicy(List<string> lines, EffectiveConfiguration configuration)
    {
        McpConfiguration mcp = McpConfigurationLoader.Load(configuration);
        int serverCount = mcp.Servers.Count;
        int enabledServerCount = mcp.Servers.Count(server => server.Enabled);
        int stdioServerCount = mcp.Servers.Count(IsStdioServer);
        int enabledStdioServerCount = mcp.Servers.Count(server => server.Enabled && IsStdioServer(server));
        int trustedStdioServerCount = mcp.Servers.Count(IsTrustedStdioServer);

        lines.Add("mcp execution policy startup risk check: enabled for stdio commands");
        lines.Add($"mcp execution policy servers: {serverCount.ToString(CultureInfo.InvariantCulture)} configured, {enabledServerCount.ToString(CultureInfo.InvariantCulture)} enabled");
        lines.Add($"mcp execution policy stdio servers: {stdioServerCount.ToString(CultureInfo.InvariantCulture)} configured, {enabledStdioServerCount.ToString(CultureInfo.InvariantCulture)} enabled");
        lines.Add($"mcp execution policy trusted stdio servers: {trustedStdioServerCount.ToString(CultureInfo.InvariantCulture)} user-config enabled");
        lines.Add("mcp execution policy registry source: user config only");
    }

    private static bool IsStdioServer(McpServerDefinition server)
    {
        return string.Equals(server.Transport, "stdio", StringComparison.Ordinal);
    }

    private static bool IsTrustedStdioServer(McpServerDefinition server)
    {
        return server.Enabled &&
            string.Equals(server.Status, "configured", StringComparison.Ordinal) &&
            IsStdioServer(server) &&
            string.Equals(server.Source, TrustedMcpRegistrySource, StringComparison.Ordinal);
    }

    private static string FormatShellPolicyCommands(IReadOnlyList<string> commands)
    {
        return JsonSerializer.Serialize(commands);
    }

    private static string FormatShellPolicyMaxTimeout(int? maxTimeoutMilliseconds)
    {
        return maxTimeoutMilliseconds.HasValue
            ? maxTimeoutMilliseconds.Value.ToString(CultureInfo.InvariantCulture)
            : "none";
    }

    private static string FormatBoolean(bool value)
    {
        return value ? "true" : "false";
    }

    private static void AddInstructionSources(List<string> lines, IReadOnlyList<InstructionSource> sources)
    {
        if (sources.Count == 0)
        {
            lines.Add("instruction sources: none");
            return;
        }

        foreach (InstructionSource source in sources)
        {
            lines.Add($"instruction source: {source.Order}: {source.SourcePath}");
        }
    }
}
