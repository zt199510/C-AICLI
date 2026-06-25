namespace CSharpAiCli.Core;

public enum ToolRiskLevel
{
    Read,
    Write,
    Shell,
    DangerousShell
}

public static class ToolRiskLevelExtensions
{
    public static string ToCanonicalName(this ToolRiskLevel riskLevel)
    {
        return riskLevel switch
        {
            ToolRiskLevel.Read => "read",
            ToolRiskLevel.Write => "write",
            ToolRiskLevel.Shell => "shell",
            ToolRiskLevel.DangerousShell => "dangerous-shell",
            _ => throw new ArgumentOutOfRangeException(nameof(riskLevel), riskLevel, "Unknown tool risk level.")
        };
    }
}
