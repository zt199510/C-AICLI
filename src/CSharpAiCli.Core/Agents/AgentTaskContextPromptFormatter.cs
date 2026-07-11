using System.Text;

namespace CSharpAiCli.Core;

internal static class AgentTaskContextPromptFormatter
{
    public static string FormatWithCurrentPrompt(AgentTaskContext taskContext, string prompt)
    {
        ArgumentNullException.ThrowIfNull(taskContext);

        AgentStartupPlan plan = AgentStartupPlanBuilder.Build(prompt, taskContext);
        StringBuilder builder = new();
        builder.AppendLine("Bounded startup context:");
        builder.AppendLine("- Workspace: " + taskContext.WorkspaceRoot);
        builder.AppendLine("- Current directory: " + taskContext.CurrentDirectory);
        builder.AppendLine("- Workspace status: " + taskContext.WorkspaceStatus);
        builder.AppendLine("- Instruction sources: " + FormatInstructionSources(taskContext));
        builder.AppendLine("- Session: " + FormatSession(taskContext));
        builder.AppendLine("- Git status: " + taskContext.Git.StatusSummary);
        builder.AppendLine("- Git diff summary:");
        builder.AppendLine(taskContext.Git.DiffSummary);
        builder.AppendLine();
        builder.AppendLine("Read-only startup plan:");
        builder.AppendLine(plan.Summary);
        builder.AppendLine();
        builder.AppendLine("Keep tool paths inside the workspace. Use read/search/git context before write or shell tools.");
        builder.AppendLine();
        builder.AppendLine("Current task:");
        builder.AppendLine(prompt);
        return builder.ToString().TrimEnd();
    }

    private static string FormatInstructionSources(AgentTaskContext taskContext)
    {
        if (taskContext.InstructionSources.Count == 0)
        {
            return "none";
        }

        return string.Join(", ", taskContext.InstructionSources.Select(source => source.SourcePath));
    }

    private static string FormatSession(AgentTaskContext taskContext)
    {
        if (string.IsNullOrWhiteSpace(taskContext.SessionName))
        {
            return "none";
        }

        return taskContext.HasTranscriptContext
            ? taskContext.SessionName + " (resumed)"
            : taskContext.SessionName;
    }
}
