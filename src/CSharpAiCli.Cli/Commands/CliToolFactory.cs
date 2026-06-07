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
            approvalPolicy));
        RegisterIfEnabled(registry, snapshot, new GitStatusTool(workspaceGuard));
        RegisterIfEnabled(registry, snapshot, new GitDiffTool(workspaceGuard));

        new McpToolBridge(new UnavailableMcpToolInvoker()).RegisterTools(
            registry,
            McpConfigurationLoader.Load(snapshot.Configuration));

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
