using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.Json;
using CSharpAiCli.Application;
using CSharpAiCli.Core;

namespace CSharpAiCli.Cli;

internal sealed class AutomationCommandModule : ICliCommandModule
{
    public Command Create(CliCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Command command = new("automation", "Inspect and manually run workspace-local automations.");
        command.Subcommands.Add(CreateListCommand(context));
        command.Subcommands.Add(CreateValidateCommand(context));
        command.Subcommands.Add(CreatePlanCommand(context));
        command.Subcommands.Add(CreateRunCommand(context));
        return command;
    }

    private static Command CreateListCommand(CliCommandContext context)
    {
        Command command = new("list", "List valid workspace-local automation manifests.");
        Option<bool> jsonOption = new("--json")
        {
            Description = "Write a single JSON automation catalog object.",
        };
        Option<string> outputOption = CreateOutputOption();
        command.Options.Add(jsonOption);
        command.Options.Add(outputOption);
        command.SetAction(parseResult =>
        {
            bool jsonOutput = IsJsonOutput(parseResult, jsonOption, outputOption);
            CliEnvironmentSnapshot snapshot = context.WorkspaceSnapshotProvider(
                parseResult.GetValue(context.GlobalOptions.Workspace));
            context.WriteVerboseDiagnostics(parseResult, "automation list", snapshot, !jsonOutput);
            AutomationCatalog catalog = AutomationCatalog.Load(snapshot.Workspace);
            if (jsonOutput)
            {
                new AutomationJsonRenderer(context.Output).WriteList(catalog);
            }
            else
            {
                new AutomationTextRenderer(context.Output).WriteList(catalog);
            }

            return 0;
        });
        return command;
    }

    private static Command CreateValidateCommand(CliCommandContext context)
    {
        Command command = new("validate", "Validate all workspace-local automation manifests.");
        Option<bool> jsonOption = new("--json")
        {
            Description = "Write a single JSON automation validation object.",
        };
        Option<string> outputOption = CreateOutputOption();
        command.Options.Add(jsonOption);
        command.Options.Add(outputOption);
        command.SetAction(parseResult =>
        {
            bool jsonOutput = IsJsonOutput(parseResult, jsonOption, outputOption);
            CliEnvironmentSnapshot snapshot = context.WorkspaceSnapshotProvider(
                parseResult.GetValue(context.GlobalOptions.Workspace));
            context.WriteVerboseDiagnostics(parseResult, "automation validate", snapshot, !jsonOutput);
            AutomationCatalog catalog = AutomationCatalog.Load(snapshot.Workspace);
            if (jsonOutput)
            {
                new AutomationJsonRenderer(context.Output).WriteValidation(catalog);
            }
            else
            {
                new AutomationTextRenderer(context.Output).WriteValidation(catalog);
            }

            return catalog.Diagnostics.Count == 0 ? 0 : 1;
        });
        return command;
    }

    private static Command CreatePlanCommand(CliCommandContext context)
    {
        Command command = new("plan", "Render a local automation and schedule preview without execution.");
        Argument<string> nameArgument = new("automation")
        {
            Description = "Workspace-local automation name.",
        };
        Option<bool> jsonOption = new("--json")
        {
            Description = "Write a single JSON automation plan object.",
        };
        Option<string> outputOption = CreateOutputOption();
        command.Arguments.Add(nameArgument);
        command.Options.Add(jsonOption);
        command.Options.Add(outputOption);
        command.SetAction(parseResult =>
        {
            string automationName = parseResult.GetValue(nameArgument) ?? string.Empty;
            bool jsonOutput = IsJsonOutput(parseResult, jsonOption, outputOption);
            CliEnvironmentSnapshot snapshot = context.WorkspaceSnapshotProvider(
                parseResult.GetValue(context.GlobalOptions.Workspace));
            context.WriteVerboseDiagnostics(parseResult, "automation plan", snapshot, !jsonOutput);
            AutomationCatalog catalog = AutomationCatalog.Load(snapshot.Workspace);
            if (!catalog.TryGet(automationName, out AutomationCatalogItem? item) || item is null)
            {
                WriteFailure(
                    context.Output,
                    AutomationErrorCode.NotFound,
                    "Valid workspace-local automation was not found. Run automation validate for diagnostics.",
                    jsonOutput,
                    "automation.plan",
                    automationName);
                return 1;
            }

            AutomationPlan plan = new(
                item,
                snapshot.Workspace.RootPath,
                AutomationManifestValidator.CreateSchedulePreview(item.Manifest.Trigger!));
            if (jsonOutput)
            {
                new AutomationJsonRenderer(context.Output).WritePlan(plan);
            }
            else
            {
                new AutomationTextRenderer(context.Output).WritePlan(plan);
            }

            return 0;
        });
        return command;
    }

    private static Command CreateRunCommand(CliCommandContext context)
    {
        Command command = new("run", "Dry-run or manually trigger a workspace-local automation.");
        Argument<string> nameArgument = new("automation")
        {
            Description = "Workspace-local automation name.",
        };
        Option<bool> dryRunOption = new("--dry-run")
        {
            Description = "Render the validated automation plan without model, tools, or persistence.",
        };
        Option<bool> manualOption = new("--manual")
        {
            Description = "Manually execute the target through existing queue or pipeline paths.",
        };
        Option<bool> jsonOption = new("--json")
        {
            Description = "Write a single JSON automation result object.",
        };
        Option<string> outputOption = CreateOutputOption();
        command.Arguments.Add(nameArgument);
        command.Options.Add(dryRunOption);
        command.Options.Add(manualOption);
        command.Options.Add(jsonOption);
        command.Options.Add(outputOption);
        command.SetAction(parseResult =>
        {
            string automationName = parseResult.GetValue(nameArgument) ?? string.Empty;
            bool dryRun = parseResult.GetValue(dryRunOption);
            bool manual = parseResult.GetValue(manualOption);
            bool jsonOutput = IsJsonOutput(parseResult, jsonOption, outputOption);
            if (dryRun == manual)
            {
                WriteFailure(
                    context.Output,
                    AutomationErrorCode.InvalidRunMode,
                    "Specify exactly one of --dry-run or --manual.",
                    jsonOutput,
                    "automation.run",
                    automationName);
                return 1;
            }

            CliEnvironmentSnapshot snapshot = context.WorkspaceSnapshotProvider(
                parseResult.GetValue(context.GlobalOptions.Workspace));
            context.WriteVerboseDiagnostics(parseResult, "automation run", snapshot, !jsonOutput);
            AutomationCatalog catalog = AutomationCatalog.Load(snapshot.Workspace);
            if (!catalog.TryGet(automationName, out AutomationCatalogItem? item) || item is null)
            {
                WriteFailure(
                    context.Output,
                    AutomationErrorCode.NotFound,
                    "Valid workspace-local automation was not found. Run automation validate for diagnostics.",
                    jsonOutput,
                    "automation.run",
                    automationName);
                return 1;
            }

            AutomationPlan plan = new(
                item,
                snapshot.Workspace.RootPath,
                AutomationManifestValidator.CreateSchedulePreview(item.Manifest.Trigger!),
                dryRun ? AutomationRunMode.DryRun : AutomationRunMode.Manual);
            if (dryRun)
            {
                if (jsonOutput)
                {
                    new AutomationJsonRenderer(context.Output).WriteDryRun(plan);
                }
                else
                {
                    new AutomationTextRenderer(context.Output).WriteDryRun(plan);
                }

                return 0;
            }

            context.TryWriteCommandLog("automation run", snapshot);
            string automationRunId = AutomationRunIdGenerator.Create(context.Dependencies.UtcNowProvider());
            AutomationRunMetadata metadata = new(
                item.Manifest.Name!,
                automationRunId,
                AutomationRunMode.Manual,
                item.Source.Path,
                item.Manifest.Target!.Type!);
            AutomationRunResult result;
            try
            {
                result = string.Equals(
                    item.Manifest.Target.Type,
                    AutomationTargetType.Pipeline,
                    StringComparison.Ordinal)
                    ? ExecutePipelineTarget(context, parseResult, snapshot, item.Manifest.Target, metadata)
                    : ExecuteQueueTarget(context, parseResult, snapshot, item.Manifest.Target, metadata);
            }
            catch (Exception exception) when (IsStoreException(exception) || exception is JsonException)
            {
                WriteFailure(
                    context.Output,
                    AutomationErrorCode.ExecutionFailed,
                    "Manual automation could not create or read its queue/job artifacts.",
                    jsonOutput,
                    "automation.run",
                    automationName);
                return 1;
            }

            if (jsonOutput)
            {
                new AutomationJsonRenderer(context.Output).WriteRunResult(result);
            }
            else
            {
                new AutomationTextRenderer(context.Output).WriteRunResult(result);
            }

            return result.ExitCode;
        });
        return command;
    }

    private static AutomationRunResult ExecuteQueueTarget(
        CliCommandContext context,
        ParseResult parseResult,
        CliEnvironmentSnapshot snapshot,
        AutomationTarget target,
        AutomationRunMetadata runMetadata)
    {
        bool skillTarget = string.Equals(target.Type, AutomationTargetType.Skill, StringComparison.Ordinal) ||
            (string.Equals(target.Type, AutomationTargetType.Queue, StringComparison.Ordinal) &&
             string.Equals(target.Family, "skill", StringComparison.Ordinal));
        string family = skillTarget ? TaskQueueCommandFamily.Skill : TaskQueueCommandFamily.Exec;
        TaskQueueRequest request = new(
            family,
            target.Task!,
            snapshot.Workspace.RootPath,
            target.Cwd,
            Skill: skillTarget ? target.Name : null,
            Expert: skillTarget ? null : target.Expert,
            ReportMode: target.Report ?? "none",
            Automation: runMetadata);
        DateTimeOffset nowUtc = context.Dependencies.UtcNowProvider();
        TaskQueueItem pending = TaskQueueItem.CreatePending(
            TaskQueueIdGenerator.Create(nowUtc),
            nowUtc,
            request,
            warnings: [$"automation={runMetadata.Automation};run={runMetadata.RunId};target={runMetadata.TargetType}"]);
        TaskQueueStore queueStore = TaskQueueStore.Create(snapshot);
        queueStore.Create(pending);

        List<string> arguments = [
            "queue", "run", pending.QueueId,
            "--workspace", snapshot.Workspace.RootPath,
            "--output", "json"
        ];
        AddGlobalFlags(context, parseResult, arguments);
        int exitCode = context.InvokeCurrentRoot(arguments).ExitCode;
        TaskQueueItem completed = queueStore.Read(pending.QueueId).Item ??
            throw new InvalidOperationException("Automation queue item could not be read after execution.");
        return new AutomationRunResult(
            runMetadata.RunId,
            runMetadata.Automation,
            runMetadata.TargetType,
            exitCode == 0 ? "succeeded" : "failed",
            exitCode,
            QueueIds: [completed.QueueId],
            JobIds: string.IsNullOrWhiteSpace(completed.LatestJobId) ? [] : [completed.LatestJobId],
            Summary: completed.Summary,
            Warnings: completed.Warnings);
    }

    private static AutomationRunResult ExecutePipelineTarget(
        CliCommandContext context,
        ParseResult parseResult,
        CliEnvironmentSnapshot snapshot,
        AutomationTarget target,
        AutomationRunMetadata runMetadata)
    {
        List<string> arguments = [
            "pipeline", "run", target.Name!,
            "--workspace", snapshot.Workspace.RootPath,
            "--output", "json",
            "--automation-name", runMetadata.Automation,
            "--automation-run-id", runMetadata.RunId,
            "--automation-source", runMetadata.SourcePath,
            "--automation-target", runMetadata.TargetType
        ];
        if (!string.IsNullOrWhiteSpace(target.Cwd))
        {
            arguments.Add("--cwd");
            arguments.Add(target.Cwd);
        }

        if (!string.IsNullOrWhiteSpace(target.Report))
        {
            arguments.Add("--report");
            arguments.Add(target.Report);
        }

        AddGlobalFlags(context, parseResult, arguments);
        arguments.Add("--");
        arguments.Add(target.Task!);
        CliCapturedInvocation delegated = context.InvokeCurrentRoot(arguments);
        using JsonDocument document = JsonDocument.Parse(delegated.Output);
        JsonElement root = document.RootElement;
        string? pipelineRunId = null;
        List<string> queueIds = [];
        List<string> jobIds = [];
        List<string> warnings = [];
        if (root.TryGetProperty("report", out JsonElement report))
        {
            if (report.TryGetProperty("runId", out JsonElement runIdElement))
            {
                pipelineRunId = runIdElement.GetString();
            }

            if (report.TryGetProperty("roles", out JsonElement roles))
            {
                foreach (JsonElement role in roles.EnumerateArray())
                {
                    if (role.TryGetProperty("queueId", out JsonElement queueId) &&
                        !string.IsNullOrWhiteSpace(queueId.GetString()))
                    {
                        queueIds.Add(queueId.GetString()!);
                    }

                    if (role.TryGetProperty("jobId", out JsonElement jobId) &&
                        !string.IsNullOrWhiteSpace(jobId.GetString()))
                    {
                        jobIds.Add(jobId.GetString()!);
                    }
                }
            }

            if (report.TryGetProperty("warnings", out JsonElement reportWarnings))
            {
                warnings.AddRange(reportWarnings.EnumerateArray()
                    .Select(value => DiagnosticSecretRedactor.Redact(value.GetString() ?? string.Empty)));
            }
        }

        return new AutomationRunResult(
            runMetadata.RunId,
            runMetadata.Automation,
            runMetadata.TargetType,
            delegated.ExitCode == 0 ? "succeeded" : "failed",
            delegated.ExitCode,
            queueIds,
            jobIds,
            pipelineRunId,
            delegated.ExitCode == 0 ? "Pipeline automation completed." : "Pipeline automation failed.",
            warnings);
    }

    private static void AddGlobalFlags(
        CliCommandContext context,
        ParseResult parseResult,
        List<string> arguments)
    {
        if (parseResult.GetValue(context.GlobalOptions.Verbose))
        {
            arguments.Add("--verbose");
        }

        if (context.IsTraceEnabled(parseResult))
        {
            arguments.Add("--trace");
        }
    }

    private static Option<string> CreateOutputOption()
    {
        Option<string> option = new("--output")
        {
            Description = "Select text or json output.",
            DefaultValueFactory = _ => "text",
        };
        CliCommandContext.AddTextJsonOutputValidator(option);
        return option;
    }

    private static bool IsJsonOutput(
        ParseResult parseResult,
        Option<bool> jsonOption,
        Option<string> outputOption)
    {
        return CliCommandContext.IsJsonOutputRequested(
            parseResult.GetValue(jsonOption),
            parseResult.GetValue(outputOption) ?? "text");
    }

    private static void WriteFailure(
        TextWriter output,
        string errorCode,
        string summary,
        bool jsonOutput,
        string type,
        string? automation = null)
    {
        if (jsonOutput)
        {
            new AutomationJsonRenderer(output).WriteFailure(type, errorCode, summary, automation);
            return;
        }

        new AutomationTextRenderer(output).WriteFailure(errorCode, summary);
    }

    private static bool IsStoreException(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException
            or InvalidOperationException;
    }
}
