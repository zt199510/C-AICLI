using System.Text;
using System.Text.RegularExpressions;

namespace CSharpAiCli.Core;

internal static class AgentStartupPlanBuilder
{
    public const int MaxPlanSummaryCharacters = 1600;
    public const string TruncationWarning = "WARNING: plan summary was truncated.";

    private static readonly Regex CandidatePathPattern = new(
        @"(?<![\w.-])(?:[A-Za-z0-9_.-]+[\\/])+[A-Za-z0-9_.-]+|(?<![\w.-])[A-Za-z0-9_.-]+\.(?:cs|csproj|sln|md|json|yml|yaml|ps1|sh|txt|xml)(?![\w.-])",
        RegexOptions.CultureInvariant);

    public static AgentStartupPlan Build(
        string prompt,
        AgentTaskContext taskContext,
        ExpertProfile? expert = null)
    {
        ArgumentNullException.ThrowIfNull(taskContext);

        string goal = NormalizeSingleLine(prompt, 500);
        IReadOnlyList<string> candidateFiles = FindCandidateFiles(prompt, taskContext);
        IReadOnlyList<string> expectedTools = SelectExpectedTools(prompt, expert);
        IReadOnlyList<string> risks = SelectRisks(taskContext, prompt, expert);
        string summary = FormatSummary(goal, candidateFiles, expectedTools, risks);
        bool truncated = false;
        if (summary.Length > MaxPlanSummaryCharacters)
        {
            summary = summary[..MaxPlanSummaryCharacters].TrimEnd() +
                Environment.NewLine +
                TruncationWarning;
            truncated = true;
        }

        return new AgentStartupPlan(goal, candidateFiles, expectedTools, risks, summary, truncated);
    }

    private static IReadOnlyList<string> FindCandidateFiles(string prompt, AgentTaskContext taskContext)
    {
        List<string> candidates = CandidatePathPattern
            .Matches(prompt ?? string.Empty)
            .Select(match => match.Value.Trim('\'', '"', '`', ',', ';', ':', '.', ')', '('))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();

        WorkflowReferenceResolution references = taskContext.References ?? WorkflowReferenceResolution.Empty;
        foreach (string referencePath in references.References
            .Select(reference => reference.ResolvedPath ?? reference.RequestedPath)
            .Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            if (!candidates.Contains(referencePath, StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add(referencePath);
            }
        }

        return candidates.Count == 0 ? ["unknown until read/search"] : candidates.Take(20).ToArray();
    }

    private static IReadOnlyList<string> SelectExpectedTools(string prompt, ExpertProfile? expert)
    {
        List<string> tools =
        [
            "git.status",
            "git.diff",
            "workspace.search_text",
            "workspace.read_text"
        ];

        if (expert is { IsReadOnly: true })
        {
            return tools;
        }

        if (ContainsAny(prompt, "edit", "modify", "fix", "implement", "update", "create", "write", "patch", "add"))
        {
            tools.Add("workspace.apply_patch");
        }

        if (ContainsAny(prompt, "build", "test", "run", "verify"))
        {
            tools.Add("workspace.run_shell");
        }

        return tools;
    }

    private static IReadOnlyList<string> SelectRisks(
        AgentTaskContext taskContext,
        string prompt,
        ExpertProfile? expert)
    {
        List<string> risks = [];
        if (expert is { IsReadOnly: true })
        {
            risks.Add("expert profile is read-only; write, shell, and MCP tools are disabled");
        }

        if (taskContext.Git.IsDirty)
        {
            risks.Add("workspace has uncommitted changes");
        }

        if (!taskContext.Git.StatusSucceeded)
        {
            risks.Add("git status unavailable: " + (taskContext.Git.StatusErrorCode ?? "unknown"));
        }

        if (!taskContext.Git.DiffSucceeded)
        {
            risks.Add("git diff summary unavailable: " + (taskContext.Git.DiffErrorCode ?? "unknown"));
        }

        if (taskContext.Git.DiffOutputTruncated || taskContext.Git.DiffSummaryTruncated)
        {
            risks.Add("git diff summary was truncated");
        }

        if (taskContext.CurrentDirectoryErrorCode is not null)
        {
            risks.Add("requested cwd was outside or unavailable: " + taskContext.CurrentDirectoryErrorCode);
        }

        if (taskContext.InstructionWarnings.Count > 0)
        {
            risks.Add("instruction loading reported warnings");
        }

        if (string.IsNullOrWhiteSpace(taskContext.Instructions))
        {
            risks.Add("no project instruction file loaded");
        }

        if (taskContext.HasTranscriptContext)
        {
            risks.Add("resumed transcript context may affect task scope");
        }

        WorkflowReferenceResolution references = taskContext.References ?? WorkflowReferenceResolution.Empty;
        if (references.HasWarnings || references.Truncated)
        {
            risks.Add("workflow references were bounded or truncated");
        }

        if (references.HasErrors)
        {
            risks.Add("workflow reference resolution failed: " + (references.FirstErrorCode ?? "unknown"));
        }

        if (expert is not { IsReadOnly: true } &&
            ContainsAny(prompt, "edit", "modify", "fix", "implement", "update", "create", "write", "patch", "add", "run", "shell"))
        {
            risks.Add("write or shell tools may require approval");
        }

        return risks.Count == 0 ? ["normal bounded workspace task"] : risks;
    }

    private static string FormatSummary(
        string goal,
        IReadOnlyList<string> candidateFiles,
        IReadOnlyList<string> expectedTools,
        IReadOnlyList<string> risks)
    {
        StringBuilder builder = new();
        builder.AppendLine("Goal: " + goal);
        builder.AppendLine("Candidate files: " + string.Join(", ", candidateFiles));
        builder.AppendLine("Expected tools: " + string.Join(", ", expectedTools));
        builder.AppendLine("Risks: " + string.Join("; ", risks));
        return builder.ToString().TrimEnd();
    }

    private static bool ContainsAny(string text, params string[] terms)
    {
        foreach (string term in terms)
        {
            if (text.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeSingleLine(string? value, int maxCharacters)
    {
        string normalized = Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
        return normalized.Length <= maxCharacters
            ? normalized
            : normalized[..maxCharacters];
    }
}
