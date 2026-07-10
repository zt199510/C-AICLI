using CSharpAiCli.Core;

namespace CSharpAiCli.Cli;

internal static class CliToolFactory
{
    public static ToolRegistry CreateRegistry(
        CliEnvironmentSnapshot snapshot,
        IApprovalPolicy approvalPolicy)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(approvalPolicy);

        WorkspaceGuard workspaceGuard = new();
        ToolRegistry registry = new();
        RegisterIfEnabled(registry, snapshot, new WorkspaceFileReadTool(workspaceGuard));
        RegisterIfEnabled(registry, snapshot, new WorkspaceSearchTool(workspaceGuard));
        RegisterIfEnabled(registry, snapshot, new WorkspacePatchTool(
            new SingleFilePatchApplier(workspaceGuard, new GitDirtyWorkspaceDetector()),
            approvalPolicy));
        RegisterIfEnabled(registry, snapshot, new WorkspaceShellTool(
            new RestrictedShellRunner(workspaceGuard),
            approvalPolicy,
            snapshot.Configuration.ShellPolicy));
        RegisterIfEnabled(registry, snapshot, new GitStatusTool(workspaceGuard));
        RegisterIfEnabled(registry, snapshot, new GitDiffTool(workspaceGuard));

        McpStdioClientSessionFactory mcpSessionFactory = new(new McpStdioTransport(
            workspaceGuard,
            snapshot.Configuration.ShellPolicy));
        McpToolBridge mcpToolBridge = new(
            new McpStdioToolDiscoverer(mcpSessionFactory),
            new McpStdioToolInvoker(mcpSessionFactory),
            approvalPolicy);
        foreach (ITool tool in mcpToolBridge.CreateTools(McpConfigurationLoader.Load(snapshot.Configuration), snapshot.Workspace))
        {
            RegisterIfEnabled(registry, snapshot, tool);
        }

        return registry;
    }

    private static void RegisterIfEnabled(
        ToolRegistry registry,
        CliEnvironmentSnapshot snapshot,
        ITool tool)
    {
        if (snapshot.Configuration.DisabledTools.Contains(tool.Definition.Name))
        {
            return;
        }

        registry.Register(tool);
    }
}
