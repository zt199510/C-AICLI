using System.CommandLine;
using CSharpAiCli.Application;
using CSharpAiCli.Core;

namespace CSharpAiCli.Cli;

internal sealed class PipelineCommandModule : ICliCommandModule
{
    public Command Create(CliCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Command command = new("pipeline", "Plan and run built-in local multi-role pipelines.");
        command.Subcommands.Add(CreateListCommand(context));
        command.Subcommands.Add(CreatePlanCommand(context));
        command.Subcommands.Add(CreateRunCommand(context));
        return command;
    }

    private static Command CreateListCommand(CliCommandContext context)
    {
        Command command = new("list", "List built-in pipelines without invoking a model or tools.");
        Option<bool> jsonOption = new("--json")
        {
            Description = "Write a single JSON pipeline catalog object.",
        };
        Option<string> outputOption = CreateOutputOption();
        command.Options.Add(jsonOption);
        command.Options.Add(outputOption);
        command.SetAction(parseResult =>
        {
            bool jsonOutput = CliCommandContext.IsJsonOutputRequested(
                parseResult.GetValue(jsonOption),
                parseResult.GetValue(outputOption) ?? "text");
            IReadOnlyList<PipelineManifest> pipelines = BuiltInPipelineCatalog.List();
            if (jsonOutput)
            {
                new PipelineJsonRenderer(context.Output).WriteList(pipelines);
            }
            else
            {
                new PipelineTextRenderer(context.Output).WriteList(pipelines);
            }

            return 0;
        });
        return command;
    }

    private static Command CreatePlanCommand(CliCommandContext context)
    {
        Command command = new("plan", "Render an auditable pipeline plan without invoking a model or tools.");
        Argument<string> nameArgument = new("pipeline")
        {
            Description = "Built-in pipeline name.",
        };
        Argument<string[]> taskArgument = new("task")
        {
            Description = "Task text. Tokens after -- are joined so quoting is optional.",
            Arity = ArgumentArity.ZeroOrMore,
        };
        Option<string> cwdOption = new("--cwd")
        {
            Description = "Use a working context path when the pipeline runs.",
        };
        Option<bool> jsonOption = new("--json")
        {
            Description = "Write a single JSON pipeline plan object.",
        };
        Option<string> outputOption = CreateOutputOption();
        command.Arguments.Add(nameArgument);
        command.Arguments.Add(taskArgument);
        command.Options.Add(cwdOption);
        command.Options.Add(jsonOption);
        command.Options.Add(outputOption);
        command.SetAction(parseResult =>
        {
            string pipelineName = parseResult.GetValue(nameArgument) ?? string.Empty;
            string task = string.Join(" ", parseResult.GetValue(taskArgument) ?? []).Trim();
            bool jsonOutput = CliCommandContext.IsJsonOutputRequested(
                parseResult.GetValue(jsonOption),
                parseResult.GetValue(outputOption) ?? "text");
            if (!BuiltInPipelineCatalog.TryGet(pipelineName, out PipelineManifest? pipeline) || pipeline is null)
            {
                WriteFailure(context.Output, "pipeline-not-found", "Built-in pipeline was not found.", jsonOutput, "pipeline.plan", pipelineName);
                return 1;
            }

            if (string.IsNullOrWhiteSpace(task))
            {
                WriteFailure(context.Output, "pipeline-invalid-request", "Pipeline task is empty.", jsonOutput, "pipeline.plan", pipelineName);
                return 1;
            }

            string? workspacePath = parseResult.GetValue(context.GlobalOptions.Workspace);
            string? cwd = parseResult.GetValue(cwdOption);
            CliEnvironmentSnapshot snapshot = context.Dependencies.SnapshotProvider(workspacePath, cwd);
            context.WriteVerboseDiagnostics(parseResult, "pipeline plan", snapshot, !jsonOutput);
            PipelinePlan plan = new(pipeline, task, snapshot.Workspace.RootPath, cwd);
            if (jsonOutput)
            {
                new PipelineJsonRenderer(context.Output).WritePlan(plan);
            }
            else
            {
                new PipelineTextRenderer(context.Output).WritePlan(plan);
            }

            return 0;
        });
        return command;
    }

    private static Command CreateRunCommand(CliCommandContext context)
    {
        Command command = new("run", "Run a built-in pipeline sequentially through queue, job, exec, and skills boundaries.");
        Argument<string> nameArgument = new("pipeline")
        {
            Description = "Built-in pipeline name.",
        };
        Argument<string[]> taskArgument = new("task")
        {
            Description = "Task text. Tokens after -- are joined so quoting is optional.",
            Arity = ArgumentArity.ZeroOrMore,
        };
        Option<string> cwdOption = new("--cwd")
        {
            Description = "Use a working context path for every role.",
        };
        Option<string> reportOption = new("--report")
        {
            Description = "Select none or markdown aggregate report output.",
            DefaultValueFactory = _ => "none",
        };
        reportOption.Validators.Add(result =>
        {
            string report = result.GetValueOrDefault<string>() ?? "none";
            if (!ExecReportModeParser.TryParse(report, out _))
            {
                result.AddError("Invalid value for --report. Allowed values are none and markdown.");
            }
        });
        Option<bool> jsonOption = new("--json")
        {
            Description = "Write a single JSON pipeline result object.",
        };
        Option<string> outputOption = CreateOutputOption();
        Option<string> automationNameOption = new("--automation-name")
        {
            Description = "Carry local automation correlation metadata into pipeline queue/job artifacts.",
        };
        Option<string> automationRunIdOption = new("--automation-run-id")
        {
            Description = "Carry a validated local automation run id.",
        };
        Option<string> automationSourceOption = new("--automation-source")
        {
            Description = "Carry the redacted workspace-local automation source path.",
        };
        Option<string> automationTargetOption = new("--automation-target")
        {
            Description = "Carry the local automation target type.",
        };
        command.Arguments.Add(nameArgument);
        command.Arguments.Add(taskArgument);
        command.Options.Add(cwdOption);
        command.Options.Add(reportOption);
        command.Options.Add(jsonOption);
        command.Options.Add(outputOption);
        command.Options.Add(automationNameOption);
        command.Options.Add(automationRunIdOption);
        command.Options.Add(automationSourceOption);
        command.Options.Add(automationTargetOption);
        command.SetAction(parseResult =>
        {
            string pipelineName = parseResult.GetValue(nameArgument) ?? string.Empty;
            string task = string.Join(" ", parseResult.GetValue(taskArgument) ?? []).Trim();
            bool jsonOutput = CliCommandContext.IsJsonOutputRequested(
                parseResult.GetValue(jsonOption),
                parseResult.GetValue(outputOption) ?? "text");
            if (!BuiltInPipelineCatalog.TryGet(pipelineName, out PipelineManifest? pipeline) || pipeline is null)
            {
                WriteFailure(context.Output, "pipeline-not-found", "Built-in pipeline was not found.", jsonOutput, "pipeline.run", pipelineName);
                return 1;
            }

            if (string.IsNullOrWhiteSpace(task))
            {
                WriteFailure(context.Output, "pipeline-invalid-request", "Pipeline task is empty.", jsonOutput, "pipeline.run", pipelineName);
                return 1;
            }

            string? workspacePath = parseResult.GetValue(context.GlobalOptions.Workspace);
            string? cwd = parseResult.GetValue(cwdOption);
            string reportMode = parseResult.GetValue(reportOption) ?? "none";
            string? automationName = parseResult.GetValue(automationNameOption);
            string? automationRunId = parseResult.GetValue(automationRunIdOption);
            string? automationSource = parseResult.GetValue(automationSourceOption);
            string? automationTarget = parseResult.GetValue(automationTargetOption);
            bool anyAutomationMetadata = !string.IsNullOrWhiteSpace(automationName) ||
                !string.IsNullOrWhiteSpace(automationRunId) ||
                !string.IsNullOrWhiteSpace(automationSource) ||
                !string.IsNullOrWhiteSpace(automationTarget);
            bool completeAutomationMetadata = !string.IsNullOrWhiteSpace(automationName) &&
                AutomationRunIdGenerator.IsValid(automationRunId) &&
                !string.IsNullOrWhiteSpace(automationSource) &&
                string.Equals(automationTarget, AutomationTargetType.Pipeline, StringComparison.Ordinal);
            if (anyAutomationMetadata && !completeAutomationMetadata)
            {
                WriteFailure(
                    context.Output,
                    AutomationErrorCode.ManifestInvalid,
                    "Pipeline automation correlation metadata is incomplete or invalid.",
                    jsonOutput,
                    "pipeline.run",
                    pipelineName);
                return 1;
            }

            AutomationRunMetadata? automationMetadata = completeAutomationMetadata
                ? new AutomationRunMetadata(
                    automationName!,
                    automationRunId!,
                    AutomationRunMode.Manual,
                    automationSource!,
                    automationTarget!)
                : null;
            CliEnvironmentSnapshot snapshot = context.Dependencies.SnapshotProvider(workspacePath, cwd);
            context.TryWriteCommandLog("pipeline run", snapshot);
            context.WriteVerboseDiagnostics(parseResult, "pipeline run", snapshot, !jsonOutput);
            PipelinePlan plan = new(pipeline, task, snapshot.Workspace.RootPath, cwd);
            DelegatePipelineRoleExecutor roleExecutor = new(
                request => ExecutePipelineRole(
                    context,
                    request,
                    automationMetadata,
                    parseResult.GetValue(context.GlobalOptions.Verbose),
                    context.IsTraceEnabled(parseResult)));
            PipelineFinalReport report;
            try
            {
                report = new PipelineRunner(roleExecutor, context.Dependencies.UtcNowProvider).Run(plan);
            }
            catch (Exception exception) when (IsStoreException(exception))
            {
                WriteFailure(
                    context.Output,
                    "pipeline-execution-failed",
                    "Pipeline execution could not create or read its queue/job records.",
                    jsonOutput,
                    "pipeline.run",
                    pipelineName);
                return 1;
            }

            if (jsonOutput)
            {
                new PipelineJsonRenderer(context.Output).WriteFinalReport(report);
            }
            else
            {
                PipelineTextRenderer renderer = new(context.Output);
                renderer.WriteFinalReport(report);
                if (string.Equals(reportMode, "markdown", StringComparison.OrdinalIgnoreCase))
                {
                    context.Output.WriteLine();
                    renderer.WriteMarkdown(report);
                }
            }

            return string.Equals(report.Status, PipelineStatus.Succeeded, StringComparison.Ordinal) ? 0 : 1;
        });
        return command;
    }

    private static PipelineRoleExecutionResult ExecutePipelineRole(
        CliCommandContext context,
        PipelineRoleExecutionRequest request,
        AutomationRunMetadata? automationMetadata,
        bool verbose,
        bool trace)
    {
        CliEnvironmentSnapshot roleSnapshot = context.Dependencies.SnapshotProvider(
            request.Plan.WorkspaceRoot,
            request.Plan.Cwd);
        TaskQueueStore queueStore = TaskQueueStore.Create(roleSnapshot);
        DateTimeOffset nowUtc = context.Dependencies.UtcNowProvider();
        string family = string.Equals(
            request.Step.CommandFamily,
            PipelineCommandFamily.Skill,
            StringComparison.Ordinal)
            ? TaskQueueCommandFamily.Skill
            : TaskQueueCommandFamily.Exec;
        TaskQueueRequest queueRequest = new(
            family,
            request.Task,
            request.Plan.WorkspaceRoot,
            request.Plan.Cwd,
            Skill: request.Step.Skill,
            Expert: family == TaskQueueCommandFamily.Exec ? request.Step.Expert : null,
            ReportMode: "none",
            Automation: automationMetadata);
        TaskQueueItem pending = TaskQueueItem.CreatePending(
            TaskQueueIdGenerator.Create(nowUtc),
            nowUtc,
            queueRequest,
            warnings: [$"pipeline={request.RunId};step={request.Step.StepId};role={request.Step.Role}"]);
        queueStore.Create(pending);

        List<string> arguments = [
            "queue", "run", pending.QueueId,
            "--workspace", request.Plan.WorkspaceRoot,
            "--output", "json"
        ];
        if (verbose)
        {
            arguments.Add("--verbose");
        }

        if (trace)
        {
            arguments.Add("--trace");
        }

        context.InvokeCurrentRoot(arguments);
        TaskQueueItem completed = queueStore.Read(pending.QueueId).Item ??
            throw new InvalidOperationException("Pipeline queue item could not be read after execution.");
        JobRecord? job = null;
        if (!string.IsNullOrWhiteSpace(completed.LatestJobId))
        {
            job = JobRecordStore.Create(roleSnapshot).Read(completed.LatestJobId).Record;
        }

        return new PipelineRoleExecutionResult(completed, job);
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

    private static void WriteFailure(
        TextWriter output,
        string errorCode,
        string summary,
        bool jsonOutput,
        string type,
        string? pipeline = null)
    {
        if (jsonOutput)
        {
            new PipelineJsonRenderer(output).WriteFailure(type, errorCode, summary, pipeline);
            return;
        }

        output.WriteLine("status: failed");
        output.WriteLine($"errorCode: {errorCode}");
        output.WriteLine("summary:");
        output.WriteLine(summary);
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
