namespace CSharpAiCli.Core;

public sealed record AgentVerificationCommand(
    string Command,
    string Source,
    string? ProfileName = null);
