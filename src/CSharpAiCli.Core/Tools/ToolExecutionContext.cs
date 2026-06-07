namespace CSharpAiCli.Core;

public sealed record ToolExecutionContext(
    string CallId,
    WorkspaceContext Workspace,
    string ArgumentsJson);
