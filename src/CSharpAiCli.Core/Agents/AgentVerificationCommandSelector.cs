using System.Text.RegularExpressions;

namespace CSharpAiCli.Core;

public static class AgentVerificationCommandSelector
{
    private static readonly Regex InstructionCommandPattern = new(
        @"^\s*(?:VerificationCommand|ValidationCommand)\s*:\s*(?<command>.+?)\s*$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static bool TrySelect(
        AgentRunRequest request,
        out AgentVerificationCommand? command,
        out string skippedReason)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (TrySelectFromInstructions(request.TaskContext?.Instructions, out command))
        {
            skippedReason = string.Empty;
            return true;
        }

        if (TrySelectFromWorkflow(request, out command, out skippedReason))
        {
            return true;
        }

        command = null;
        skippedReason = string.IsNullOrWhiteSpace(skippedReason)
            ? "No explicit verification command configured."
            : skippedReason;
        return false;
    }

    private static bool TrySelectFromInstructions(
        string? instructions,
        out AgentVerificationCommand? command)
    {
        command = null;
        if (string.IsNullOrWhiteSpace(instructions))
        {
            return false;
        }

        foreach (string line in instructions.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            Match match = InstructionCommandPattern.Match(line);
            if (!match.Success)
            {
                continue;
            }

            string value = match.Groups["command"].Value.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            command = new AgentVerificationCommand(value, "project-instructions");
            return true;
        }

        return false;
    }

    private static bool TrySelectFromWorkflow(
        AgentRunRequest request,
        out AgentVerificationCommand? command,
        out string skippedReason)
    {
        command = null;
        skippedReason = string.Empty;

        WorkflowProfile[] candidates = (request.WorkflowConfiguration?.Profiles ?? [])
            .Where(profile => !string.IsNullOrWhiteSpace(profile.ValidationCommand))
            .ToArray();
        if (candidates.Length == 0)
        {
            return false;
        }

        WorkflowProfile[] workspaceMatches = candidates
            .Where(profile => !string.IsNullOrWhiteSpace(profile.WorkspacePath) &&
                IsSamePath(profile.WorkspacePath!, request.Workspace.RootPath))
            .ToArray();
        WorkflowProfile[] genericCandidates = candidates
            .Where(profile => string.IsNullOrWhiteSpace(profile.WorkspacePath))
            .ToArray();
        WorkflowProfile[] selectedCandidates = workspaceMatches.Length > 0
            ? workspaceMatches
            : genericCandidates;

        if (selectedCandidates.Length == 0)
        {
            skippedReason = "No workflow validation command matched the current workspace.";
            return false;
        }

        if (selectedCandidates.Length != 1)
        {
            skippedReason = "Multiple workflow validation commands are configured; automatic verification requires an unambiguous command.";
            return false;
        }

        WorkflowProfile selected = selectedCandidates[0];
        command = new AgentVerificationCommand(
            selected.ValidationCommand!,
            "workflow:" + selected.Source,
            selected.Name);
        return true;
    }

    private static bool IsSamePath(string left, string right)
    {
        try
        {
            string fullLeft = Path.TrimEndingDirectorySeparator(Path.GetFullPath(left));
            string fullRight = Path.TrimEndingDirectorySeparator(Path.GetFullPath(right));
            StringComparison comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return string.Equals(fullLeft, fullRight, comparison);
        }
        catch (Exception exception) when (exception is ArgumentException
            or IOException
            or NotSupportedException
            or PathTooLongException
            or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
