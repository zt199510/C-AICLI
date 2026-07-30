using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text;
using System.Text.Json;
using CSharpAiCli.Application;
using CSharpAiCli.Core;

namespace CSharpAiCli.Cli;

public static partial class CliCommandFactory
{
    private sealed class SkillsCommandModule : ICliCommandModule
    {
        public Command Create(CliCommandContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            TextWriter output = context.Output;
            Func<string?, string?, CliEnvironmentSnapshot> snapshotProvider = context.Dependencies.SnapshotProvider;
            Func<string?, CliEnvironmentSnapshot> workspaceSnapshotProvider = context.WorkspaceSnapshotProvider;
            Action<string, CliEnvironmentSnapshot> commandLogger = context.Dependencies.CommandLogger;
            Func<CliEnvironmentSnapshot, IConversationStore> conversationStoreFactory = context.Dependencies.ConversationStoreFactory;
            Func<DateTimeOffset> utcNowProvider = context.Dependencies.UtcNowProvider;
            Func<CliEnvironmentSnapshot, ToolRegistry, IToolExecutor, IAgentRunner> execAgentRunnerFactory = context.Dependencies.ExecAgentRunnerFactory;
            Option<string> workspaceOption = context.GlobalOptions.Workspace;

            void WriteVerboseDiagnostics(ParseResult parseResult, string commandName, CliEnvironmentSnapshot snapshot, bool humanReadableOutput = true) =>
                context.WriteVerboseDiagnostics(parseResult, commandName, snapshot, humanReadableOutput);

            DiagnosticContext? CreateTraceContext(ParseResult parseResult, CliEnvironmentSnapshot snapshot) =>
                context.CreateTraceContext(parseResult, snapshot);

            void TryWriteTraceCommandEvent(
                string commandName,
                CliEnvironmentSnapshot snapshot,
                DiagnosticContext? diagnosticContext,
                string type,
                long sequence,
                string status,
                string? summary = null,
                string? errorCode = null,
                DateTimeOffset? timestampUtc = null) =>
                context.TryWriteTraceCommandEvent(commandName, snapshot, diagnosticContext, type, sequence, status, summary, errorCode, timestampUtc);

            void TryWriteTraceExecResult(
                string commandName,
                CliEnvironmentSnapshot snapshot,
                DiagnosticContext? diagnosticContext,
                ExecResult result) =>
                context.TryWriteTraceExecResult(commandName, snapshot, diagnosticContext, result);

            Command skillsCommand = new("skills", "List and run local skill workflow packs.");
            Command skillsListCommand = new("list", "List built-in and workspace-local skill packs.");
            Option<bool> skillsListJsonOption = new("--json")
            {
                Description = "Write a single JSON skills list object.",
            };
            Option<string> skillsListOutputOption = new("--output")
            {
                Description = "Select text or json output.",
            };
            skillsListOutputOption.DefaultValueFactory = _ => "text";
            skillsListOutputOption.Validators.Add(result =>
            {
                string outputMode = result.GetValueOrDefault<string>() ?? "text";
                if (!string.Equals(outputMode, "text", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(outputMode, "json", StringComparison.OrdinalIgnoreCase))
                {
                    result.AddError("Invalid value for --output. Allowed values are text and json.");
                }
            });
            skillsListCommand.Options.Add(skillsListJsonOption);
            skillsListCommand.Options.Add(skillsListOutputOption);
            skillsListCommand.SetAction(parseResult =>
            {
                string? workspacePath = parseResult.GetValue(workspaceOption);
                bool jsonRequested = parseResult.GetValue(skillsListJsonOption);
                string outputMode = parseResult.GetValue(skillsListOutputOption) ?? "text";
                CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
                WriteVerboseDiagnostics(
                    parseResult,
                    "skills list",
                    snapshot,
                    humanReadableOutput: !IsJsonOutputRequested(jsonRequested, outputMode));

                SkillPackCatalog catalog = SkillPackCatalog.Load(snapshot.Workspace);
                output.WriteLine(IsJsonOutputRequested(jsonRequested, outputMode)
                    ? SkillPackReportRenderer.RenderListJson(catalog)
                    : SkillPackReportRenderer.RenderListText(catalog));
                return 0;
            });

            Command skillsRunCommand = new("run", "Expand and run a local skill workflow pack.");
            Argument<string> skillNameArgument = new("name")
            {
                Description = "Skill pack name.",
            };
            Argument<string[]> skillTaskArgument = new("task")
            {
                Description = "Task text. Tokens after -- are joined so quoting is optional.",
                Arity = ArgumentArity.ZeroOrMore,
            };
            Option<bool> skillsRunDryRunOption = new("--dry-run")
            {
                Description = "Only print the expanded skill run plan.",
            };
            Option<bool> skillsRunJsonOption = new("--json")
            {
                Description = "Write newline-delimited JSON events or a JSON dry-run plan.",
            };
            Option<string> skillsRunOutputOption = new("--output")
            {
                Description = "Select text or json output.",
            };
            Option<string> skillsRunReportOption = new("--report")
            {
                Description = "Override the skill report mode: none or markdown.",
            };
            Option<string> skillsRunReportPathOption = new("--report-path")
            {
                Description = "Write a markdown task report to a workspace path.",
            };
            Option<bool> skillsRunApproveOption = new("--approve")
            {
                Description = "Approve patch or shell tools used by this skill run.",
            };
            Option<string> skillsRunApprovalOption = new("--approval")
            {
                Description = "Set approval mode for this skill run: never, on-request, on-failure, or always.",
            };
            Option<int?> skillsRunMaxTurnsOption = new("--max-turns")
            {
                Description = "Legacy alias for --max-steps.",
            };
            Option<int?> skillsRunMaxStepsOption = new("--max-steps")
            {
                Description = "Maximum agent loop steps for this skill run.",
            };
            Option<int?> skillsRunMaxToolCallsOption = new("--max-tool-calls")
            {
                Description = "Maximum total tool calls for this skill run.",
            };
            Option<int?> skillsRunMaxRetriesOption = new("--max-retries")
            {
                Description = "Maximum failure-feedback retries for this skill run. Use 0 to disable.",
            };
            Option<int?> skillsRunTimeoutSecondsOption = new("--timeout-seconds")
            {
                Description = "Overall skill run timeout in seconds.",
            };
            Option<string> skillsRunSessionOption = new("--session")
            {
                Description = "Create or append to a named skill run transcript.",
            };
            Option<string> skillsRunResumeOption = new("--resume")
            {
                Description = "Resume an existing named skill run transcript.",
            };
            Option<string> skillsRunCwdOption = new("--cwd")
            {
                Description = "Use a working context path for hierarchical instruction discovery.",
            };
            Option<bool> skillsRunRecordJobOption = new("--record-job")
            {
                Description = "Record redacted job metadata in the user-level local job store.",
            };
            Option<string> skillsRunJobNameOption = new("--job-name")
            {
                Description = "Optional human-readable name for a recorded job.",
            };
            skillsRunOutputOption.DefaultValueFactory = _ => "text";
            skillsRunOutputOption.Validators.Add(result =>
            {
                string outputMode = result.GetValueOrDefault<string>() ?? "text";
                if (!string.Equals(outputMode, "text", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(outputMode, "json", StringComparison.OrdinalIgnoreCase))
                {
                    result.AddError("Invalid value for --output. Allowed values are text and json.");
                }
            });
            skillsRunReportOption.Validators.Add(result =>
            {
                string? reportMode = result.GetValueOrDefault<string>();
                if (!string.IsNullOrWhiteSpace(reportMode) &&
                    !ExecReportModeParser.TryParse(reportMode, out _))
                {
                    result.AddError("Invalid value for --report. Allowed values are none and markdown.");
                }
            });
            AddApprovalModeValidator(skillsRunApprovalOption);
            AddPositiveIntegerValidator(skillsRunMaxTurnsOption, "--max-turns");
            AddPositiveIntegerValidator(skillsRunMaxStepsOption, "--max-steps");
            AddPositiveIntegerValidator(skillsRunMaxToolCallsOption, "--max-tool-calls");
            AddNonNegativeIntegerValidator(skillsRunMaxRetriesOption, "--max-retries");
            AddPositiveIntegerValidator(skillsRunTimeoutSecondsOption, "--timeout-seconds");
            skillsRunCommand.Arguments.Add(skillNameArgument);
            skillsRunCommand.Arguments.Add(skillTaskArgument);
            skillsRunCommand.Options.Add(skillsRunDryRunOption);
            skillsRunCommand.Options.Add(skillsRunJsonOption);
            skillsRunCommand.Options.Add(skillsRunOutputOption);
            skillsRunCommand.Options.Add(skillsRunReportOption);
            skillsRunCommand.Options.Add(skillsRunReportPathOption);
            skillsRunCommand.Options.Add(skillsRunApproveOption);
            skillsRunCommand.Options.Add(skillsRunApprovalOption);
            skillsRunCommand.Options.Add(skillsRunMaxStepsOption);
            skillsRunCommand.Options.Add(skillsRunMaxTurnsOption);
            skillsRunCommand.Options.Add(skillsRunMaxToolCallsOption);
            skillsRunCommand.Options.Add(skillsRunMaxRetriesOption);
            skillsRunCommand.Options.Add(skillsRunTimeoutSecondsOption);
            skillsRunCommand.Options.Add(skillsRunSessionOption);
            skillsRunCommand.Options.Add(skillsRunResumeOption);
            skillsRunCommand.Options.Add(skillsRunCwdOption);
            skillsRunCommand.Options.Add(skillsRunRecordJobOption);
            skillsRunCommand.Options.Add(skillsRunJobNameOption);
            skillsRunCommand.SetAction(parseResult =>
            {
                string? workspacePath = parseResult.GetValue(workspaceOption);
                string? cwdPath = parseResult.GetValue(skillsRunCwdOption);
                string skillName = parseResult.GetValue(skillNameArgument) ?? string.Empty;
                string task = string.Join(" ", parseResult.GetValue(skillTaskArgument) ?? []).Trim();
                bool dryRun = parseResult.GetValue(skillsRunDryRunOption);
                bool jsonRequested = parseResult.GetValue(skillsRunJsonOption);
                string outputMode = parseResult.GetValue(skillsRunOutputOption) ?? "text";
                string? reportOverride = parseResult.GetValue(skillsRunReportOption);
                string? reportPath = parseResult.GetValue(skillsRunReportPathOption);
                bool approve = parseResult.GetValue(skillsRunApproveOption);
                string? approvalModeValue = parseResult.GetValue(skillsRunApprovalOption);
                int? maxSteps = parseResult.GetValue(skillsRunMaxStepsOption);
                int? maxTurns = parseResult.GetValue(skillsRunMaxTurnsOption);
                int? maxToolCalls = parseResult.GetValue(skillsRunMaxToolCallsOption);
                int? maxRetries = parseResult.GetValue(skillsRunMaxRetriesOption);
                int? timeoutSeconds = parseResult.GetValue(skillsRunTimeoutSecondsOption);
                string? session = parseResult.GetValue(skillsRunSessionOption);
                string? resume = parseResult.GetValue(skillsRunResumeOption);
                bool recordJob = parseResult.GetValue(skillsRunRecordJobOption);
                string? jobName = parseResult.GetValue(skillsRunJobNameOption);
                bool sessionSupplied = IsOptionExplicit(parseResult, skillsRunSessionOption);
                bool resumeSupplied = IsOptionExplicit(parseResult, skillsRunResumeOption);
                bool maxStepsSupplied = IsOptionExplicit(parseResult, skillsRunMaxStepsOption);
                bool maxTurnsSupplied = IsOptionExplicit(parseResult, skillsRunMaxTurnsOption);
                CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath, cwdPath);
                DiagnosticContext? traceContext = CreateTraceContext(parseResult, snapshot);
                WriteVerboseDiagnostics(
                    parseResult,
                    "skills run",
                    snapshot,
                    humanReadableOutput: !IsJsonOutputRequested(jsonRequested, outputMode));

                int WriteSkillFailure(string errorCode, string summary)
                {
                    TryWriteTraceCommandEvent(
                        "skills run",
                        snapshot,
                        traceContext,
                        "skill.failed",
                        0,
                        "failure",
                        summary,
                        errorCode,
                        utcNowProvider());

                    if (IsJsonOutputRequested(jsonRequested, outputMode))
                    {
                        output.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object?>
                        {
                            ["type"] = "skills.error",
                            ["status"] = "failed",
                            ["errorCode"] = errorCode,
                            ["summary"] = summary
                        }, JsonOptions));
                    }
                    else
                    {
                        WriteSafeFailure(output, errorCode, summary);
                    }

                    return 1;
                }

                if (string.IsNullOrWhiteSpace(task))
                {
                    return WriteSkillFailure(
                        SkillPackErrorCode.RunPlanInvalid,
                        "Skill run task is empty.");
                }

                if (sessionSupplied && resumeSupplied)
                {
                    return WriteSkillFailure(
                        "session-option-conflict",
                        "Use either --session or --resume, not both.");
                }

                if (maxStepsSupplied &&
                    maxTurnsSupplied &&
                    maxSteps.HasValue &&
                    maxTurns.HasValue &&
                    maxSteps.Value != maxTurns.Value)
                {
                    return WriteSkillFailure(
                        "invalid-agent-limits",
                        "Use either --max-steps or --max-turns, or set them to the same value.");
                }

                SkillPackCatalog catalog = SkillPackCatalog.Load(snapshot.Workspace);
                SkillRunPlanResult planResult = new SkillRunPlanner().Plan(
                    catalog,
                    new SkillRunRequest(skillName, task, reportOverride));
                if (!planResult.Succeeded || planResult.Plan is null)
                {
                    return WriteSkillFailure(
                        planResult.ErrorCode ?? SkillPackErrorCode.RunPlanInvalid,
                        planResult.Summary ?? "Skill run plan could not be created.");
                }

                SkillRunPlan plan = planResult.Plan;
                JobRecordStore? jobStore = null;
                JobRecord? jobRecord = null;
                if (recordJob)
                {
                    try
                    {
                        DateTimeOffset nowUtc = utcNowProvider();
                        jobStore = JobRecordStore.Create(snapshot);
                        jobRecord = JobRecord.CreateRunning(
                            JobIdGenerator.Create(nowUtc),
                            nowUtc,
                            new JobCommandSummary(
                                Family: "skills run",
                                Task: task,
                                Name: jobName,
                                WorkspaceRoot: snapshot.Workspace.RootPath,
                                Cwd: cwdPath,
                                Skill: plan.Manifest.Name,
                                Expert: plan.Expert,
                                ReportMode: plan.Report,
                                OutputMode: IsJsonOutputRequested(jsonRequested, outputMode) ? "json" : "text",
                                DryRun: dryRun,
                                SkillMetadata: JobSkillSummary.FromMetadata(plan.ToMetadata())),
                            jobName);
                        jobStore.Create(jobRecord);
                    }
                    catch (Exception exception) when (IsJobStoreException(exception))
                    {
                        return WriteSkillFailure(
                            "job-record-write-failed",
                            "Job record could not be created.");
                    }
                }

                if (dryRun)
                {
                    if (jobStore is not null && jobRecord is not null)
                    {
                        try
                        {
                            JobRecord completed = jobRecord.WithStatus(
                                JobStatus.DryRun,
                                utcNowProvider(),
                                exitCode: 0,
                                stopReason: "dry-run",
                                summary: "Skill dry-run plan recorded.",
                                artifacts:
                                [
                                    new JobArtifact(
                                        JobArtifactKind.SkillPlan,
                                        "inline:skills.runPlan",
                                        Exists: true,
                                        Summary: "Expanded skill plan metadata.")
                                ]);
                            jobStore.Update(completed);
                        }
                        catch (Exception exception) when (IsJobStoreException(exception))
                        {
                            return WriteSkillFailure(
                                "job-record-write-failed",
                                "Job record could not be updated.");
                        }
                    }

                    output.WriteLine(IsJsonOutputRequested(jsonRequested, outputMode)
                        ? SkillPackReportRenderer.RenderPlanJson(plan, dryRun: true)
                        : SkillPackReportRenderer.RenderPlanText(plan, dryRun: true));
                    return 0;
                }

                TryWriteCommandLog(commandLogger, "skills run", snapshot);

                ExecReportModeParser.TryParse(plan.Report, out ExecReportMode reportMode);
                ExecReportOptions reportOptions = new(reportMode, reportPath);
                if (!reportOptions.ShouldGenerate && !string.IsNullOrWhiteSpace(reportOptions.ReportPath))
                {
                    return WriteSkillFailure(
                        "report-path-requires-markdown",
                        "Use --report markdown with --report-path.");
                }

                ReportPathResolver reportPathResolver = new();
                ReportWriteResult? reportPathPrecheck = null;
                if (reportOptions.ShouldWriteFile)
                {
                    reportPathPrecheck = reportPathResolver.ResolveForWrite(snapshot.Workspace, reportOptions.ReportPath);
                    if (!reportPathPrecheck.Succeeded)
                    {
                        return WriteSkillFailure(
                            reportPathPrecheck.ErrorCode ?? "invalid-report-path",
                            reportPathPrecheck.Summary ?? "Report path is invalid.");
                    }
                }

                ApprovalMode? cliApprovalMode = GetApprovalOverride(approvalModeValue, parseResult.GetResult(skillsRunApprovalOption), approve);
                IApprovalPolicy approvalPolicy = ApprovalPolicyResolver.Resolve(snapshot.Configuration.ApprovalMode, cliApprovalMode);
                ToolRegistry registry = CliToolFactory.CreateRegistry(snapshot, approvalPolicy, plan.ToolBoundary);
                ToolExecutor executor = new(registry, snapshot.Configuration.DisabledTools, plan.ToolBoundary);
                ExpertProfile? expertProfile = ExpertProfileCatalog.GetOrNull(plan.Expert);
                string? markdownReportForStdout = null;
                string? recordedSessionPath = null;

                int WriteSkillExecResultWithTrace(ExecResult result)
                {
                    TryWriteTraceExecResult("skills run", snapshot, traceContext, result);
                    result = CompleteRecordedJob(
                        jobStore,
                        jobRecord,
                        result,
                        utcNowProvider(),
                        recordedSessionPath);
                    if (jobRecord is not null && result.TaskReport is not null)
                    {
                        JobRecordReadResult updated = jobStore?.Read(jobRecord.JobId) ??
                            JobRecordReadResult.Failure("job-not-found", "Job record was not found.");
                        if (updated.Record is not null)
                        {
                            jobRecord = updated.Record;
                        }
                    }

                    if (IsJsonOutputRequested(jsonRequested, outputMode))
                    {
                        ExecJsonRenderer renderer = new(output);
                        WriteExecOutput(renderer, result);
                    }
                    else
                    {
                        ExecTextRenderer renderer = new(output);
                        WriteExecOutput(renderer, result);
                        if (!string.IsNullOrWhiteSpace(markdownReportForStdout))
                        {
                            output.WriteLine();
                            output.WriteLine(markdownReportForStdout);
                        }
                    }

                    return result.ExitCode;
                }

                int WriteSkillExecFailureWithTrace(
                    string errorCode,
                    string summary,
                    IReadOnlyList<ExecEvent>? events = null,
                    string? approvalStatus = null)
                {
                    ExecResult failure = ExecResult.Failure(
                        ExitCode: 1,
                        Summary: summary,
                        ErrorCode: errorCode,
                        Events: events ?? [],
                        ApprovalStatus: approvalStatus);

                    if (IsJsonOutputRequested(jsonRequested, outputMode) || failure.Events.Count > 0)
                    {
                        return WriteSkillExecResultWithTrace(failure);
                    }

                    TryWriteTraceExecResult("skills run", snapshot, traceContext, failure);
                    ExecResult completedFailure = CompleteRecordedJob(
                        jobStore,
                        jobRecord,
                        failure,
                        utcNowProvider(),
                        recordedSessionPath);
                    WriteSafeFailure(
                        output,
                        completedFailure.ErrorCode ?? errorCode,
                        completedFailure.Summary ?? summary);
                    return completedFailure.ExitCode;
                }

                int WriteSkillConversationStoreFailureWithTrace(
                    Exception exception,
                    IReadOnlyList<ExecEvent>? events = null,
                    string? approvalStatus = null)
                {
                    (string errorCode, string summary) = GetConversationStoreFailure(exception);
                    return WriteSkillExecFailureWithTrace(errorCode, summary, events, approvalStatus);
                }

                ExecResult FinalizeSkillExecResultWithTaskReport(
                    ExecResult baseResult,
                    AgentTaskReport baseTaskReport,
                    IReadOnlyList<ExecEvent>? additionalEvents = null)
                {
                    List<ExecEvent> reportEvents = additionalEvents?.ToList() ?? [];
                    AgentTaskReport finalTaskReport = baseTaskReport;
                    ReportWriteResult? writeResult = null;
                    if (reportOptions.ShouldGenerate)
                    {
                        MarkdownTaskReportRenderer reportRenderer = new();
                        if (reportOptions.ShouldWriteFile)
                        {
                            string markdown = reportRenderer.Render(baseTaskReport);
                            writeResult = reportPathResolver.WriteMarkdown(
                                snapshot.Workspace,
                                reportOptions.ReportPath,
                                markdown);
                        }

                        ExecReportMetadata reportMetadata = new(
                            Mode: reportOptions.Mode.ToCanonicalName(),
                            Generated: true,
                            Path: writeResult?.Path ?? reportPathPrecheck?.Path,
                            WriteStatus: reportOptions.ShouldWriteFile
                                ? writeResult?.Succeeded == true ? "written" : "failed"
                                : "stdout",
                            ErrorCode: writeResult?.ErrorCode,
                            Summary: reportOptions.ShouldWriteFile
                                ? writeResult?.Summary
                                : "Markdown report generated for stdout.");
                        finalTaskReport = baseTaskReport.WithReportMetadata(reportMetadata);
                        long reportSequence = reportEvents.Count > 0
                            ? reportEvents[^1].Sequence + 1
                            : baseResult.Events.Count == 0
                                ? 0
                                : baseResult.Events[^1].Sequence + 1;
                        reportEvents.Add(CreateReportGeneratedEvent(
                            reportMetadata,
                            reportSequence,
                            utcNowProvider()));
                        if (!reportOptions.ShouldWriteFile &&
                            !IsJsonOutputRequested(jsonRequested, outputMode))
                        {
                            markdownReportForStdout = reportRenderer.Render(finalTaskReport);
                        }
                    }

                    ExecResult finalResult = baseResult.WithTaskReport(
                        finalTaskReport,
                        utcNowProvider(),
                        reportEvents);
                    if (writeResult is { Succeeded: false })
                    {
                        finalResult = ExecResult.Failure(
                            ExitCode: 1,
                            Summary: writeResult.Summary ?? "Markdown report could not be written.",
                            ErrorCode: writeResult.ErrorCode ?? "report-write-failed",
                            Events: finalResult.Events,
                            ApprovalStatus: finalResult.ApprovalStatus,
                            StopReason: finalResult.StopReason,
                            ChangedFiles: finalResult.ChangedFiles,
                            VerificationResults: finalResult.VerificationResults,
                            RetryAttempts: finalResult.RetryAttempts,
                            FailureSummary: finalResult.FailureSummary,
                            TaskReport: finalTaskReport);
                    }

                    return finalResult;
                }

                string? effectiveSession = resumeSupplied ? resume : session;
                ConversationSessionName? sessionName = null;
                ConversationTranscript? transcript = null;
                ConversationTranscript? transcriptContext = null;
                IConversationStore? conversationStore = null;
                if (sessionSupplied || resumeSupplied)
                {
                    try
                    {
                        sessionName = ConversationSessionName.Parse(effectiveSession);
                        recordedSessionPath = ResolveSessionPath(snapshot, sessionName);
                    }
                    catch (ArgumentException exception)
                    {
                        return WriteSkillExecFailureWithTrace(
                            "invalid-session-name",
                            GetSafeSessionNameParseMessage(exception),
                            []);
                    }

                    try
                    {
                        conversationStore = conversationStoreFactory(snapshot);
                        if (resumeSupplied)
                        {
                            if (!conversationStore.TryLoad(sessionName, out transcript) || transcript is null)
                            {
                                return WriteSkillExecFailureWithTrace(
                                    "session-not-found",
                                    "Session transcript was not found.",
                                    []);
                            }

                            transcriptContext = transcript;
                        }
                        else
                        {
                            transcript = conversationStore.LoadOrCreate(sessionName, utcNowProvider());
                        }
                    }
                    catch (Exception exception) when (IsConversationStoreException(exception))
                    {
                        return WriteSkillConversationStoreFailureWithTrace(exception);
                    }
                }

                AgentTaskContext taskContext = new AgentTaskContextCollector().Collect(
                    snapshot.Workspace,
                    snapshot.Instructions,
                    cwdPath,
                    effectiveSession,
                    transcriptContext is not null,
                    plan.ExpandedTask);

                AgentRunRequest request = new(
                    plan.ExpandedTask,
                    snapshot.Workspace,
                    snapshot.Instructions.Instructions,
                    effectiveSession,
                    Limits: new AgentRunLimits(
                        MaxSteps: maxSteps ?? maxTurns,
                        MaxToolCalls: maxToolCalls,
                        MaxRetries: maxRetries,
                        ModelCallTimeout: timeoutSeconds is null ? null : TimeSpan.FromSeconds(timeoutSeconds.Value),
                        OverallTimeout: timeoutSeconds is null ? null : TimeSpan.FromSeconds(timeoutSeconds.Value))
                        .MergeWith(snapshot.Configuration.AgentRunLimits),
                    TranscriptContext: transcriptContext,
                    TaskContext: taskContext,
                    WorkflowConfiguration: WorkflowProfileLoader.Load(snapshot.Configuration),
                    ExpertProfile: expertProfile,
                    Skill: plan.ToMetadata());

                WorkflowReferenceResolution references = taskContext.References ?? WorkflowReferenceResolution.Empty;
                if (references.HasErrors)
                {
                    AgentRunResult referenceFailure = CreateWorkflowReferenceFailureResult(
                        references,
                        utcNowProvider());
                    ExecResult referenceExecResult = AgentExecResultAdapter.FromAgentResult(referenceFailure);
                    string? referenceTracePath = traceContext is null
                        ? null
                        : TraceLogger.ResolveTracePath(snapshot, traceContext.TimestampUtc);
                    AgentTaskReport referenceTaskReport = AgentTaskReportBuilder.Build(
                        request,
                        referenceFailure,
                        referenceTracePath);
                    referenceExecResult = FinalizeSkillExecResultWithTaskReport(
                        referenceExecResult,
                        referenceTaskReport);
                    return WriteSkillExecResultWithTrace(referenceExecResult);
                }

                IAgentRunner runner = execAgentRunnerFactory(snapshot, registry, executor);
                AgentRunResult agentResult = runner.Run(request, transcript);
                ExecResult execResult = AgentExecResultAdapter.FromAgentResult(agentResult);
                AgentTaskReviewGateReport reviewGate = RunReadOnlyReviewGate(snapshot.Workspace, executor);
                List<ExecEvent> additionalReportEvents = [];
                ExecEvent? referenceEvent = CreateReferenceContextEventIfMissing(
                    execResult.Events,
                    references,
                    utcNowProvider());
                if (referenceEvent is not null)
                {
                    additionalReportEvents.Add(referenceEvent);
                }

                long nextSequence = execResult.Events.Count == 0
                    ? 0
                    : execResult.Events[^1].Sequence + 1;
                if (additionalReportEvents.Count > 0)
                {
                    nextSequence = additionalReportEvents[^1].Sequence + 1;
                }

                additionalReportEvents.Add(CreateReviewGateEvent(
                    reviewGate,
                    nextSequence,
                    utcNowProvider()));
                string? tracePath = traceContext is null
                    ? null
                    : TraceLogger.ResolveTracePath(snapshot, traceContext.TimestampUtc);
                AgentTaskReport taskReport = AgentTaskReportBuilder.Build(
                    request,
                    agentResult,
                    tracePath,
                    reviewGate);
                execResult = FinalizeSkillExecResultWithTaskReport(
                    execResult,
                    taskReport,
                    additionalReportEvents);
                if (sessionName is not null && transcript is not null && conversationStore is not null)
                {
                    transcript.AddAgentRun(ConversationAgentRun.FromAgentResult(agentResult, utcNowProvider(), taskReport));
                    try
                    {
                        conversationStore.Save(sessionName, transcript);
                    }
                    catch (Exception exception) when (IsConversationStoreException(exception))
                    {
                        return WriteSkillConversationStoreFailureWithTrace(
                            exception,
                            execResult.Events,
                            execResult.ApprovalStatus);
                    }
                }

                return WriteSkillExecResultWithTrace(execResult);
            });
            skillsCommand.Subcommands.Add(skillsListCommand);
            skillsCommand.Subcommands.Add(skillsRunCommand);

            return skillsCommand;
        }
    }
}
