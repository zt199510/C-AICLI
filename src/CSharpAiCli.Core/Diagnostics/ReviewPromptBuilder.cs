namespace CSharpAiCli.Core;

public static class ReviewPromptBuilder
{
    public static string Build(string gitDiff)
    {
        string diff = string.IsNullOrWhiteSpace(gitDiff) ? "no diff" : gitDiff.Trim();

        return $$"""
        You are performing a read-only code review of the current git diff.

        Review goals:
        - Lead with findings, ordered by severity.
        - Include file and line references when possible.
        - Focus on bugs, behavior regressions, security issues, data loss, concurrency, and missing tests.
        - Keep summaries brief and avoid suggesting unrelated refactors.
        - Do not modify files, produce patches, or run commands.

        Current git diff:
        ````diff
        {{diff}}
        ````
        """;
    }
}
