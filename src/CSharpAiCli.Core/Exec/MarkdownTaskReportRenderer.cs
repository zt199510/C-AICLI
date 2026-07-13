using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CSharpAiCli.Core;

public sealed class MarkdownTaskReportRenderer
{
    private static readonly Regex WhitespacePattern = new(@"\s+", RegexOptions.CultureInvariant);

    public string Render(AgentTaskReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        StringBuilder builder = new();
        builder.AppendLine("# C# AI CLI Task Report");
        builder.AppendLine();
        AppendSummary(builder, report);
        AppendFencedSection(builder, "Prompt", report.Prompt);
        AppendScalarSection(builder, "Workspace", [
            ("Workspace root", report.WorkspaceRoot),
            ("Session", string.IsNullOrWhiteSpace(report.SessionName) ? "none" : report.SessionName)
        ]);
        AppendExpert(builder, report);
        AppendSkill(builder, report);
        AppendReferences(builder, report);
        AppendFencedSection(builder, "Plan", report.Plan ?? "none");
        AppendChangedFiles(builder, report);
        AppendCommands(builder, report);
        AppendVerification(builder, report);
        AppendReviewGate(builder, report);
        AppendListSection(builder, "Remaining Risks", report.Risks);
        AppendScalarSection(builder, "Trace And Session", [
            ("Trace path", report.TracePath),
            ("Session", string.IsNullOrWhiteSpace(report.SessionName) ? "none" : report.SessionName)
        ]);
        AppendRedaction(builder, report);
        AppendReport(builder, report);

        return builder.ToString().TrimEnd();
    }

    private static void AppendSummary(StringBuilder builder, AgentTaskReport report)
    {
        builder.AppendLine("## Summary");
        builder.AppendLine();
        builder.AppendLine("- Status: " + Metadata(report.Status));
        builder.AppendLine("- Stop reason: " + Metadata(report.StopReason));
        if (!string.IsNullOrWhiteSpace(report.ErrorCode))
        {
            builder.AppendLine("- Error code: " + Metadata(report.ErrorCode));
        }

        if (!string.IsNullOrWhiteSpace(report.Summary))
        {
            builder.AppendLine("- Summary:");
            AppendFencedBlock(builder, report.Summary);
        }

        builder.AppendLine();
    }

    private static void AppendExpert(StringBuilder builder, AgentTaskReport report)
    {
        builder.AppendLine("## Expert");
        builder.AppendLine();
        if (report.Expert is null)
        {
            builder.AppendLine("- Selected: none");
            builder.AppendLine();
            return;
        }

        builder.AppendLine("- Selected: " + Metadata(report.Expert.Name));
        builder.AppendLine("- Boundary: " + Metadata(report.Expert.ToolBoundary));
        builder.AppendLine("- Tool boundary: " + Metadata(report.Expert.BoundarySummary));
        builder.AppendLine("- Report focus: " + Metadata(report.Expert.ReportFocus));
        builder.AppendLine();
    }

    private static void AppendSkill(StringBuilder builder, AgentTaskReport report)
    {
        builder.AppendLine("## Skill");
        builder.AppendLine();
        if (report.Skill is null)
        {
            builder.AppendLine("- Selected: none");
            builder.AppendLine();
            return;
        }

        builder.AppendLine("- Selected: " + Metadata(report.Skill.Name));
        builder.AppendLine("- Version: " + Metadata(report.Skill.Version));
        builder.AppendLine("- Source: " + Metadata(FormatSkillSource(report.Skill)));
        builder.AppendLine("- Entry: " + Metadata(report.Skill.EntryMode));
        builder.AppendLine("- Expert: " + Metadata(report.Skill.Expert));
        builder.AppendLine("- Report: " + Metadata(report.Skill.Report));
        builder.AppendLine("- Safety: " + Metadata(report.Skill.SafetySummary));
        builder.AppendLine("- Validation command: " + Metadata(report.Skill.ValidationCommand));
        if (report.Skill.SuggestedReferences.Count > 0)
        {
            builder.AppendLine("- Suggested references: " + Metadata(string.Join(", ", report.Skill.SuggestedReferences)));
        }

        builder.AppendLine();
    }

    private static void AppendReferences(StringBuilder builder, AgentTaskReport report)
    {
        builder.AppendLine("## References");
        builder.AppendLine();
        if (report.References.Count == 0)
        {
            builder.AppendLine("- none");
            builder.AppendLine();
            return;
        }

        foreach (AgentTaskReferenceReport reference in report.References)
        {
            builder.AppendLine(
                $"- {Metadata(reference.Kind)} {Metadata(reference.ResolvedPath ?? reference.RequestedPath)} status={Metadata(reference.Status)} files={reference.IncludedFileCount.ToString(CultureInfo.InvariantCulture)} skipped={reference.SkippedFileCount.ToString(CultureInfo.InvariantCulture)} bytes={reference.ByteCount.ToString(CultureInfo.InvariantCulture)} truncated={(reference.Truncated ? "true" : "false")}");
            if (!string.IsNullOrWhiteSpace(reference.ErrorCode))
            {
                builder.AppendLine("  errorCode: " + Metadata(reference.ErrorCode));
            }

            foreach (string warning in reference.Warnings)
            {
                builder.AppendLine("  warning: " + Metadata(warning));
            }
        }

        builder.AppendLine();
    }

    private static void AppendChangedFiles(StringBuilder builder, AgentTaskReport report)
    {
        builder.AppendLine("## Changed Files");
        builder.AppendLine();
        if (report.ChangedFiles.Count == 0)
        {
            builder.AppendLine("- none");
            builder.AppendLine();
            return;
        }

        foreach (ChangedFileSummary file in report.ChangedFiles)
        {
            builder.AppendLine($"- {Metadata(file.Path)} status={Metadata(file.Status)} source={Metadata(file.SourceToolCallId)}");
            if (!string.IsNullOrWhiteSpace(file.ErrorCode))
            {
                builder.AppendLine("  errorCode: " + Metadata(file.ErrorCode));
            }

            if (!string.IsNullOrWhiteSpace(file.DiffStat))
            {
                builder.AppendLine("  diffStat:");
                AppendFencedBlock(builder, file.DiffStat);
            }
        }

        builder.AppendLine();
    }

    private static void AppendCommands(StringBuilder builder, AgentTaskReport report)
    {
        builder.AppendLine("## Commands");
        builder.AppendLine();
        if (report.Commands.Count == 0)
        {
            builder.AppendLine("- none");
            builder.AppendLine();
            return;
        }

        foreach (AgentTaskCommandReport command in report.Commands)
        {
            builder.AppendLine($"- source={Metadata(command.Source)} status={Metadata(command.Status)} errorCode={Metadata(command.ErrorCode)}");
            AppendFencedBlock(builder, command.Command);
        }

        builder.AppendLine();
    }

    private static void AppendVerification(StringBuilder builder, AgentTaskReport report)
    {
        builder.AppendLine("## Verification");
        builder.AppendLine();
        if (report.Verification.Count == 0)
        {
            builder.AppendLine("- none");
            builder.AppendLine();
            return;
        }

        foreach (AgentTaskVerificationReport verification in report.Verification)
        {
            builder.AppendLine(
                $"- status={Metadata(verification.Status)} source={Metadata(verification.Source)} succeeded={(verification.Succeeded ? "true" : "false")} approvalStatus={Metadata(verification.ApprovalStatus)} errorCode={Metadata(verification.ErrorCode)} exitCode={verification.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "none"} timedOut={(verification.TimedOut ? "true" : "false")}");
            if (!string.IsNullOrWhiteSpace(verification.Command))
            {
                builder.AppendLine("  command:");
                AppendFencedBlock(builder, verification.Command);
            }

            if (!string.IsNullOrWhiteSpace(verification.Summary))
            {
                builder.AppendLine("  summary:");
                AppendFencedBlock(builder, verification.Summary);
            }
        }

        builder.AppendLine();
    }

    private static void AppendReviewGate(StringBuilder builder, AgentTaskReport report)
    {
        builder.AppendLine("## Review Gate");
        builder.AppendLine();
        if (report.ReviewGate is null)
        {
            builder.AppendLine("- none");
            builder.AppendLine();
            return;
        }

        builder.AppendLine("- Status: " + Metadata(report.ReviewGate.Status));
        builder.AppendLine("- Has diff: " + (report.ReviewGate.HasDiff ? "true" : "false"));
        builder.AppendLine("- Truncated: " + (report.ReviewGate.Truncated ? "true" : "false"));
        if (!string.IsNullOrWhiteSpace(report.ReviewGate.ErrorCode))
        {
            builder.AppendLine("- Error code: " + Metadata(report.ReviewGate.ErrorCode));
        }

        builder.AppendLine("- Summary:");
        AppendFencedBlock(builder, report.ReviewGate.Summary);
        builder.AppendLine();
    }

    private static void AppendRedaction(StringBuilder builder, AgentTaskReport report)
    {
        builder.AppendLine("## Redaction");
        builder.AppendLine();
        builder.AppendLine("- Secret presence count: " + report.Secrets.Count.ToString(CultureInfo.InvariantCulture));
        foreach (AgentTaskSecretPresence secret in report.Secrets)
        {
            builder.AppendLine($"- {Metadata(secret.Source)}: {Metadata(secret.Kind)}");
        }

        builder.AppendLine();
    }

    private static void AppendReport(StringBuilder builder, AgentTaskReport report)
    {
        if (report.Report is null)
        {
            return;
        }

        builder.AppendLine("## Report");
        builder.AppendLine();
        builder.AppendLine("- Mode: " + Metadata(report.Report.Mode));
        builder.AppendLine("- Generated: " + (report.Report.Generated ? "true" : "false"));
        if (!string.IsNullOrWhiteSpace(report.Report.Path))
        {
            builder.AppendLine("- Path: " + Metadata(report.Report.Path));
        }

        if (!string.IsNullOrWhiteSpace(report.Report.WriteStatus))
        {
            builder.AppendLine("- Write status: " + Metadata(report.Report.WriteStatus));
        }

        if (!string.IsNullOrWhiteSpace(report.Report.ErrorCode))
        {
            builder.AppendLine("- Error code: " + Metadata(report.Report.ErrorCode));
        }

        if (!string.IsNullOrWhiteSpace(report.Report.Summary))
        {
            builder.AppendLine("- Summary: " + Metadata(report.Report.Summary));
        }

        builder.AppendLine();
    }

    private static void AppendScalarSection(
        StringBuilder builder,
        string heading,
        IReadOnlyList<(string Name, string? Value)> values)
    {
        builder.AppendLine("## " + heading);
        builder.AppendLine();
        foreach ((string name, string? value) in values)
        {
            builder.AppendLine($"- {name}: {Metadata(value)}");
        }

        builder.AppendLine();
    }

    private static void AppendListSection(
        StringBuilder builder,
        string heading,
        IReadOnlyList<string> values)
    {
        builder.AppendLine("## " + heading);
        builder.AppendLine();
        if (values.Count == 0)
        {
            builder.AppendLine("- none");
        }
        else
        {
            foreach (string value in values)
            {
                builder.AppendLine("- " + Metadata(value));
            }
        }

        builder.AppendLine();
    }

    private static void AppendFencedSection(StringBuilder builder, string heading, string content)
    {
        builder.AppendLine("## " + heading);
        builder.AppendLine();
        AppendFencedBlock(builder, content);
        builder.AppendLine();
    }

    private static void AppendFencedBlock(StringBuilder builder, string? content)
    {
        string safeContent = DiagnosticSecretRedactor.Redact(content ?? string.Empty);
        string fence = CreateFence(safeContent);
        builder.AppendLine(fence);
        builder.AppendLine(safeContent);
        builder.AppendLine(fence);
    }

    private static string Metadata(string? value)
    {
        string normalized = WhitespacePattern.Replace(
            DiagnosticSecretRedactor.Redact(value ?? "none"),
            " ").Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "none";
        }

        return EscapeMarkdownMetadata(normalized);
    }

    private static string FormatSkillSource(AgentTaskSkillReport skill)
    {
        return string.IsNullOrWhiteSpace(skill.SourcePath)
            ? skill.SourceKind
            : skill.SourceKind + ":" + skill.SourcePath;
    }

    private static string EscapeMarkdownMetadata(string value)
    {
        StringBuilder escaped = new(value.Length);
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            escaped.Append(character switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '[' => "&#91;",
                ']' => "&#93;",
                '(' => "&#40;",
                ')' => "&#41;",
                '!' => "&#33;",
                '#' => "&#35;",
                '*' => "&#42;",
                '-' when index + 1 < value.Length && value[index + 1] == ' ' => "&#45;",
                '+' => "&#43;",
                '`' => "&#96;",
                '\\' => "&#92;",
                _ => character,
            });
        }

        return escaped.ToString();
    }

    private static string CreateFence(string content)
    {
        int longestRun = 0;
        int currentRun = 0;
        foreach (char character in content)
        {
            if (character == '`')
            {
                currentRun++;
                longestRun = Math.Max(longestRun, currentRun);
                continue;
            }

            currentRun = 0;
        }

        return new string('`', Math.Max(3, longestRun + 1));
    }
}
