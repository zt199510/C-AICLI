namespace CSharpAiCli.Core;

internal static class SkillPackBuiltIns
{
    public static IReadOnlyList<SkillPackCatalogItem> Create()
    {
        return
        [
            new SkillPackCatalogItem(
                new SkillPackManifest
                {
                    Name = "test-fix",
                    Version = "0.1.0",
                    Description = "Find and fix failing .NET tests.",
                    AppliesTo = DotnetAppliesTo(),
                    Entry = Entry("exec", "tester", "markdown"),
                    References = References(["@folder:src", "@folder:tests"], maxFiles: 50),
                    Instructions =
                    [
                        "Prefer minimal changes.",
                        "Identify the failing test or failure mode before editing.",
                        "Run the configured verification command when available."
                    ],
                    ValidationCommand = "dotnet test",
                    ReportTemplate = "default",
                    Safety = Safety(allowWrites: true, allowShell: "verification-only")
                },
                SkillPackSource.BuiltIn),
            new SkillPackCatalogItem(
                new SkillPackManifest
                {
                    Name = "review-only",
                    Version = "0.1.0",
                    Description = "Review workspace changes or bounded references without writing files.",
                    AppliesTo = DotnetAppliesTo(),
                    Entry = Entry("exec", "reviewer", "markdown"),
                    References = References(["@folder:src"], maxFiles: 50),
                    Instructions =
                    [
                        "Stay read-only.",
                        "Prioritize findings with file and line references where available.",
                        "Do not write files, run shell commands, or call MCP tools."
                    ],
                    ReportTemplate = "default",
                    Safety = Safety(allowWrites: false, allowShell: "none")
                },
                SkillPackSource.BuiltIn),
            new SkillPackCatalogItem(
                new SkillPackManifest
                {
                    Name = "upgrade-package",
                    Version = "0.1.0",
                    Description = "Upgrade a focused NuGet package or constrained package set.",
                    AppliesTo = DotnetAppliesTo(),
                    Entry = Entry("exec", "bugfix", "markdown"),
                    References = References(["@folder:src"], maxFiles: 50),
                    Instructions =
                    [
                        "Limit the change to the requested package scope.",
                        "Read project files before editing package references.",
                        "List changed files, commands, risks, and verification results."
                    ],
                    ValidationCommand = "dotnet test",
                    ReportTemplate = "default",
                    Safety = Safety(allowWrites: true, allowShell: "verification-only")
                },
                SkillPackSource.BuiltIn),
            new SkillPackCatalogItem(
                new SkillPackManifest
                {
                    Name = "doc-sync",
                    Version = "0.1.0",
                    Description = "Synchronize docs with release and capability status facts.",
                    AppliesTo = DotnetAppliesTo(),
                    Entry = Entry("exec", "refactor", "markdown"),
                    References = References(["@folder:docs_md"], maxFiles: 50),
                    Instructions =
                    [
                        "Keep code behavior unchanged.",
                        "Prefer documentation-only edits unless the user explicitly asks for code changes.",
                        "Record changed docs and remaining uncertainties."
                    ],
                    ReportTemplate = "default",
                    Safety = Safety(allowWrites: true, allowShell: "none")
                },
                SkillPackSource.BuiltIn)
        ];
    }

    private static SkillPackAppliesTo DotnetAppliesTo()
    {
        return new SkillPackAppliesTo
        {
            ProjectTypes = ["dotnet"],
            RequiredFiles = ["*.sln", "*.csproj"]
        };
    }

    private static SkillPackEntry Entry(string mode, string expert, string report)
    {
        return new SkillPackEntry
        {
            Mode = mode,
            Expert = expert,
            Report = report
        };
    }

    private static SkillPackReferences References(IReadOnlyList<string> suggested, int maxFiles)
    {
        return new SkillPackReferences
        {
            Suggested = suggested,
            MaxFiles = maxFiles
        };
    }

    private static SkillPackSafety Safety(bool allowWrites, string allowShell)
    {
        return new SkillPackSafety
        {
            AllowWrites = allowWrites,
            AllowShell = allowShell,
            AllowMcp = false
        };
    }
}
