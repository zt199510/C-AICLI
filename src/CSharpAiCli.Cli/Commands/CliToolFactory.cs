using CSharpAiCli.Core;

namespace CSharpAiCli.Cli;

internal static class CliToolFactory
{
    public static ToolRegistry CreateRegistry(
        CliEnvironmentSnapshot snapshot,
        IApprovalPolicy approvalPolicy,
        ToolExecutionBoundary? boundary = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(approvalPolicy);
        boundary ??= ToolExecutionBoundary.Default;

        WorkspaceGuard workspaceGuard = new();
        ToolRegistry registry = new();
        RegisterIfEnabled(registry, snapshot, boundary, new AgentPlanTool());
        RegisterIfEnabled(registry, snapshot, boundary, new WorkspaceFileReadTool(workspaceGuard));
        RegisterIfEnabled(registry, snapshot, boundary, new WorkspaceSearchTool(workspaceGuard));
        RegisterIfEnabled(registry, snapshot, boundary, new WorkspacePatchTool(
            new SingleFilePatchApplier(workspaceGuard, new GitDirtyWorkspaceDetector()),
            approvalPolicy));
        RegisterIfEnabled(registry, snapshot, boundary, new WorkspaceShellTool(
            new RestrictedShellRunner(workspaceGuard),
            approvalPolicy,
            snapshot.Configuration.ShellPolicy));
        RegisterIfEnabled(registry, snapshot, boundary, new GitStatusTool(workspaceGuard));
        RegisterIfEnabled(registry, snapshot, boundary, new GitDiffTool(workspaceGuard));

        if (!boundary.AllowMcpDiscovery)
        {
            return registry;
        }

        McpStdioClientSessionFactory mcpSessionFactory = new(new McpStdioTransport(
            workspaceGuard,
            snapshot.Configuration.ShellPolicy));
        McpToolBridge mcpToolBridge = new(
            new McpStdioToolDiscoverer(mcpSessionFactory),
            new McpStdioToolInvoker(mcpSessionFactory),
            approvalPolicy);
        foreach (ITool tool in mcpToolBridge.CreateTools(McpConfigurationLoader.Load(snapshot.Configuration), snapshot.Workspace))
        {
            RegisterIfEnabled(registry, snapshot, boundary, tool);
        }

        return registry;
    }

    private static void RegisterIfEnabled(
        ToolRegistry registry,
        CliEnvironmentSnapshot snapshot,
        ToolExecutionBoundary boundary,
        ITool tool)
    {
        if (snapshot.Configuration.DisabledTools.Contains(tool.Definition.Name))
        {
            return;
        }

        if (!boundary.AllowsTool(tool.Definition))
        {
            return;
        }

        registry.Register(tool);
    }
}
