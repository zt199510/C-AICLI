namespace CSharpAiCli.AgentFramework;

public sealed record AgentFrameworkCapabilityReport(
    string BackendName,
    string Status,
    bool IsAvailable,
    string SafeMessage);
