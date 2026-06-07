namespace CSharpAiCli.Core;

public sealed record AgentToolCallResult(
    AgentToolCallRequest Request,
    ToolExecutionResult Result);
