using System.CommandLine;
using System.Text.Json;
using CSharpAiCli.Core;

namespace CSharpAiCli.Cli;

internal sealed class JobsCommandModule : ICliCommandModule
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Command Create(CliCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Command command = new("jobs", "Read local job history records.");
        Command listCommand = new("list", "List local job records.");
        Option<bool> listJsonOption = new("--json")
        {
            Description = "Write a single JSON jobs list object.",
        };
        Option<string> listOutputOption = new("--output")
        {
            Description = "Select text or json output.",
        };
        Option<int?> listLimitOption = new("--limit")
        {
            Description = "Maximum number of job records to list.",
        };
        listOutputOption.DefaultValueFactory = _ => "text";
        CliCommandContext.AddTextJsonOutputValidator(listOutputOption);
        CliCommandContext.AddPositiveIntegerValidator(listLimitOption, "--limit");
        listCommand.Options.Add(listJsonOption);
        listCommand.Options.Add(listOutputOption);
        listCommand.Options.Add(listLimitOption);
        listCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(context.GlobalOptions.Workspace);
            bool jsonRequested = parseResult.GetValue(listJsonOption);
            string outputMode = parseResult.GetValue(listOutputOption) ?? "text";
            int? limit = parseResult.GetValue(listLimitOption);
            CliEnvironmentSnapshot snapshot = context.WorkspaceSnapshotProvider(workspacePath);
            context.WriteVerboseDiagnostics(
                parseResult,
                "jobs list",
                snapshot,
                humanReadableOutput: !CliCommandContext.IsJsonOutputRequested(jsonRequested, outputMode));

            JobRecordListResult result = JobRecordStore.Create(snapshot).List(limit);
            if (CliCommandContext.IsJsonOutputRequested(jsonRequested, outputMode))
            {
                new JobsJsonRenderer(context.Output).WriteList(result);
            }
            else
            {
                new JobsTextRenderer(context.Output).WriteList(result);
            }

            return 0;
        });

        Command showCommand = new("show", "Show one local job record.");
        Argument<string> showIdArgument = new("job-id")
        {
            Description = "Job id.",
        };
        Option<bool> showJsonOption = new("--json")
        {
            Description = "Write a single JSON jobs show object.",
        };
        Option<string> showOutputOption = new("--output")
        {
            Description = "Select text or json output.",
        };
        showOutputOption.DefaultValueFactory = _ => "text";
        CliCommandContext.AddTextJsonOutputValidator(showOutputOption);
        showCommand.Arguments.Add(showIdArgument);
        showCommand.Options.Add(showJsonOption);
        showCommand.Options.Add(showOutputOption);
        showCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(context.GlobalOptions.Workspace);
            string jobId = parseResult.GetValue(showIdArgument) ?? string.Empty;
            bool jsonRequested = parseResult.GetValue(showJsonOption);
            string outputMode = parseResult.GetValue(showOutputOption) ?? "text";
            CliEnvironmentSnapshot snapshot = context.WorkspaceSnapshotProvider(workspacePath);
            context.WriteVerboseDiagnostics(
                parseResult,
                "jobs show",
                snapshot,
                humanReadableOutput: !CliCommandContext.IsJsonOutputRequested(jsonRequested, outputMode));

            JobRecordReadResult result = JobRecordStore.Create(snapshot).Read(jobId);
            if (!result.Succeeded || result.Record is null)
            {
                WriteJobReadFailure(
                    context,
                    result.Diagnostic,
                    CliCommandContext.IsJsonOutputRequested(jsonRequested, outputMode),
                    "jobs.show");
                return 1;
            }

            if (CliCommandContext.IsJsonOutputRequested(jsonRequested, outputMode))
            {
                new JobsJsonRenderer(context.Output).WriteShow(result.Record);
            }
            else
            {
                new JobsTextRenderer(context.Output).WriteShow(result.Record);
            }

            return 0;
        });

        Command exportCommand = new("export", "Export one local job record.");
        Argument<string> exportIdArgument = new("job-id")
        {
            Description = "Job id.",
        };
        Option<string> exportFormatOption = new("--format")
        {
            Description = "Select json or markdown export format.",
        };
        exportFormatOption.DefaultValueFactory = _ => "json";
        exportFormatOption.Validators.Add(result =>
        {
            string format = result.GetValueOrDefault<string>() ?? "json";
            if (!string.Equals(format, "json", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(format, "markdown", StringComparison.OrdinalIgnoreCase))
            {
                result.AddError("Invalid value for --format. Allowed values are json and markdown.");
            }
        });
        exportCommand.Arguments.Add(exportIdArgument);
        exportCommand.Options.Add(exportFormatOption);
        exportCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(context.GlobalOptions.Workspace);
            string jobId = parseResult.GetValue(exportIdArgument) ?? string.Empty;
            string format = parseResult.GetValue(exportFormatOption) ?? "json";
            bool jsonOutput = string.Equals(format, "json", StringComparison.OrdinalIgnoreCase);
            CliEnvironmentSnapshot snapshot = context.WorkspaceSnapshotProvider(workspacePath);
            context.WriteVerboseDiagnostics(
                parseResult,
                "jobs export",
                snapshot,
                humanReadableOutput: !jsonOutput);

            JobRecordReadResult result = JobRecordStore.Create(snapshot).Read(jobId);
            if (!result.Succeeded || result.Record is null)
            {
                WriteJobReadFailure(context, result.Diagnostic, jsonOutput, "jobs.export");
                return 1;
            }

            if (jsonOutput)
            {
                new JobsJsonRenderer(context.Output).WriteExport(result.Record);
            }
            else
            {
                new JobsTextRenderer(context.Output).WriteMarkdown(result.Record);
            }

            return 0;
        });

        command.Subcommands.Add(listCommand);
        command.Subcommands.Add(showCommand);
        command.Subcommands.Add(exportCommand);
        return command;
    }

    private static void WriteJobReadFailure(
        CliCommandContext context,
        JobRecordDiagnostic? diagnostic,
        bool jsonOutput,
        string type)
    {
        string errorCode = diagnostic?.ErrorCode ?? "job-not-found";
        string summary = diagnostic?.Summary ?? "Job record was not found.";
        if (jsonOutput)
        {
            context.Output.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["type"] = type,
                ["status"] = "failed",
                ["errorCode"] = DiagnosticSecretRedactor.Redact(errorCode),
                ["summary"] = DiagnosticSecretRedactor.Redact(summary),
                ["jobId"] = string.IsNullOrWhiteSpace(diagnostic?.JobId)
                    ? null
                    : DiagnosticSecretRedactor.Redact(diagnostic.JobId),
                ["path"] = string.IsNullOrWhiteSpace(diagnostic?.Path)
                    ? null
                    : DiagnosticSecretRedactor.Redact(diagnostic.Path)
            }, JsonOptions));
            return;
        }

        context.WriteSafeFailure(errorCode, summary);
    }
}
