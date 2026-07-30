using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using CSharpAiCli.Core;

namespace CSharpAiCli.Cli;

internal sealed class CiCommandModule : ICliCommandModule
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Command Create(CliCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Command command = new("ci", "Generate provider-neutral CI artifacts from local job records.");
        Command summarizeCommand = new("summarize", "Generate a CI JSON or markdown summary for one job.");
        Option<string> summarizeJobOption = new("--job")
        {
            Description = "Job id to summarize.",
        };
        Option<string> summarizeOutputOption = new("--output")
        {
            Description = "Select json or markdown output.",
        };
        Option<string> summarizeMarkdownPathOption = new("--markdown-path")
        {
            Description = "Write the markdown summary to a new workspace-local file.",
        };
        summarizeOutputOption.DefaultValueFactory = _ => "json";
        AddRequiredJobValidator(summarizeJobOption);
        AddJsonMarkdownValidator(summarizeOutputOption);
        summarizeCommand.Options.Add(summarizeJobOption);
        summarizeCommand.Options.Add(summarizeOutputOption);
        summarizeCommand.Options.Add(summarizeMarkdownPathOption);
        summarizeCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(context.GlobalOptions.Workspace);
            string jobId = parseResult.GetValue(summarizeJobOption) ?? string.Empty;
            string outputMode = parseResult.GetValue(summarizeOutputOption) ?? "json";
            string? markdownPath = parseResult.GetValue(summarizeMarkdownPathOption);
            bool jsonOutput = string.Equals(outputMode, "json", StringComparison.OrdinalIgnoreCase);
            CliEnvironmentSnapshot snapshot = context.WorkspaceSnapshotProvider(workspacePath);
            context.WriteVerboseDiagnostics(parseResult, "ci summarize", snapshot, !jsonOutput);

            JobRecordReadResult readResult = JobRecordStore.Create(snapshot).Read(jobId);
            if (!readResult.Succeeded || readResult.Record is null)
            {
                WriteCiFailure(
                    context,
                    readResult.Diagnostic?.ErrorCode ?? "job-not-found",
                    readResult.Diagnostic?.Summary ?? "Job record was not found.",
                    jsonOutput,
                    jobId);
                return CiExitCodePolicy.ConfigError;
            }

            CiArtifact artifact = new CiArtifactRenderer().Render(readResult.Record);
            CiMarkdownRenderer markdownRenderer = new();
            if (!string.IsNullOrWhiteSpace(markdownPath))
            {
                ReportWriteResult writeResult = new ReportPathResolver().WriteMarkdown(
                    snapshot.Workspace,
                    markdownPath,
                    markdownRenderer.Render(artifact));
                if (!writeResult.Succeeded)
                {
                    WriteCiFailure(
                        context,
                        writeResult.ErrorCode ?? "ci-markdown-write-failed",
                        writeResult.Summary ?? "CI markdown summary could not be written.",
                        jsonOutput,
                        jobId);
                    return CiExitCodePolicy.ConfigError;
                }
            }

            if (jsonOutput)
            {
                new CiJsonRenderer(context.Output).Write(artifact);
            }
            else
            {
                markdownRenderer.Write(context.Output, artifact);
            }

            return string.Equals(
                artifact.Check.Outcome,
                CiCheckOutcome.ConfigError,
                StringComparison.Ordinal)
                ? CiExitCodePolicy.ConfigError
                : CiExitCodePolicy.Success;
        });

        Command checkCommand = new("check", "Evaluate one job using the deterministic CI exit-code policy.");
        Option<string> checkJobOption = new("--job")
        {
            Description = "Job id to check.",
        };
        Option<string> checkOutputOption = new("--output")
        {
            Description = "Select json or markdown output.",
        };
        Option<string> checkFailOnOption = new("--fail-on")
        {
            Description = "Promote none, warnings, or remaining risks to a failed check.",
        };
        checkOutputOption.DefaultValueFactory = _ => "json";
        checkFailOnOption.DefaultValueFactory = _ => "none";
        AddRequiredJobValidator(checkJobOption);
        AddJsonMarkdownValidator(checkOutputOption);
        checkFailOnOption.Validators.Add(result =>
        {
            string value = result.GetValueOrDefault<string>() ?? "none";
            if (!string.Equals(value, "none", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(value, "warnings", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(value, "risks", StringComparison.OrdinalIgnoreCase))
            {
                result.AddError("Invalid value for --fail-on. Allowed values are none, warnings, and risks.");
            }
        });
        checkCommand.Options.Add(checkJobOption);
        checkCommand.Options.Add(checkOutputOption);
        checkCommand.Options.Add(checkFailOnOption);
        checkCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(context.GlobalOptions.Workspace);
            string jobId = parseResult.GetValue(checkJobOption) ?? string.Empty;
            string outputMode = parseResult.GetValue(checkOutputOption) ?? "json";
            string failOn = parseResult.GetValue(checkFailOnOption) ?? "none";
            bool jsonOutput = string.Equals(outputMode, "json", StringComparison.OrdinalIgnoreCase);
            CliEnvironmentSnapshot snapshot = context.WorkspaceSnapshotProvider(workspacePath);
            context.WriteVerboseDiagnostics(parseResult, "ci check", snapshot, !jsonOutput);

            JobRecordReadResult readResult = JobRecordStore.Create(snapshot).Read(jobId);
            if (!readResult.Succeeded || readResult.Record is null)
            {
                WriteCiFailure(
                    context,
                    readResult.Diagnostic?.ErrorCode ?? "job-not-found",
                    readResult.Diagnostic?.Summary ?? "Job record was not found.",
                    jsonOutput,
                    jobId);
                return CiExitCodePolicy.ConfigError;
            }

            CiArtifact artifact = CiExitCodePolicy.Apply(
                new CiArtifactRenderer().Render(readResult.Record),
                failOn);
            if (jsonOutput)
            {
                new CiJsonRenderer(context.Output).Write(artifact);
            }
            else
            {
                new CiMarkdownRenderer().Write(context.Output, artifact);
            }

            return artifact.Check.RecommendedExitCode;
        });

        command.Subcommands.Add(summarizeCommand);
        command.Subcommands.Add(checkCommand);
        return command;
    }

    private static void AddRequiredJobValidator(Option<string> option)
    {
        option.Validators.Add(result =>
        {
            if (string.IsNullOrWhiteSpace(result.GetValueOrDefault<string>()))
            {
                result.AddError("Option --job is required.");
            }
        });
    }

    private static void AddJsonMarkdownValidator(Option<string> option)
    {
        option.Validators.Add(result =>
        {
            string value = result.GetValueOrDefault<string>() ?? "json";
            if (!string.Equals(value, "json", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(value, "markdown", StringComparison.OrdinalIgnoreCase))
            {
                result.AddError("Invalid value for --output. Allowed values are json and markdown.");
            }
        });
    }

    private static void WriteCiFailure(
        CliCommandContext context,
        string errorCode,
        string summary,
        bool jsonOutput,
        string? jobId)
    {
        string safeErrorCode = DiagnosticSecretRedactor.Redact(errorCode);
        string safeSummary = DiagnosticSecretRedactor.Redact(summary);
        if (jsonOutput)
        {
            context.Output.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["type"] = "caicli.ci.error",
                ["outcome"] = CiCheckOutcome.ConfigError,
                ["exitCode"] = CiExitCodePolicy.ConfigError,
                ["errorCode"] = safeErrorCode,
                ["summary"] = safeSummary,
                ["jobId"] = string.IsNullOrWhiteSpace(jobId)
                    ? null
                    : DiagnosticSecretRedactor.Redact(jobId)
            }, JsonOptions));
            return;
        }

        context.Output.WriteLine("# C-AICLI CI summary");
        context.Output.WriteLine();
        context.Output.WriteLine("- Outcome: **config-error**");
        context.Output.WriteLine("- Exit code: " + CiExitCodePolicy.ConfigError.ToString(CultureInfo.InvariantCulture));
        context.Output.WriteLine("- Error code: " + safeErrorCode);
        context.Output.WriteLine();
        context.Output.WriteLine(safeSummary);
    }
}
