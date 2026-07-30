using System.CommandLine;
using System.Text.Json;
using CSharpAiCli.Core;

namespace CSharpAiCli.Cli;

internal sealed class QueueCommandModule : ICliCommandModule
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Command Create(CliCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Command command = new("queue", "Manage the local task queue.");
        command.Subcommands.Add(CreateAddCommand(context));
        command.Subcommands.Add(CreateListCommand(context));
        command.Subcommands.Add(CreateShowCommand(context));
        command.Subcommands.Add(CreateCancelCommand(context));
        command.Subcommands.Add(CreateCleanupCommand(context));
        command.Subcommands.Add(CreateRunCommand(context));
        return command;
    }

    private static Command CreateAddCommand(CliCommandContext context)
    {
        Command command = new("add", "Add a pending request to the local task queue.");
        command.Subcommands.Add(CreateAddExecCommand(context));
        command.Subcommands.Add(CreateAddSkillCommand(context));
        return command;
    }

    private static Command CreateAddExecCommand(CliCommandContext context)
    {
        Command command = new("exec", "Add a pending exec request.");
        Argument<string[]> taskArgument = new("task")
        {
            Description = "Task text. Tokens after -- are joined so quoting is optional.",
            Arity = ArgumentArity.ZeroOrMore,
        };
        Option<string> cwdOption = new("--cwd")
        {
            Description = "Use a working context path when the queue item runs.",
        };
        Option<string> expertOption = new("--expert")
        {
            Description = "Select a local expert profile when the queue item runs.",
        };
        Option<string> reportOption = new("--report")
        {
            Description = "Select none or markdown report output when the queue item runs.",
            DefaultValueFactory = _ => "none",
        };
        Option<bool> jsonOption = new("--json")
        {
            Description = "Write a single JSON queue add object.",
        };
        Option<string> outputOption = CreateOutputOption();
        CliCommandContext.AddTextJsonOutputValidator(outputOption);
        expertOption.Validators.Add(result =>
        {
            string? expert = result.GetValueOrDefault<string>();
            if (!string.IsNullOrWhiteSpace(expert) && !ExpertProfileCatalog.TryGet(expert, out _))
            {
                result.AddError(
                    "Invalid value for --expert. Allowed values are bugfix, refactor, reviewer, security, and tester.");
            }
        });
        reportOption.Validators.Add(result =>
        {
            string report = result.GetValueOrDefault<string>() ?? "none";
            if (!ExecReportModeParser.TryParse(report, out _))
            {
                result.AddError("Invalid value for --report. Allowed values are none and markdown.");
            }
        });
        command.Arguments.Add(taskArgument);
        command.Options.Add(cwdOption);
        command.Options.Add(expertOption);
        command.Options.Add(reportOption);
        command.Options.Add(jsonOption);
        command.Options.Add(outputOption);
        command.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(context.GlobalOptions.Workspace);
            string? cwd = parseResult.GetValue(cwdOption);
            string task = string.Join(" ", parseResult.GetValue(taskArgument) ?? []).Trim();
            string? expert = parseResult.GetValue(expertOption);
            string report = parseResult.GetValue(reportOption) ?? "none";
            bool jsonOutput = IsJsonOutput(parseResult, jsonOption, outputOption);
            CliEnvironmentSnapshot snapshot = context.Dependencies.SnapshotProvider(workspacePath, cwd);
            context.WriteVerboseDiagnostics(parseResult, "queue add exec", snapshot, !jsonOutput);
            if (string.IsNullOrWhiteSpace(task))
            {
                WriteFailure(
                    context.Output,
                    TaskQueueErrorCode.InvalidRequest,
                    "Queue task is empty.",
                    jsonOutput,
                    "queue.add");
                return 1;
            }

            try
            {
                DateTimeOffset nowUtc = context.Dependencies.UtcNowProvider();
                TaskQueueRequest request = new(
                    TaskQueueCommandFamily.Exec,
                    task,
                    snapshot.Workspace.RootPath,
                    cwd,
                    Expert: expert,
                    ReportMode: report);
                IReadOnlyList<string> warnings = string.Equals(task, request.Task, StringComparison.Ordinal)
                    ? []
                    : ["Secret-like task content was stored in redacted form."];
                TaskQueueItem item = TaskQueueItem.CreatePending(
                    TaskQueueIdGenerator.Create(nowUtc),
                    nowUtc,
                    request,
                    warnings);
                TaskQueueStore.Create(snapshot).Create(item);
                WriteAdded(context.Output, item, jsonOutput);
                return 0;
            }
            catch (Exception exception) when (IsStoreException(exception))
            {
                WriteFailure(
                    context.Output,
                    TaskQueueErrorCode.RecordWriteFailed,
                    "Queue record could not be created.",
                    jsonOutput,
                    "queue.add");
                return 1;
            }
        });
        return command;
    }

    private static Command CreateAddSkillCommand(CliCommandContext context)
    {
        Command command = new("skill", "Add a pending skills run request.");
        Argument<string> nameArgument = new("name") { Description = "Skill pack name." };
        Argument<string[]> taskArgument = new("task")
        {
            Description = "Task text. Tokens after -- are joined so quoting is optional.",
            Arity = ArgumentArity.ZeroOrMore,
        };
        Option<string> cwdOption = new("--cwd")
        {
            Description = "Use a working context path when the queue item runs.",
        };
        Option<string> reportOption = new("--report")
        {
            Description = "Override the skill report mode: none or markdown.",
        };
        Option<bool> jsonOption = new("--json")
        {
            Description = "Write a single JSON queue add object.",
        };
        Option<string> outputOption = CreateOutputOption();
        CliCommandContext.AddTextJsonOutputValidator(outputOption);
        reportOption.Validators.Add(result =>
        {
            string? report = result.GetValueOrDefault<string>();
            if (!string.IsNullOrWhiteSpace(report) && !ExecReportModeParser.TryParse(report, out _))
            {
                result.AddError("Invalid value for --report. Allowed values are none and markdown.");
            }
        });
        command.Arguments.Add(nameArgument);
        command.Arguments.Add(taskArgument);
        command.Options.Add(cwdOption);
        command.Options.Add(reportOption);
        command.Options.Add(jsonOption);
        command.Options.Add(outputOption);
        command.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(context.GlobalOptions.Workspace);
            string? cwd = parseResult.GetValue(cwdOption);
            string skill = parseResult.GetValue(nameArgument) ?? string.Empty;
            string task = string.Join(" ", parseResult.GetValue(taskArgument) ?? []).Trim();
            string? report = parseResult.GetValue(reportOption);
            bool jsonOutput = IsJsonOutput(parseResult, jsonOption, outputOption);
            CliEnvironmentSnapshot snapshot = context.Dependencies.SnapshotProvider(workspacePath, cwd);
            context.WriteVerboseDiagnostics(parseResult, "queue add skill", snapshot, !jsonOutput);
            if (string.IsNullOrWhiteSpace(skill) || string.IsNullOrWhiteSpace(task))
            {
                WriteFailure(
                    context.Output,
                    TaskQueueErrorCode.InvalidRequest,
                    "Queue skill name and task are required.",
                    jsonOutput,
                    "queue.add");
                return 1;
            }

            try
            {
                DateTimeOffset nowUtc = context.Dependencies.UtcNowProvider();
                TaskQueueRequest request = new(
                    TaskQueueCommandFamily.Skill,
                    task,
                    snapshot.Workspace.RootPath,
                    cwd,
                    Skill: skill,
                    ReportMode: report);
                IReadOnlyList<string> warnings = string.Equals(task, request.Task, StringComparison.Ordinal)
                    ? []
                    : ["Secret-like task content was stored in redacted form."];
                TaskQueueItem item = TaskQueueItem.CreatePending(
                    TaskQueueIdGenerator.Create(nowUtc),
                    nowUtc,
                    request,
                    warnings);
                TaskQueueStore.Create(snapshot).Create(item);
                WriteAdded(context.Output, item, jsonOutput);
                return 0;
            }
            catch (Exception exception) when (IsStoreException(exception))
            {
                WriteFailure(
                    context.Output,
                    TaskQueueErrorCode.RecordWriteFailed,
                    "Queue record could not be created.",
                    jsonOutput,
                    "queue.add");
                return 1;
            }
        });
        return command;
    }

    private static Command CreateListCommand(CliCommandContext context)
    {
        Command command = new("list", "List local queue items.");
        Option<bool> jsonOption = new("--json")
        {
            Description = "Write a single JSON queue list object.",
        };
        Option<string> outputOption = CreateOutputOption();
        Option<int?> limitOption = new("--limit")
        {
            Description = "Maximum number of queue items to list.",
        };
        Option<string> statusOption = new("--status")
        {
            Description = "Filter by pending, running, succeeded, failed, or canceled status.",
        };
        CliCommandContext.AddTextJsonOutputValidator(outputOption);
        CliCommandContext.AddPositiveIntegerValidator(limitOption, "--limit");
        statusOption.Validators.Add(result =>
        {
            string? status = result.GetValueOrDefault<string>();
            if (!string.IsNullOrWhiteSpace(status) && !TaskQueueStatus.IsKnown(status))
            {
                result.AddError(
                    "Invalid value for --status. Allowed values are pending, running, succeeded, failed, and canceled.");
            }
        });
        command.Options.Add(jsonOption);
        command.Options.Add(outputOption);
        command.Options.Add(limitOption);
        command.Options.Add(statusOption);
        command.SetAction(parseResult =>
        {
            bool jsonOutput = IsJsonOutput(parseResult, jsonOption, outputOption);
            int? limit = parseResult.GetValue(limitOption);
            string? status = parseResult.GetValue(statusOption);
            CliEnvironmentSnapshot snapshot = GetSnapshot(context, parseResult, "queue list", jsonOutput);
            TaskQueueListResult result = TaskQueueStore.Create(snapshot).List(limit, status);
            if (jsonOutput)
            {
                new TaskQueueJsonRenderer(context.Output).WriteList(result);
            }
            else
            {
                new TaskQueueTextRenderer(context.Output).WriteList(result);
            }

            return 0;
        });
        return command;
    }

    private static Command CreateShowCommand(CliCommandContext context)
    {
        Command command = new("show", "Show one local queue item.");
        Argument<string> idArgument = new("queue-id") { Description = "Queue id." };
        Option<bool> jsonOption = new("--json")
        {
            Description = "Write a single JSON queue show object.",
        };
        Option<string> outputOption = CreateOutputOption();
        CliCommandContext.AddTextJsonOutputValidator(outputOption);
        command.Arguments.Add(idArgument);
        command.Options.Add(jsonOption);
        command.Options.Add(outputOption);
        command.SetAction(parseResult =>
        {
            string queueId = parseResult.GetValue(idArgument) ?? string.Empty;
            bool jsonOutput = IsJsonOutput(parseResult, jsonOption, outputOption);
            CliEnvironmentSnapshot snapshot = GetSnapshot(context, parseResult, "queue show", jsonOutput);
            TaskQueueReadResult result = TaskQueueStore.Create(snapshot).Read(queueId);
            if (!result.Succeeded || result.Item is null)
            {
                WriteFailure(context.Output, result.Diagnostic, jsonOutput, "queue.show");
                return 1;
            }

            if (jsonOutput)
            {
                new TaskQueueJsonRenderer(context.Output).WriteShow(result.Item);
            }
            else
            {
                new TaskQueueTextRenderer(context.Output).WriteShow(result.Item);
            }

            return 0;
        });
        return command;
    }

    private static Command CreateCancelCommand(CliCommandContext context)
    {
        Command command = new("cancel", "Cancel one pending queue item.");
        Argument<string> idArgument = new("queue-id") { Description = "Queue id." };
        Option<bool> jsonOption = new("--json")
        {
            Description = "Write a single JSON queue cancel object.",
        };
        Option<string> outputOption = CreateOutputOption();
        CliCommandContext.AddTextJsonOutputValidator(outputOption);
        command.Arguments.Add(idArgument);
        command.Options.Add(jsonOption);
        command.Options.Add(outputOption);
        command.SetAction(parseResult =>
        {
            string queueId = parseResult.GetValue(idArgument) ?? string.Empty;
            bool jsonOutput = IsJsonOutput(parseResult, jsonOption, outputOption);
            CliEnvironmentSnapshot snapshot = GetSnapshot(context, parseResult, "queue cancel", jsonOutput);
            TaskQueueTransitionResult result = TaskQueueStore.Create(snapshot)
                .Cancel(queueId, context.Dependencies.UtcNowProvider());
            if (!result.Succeeded || result.Item is null)
            {
                WriteFailure(context.Output, result.Diagnostic, jsonOutput, "queue.cancel");
                return 1;
            }

            if (jsonOutput)
            {
                new TaskQueueJsonRenderer(context.Output).WriteTransition("queue.cancel", result.Item);
            }
            else
            {
                new TaskQueueTextRenderer(context.Output)
                    .WriteTransition("C# AI CLI queue item canceled", result.Item);
            }

            return 0;
        });
        return command;
    }

    private static Command CreateCleanupCommand(CliCommandContext context)
    {
        Command command = new("cleanup", "Delete old terminal queue items.");
        Option<string> statusOption = new("--status")
        {
            Description = "Terminal status to delete: succeeded, failed, or canceled.",
        };
        Option<int?> olderThanDaysOption = new("--older-than-days")
        {
            Description = "Delete matching items completed at least this many days ago.",
            DefaultValueFactory = _ => 30,
        };
        Option<bool> jsonOption = new("--json")
        {
            Description = "Write a single JSON queue cleanup object.",
        };
        Option<string> outputOption = CreateOutputOption();
        statusOption.Validators.Add(result =>
        {
            string? status = result.GetValueOrDefault<string>();
            if (!string.IsNullOrWhiteSpace(status) && !TaskQueueStatus.IsTerminal(status))
            {
                result.AddError("Invalid value for --status. Allowed values are succeeded, failed, and canceled.");
            }
        });
        CliCommandContext.AddPositiveIntegerValidator(olderThanDaysOption, "--older-than-days");
        CliCommandContext.AddTextJsonOutputValidator(outputOption);
        command.Options.Add(statusOption);
        command.Options.Add(olderThanDaysOption);
        command.Options.Add(jsonOption);
        command.Options.Add(outputOption);
        command.SetAction(parseResult =>
        {
            string? status = parseResult.GetValue(statusOption);
            int olderThanDays = parseResult.GetValue(olderThanDaysOption) ?? 30;
            bool jsonOutput = IsJsonOutput(parseResult, jsonOption, outputOption);
            CliEnvironmentSnapshot snapshot = GetSnapshot(context, parseResult, "queue cleanup", jsonOutput);
            if (string.IsNullOrWhiteSpace(status))
            {
                WriteFailure(
                    context.Output,
                    TaskQueueErrorCode.UnsafeCleanupStatus,
                    "Cleanup requires --status succeeded, failed, or canceled.",
                    jsonOutput,
                    "queue.cleanup");
                return 1;
            }

            TaskQueueCleanupResult result = TaskQueueStore.Create(snapshot).Cleanup(
                status,
                context.Dependencies.UtcNowProvider().AddDays(-olderThanDays));
            if (jsonOutput)
            {
                new TaskQueueJsonRenderer(context.Output).WriteCleanup(result);
            }
            else
            {
                new TaskQueueTextRenderer(context.Output).WriteCleanup(result);
            }

            return result.Succeeded ? 0 : 1;
        });
        return command;
    }

    private static Command CreateRunCommand(CliCommandContext context)
    {
        Command command = new("run", "Run one queued request through the controlled exec or skills path.");
        Argument<string> idArgument = new("queue-id") { Description = "Queue id." };
        Option<bool> jsonOption = new("--json")
        {
            Description = "Write newline-delimited JSON execution events and queue state.",
        };
        Option<string> outputOption = CreateOutputOption();
        CliCommandContext.AddTextJsonOutputValidator(outputOption);
        command.Arguments.Add(idArgument);
        command.Options.Add(jsonOption);
        command.Options.Add(outputOption);
        command.SetAction(parseResult =>
        {
            string queueId = parseResult.GetValue(idArgument) ?? string.Empty;
            bool jsonOutput = IsJsonOutput(parseResult, jsonOption, outputOption);
            CliEnvironmentSnapshot queueSnapshot = GetSnapshot(context, parseResult, "queue run", jsonOutput);
            TaskQueueStore queueStore = TaskQueueStore.Create(queueSnapshot);
            TaskQueueReadResult read = queueStore.Read(queueId);
            if (!read.Succeeded || read.Item is null)
            {
                WriteFailure(context.Output, read.Diagnostic, jsonOutput, "queue.run");
                return 1;
            }

            TaskQueueTransitionResult started = queueStore.Start(
                queueId,
                context.Dependencies.UtcNowProvider());
            if (!started.Succeeded || started.Item is null)
            {
                WriteFailure(context.Output, started.Diagnostic, jsonOutput, "queue.run");
                return 1;
            }

            TaskQueueItem runningItem = started.Item;
            int attempt = runningItem.Attempts[^1].Attempt;
            if (jsonOutput)
            {
                new TaskQueueJsonRenderer(context.Output).WriteTransition("queue.run.started", runningItem);
            }
            else
            {
                new TaskQueueTextRenderer(context.Output)
                    .WriteTransition("C# AI CLI queue run started", runningItem);
            }

            JobRecordStore? jobStore = null;
            HashSet<string> existingJobIds = new(StringComparer.Ordinal);
            int innerExitCode = 1;
            string? executionErrorCode = null;
            string? executionSummary = null;
            try
            {
                CliEnvironmentSnapshot executionSnapshot = context.Dependencies.SnapshotProvider(
                    runningItem.Request.WorkspaceRoot,
                    runningItem.Request.Cwd);
                jobStore = JobRecordStore.Create(executionSnapshot);
                existingJobIds = jobStore.List().Records
                    .Select(record => record.JobId)
                    .ToHashSet(StringComparer.Ordinal);
                IReadOnlyList<string> innerArguments = BuildDelegatedArguments(
                    runningItem,
                    jsonOutput,
                    parseResult.GetValue(context.GlobalOptions.Verbose),
                    context.IsTraceEnabled(parseResult));
                innerExitCode = context.RootCommand.Parse(innerArguments).Invoke();
            }
            catch (Exception)
            {
                executionErrorCode = TaskQueueErrorCode.ExecutionFailed;
                executionSummary =
                    "Queued execution failed before the delegated command reached a terminal result.";
            }

            JobRecord? jobRecord = null;
            if (jobStore is not null)
            {
                try
                {
                    jobRecord = jobStore.List().Records
                        .Where(record => !existingJobIds.Contains(record.JobId))
                        .Where(record => string.Equals(record.JobName, queueId, StringComparison.Ordinal))
                        .OrderByDescending(record => record.CreatedAtUtc)
                        .ThenByDescending(record => record.JobId, StringComparer.Ordinal)
                        .FirstOrDefault();
                    if (jobRecord?.Status == JobStatus.Running)
                    {
                        executionErrorCode ??= TaskQueueErrorCode.ExecutionFailed;
                        executionSummary ??=
                            "Queued execution stopped before the delegated command finalized its job record.";
                        JobRecord failedJob = jobRecord.WithStatus(
                            JobStatus.Failed,
                            context.Dependencies.UtcNowProvider(),
                            exitCode: 1,
                            stopReason: "queue-execution-failed",
                            errorCode: executionErrorCode,
                            summary: executionSummary);
                        jobStore.Update(failedJob);
                        jobRecord = failedJob;
                    }

                    if (jobRecord is not null && runningItem.Request.Automation is not null)
                    {
                        jobRecord = jobRecord.WithAutomation(runningItem.Request.Automation);
                        jobStore.Update(jobRecord);
                    }
                }
                catch (Exception exception) when (IsStoreException(exception))
                {
                    executionErrorCode ??= TaskQueueErrorCode.ExecutionFailed;
                    executionSummary ??= "Queued execution job history could not be finalized.";
                }
            }

            string? completionErrorCode = executionErrorCode ?? jobRecord?.ErrorCode;
            string? completionSummary = executionSummary ?? jobRecord?.Summary;
            int completionExitCode = innerExitCode;
            if (executionErrorCode is not null)
            {
                completionExitCode = 1;
            }
            else if (jobRecord is null)
            {
                completionExitCode = 1;
                completionErrorCode = TaskQueueErrorCode.JobRecordMissing;
                completionSummary = "Queued execution did not produce the required job history record.";
            }

            TaskQueueTransitionResult completed = queueStore.Complete(
                queueId,
                attempt,
                context.Dependencies.UtcNowProvider(),
                completionExitCode,
                jobRecord?.JobId,
                completionErrorCode,
                completionSummary);
            if (!completed.Succeeded || completed.Item is null)
            {
                WriteFailure(context.Output, completed.Diagnostic, jsonOutput, "queue.run");
                return 1;
            }

            if (jsonOutput)
            {
                new TaskQueueJsonRenderer(context.Output).WriteTransition("queue.run.completed", completed.Item);
            }
            else
            {
                context.Output.WriteLine();
                new TaskQueueTextRenderer(context.Output)
                    .WriteTransition("C# AI CLI queue run completed", completed.Item);
            }

            return completionExitCode;
        });
        return command;
    }

    private static Option<string> CreateOutputOption()
    {
        return new Option<string>("--output")
        {
            Description = "Select text or json output.",
            DefaultValueFactory = _ => "text",
        };
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

    private static CliEnvironmentSnapshot GetSnapshot(
        CliCommandContext context,
        ParseResult parseResult,
        string commandName,
        bool jsonOutput)
    {
        string? workspacePath = parseResult.GetValue(context.GlobalOptions.Workspace);
        CliEnvironmentSnapshot snapshot = context.WorkspaceSnapshotProvider(workspacePath);
        context.WriteVerboseDiagnostics(parseResult, commandName, snapshot, !jsonOutput);
        return snapshot;
    }

    private static void WriteAdded(TextWriter output, TaskQueueItem item, bool jsonOutput)
    {
        if (jsonOutput)
        {
            new TaskQueueJsonRenderer(output).WriteAdded(item);
        }
        else
        {
            new TaskQueueTextRenderer(output).WriteAdded(item);
        }
    }

    private static IReadOnlyList<string> BuildDelegatedArguments(
        TaskQueueItem item,
        bool jsonOutput,
        bool verbose,
        bool trace)
    {
        List<string> arguments = [];
        if (item.Request.Family == TaskQueueCommandFamily.Exec)
        {
            arguments.Add("exec");
        }
        else
        {
            arguments.Add("skills");
            arguments.Add("run");
            arguments.Add(item.Request.Skill ?? string.Empty);
        }

        arguments.Add("--record-job");
        arguments.Add("--job-name");
        arguments.Add(item.QueueId);
        arguments.Add("--workspace");
        arguments.Add(item.Request.WorkspaceRoot);
        arguments.Add("--output");
        arguments.Add(jsonOutput ? "json" : "text");
        if (!string.IsNullOrWhiteSpace(item.Request.Cwd))
        {
            arguments.Add("--cwd");
            arguments.Add(item.Request.Cwd);
        }

        if (item.Request.Family == TaskQueueCommandFamily.Exec &&
            !string.IsNullOrWhiteSpace(item.Request.Expert))
        {
            arguments.Add("--expert");
            arguments.Add(item.Request.Expert);
        }

        if (!string.IsNullOrWhiteSpace(item.Request.ReportMode))
        {
            arguments.Add("--report");
            arguments.Add(item.Request.ReportMode);
        }

        if (verbose)
        {
            arguments.Add("--verbose");
        }

        if (trace)
        {
            arguments.Add("--trace");
        }

        arguments.Add("--");
        arguments.Add(item.Request.Task);
        return arguments;
    }

    private static void WriteFailure(
        TextWriter output,
        TaskQueueDiagnostic? diagnostic,
        bool jsonOutput,
        string type)
    {
        WriteFailure(
            output,
            diagnostic?.ErrorCode ?? TaskQueueErrorCode.NotFound,
            diagnostic?.Summary ?? "Queue item was not found.",
            jsonOutput,
            type,
            diagnostic?.QueueId,
            diagnostic?.Path);
    }

    private static void WriteFailure(
        TextWriter output,
        string errorCode,
        string summary,
        bool jsonOutput,
        string type,
        string? queueId = null,
        string? path = null)
    {
        if (jsonOutput)
        {
            output.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["type"] = type,
                ["status"] = "failed",
                ["errorCode"] = DiagnosticSecretRedactor.Redact(errorCode),
                ["summary"] = DiagnosticSecretRedactor.Redact(summary),
                ["queueId"] = string.IsNullOrWhiteSpace(queueId)
                    ? null
                    : DiagnosticSecretRedactor.Redact(queueId),
                ["path"] = string.IsNullOrWhiteSpace(path)
                    ? null
                    : DiagnosticSecretRedactor.Redact(path)
            }, JsonOptions));
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
