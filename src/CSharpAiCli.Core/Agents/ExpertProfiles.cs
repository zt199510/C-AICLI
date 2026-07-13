namespace CSharpAiCli.Core;

public sealed record ExpertProfile(
    string Name,
    string DisplayName,
    bool IsReadOnly,
    string ToolBoundary,
    string PromptGuidance,
    string ReportFocus)
{
    public AgentTaskExpertReport ToReportMetadata()
    {
        return new AgentTaskExpertReport(
            Name,
            DisplayName,
            IsReadOnly ? "read-only" : "write-capable",
            ToolBoundary,
            ReportFocus);
    }
}

public static class ExpertProfileCatalog
{
    private static readonly IReadOnlyDictionary<string, ExpertProfile> Profiles =
        new Dictionary<string, ExpertProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["bugfix"] = new ExpertProfile(
                "bugfix",
                "Bugfix",
                IsReadOnly: false,
                ToolBoundary: "default exec tools; approval, workspace guard, shell policy, and disabled tools still apply",
                PromptGuidance: "Focus on reproducing the failure, making the smallest corrective change, and verifying the fix.",
                ReportFocus: "root cause, changed files, verification, and remaining risks"),
            ["reviewer"] = new ExpertProfile(
                "reviewer",
                "Reviewer",
                IsReadOnly: true,
                ToolBoundary: "read-only tools only; patch, shell, and MCP tools are disabled",
                PromptGuidance: "Review the requested code or workspace context. Do not write files, run shell commands, or call MCP tools.",
                ReportFocus: "findings, assumptions, changed files observed, and remaining risks"),
            ["tester"] = new ExpertProfile(
                "tester",
                "Tester",
                IsReadOnly: false,
                ToolBoundary: "default exec tools; approval, workspace guard, shell policy, and disabled tools still apply",
                PromptGuidance: "Focus on test coverage, likely failure modes, and verification commands. Add or adjust tests only when needed.",
                ReportFocus: "test scope, commands, verification status, and uncovered risks"),
            ["security"] = new ExpertProfile(
                "security",
                "Security",
                IsReadOnly: true,
                ToolBoundary: "read-only tools only; patch, shell, and MCP tools are disabled",
                PromptGuidance: "Audit for security risks, secret exposure, dangerous operations, and assumptions. Do not write files, run shell commands, or call MCP tools.",
                ReportFocus: "risks, secrets, dangerous operations, assumptions, and remaining risks"),
            ["refactor"] = new ExpertProfile(
                "refactor",
                "Refactor",
                IsReadOnly: false,
                ToolBoundary: "default exec tools; approval, workspace guard, shell policy, and disabled tools still apply",
                PromptGuidance: "Improve structure while preserving behavior. Prefer focused changes and explicit verification.",
                ReportFocus: "behavior preservation, changed files, verification, and remaining risks")
        };

    public static IReadOnlyList<string> Names => Profiles.Keys.Order(StringComparer.Ordinal).ToArray();

    public static bool TryGet(string? name, out ExpertProfile? profile)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            profile = null;
            return true;
        }

        if (Profiles.TryGetValue(name, out ExpertProfile? found))
        {
            profile = found;
            return true;
        }

        profile = null;
        return false;
    }

    public static ExpertProfile? GetOrNull(string? name)
    {
        return TryGet(name, out ExpertProfile? profile)
            ? profile
            : null;
    }
}

public static class ExpertProfilePromptFormatter
{
    public static string FormatWithCurrentPrompt(ExpertProfile? expert, string prompt)
    {
        if (expert is null)
        {
            return prompt;
        }

        return string.Join(
            Environment.NewLine,
            "Expert profile:",
            "- Name: " + expert.Name,
            "- Tool boundary: " + expert.ToolBoundary,
            "- Report focus: " + expert.ReportFocus,
            "- Guidance: " + expert.PromptGuidance,
            string.Empty,
            "Current task:",
            prompt);
    }
}

