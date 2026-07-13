using System.Text;

namespace CSharpAiCli.Core;

internal static class AgentTaskContextPromptFormatter
{
    public static string FormatWithCurrentPrompt(
        AgentTaskContext taskContext,
        string prompt,
        ExpertProfile? expert = null)
    {
        ArgumentNullException.ThrowIfNull(taskContext);

        AgentStartupPlan plan = AgentStartupPlanBuilder.Build(prompt, taskContext, expert);
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
        AppendReferences(builder, taskContext.References ?? WorkflowReferenceResolution.Empty);
        AppendExpert(builder, expert);
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

    private static void AppendExpert(StringBuilder builder, ExpertProfile? expert)
    {
        builder.AppendLine("- Expert: " + (expert?.Name ?? "none"));
        if (expert is null)
        {
            return;
        }

        builder.AppendLine("- Expert tool boundary: " + expert.ToolBoundary);
        builder.AppendLine("- Expert report focus: " + expert.ReportFocus);
        builder.AppendLine("- Expert guidance: " + expert.PromptGuidance);
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

    private static void AppendReferences(StringBuilder builder, WorkflowReferenceResolution references)
    {
        builder.AppendLine("- Workflow references: " + (references.HasReferences ? references.References.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) : "none"));
        if (!references.HasReferences)
        {
            return;
        }

        foreach (WorkflowReferenceEntry reference in references.References)
        {
            builder.AppendLine($"  - {reference.Kind}:{reference.ResolvedPath ?? reference.RequestedPath} status={reference.Status} files={reference.IncludedFileCount.ToString(System.Globalization.CultureInfo.InvariantCulture)} skipped={reference.SkippedFileCount.ToString(System.Globalization.CultureInfo.InvariantCulture)} truncated={(reference.Truncated ? "true" : "false")}");
            foreach (WorkflowReferenceWarning warning in reference.Warnings)
            {
                builder.AppendLine($"    warning: {warning.ErrorCode} {warning.Path}");
            }

            foreach (WorkflowReferenceFile file in reference.Files)
            {
                builder.AppendLine($"    file: {file.Path} bytes={file.ByteCount.ToString(System.Globalization.CultureInfo.InvariantCulture)} truncated={(file.Truncated ? "true" : "false")}");
                builder.AppendLine("    content:");
                builder.AppendLine(file.Content);
            }
        }
    }
}
