using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ToolRiskMetadataTests
{
    [Fact]
    public void Built_in_local_tools_define_static_risk_levels()
    {
        WorkspaceGuard guard = new();
        ITool[] tools =
        [
            new WorkspaceFileReadTool(guard),
            new WorkspaceSearchTool(guard),
            new GitStatusTool(guard),
            new GitDiffTool(guard),
            CreatePatchTool(),
            CreateShellTool()
        ];

        IReadOnlyDictionary<string, ToolRiskLevel> risks = tools.ToDictionary(
            tool => tool.Definition.Name,
            tool => tool.Definition.RiskLevel);

        Assert.Equal(ToolRiskLevel.Read, risks["workspace.read_text"]);
        Assert.Equal(ToolRiskLevel.Read, risks["workspace.search_text"]);
        Assert.Equal(ToolRiskLevel.Read, risks["git.status"]);
        Assert.Equal(ToolRiskLevel.Read, risks["git.diff"]);
        Assert.Equal(ToolRiskLevel.Write, risks["workspace.apply_patch"]);
        Assert.Equal(ToolRiskLevel.Shell, risks["workspace.run_shell"]);
    }

    [Fact]
    public void Mcp_external_tools_default_to_conservative_shell_risk_when_specific_risk_is_unknown()
    {
        McpServerDefinition server = new(
            "active",
            Enabled: true,
            Status: "configured",
            TransportSummary: "stdio command: mcp-active",
            Source: "workspace config");
        McpExternalTool tool = new(server, new FakeMcpToolInvoker());

        Assert.Equal(ToolRiskLevel.Shell, tool.Definition.RiskLevel);
    }

    private static WorkspacePatchTool CreatePatchTool()
    {
        return new WorkspacePatchTool(
            new SingleFilePatchApplier(
                new WorkspaceGuard(),
                new StaticDirtyWorkspaceDetector(new DirtyWorkspaceStatus(false, "clean"))),
            new AlwaysApproveApprovalPolicy());
    }

    private static WorkspaceShellTool CreateShellTool()
    {
        return new WorkspaceShellTool(
            new RestrictedShellRunner(new WorkspaceGuard()),
            new AlwaysApproveApprovalPolicy());
    }

    private sealed class StaticDirtyWorkspaceDetector(DirtyWorkspaceStatus status) : IDirtyWorkspaceDetector
    {
        public DirtyWorkspaceStatus Detect(WorkspaceContext workspace) => status;
    }

    private sealed class FakeMcpToolInvoker : IMcpToolInvoker
    {
        public ToolExecutionResult Invoke(McpToolRequest request, CancellationToken cancellationToken = default)
        {
            return ToolExecutionResult.Success("{}");
        }
    }
}
