namespace CSharpAiCli.AgentFramework;

public static class AgentFrameworkAdapterInfo
{
    public const string BackendName = "maf";
    public const string Status = "experimental-stub";

    public static AgentFrameworkCapabilityReport CreateReport()
    {
        return new AgentFrameworkCapabilityReport(
            BackendName: BackendName,
            Status: Status,
            IsAvailable: false,
            SafeMessage: "Microsoft Agent Framework adapter is scaffolded but no framework package is enabled.");
    }
}
