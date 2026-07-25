using CSharpAiCli.Core;

namespace CSharpAiCli.Cli;

internal static class CliToolFactory
{
    public static ToolRegistry CreateRegistry(
        CliEnvironmentSnapshot snapshot,
        IApprovalPolicy approvalPolicy,
        ToolExecutionBoundary? boundary = null) =>
        BuiltInToolRegistryFactory.Create(snapshot, approvalPolicy, boundary);
}
