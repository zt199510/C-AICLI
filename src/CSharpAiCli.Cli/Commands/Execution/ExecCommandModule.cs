using System.CommandLine;
using System.CommandLine.Parsing;
using CSharpAiCli.Application;
using CSharpAiCli.Core;

namespace CSharpAiCli.Cli;

public static partial class CliCommandFactory
{
    private sealed class ExecCommandModule : ICliCommandModule
    {
        public Command Create(CliCommandContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            TextWriter output = context.Output;
            Func<string?, string?, CliEnvironmentSnapshot> snapshotProvider = context.Dependencies.SnapshotProvider;
            Action<string, CliEnvironmentSnapshot> commandLogger = context.Dependencies.CommandLogger;
            Func<CliEnvironmentSnapshot, IConversationStore> conversationStoreFactory = context.Dependencies.ConversationStoreFactory;
            Func<DateTimeOffset> utcNowProvider = context.Dependencies.UtcNowProvider;
            Func<CliEnvironmentSnapshot, ToolRegistry, IToolExecutor, IAgentRunner> execAgentRunnerFactory = context.Dependencies.ExecAgentRunnerFactory;
            Option<string> workspaceOption = context.GlobalOptions.Workspace;

            void WriteVerboseDiagnostics(ParseResult parseResult, string commandName, CliEnvironmentSnapshot snapshot, bool humanReadableOutput = true) =>
                context.WriteVerboseDiagnostics(parseResult, commandName, snapshot, humanReadableOutput);

            DiagnosticContext? CreateTraceContext(ParseResult parseResult, CliEnvironmentSnapshot snapshot) =>
                context.CreateTraceContext(parseResult, snapshot);

            void TryWriteTraceExecResult(
                string commandName,
                CliEnvironmentSnapshot snapshot,
                DiagnosticContext? diagnosticContext,
                ExecResult result) =>
                context.TryWriteTraceExecResult(commandName, snapshot, diagnosticContext, result);

            Command execCommand = new("exec", "Run an agentic local workspace task and emit exec events.");
            Argument<string> execTaskArgument = new("task")
            {
                Description = "Task text for the local agent runner.",
            };
            Option<bool> execApproveOption = new("--approve")
            {
                Description = "Approve patch or shell tools used by this exec.",
            };
            Option<string> execApprovalOption = new("--approval")
            {
                Description = "Set approval mode for this exec: never, on-request, on-failure, or always.",
            };
            AddApprovalModeValidator(execApprovalOption);
            Option<bool> execJsonOption = new("--json")
            {
                Description = "Write newline-delimited JSON events.",
            };
            Option<string> execOutputOption = new("--output")
            {
                Description = "Select text or json output.",
            };
            Option<int?> execMaxTurnsOption = new("--max-turns")
            {
                Description = "Legacy alias for --max-steps.",
            };
            Option<int?> execMaxStepsOption = new("--max-steps")
            {
                Description = "Maximum agent loop steps for agentic exec.",
            };
            Option<int?> execMaxToolCallsOption = new("--max-tool-calls")
            {
                Description = "Maximum total tool calls for agentic exec.",
            };
            Option<int?> execMaxRetriesOption = new("--max-retries")
            {
                Description = "Maximum failure-feedback retries for agentic exec. Use 0 to disable.",
            };
            Option<int?> execTimeoutSecondsOption = new("--timeout-seconds")
            {
                Description = "Overall agentic exec timeout in seconds.",
            };
            Option<string> execSessionOption = new("--session")
            {
                Description = "Create or append to a named agentic exec transcript.",
            };
            Option<string> execResumeOption = new("--resume")
            {
                Description = "Resume an existing named agentic exec transcript.",
            };
            Option<string> execCwdOption = new("--cwd")
            {
                Description = "Use a working context path for hierarchical instruction discovery.",
            };
            Option<string> execReportOption = new("--report")
            {
                Description = "Generate an additional task report: none or markdown.",
            };
            Option<string> execReportPathOption = new("--report-path")
            {
                Description = "Write a markdown task report to a workspace path.",
            };
            Option<string> execExpertOption = new("--expert")
            {
                Description = "Select a local expert profile: bugfix, reviewer, tester, security, or refactor.",
            };
            Option<bool> execRecordJobOption = new("--record-job")
            {
                Description = "Record redacted job metadata in the user-level local job store.",
            };
            Option<string> execJobNameOption = new("--job-name")
            {
                Description = "Optional human-readable name for a recorded job.",
            };
            execOutputOption.DefaultValueFactory = _ => "text";
            execReportOption.DefaultValueFactory = _ => "none";
            execOutputOption.Validators.Add(result =>
            {
                string outputMode = result.GetValueOrDefault<string>() ?? "text";
                if (!string.Equals(outputMode, "text", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(outputMode, "json", StringComparison.OrdinalIgnoreCase))
                {
                    result.AddError("Invalid value for --output. Allowed values are text and json.");
                }
            });
            execReportOption.Validators.Add(result =>
            {
                string reportMode = result.GetValueOrDefault<string>() ?? "none";
                if (!ExecReportModeParser.TryParse(reportMode, out _))
                {
                    result.AddError("Invalid value for --report. Allowed values are none and markdown.");
                }
            });
            execExpertOption.Validators.Add(result =>
            {
                string? expert = result.GetValueOrDefault<string>();
                if (!ExpertProfileCatalog.TryGet(expert, out _))
                {
                    result.AddError("Invalid value for --expert. Allowed values are bugfix, refactor, reviewer, security, and tester.");
                }
            });
            AddPositiveIntegerValidator(execMaxTurnsOption, "--max-turns");
            AddPositiveIntegerValidator(execMaxStepsOption, "--max-steps");
            AddPositiveIntegerValidator(execMaxToolCallsOption, "--max-tool-calls");
            AddNonNegativeIntegerValidator(execMaxRetriesOption, "--max-retries");
            AddPositiveIntegerValidator(execTimeoutSecondsOption, "--timeout-seconds");
            execCommand.Arguments.Add(execTaskArgument);
            execCommand.Options.Add(execApproveOption);
            execCommand.Options.Add(execApprovalOption);
            execCommand.Options.Add(execJsonOption);
            execCommand.Options.Add(execOutputOption);
            execCommand.Options.Add(execMaxStepsOption);
            execCommand.Options.Add(execMaxTurnsOption);
            execCommand.Options.Add(execMaxToolCallsOption);
            execCommand.Options.Add(execMaxRetriesOption);
            execCommand.Options.Add(execTimeoutSecondsOption);
            execCommand.Options.Add(execSessionOption);
            execCommand.Options.Add(execResumeOption);
            execCommand.Options.Add(execCwdOption);
            execCommand.Options.Add(execReportOption);
            execCommand.Options.Add(execReportPathOption);
            execCommand.Options.Add(execExpertOption);
            execCommand.Options.Add(execRecordJobOption);
            execCommand.Options.Add(execJobNameOption);
            execCommand.SetAction(parseResult =>
            {
                string? workspacePath = parseResult.GetValue(workspaceOption);
                string? cwdPath = parseResult.GetValue(execCwdOption);
                string task = parseResult.GetValue(execTaskArgument) ?? string.Empty;
                bool approve = parseResult.GetValue(execApproveOption);
                string? approvalModeValue = parseResult.GetValue(execApprovalOption);
                bool jsonRequested = parseResult.GetValue(execJsonOption);
                string outputMode = parseResult.GetValue(execOutputOption) ?? "text";
                int? maxSteps = parseResult.GetValue(execMaxStepsOption);
                int? maxTurns = parseResult.GetValue(execMaxTurnsOption);
                int? maxToolCalls = parseResult.GetValue(execMaxToolCallsOption);
                int? maxRetries = parseResult.GetValue(execMaxRetriesOption);
                int? timeoutSeconds = parseResult.GetValue(execTimeoutSecondsOption);
                string? session = parseResult.GetValue(execSessionOption);
                string? resume = parseResult.GetValue(execResumeOption);
                string reportModeValue = parseResult.GetValue(execReportOption) ?? "none";
                string? reportPath = parseResult.GetValue(execReportPathOption);
                string? expertName = parseResult.GetValue(execExpertOption);
                bool recordJob = parseResult.GetValue(execRecordJobOption);
                string? jobName = parseResult.GetValue(execJobNameOption);
                bool sessionSupplied = IsOptionExplicit(parseResult, execSessionOption);
                bool resumeSupplied = IsOptionExplicit(parseResult, execResumeOption);
                bool maxStepsSupplied = IsOptionExplicit(parseResult, execMaxStepsOption);
                bool maxTurnsSupplied = IsOptionExplicit(parseResult, execMaxTurnsOption);
                CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath, cwdPath);
                DiagnosticContext? traceContext = CreateTraceContext(parseResult, snapshot);
                ApprovalMode? cliApprovalMode = GetApprovalOverride(approvalModeValue, parseResult.GetResult(execApprovalOption), approve);
                ExecReportModeParser.TryParse(reportModeValue, out ExecReportMode reportMode);
                ExecReportOptions reportOptions = new(reportMode, reportPath);
                ExpertProfile? expertProfile = ExpertProfileCatalog.GetOrNull(expertName);
                ToolExecutionBoundary toolBoundary = ExpertToolBoundary.FromExpert(expertProfile);
                ReportPathResolver reportPathResolver = new();
                string? markdownReportForStdout = null;
                string? recordedSessionPath = null;
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
                                Family: "exec",
                                Task: task,
                                Name: jobName,
                                WorkspaceRoot: snapshot.Workspace.RootPath,
                                Cwd: cwdPath,
                                Expert: expertProfile?.Name ?? expertName,
                                ReportMode: reportMode.ToCanonicalName(),
                                OutputMode: IsJsonOutputRequested(jsonRequested, outputMode) ? "json" : "text"),
                            jobName);
                        jobStore.Create(jobRecord);
                    }
                    catch (Exception exception) when (IsJobStoreException(exception))
                    {
                        WriteSafeFailure(output, "job-record-write-failed", "Job record could not be created.");
                        return 1;
                    }
                }

                TryWriteCommandLog(commandLogger, "exec", snapshot);
                WriteVerboseDiagnostics(
                    parseResult,
                    "exec",
                    snapshot,
                    humanReadableOutput: !IsJsonOutputRequested(jsonRequested, outputMode));

                int WriteExecResultWithTrace(ExecResult result)
                {
                    TryWriteTraceExecResult("exec", snapshot, traceContext, result);
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

                int WriteExecFailureWithTrace(
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
                        return WriteExecResultWithTrace(failure);
                    }

                    TryWriteTraceExecResult("exec", snapshot, traceContext, failure);
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

                int WriteExecConversationStoreFailureWithTrace(
                    Exception exception,
                    IReadOnlyList<ExecEvent>? events = null,
                    string? approvalStatus = null)
                {
                    (string errorCode, string summary) = GetConversationStoreFailure(exception);
                    return WriteExecFailureWithTrace(errorCode, summary, events, approvalStatus);
                }

                if (sessionSupplied && resumeSupplied)
                {
                    return WriteExecFailureWithTrace(
                        "session-option-conflict",
                        "Use either --session or --resume, not both.",
                        []);
                }

                if (maxStepsSupplied &&
                    maxTurnsSupplied &&
                    maxSteps.HasValue &&
                    maxTurns.HasValue &&
                    maxSteps.Value != maxTurns.Value)
                {
                    return WriteExecFailureWithTrace(
                        "invalid-agent-limits",
                        "Use either --max-steps or --max-turns, or set them to the same value.",
                        []);
                }

                if (!reportOptions.ShouldGenerate && !string.IsNullOrWhiteSpace(reportOptions.ReportPath))
                {
                    return WriteExecFailureWithTrace(
                        "report-path-requires-markdown",
                        "Use --report markdown with --report-path.",
                        []);
                }

                ReportWriteResult? reportPathPrecheck = null;
                if (reportOptions.ShouldWriteFile)
                {
                    reportPathPrecheck = reportPathResolver.ResolveForWrite(snapshot.Workspace, reportOptions.ReportPath);
                    if (!reportPathPrecheck.Succeeded)
                    {
                        return WriteExecFailureWithTrace(
                            reportPathPrecheck.ErrorCode ?? "invalid-report-path",
                            reportPathPrecheck.Summary ?? "Report path is invalid.",
                            []);
                    }
                }

                ExecResult FinalizeExecResultWithTaskReport(
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

                IApprovalPolicy approvalPolicy = ApprovalPolicyResolver.Resolve(snapshot.Configuration.ApprovalMode, cliApprovalMode);
                ToolRegistry registry = CliToolFactory.CreateRegistry(snapshot, approvalPolicy, toolBoundary);
                ToolExecutor executor = new(registry, snapshot.Configuration.DisabledTools, toolBoundary);
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
                        return WriteExecFailureWithTrace(
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
                                return WriteExecFailureWithTrace(
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
                        return WriteExecConversationStoreFailureWithTrace(exception);
                    }
                }

                AgentTaskContext taskContext = new AgentTaskContextCollector().Collect(
                    snapshot.Workspace,
                    snapshot.Instructions,
                    cwdPath,
                    effectiveSession,
                    transcriptContext is not null,
                    task);

                AgentRunRequest request = new(
                    task,
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
                    ExpertProfile: expertProfile);

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
                    referenceExecResult = FinalizeExecResultWithTaskReport(
                        referenceExecResult,
                        referenceTaskReport);
                    return WriteExecResultWithTrace(referenceExecResult);
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

                ExecEvent reviewGateEvent = CreateReviewGateEvent(
                    reviewGate,
                    nextSequence,
                    utcNowProvider());
                additionalReportEvents.Add(reviewGateEvent);
                string? tracePath = traceContext is null
                    ? null
                    : TraceLogger.ResolveTracePath(snapshot, traceContext.TimestampUtc);
                AgentTaskReport taskReport = AgentTaskReportBuilder.Build(
                    request,
                    agentResult,
                    tracePath,
                    reviewGate);
                execResult = FinalizeExecResultWithTaskReport(
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
                        return WriteExecConversationStoreFailureWithTrace(
                            exception,
                            execResult.Events,
                            execResult.ApprovalStatus);
                    }
                }

                return WriteExecResultWithTrace(execResult);
            });

            return execCommand;
        }
    }
}
