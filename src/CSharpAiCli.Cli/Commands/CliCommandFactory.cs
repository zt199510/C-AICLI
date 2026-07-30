using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text;
using System.Text.Json;
using CSharpAiCli.Application;
using CSharpAiCli.Core;
using CSharpAiCli.ProjectPacks;
using CSharpAiCli.ProjectPacks.GerberTiff;
using CSharpAiCli.ProjectPacks.Runtime;

namespace CSharpAiCli.Cli;

public static partial class CliCommandFactory
{
    private const string InvalidConversationTranscriptSummary =
        "Conversation transcript is missing or uses an unsupported schema version.";

    private const string SessionStoreErrorSummary =
        "Conversation session store operation failed.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static RootCommand Create(TextWriter output)
    {
        return Create(
            output,
            (workspacePath, instructionTargetPath) => CliEnvironmentSnapshot.Create(
                workspacePath: workspacePath,
                instructionTargetPath: instructionTargetPath),
            (commandName, snapshot) => CommandLogger.Append(commandName, snapshot),
            snapshot => OpenAiResponsesModelClient.Create(snapshot),
            writer => new TerminalChatStreamingRenderer(writer));
    }

    public static RootCommand Create(TextWriter output, Func<string?, CliEnvironmentSnapshot> snapshotProvider)
    {
        return Create(
            output,
            snapshotProvider,
            (_, _) => { },
            snapshot => OpenAiResponsesModelClient.Create(snapshot),
            writer => new TerminalChatStreamingRenderer(writer));
    }

    public static RootCommand Create(TextWriter output, Func<string?, CliEnvironmentSnapshot> snapshotProvider, TextReader input)
    {
        return Create(
            output,
            snapshotProvider,
            (_, _) => { },
            snapshot => OpenAiResponsesModelClient.Create(snapshot),
            writer => new TerminalChatStreamingRenderer(writer),
            input);
    }

    public static RootCommand Create(
        TextWriter output,
        Func<string?, CliEnvironmentSnapshot> snapshotProvider,
        Action<string, CliEnvironmentSnapshot> commandLogger)
    {
        return Create(
            output,
            snapshotProvider,
            commandLogger,
            snapshot => OpenAiResponsesModelClient.Create(snapshot),
            writer => new TerminalChatStreamingRenderer(writer));
    }

    public static RootCommand Create(
        TextWriter output,
        Func<string?, CliEnvironmentSnapshot> snapshotProvider,
        Action<string, CliEnvironmentSnapshot> commandLogger,
        Func<CliEnvironmentSnapshot, IChatModelClient> chatModelClientFactory)
    {
        return Create(
            output,
            snapshotProvider,
            commandLogger,
            chatModelClientFactory,
            writer => new TerminalChatStreamingRenderer(writer));
    }

    public static RootCommand Create(
        TextWriter output,
        Func<string?, CliEnvironmentSnapshot> snapshotProvider,
        Action<string, CliEnvironmentSnapshot> commandLogger,
        Func<CliEnvironmentSnapshot, IChatModelClient> chatModelClientFactory,
        Func<TextWriter, IChatStreamingRenderer> streamingRendererFactory)
    {
        return Create(
            output,
            snapshotProvider,
            commandLogger,
            chatModelClientFactory,
            streamingRendererFactory,
            snapshot => FileConversationStore.Create(snapshot),
            () => DateTimeOffset.UtcNow,
            CreateDefaultExecAgentRunner);
    }

    private static RootCommand Create(
        TextWriter output,
        Func<string?, CliEnvironmentSnapshot> snapshotProvider,
        Action<string, CliEnvironmentSnapshot> commandLogger,
        Func<CliEnvironmentSnapshot, IChatModelClient> chatModelClientFactory,
        Func<TextWriter, IChatStreamingRenderer> streamingRendererFactory,
        TextReader input)
    {
        return Create(
            output,
            (workspacePath, _) => snapshotProvider(workspacePath),
            commandLogger,
            chatModelClientFactory,
            streamingRendererFactory,
            snapshot => FileConversationStore.Create(snapshot),
            () => DateTimeOffset.UtcNow,
            CreateDefaultExecAgentRunner,
            input);
    }

    public static RootCommand Create(
        TextWriter output,
        Func<string?, string?, CliEnvironmentSnapshot> snapshotProvider,
        Action<string, CliEnvironmentSnapshot> commandLogger,
        Func<CliEnvironmentSnapshot, IChatModelClient> chatModelClientFactory,
        Func<TextWriter, IChatStreamingRenderer> streamingRendererFactory)
    {
        return Create(
            output,
            snapshotProvider,
            commandLogger,
            chatModelClientFactory,
            streamingRendererFactory,
            snapshot => FileConversationStore.Create(snapshot),
            () => DateTimeOffset.UtcNow,
            CreateDefaultExecAgentRunner);
    }

    public static RootCommand Create(
        TextWriter output,
        Func<string?, CliEnvironmentSnapshot> snapshotProvider,
        Action<string, CliEnvironmentSnapshot> commandLogger,
        Func<CliEnvironmentSnapshot, IChatModelClient> chatModelClientFactory,
        Func<TextWriter, IChatStreamingRenderer> streamingRendererFactory,
        Func<CliEnvironmentSnapshot, IConversationStore> conversationStoreFactory,
        Func<DateTimeOffset> utcNowProvider)
    {
        return Create(
            output,
            snapshotProvider,
            commandLogger,
            chatModelClientFactory,
            streamingRendererFactory,
            conversationStoreFactory,
            utcNowProvider,
            CreateDefaultExecAgentRunner);
    }

    public static RootCommand Create(
        TextWriter output,
        Func<string?, CliEnvironmentSnapshot> snapshotProvider,
        Action<string, CliEnvironmentSnapshot> commandLogger,
        Func<CliEnvironmentSnapshot, IChatModelClient> chatModelClientFactory,
        Func<TextWriter, IChatStreamingRenderer> streamingRendererFactory,
        Func<CliEnvironmentSnapshot, IConversationStore> conversationStoreFactory,
        Func<DateTimeOffset> utcNowProvider,
        Func<CliEnvironmentSnapshot, ToolRegistry, IToolExecutor, IAgentRunner> execAgentRunnerFactory)
    {
        ArgumentNullException.ThrowIfNull(snapshotProvider);

        return Create(
            output,
            (workspacePath, _) => snapshotProvider(workspacePath),
            commandLogger,
            chatModelClientFactory,
            streamingRendererFactory,
            conversationStoreFactory,
            utcNowProvider,
            execAgentRunnerFactory);
    }

    public static RootCommand Create(
        TextWriter output,
        Func<string?, CliEnvironmentSnapshot> snapshotProvider,
        Action<string, CliEnvironmentSnapshot> commandLogger,
        Func<CliEnvironmentSnapshot, IChatModelClient> chatModelClientFactory,
        Func<TextWriter, IChatStreamingRenderer> streamingRendererFactory,
        Func<CliEnvironmentSnapshot, IConversationStore> conversationStoreFactory,
        Func<DateTimeOffset> utcNowProvider,
        Func<CliEnvironmentSnapshot, ToolRegistry, IToolExecutor, IAgentRunner> execAgentRunnerFactory,
        Func<string, string?> environmentVariableProvider)
    {
        ArgumentNullException.ThrowIfNull(snapshotProvider);

        return Create(
            output,
            (workspacePath, _) => snapshotProvider(workspacePath),
            commandLogger,
            chatModelClientFactory,
            streamingRendererFactory,
            conversationStoreFactory,
            utcNowProvider,
            execAgentRunnerFactory,
            environmentVariableProvider);
    }

    public static RootCommand Create(
        TextWriter output,
        Func<string?, string?, CliEnvironmentSnapshot> snapshotProvider,
        Action<string, CliEnvironmentSnapshot> commandLogger,
        Func<CliEnvironmentSnapshot, IChatModelClient> chatModelClientFactory,
        Func<TextWriter, IChatStreamingRenderer> streamingRendererFactory,
        Func<CliEnvironmentSnapshot, IConversationStore> conversationStoreFactory,
        Func<DateTimeOffset> utcNowProvider,
        Func<CliEnvironmentSnapshot, ToolRegistry, IToolExecutor, IAgentRunner> execAgentRunnerFactory)
    {
        return Create(
            output,
            snapshotProvider,
            commandLogger,
            chatModelClientFactory,
            streamingRendererFactory,
            conversationStoreFactory,
            utcNowProvider,
            execAgentRunnerFactory,
            Console.In);
    }

    public static RootCommand Create(
        TextWriter output,
        Func<string?, string?, CliEnvironmentSnapshot> snapshotProvider,
        Action<string, CliEnvironmentSnapshot> commandLogger,
        Func<CliEnvironmentSnapshot, IChatModelClient> chatModelClientFactory,
        Func<TextWriter, IChatStreamingRenderer> streamingRendererFactory,
        Func<CliEnvironmentSnapshot, IConversationStore> conversationStoreFactory,
        Func<DateTimeOffset> utcNowProvider,
        Func<CliEnvironmentSnapshot, ToolRegistry, IToolExecutor, IAgentRunner> execAgentRunnerFactory,
        Func<string, string?> environmentVariableProvider)
    {
        return Create(
            output,
            snapshotProvider,
            commandLogger,
            chatModelClientFactory,
            streamingRendererFactory,
            conversationStoreFactory,
            utcNowProvider,
            execAgentRunnerFactory,
            Console.In,
            environmentVariableProvider);
    }

    private static RootCommand Create(
        TextWriter output,
        Func<string?, string?, CliEnvironmentSnapshot> snapshotProvider,
        Action<string, CliEnvironmentSnapshot> commandLogger,
        Func<CliEnvironmentSnapshot, IChatModelClient> chatModelClientFactory,
        Func<TextWriter, IChatStreamingRenderer> streamingRendererFactory,
        Func<CliEnvironmentSnapshot, IConversationStore> conversationStoreFactory,
        Func<DateTimeOffset> utcNowProvider,
        Func<CliEnvironmentSnapshot, ToolRegistry, IToolExecutor, IAgentRunner> execAgentRunnerFactory,
        TextReader input,
        Func<string, string?>? environmentVariableProvider = null)
    {
        CliDependencies dependencies = new(
            snapshotProvider,
            commandLogger,
            chatModelClientFactory,
            streamingRendererFactory,
            conversationStoreFactory,
            utcNowProvider,
            execAgentRunnerFactory,
            environmentVariableProvider ?? Environment.GetEnvironmentVariable);

        return Create(output, dependencies, input);
    }

    private static RootCommand Create(
        TextWriter output,
        CliDependencies dependencies,
        TextReader input)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(input);

        output = new CliOutputRouter(output);
        Func<string?, string?, CliEnvironmentSnapshot> snapshotProvider = dependencies.SnapshotProvider;
        Action<string, CliEnvironmentSnapshot> commandLogger = dependencies.CommandLogger;
        Func<CliEnvironmentSnapshot, IConversationStore> conversationStoreFactory = dependencies.ConversationStoreFactory;
        Func<DateTimeOffset> utcNowProvider = dependencies.UtcNowProvider;
        Func<CliEnvironmentSnapshot, ToolRegistry, IToolExecutor, IAgentRunner> execAgentRunnerFactory = dependencies.ExecAgentRunnerFactory;

        CliGlobalOptions globalOptions = new();
        CliCommandContext commandContext = new(output, input, dependencies, globalOptions);
        Func<string?, CliEnvironmentSnapshot> workspaceSnapshotProvider =
            commandContext.WorkspaceSnapshotProvider;
        CliRootComposer rootComposer = new(globalOptions);
        RootCommand rootCommand = rootComposer.RootCommand;
        commandContext.AttachRoot(rootCommand);
        Option<string> workspaceOption = globalOptions.Workspace;
        Option<bool> verboseOption = globalOptions.Verbose;
        Option<bool> traceOption = globalOptions.Trace;

        void WriteVerboseDiagnostics(
            ParseResult parseResult,
            string commandName,
            CliEnvironmentSnapshot snapshot,
            bool humanReadableOutput = true)
        {
            commandContext.WriteVerboseDiagnostics(
                parseResult,
                commandName,
                snapshot,
                humanReadableOutput);
        }

        DiagnosticContext? CreateTraceContext(ParseResult parseResult, CliEnvironmentSnapshot snapshot)
        {
            return commandContext.CreateTraceContext(parseResult, snapshot);
        }

        void TryWriteTraceCommandEvent(
            string commandName,
            CliEnvironmentSnapshot snapshot,
            DiagnosticContext? context,
            string type,
            long sequence,
            string status,
            string? summary = null,
            string? errorCode = null,
            DateTimeOffset? timestampUtc = null)
        {
            commandContext.TryWriteTraceCommandEvent(
                commandName,
                snapshot,
                context,
                type,
                sequence,
                status,
                summary,
                errorCode,
                timestampUtc);
        }

        void TryWriteTraceExecResult(
            string commandName,
            CliEnvironmentSnapshot snapshot,
            DiagnosticContext? context,
            ExecResult result)
        {
            commandContext.TryWriteTraceExecResult(commandName, snapshot, context, result);
        }


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

        rootComposer.Add(new VersionCommandModule(), commandContext);
        rootComposer.Add(new DoctorCommandModule(), commandContext);
        rootComposer.Add(new StatusCommandModule(), commandContext);
        rootComposer.Add(new ModelsCommandModule(), commandContext);
        rootComposer.Add(new DiffCommandModule(), commandContext);
        rootComposer.Add(new ChangesCommandModule(), commandContext);
        rootComposer.Add(new JobsCommandModule(), commandContext);
        rootComposer.Add(new CiCommandModule(), commandContext);
        rootComposer.Add(new DaemonCommandModule(), commandContext);
        rootComposer.Add(new ApiCommandModule(), commandContext);
        rootComposer.Add(new QueueCommandModule(), commandContext);
        rootComposer.Add(new AutomationCommandModule(), commandContext);
        rootComposer.Add(new PipelineCommandModule(), commandContext);
        rootComposer.Add(new ReviewCommandModule(), commandContext);
        rootComposer.Add(new ConfigCommandModule(), commandContext);
        rootComposer.Add(new McpCommandModule(), commandContext);
        rootComposer.Add(new WorkflowCommandModule(), commandContext);
        rootComposer.Add(new PacksCommandModule(), commandContext);
        rootComposer.Add(new ArtifactsCommandModule(), commandContext);
        rootComposer.Add(skillsCommand);
        rootComposer.Add(new ToolsCommandModule(), commandContext);
        rootComposer.Add(new LogsCommandModule(), commandContext);
        rootComposer.Add(execCommand);
        rootComposer.Add(new RunCommandModule(), commandContext);
        rootComposer.Add(new SessionCommandModule(), commandContext);
        rootComposer.Add(new ChatCommandModule(), commandContext);

        return rootComposer.Build();
    }

    public static int Invoke(RootCommand rootCommand, string[] args, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(rootCommand);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(error);

        ParseResult parseResult = rootCommand.Parse(args);
        if (parseResult.Errors.Count > 0)
        {
            foreach (ParseError parseError in parseResult.Errors)
            {
                error.WriteLine(parseError.Message);
            }

            return 2;
        }

        return parseResult.Invoke(new InvocationConfiguration
        {
            EnableDefaultExceptionHandler = !IsExecCommand(parseResult),
            Error = error,
        });
    }

    private static void TryWriteCommandLog(
        Action<string, CliEnvironmentSnapshot> commandLogger,
        string commandName,
        CliEnvironmentSnapshot snapshot)
    {
        try
        {
            commandLogger(commandName, snapshot);
        }
        catch
        {
        }
    }

    private static bool IsJsonOutputRequested(bool jsonRequested, string outputMode)
    {
        return jsonRequested || string.Equals(outputMode, "json", StringComparison.OrdinalIgnoreCase);
    }

    private static ApprovalMode? GetApprovalOverride(
        string? approvalModeValue,
        OptionResult? approvalOptionResult,
        bool approve)
    {
        if (approvalOptionResult is { Implicit: false } &&
            ConfigLoader.TryNormalizeApprovalMode(approvalModeValue, out ApprovalMode mode))
        {
            return mode;
        }

        return approve ? ApprovalMode.Always : null;
    }

    private static bool IsOptionExplicit<T>(ParseResult parseResult, Option<T> option)
    {
        return parseResult.GetResult(option) is { Implicit: false };
    }

    private static void AddApprovalModeValidator(Option<string> option)
    {
        option.Validators.Add(result =>
        {
            if (result.Implicit)
            {
                return;
            }

            string? approvalMode = result.GetValueOrDefault<string>();
            if (!ConfigLoader.TryNormalizeApprovalMode(approvalMode, out _))
            {
                result.AddError("Invalid value for --approval. Allowed values are never, on-request, on-failure, and always.");
            }
        });
    }

    private static IAgentRunner CreateDefaultExecAgentRunner(
        CliEnvironmentSnapshot snapshot,
        ToolRegistry registry,
        IToolExecutor executor) =>
        new OpenAiAgentRunnerFactory().Create(snapshot, registry, executor);

    private static bool IsExecCommand(ParseResult parseResult)
    {
        return string.Equals(parseResult.CommandResult.Command.Name, "exec", StringComparison.Ordinal);
    }

    private static AgentTaskReviewGateReport RunReadOnlyReviewGate(
        WorkspaceContext workspace,
        IToolExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(executor);

        ToolExecutionResult gitDiff = executor.Execute(
            "git.diff",
            new ToolExecutionContext(
                CallId: "review_gate_diff",
                Workspace: workspace,
                ArgumentsJson: """{"stat":true}""",
                Phase: ToolExecutionPhase.Planning));
        bool truncated = ReadBool(gitDiff.StructuredPayload, "truncated");
        bool hasDiff = gitDiff.Succeeded &&
            !string.IsNullOrWhiteSpace(gitDiff.Summary) &&
            !string.Equals(gitDiff.Summary.Trim(), "no diff", StringComparison.OrdinalIgnoreCase);

        if (!gitDiff.Succeeded)
        {
            return new AgentTaskReviewGateReport(
                Status: "warning",
                Summary: "Review gate could not read final diff: " + gitDiff.Summary,
                HasDiff: false,
                Truncated: truncated,
                ErrorCode: gitDiff.ErrorCode);
        }

        return new AgentTaskReviewGateReport(
            Status: truncated ? "warning" : "success",
            Summary: hasDiff ? gitDiff.Summary : "No diff to review.",
            HasDiff: hasDiff,
            Truncated: truncated,
            ErrorCode: gitDiff.ErrorCode);
    }

    private static ExecEvent CreateReviewGateEvent(
        AgentTaskReviewGateReport reviewGate,
        long sequence,
        DateTimeOffset timestampUtc)
    {
        ArgumentNullException.ThrowIfNull(reviewGate);

        Dictionary<string, string> payload = new(StringComparer.Ordinal)
        {
            ["phase"] = "review",
            ["readOnly"] = "true",
            ["toolName"] = "git.diff",
            ["hasDiff"] = reviewGate.HasDiff ? "true" : "false",
            ["truncated"] = reviewGate.Truncated ? "true" : "false"
        };
        if (!string.IsNullOrWhiteSpace(reviewGate.ErrorCode))
        {
            payload["errorCode"] = reviewGate.ErrorCode;
        }

        return new ExecEvent(
            Type: "review.gate",
            Sequence: sequence,
            Timestamp: timestampUtc,
            Message: "Read-only review gate summarized the final diff.",
            Summary: reviewGate.Summary,
            Payload: payload,
            ErrorCode: reviewGate.ErrorCode,
            Status: reviewGate.Status);
    }

    private static ExecEvent CreateReportGeneratedEvent(
        ExecReportMetadata report,
        long sequence,
        DateTimeOffset timestampUtc)
    {
        ArgumentNullException.ThrowIfNull(report);

        Dictionary<string, string> payload = new(StringComparer.Ordinal)
        {
            ["mode"] = report.Mode,
            ["generated"] = report.Generated ? "true" : "false"
        };
        if (!string.IsNullOrWhiteSpace(report.Path))
        {
            payload["path"] = report.Path;
        }

        if (!string.IsNullOrWhiteSpace(report.WriteStatus))
        {
            payload["writeStatus"] = report.WriteStatus;
        }

        if (!string.IsNullOrWhiteSpace(report.ErrorCode))
        {
            payload["errorCode"] = report.ErrorCode;
        }

        if (!string.IsNullOrWhiteSpace(report.Summary))
        {
            payload["summary"] = report.Summary;
        }

        return new ExecEvent(
            Type: "report.generated",
            Sequence: sequence,
            Timestamp: timestampUtc,
            Message: "Markdown task report metadata recorded.",
            Summary: report.Summary,
            Payload: payload,
            ErrorCode: report.ErrorCode,
            Status: report.ErrorCode is null ? "success" : "failure");
    }

    private static AgentRunResult CreateWorkflowReferenceFailureResult(
        WorkflowReferenceResolution references,
        DateTimeOffset timestampUtc)
    {
        ArgumentNullException.ThrowIfNull(references);

        string errorCode = references.FirstErrorCode ?? WorkflowReferenceErrorCode.ResolutionFailed;
        AgentError error = new(
            errorCode,
            "Workflow reference resolution failed.",
            Retryable: false);
        AgentRunEvent referenceEvent = WorkflowReferenceEventFactory.Create(
            references,
            sequence: 0,
            timestampUtc);
        AgentRunEvent errorEvent = new(
            Type: "agent.error",
            Sequence: 1,
            Timestamp: timestampUtc,
            Message: error.SafeMessage,
            ErrorCode: error.LocalErrorCode,
            Status: "failure",
            StopReason: AgentStopReason.FromErrorCode(error.LocalErrorCode));

        return AgentRunResult.Failure(
            error,
            [],
            [referenceEvent, errorEvent],
            stopReason: AgentStopReason.FromErrorCode(error.LocalErrorCode),
            status: "failure");
    }

    private static ExecEvent? CreateReferenceContextEventIfMissing(
        IReadOnlyList<ExecEvent> existingEvents,
        WorkflowReferenceResolution references,
        DateTimeOffset timestampUtc)
    {
        ArgumentNullException.ThrowIfNull(existingEvents);
        ArgumentNullException.ThrowIfNull(references);

        if (!references.HasReferences ||
            existingEvents.Any(execEvent => string.Equals(execEvent.Type, "context.references", StringComparison.Ordinal)))
        {
            return null;
        }

        long sequence = existingEvents.Count == 0
            ? 0
            : existingEvents[^1].Sequence + 1;
        AgentRunEvent referenceEvent = WorkflowReferenceEventFactory.Create(
            references,
            sequence,
            timestampUtc);
        return new ExecEvent(
            Type: referenceEvent.Type,
            Sequence: referenceEvent.Sequence,
            Timestamp: referenceEvent.Timestamp,
            Message: referenceEvent.Message,
            Summary: referenceEvent.Summary,
            Payload: referenceEvent.Payload,
            ErrorCode: referenceEvent.ErrorCode,
            ApprovalStatus: referenceEvent.ApprovalStatus,
            Status: referenceEvent.Status,
            DurationMs: referenceEvent.DurationMs,
            ApprovalDurationMs: referenceEvent.ApprovalDurationMs,
            StepIndex: referenceEvent.StepIndex,
            StopReason: referenceEvent.StopReason);
    }

    private static bool ReadBool(
        IReadOnlyDictionary<string, JsonElement>? structuredPayload,
        string key)
    {
        if (structuredPayload is null ||
            !structuredPayload.TryGetValue(key, out JsonElement value))
        {
            return false;
        }

        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean();
        }

        return value.ValueKind == JsonValueKind.String &&
            bool.TryParse(value.GetString(), out bool parsed) &&
            parsed;
    }

    private static void AddPositiveIntegerValidator(Option<int?> option, string optionName)
    {
        option.Validators.Add(result =>
        {
            int? value = result.GetValueOrDefault<int?>();
            if (value is <= 0)
            {
                result.AddError($"Invalid value for {optionName}. Value must be greater than zero.");
            }
        });
    }

    private static bool TryParseProjectPackToolPaths(
        ProjectPackManifest manifest,
        IReadOnlyList<string> values,
        out Dictionary<string, string> toolPaths,
        out string? error)
    {
        toolPaths = new Dictionary<string, string>(StringComparer.Ordinal);
        error = null;
        string? primaryDependencyId = manifest.Dependencies.FirstOrDefault()?.Id;
        foreach (string value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                error = "Tool path binding cannot be empty.";
                return false;
            }

            string dependencyId;
            string path;
            int separator = value.IndexOf('=');
            if (separator > 0)
            {
                dependencyId = value[..separator];
                path = value[(separator + 1)..];
            }
            else
            {
                if (primaryDependencyId is null || toolPaths.ContainsKey(primaryDependencyId))
                {
                    error = "Only one bare --tool-path value is allowed; use dependency=path for additional tools.";
                    return false;
                }

                dependencyId = primaryDependencyId;
                path = value;
            }

            if (!manifest.Dependencies.Any(dependency => string.Equals(dependency.Id, dependencyId, StringComparison.Ordinal)))
            {
                error = $"Unknown project pack dependency '{DiagnosticSecretRedactor.Redact(dependencyId)}'.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                error = $"Tool path for dependency '{dependencyId}' cannot be empty.";
                return false;
            }

            if (!toolPaths.TryAdd(dependencyId, path))
            {
                error = $"Tool path for dependency '{dependencyId}' was specified more than once.";
                return false;
            }
        }

        return true;
    }

    private static void AddTextJsonOutputValidator(Option<string> option)
    {
        option.Validators.Add(result =>
        {
            string outputMode = result.GetValueOrDefault<string>() ?? "text";
            if (!string.Equals(outputMode, "text", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(outputMode, "json", StringComparison.OrdinalIgnoreCase))
            {
                result.AddError("Invalid value for --output. Allowed values are text and json.");
            }
        });
    }

    private static void AddNonNegativeIntegerValidator(Option<int?> option, string optionName)
    {
        option.Validators.Add(result =>
        {
            int? value = result.GetValueOrDefault<int?>();
            if (value is < 0)
            {
                result.AddError($"Invalid value for {optionName}. Value must be greater than or equal to zero.");
            }
        });
    }

    private static void WriteExecOutput(ExecTextRenderer renderer, ExecResult result)
    {
        foreach (ExecEvent execEvent in result.Events)
        {
            renderer.WriteEvent(execEvent);
        }

        renderer.WriteResult(result);
    }

    private static void WriteExecOutput(ExecJsonRenderer renderer, ExecResult result)
    {
        foreach (ExecEvent execEvent in result.Events)
        {
            renderer.WriteEvent(execEvent);
        }

        renderer.WriteResult(result);
    }

    private static (string ErrorCode, string Summary) GetConversationStoreFailure(Exception exception)
    {
        if (IsInvalidConversationTranscriptException(exception))
        {
            return ("session-transcript-invalid", InvalidConversationTranscriptSummary);
        }

        return ("session-store-error", SessionStoreErrorSummary);
    }

    private static bool IsInvalidConversationTranscriptException(Exception exception)
    {
        return exception is JsonException ||
            exception is InvalidOperationException invalidOperationException &&
            string.Equals(
                invalidOperationException.Message,
                InvalidConversationTranscriptSummary,
                StringComparison.Ordinal);
    }

    private static bool IsConversationStoreException(Exception exception)
    {
        return exception is InvalidOperationException or IOException or UnauthorizedAccessException or JsonException or NotSupportedException;
    }

    private static string GetSafeSessionNameParseMessage(ArgumentException exception)
    {
        string message = exception.Message;
        if (string.IsNullOrEmpty(exception.ParamName))
        {
            return message;
        }

        string parameterSuffix = $" (Parameter '{exception.ParamName}')";
        return message.EndsWith(parameterSuffix, StringComparison.Ordinal)
            ? message[..^parameterSuffix.Length]
            : message;
    }

    private static void WriteSafeFailure(TextWriter output, string errorCode, string summary)
    {
        output.WriteLine("status: failed");
        output.WriteLine($"errorCode: {errorCode}");
        output.WriteLine("summary:");
        output.WriteLine(summary);
    }

    private static ExecResult CompleteRecordedJob(
        JobRecordStore? jobStore,
        JobRecord? currentRecord,
        ExecResult result,
        DateTimeOffset nowUtc,
        string? sessionPath = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (jobStore is null || currentRecord is null)
        {
            return result;
        }

        try
        {
            JobRecord updated = JobRecord.FromExecResult(
                currentRecord,
                result,
                nowUtc,
                CreateJobArtifacts(result, sessionPath));
            jobStore.Update(updated);
            return result;
        }
        catch (Exception exception) when (IsJobStoreException(exception))
        {
            return ExecResult.Failure(
                ExitCode: 1,
                Summary: "Job record could not be updated.",
                ErrorCode: "job-record-write-failed",
                Events: result.Events,
                ApprovalStatus: result.ApprovalStatus,
                StopReason: result.StopReason,
                ChangedFiles: result.ChangedFiles,
                VerificationResults: result.VerificationResults,
                RetryAttempts: result.RetryAttempts,
                FailureSummary: result.FailureSummary,
                TaskReport: result.TaskReport);
        }
    }

    private static IReadOnlyList<JobArtifact> CreateJobArtifacts(ExecResult result, string? sessionPath)
    {
        List<JobArtifact> artifacts = [];
        if (result.TaskReport is not null)
        {
            artifacts.Add(new JobArtifact(
                JobArtifactKind.TaskReport,
                "inline:taskReport",
                Exists: true,
                Summary: AgentTaskReportBuilder.CreateSummary(result.TaskReport)));

            if (!string.IsNullOrWhiteSpace(result.TaskReport.TracePath))
            {
                artifacts.Add(JobArtifact.FromPath(
                    JobArtifactKind.Trace,
                    result.TaskReport.TracePath,
                    "Trace JSONL diagnostics."));
            }

            if (!string.IsNullOrWhiteSpace(result.TaskReport.Report?.Path))
            {
                artifacts.Add(JobArtifact.FromPath(
                    JobArtifactKind.MarkdownReport,
                    result.TaskReport.Report.Path,
                    result.TaskReport.Report.Summary));
            }
        }

        if (!string.IsNullOrWhiteSpace(sessionPath))
        {
            artifacts.Add(JobArtifact.FromPath(
                JobArtifactKind.Session,
                sessionPath,
                "Conversation transcript."));
        }

        return artifacts;
    }

    private static bool TryReadProjectPackPlan(
        WorkspaceContext workspace,
        string? requestedPath,
        out string? json,
        out string? errorCode,
        out string? summary)
    {
        json = null;
        errorCode = null;
        summary = null;
        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            errorCode = ProjectPackRunErrorCode.PlanInvalid;
            summary = "An explicit project pack plan file is required.";
            return false;
        }

        WorkspaceGuardResult guard = new WorkspaceGuard().ResolvePath(workspace, requestedPath);
        if (!guard.IsAllowed || guard.FullPath is null || !File.Exists(guard.FullPath))
        {
            errorCode = ProjectPackRunErrorCode.PlanInvalid;
            summary = "Project pack plan must be a file inside the workspace.";
            return false;
        }

        try
        {
            ManagedProjectPackRunStore.EnsureNoReparseInExistingChain(guard.FullPath);
            FileInfo info = new(guard.FullPath);
            if (info.Length is <= 0 or > 2 * 1024 * 1024)
            {
                errorCode = ProjectPackRunErrorCode.PlanInvalid;
                summary = "Project pack plan file is empty or exceeds the size limit.";
                return false;
            }

            using FileStream stream = new(
                guard.FullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.SequentialScan);
            using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            json = reader.ReadToEnd();
            info.Refresh();
            if (!info.Exists || info.Length != stream.Length)
            {
                json = null;
                errorCode = ProjectPackRunErrorCode.PlanInvalid;
                summary = "Project pack plan changed while it was read.";
                return false;
            }

            return true;
        }
        catch (ProjectPackContractException exception)
        {
            errorCode = exception.ErrorCode;
            summary = exception.Message;
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            errorCode = ProjectPackRunErrorCode.PlanInvalid;
            summary = "Project pack plan could not be read safely.";
            return false;
        }
    }

    private static string GetProjectPackPolicyFingerprint(CliEnvironmentSnapshot snapshot, string packId) =>
        ProjectPackRunPolicyFingerprint.Compute(
            $"pack={packId};schema={ProjectPackSchema.CurrentVersion};approval={snapshot.Configuration.ApprovalMode}");

    private static string MapProjectPackExecutionProbeError(ProjectPackDoctorReport report)
    {
        if (report.Status == ProjectPackDoctorStatus.ApprovalRequired ||
            report.Diagnostics.Any(diagnostic => diagnostic.Code == ExternalToolDiagnosticCode.ApprovalDenied))
        {
            return ProjectPackRunErrorCode.ApprovalRequired;
        }

        if (report.Diagnostics.Any(diagnostic => diagnostic.Code == ExternalToolDiagnosticCode.VersionUnsupported))
        {
            return ProjectPackRunErrorCode.ToolVersionUnsupported;
        }

        if (report.Diagnostics.Any(diagnostic => diagnostic.Code is ExternalToolDiagnosticCode.PathMissing
            or ExternalToolDiagnosticCode.PathNotFound
            or ExternalToolDiagnosticCode.PathDirectory))
        {
            return ProjectPackRunErrorCode.ToolNotFound;
        }

        if (report.Diagnostics.Any(diagnostic => diagnostic.Code is ExternalToolDiagnosticCode.FileChanged
            or ExternalToolDiagnosticCode.HashChanged
            or ExternalToolDiagnosticCode.PathReparsePoint))
        {
            return ProjectPackRunErrorCode.ToolIdentityChanged;
        }

        return ProjectPackRunErrorCode.ExecutionFailed;
    }

    private static string? ResolveProjectPackArtifactPath(
        ProjectPackRunArtifactPointer pointer,
        ManagedProjectPackRunLayout layout,
        WorkspaceContext workspace)
    {
        try
        {
            string root = pointer.Scope switch
            {
                "managed-run" => layout.RunRoot,
                "workspace-output" => workspace.RootPath,
                _ => string.Empty
            };
            if (root.Length == 0 || Path.IsPathRooted(pointer.Path) || pointer.Path.Contains("..", StringComparison.Ordinal))
            {
                return null;
            }

            string path = Path.GetFullPath(Path.Combine(
                root,
                pointer.Path.Replace('/', Path.DirectorySeparatorChar)));
            string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(
                normalizedRoot,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                return null;
            }

            ManagedProjectPackRunStore.EnsureNoReparseInExistingChain(path);
            return path;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ProjectPackContractException)
        {
            return null;
        }
    }

    private static bool TryUpdateProjectPackVerificationJob(
        CliEnvironmentSnapshot snapshot,
        ProjectPackRunRecord record,
        bool previewCommand,
        bool commandSucceeded,
        DateTimeOffset nowUtc,
        out string? error)
    {
        error = null;
        string? jobId = record.Correlation.JobId;
        if (jobId is null)
        {
            error = "Project pack run does not contain its original job correlation.";
            return false;
        }

        try
        {
            JobRecordStore jobStore = JobRecordStore.Create(snapshot);
            JobRecordReadResult read = jobStore.Read(jobId);
            if (!read.Succeeded || read.Record is null)
            {
                error = "Correlated project pack job record was not found.";
                return false;
            }

            ManagedProjectPackRunStore runStore = ManagedProjectPackRunStore.Create(snapshot);
            ManagedProjectPackRunLayout layout = runStore.GetLayout(record.RunId);
            List<JobArtifact> artifacts = read.Record.Artifacts
                .Where(artifact => artifact.Kind != JobArtifactKind.ProjectPackRun &&
                    artifact.Kind != JobArtifactKind.ProjectPackVerificationReport &&
                    artifact.Kind != JobArtifactKind.ProjectPackPreview)
                .ToList();
            artifacts.Add(JobArtifact.FromPath(
                JobArtifactKind.ProjectPackRun,
                layout.RunRecordPath,
                $"packRunId={record.RunId}; operational checkpoint pointer only"));
            foreach (ProjectPackRunArtifactPointer pointer in record.Artifacts.Where(pointer => pointer.Kind is
                "tiff-verification-json" or
                "tiff-verification-markdown" or
                "tiff-preview-report" or
                "tiff-preview" or
                "tiff-contact-sheet"))
            {
                string? path = ResolveProjectPackArtifactPath(pointer, layout, snapshot.Workspace);
                if (path is null || !File.Exists(path))
                {
                    error = "A managed TIFF verification/preview artifact pointer is missing or unsafe.";
                    return false;
                }

                string kind = pointer.Kind is "tiff-preview" or "tiff-contact-sheet"
                    ? JobArtifactKind.ProjectPackPreview
                    : JobArtifactKind.ProjectPackVerificationReport;
                artifacts.Add(JobArtifact.FromPath(
                    kind,
                    path,
                    pointer.Kind is "tiff-preview" or "tiff-contact-sheet"
                        ? "Human-review preview only; not a correctness proof."
                        : "Deterministic TIFF verification/preview report evidence."));
            }

            JobArtifact[] uniqueArtifacts = artifacts
                .GroupBy(artifact => artifact.Kind + "\n" + artifact.Path, StringComparer.Ordinal)
                .Select(group => group.Last())
                .ToArray();
            string status = previewCommand
                ? read.Record.Status
                : commandSucceeded ? JobStatus.Succeeded : JobStatus.Failed;
            string summary = previewCommand
                ? commandSucceeded
                    ? "Managed PNG preview/contact sheet evidence was generated; explicit human accept/reject remains required."
                    : "Managed PNG preview generation was incomplete; hard verification state was unchanged."
                : commandSucceeded
                    ? "TIFF hard verification passed; the run is awaiting explicit human acceptance."
                    : "TIFF hard verification failed; the run did not reach human acceptance.";
            JobRecord updated = read.Record.WithStatus(
                status,
                nowUtc,
                exitCode: previewCommand ? read.Record.ExitCode : commandSucceeded ? 0 : 1,
                stopReason: previewCommand
                    ? commandSucceeded ? "preview-generated-awaiting-acceptance" : "preview-failed-awaiting-acceptance"
                    : commandSucceeded ? "verification-passed-awaiting-acceptance" : "verification-failed",
                errorCode: previewCommand || commandSucceeded ? null : record.ErrorCode,
                summary: summary,
                taskReport: null,
                artifacts: uniqueArtifacts,
                warnings: read.Record.Warnings.Concat(
                [
                    previewCommand
                        ? "Preview is a human-review aid and does not alter hard verification or acceptance."
                        : "Verification levels are independent; lower-level success does not imply content comparison.",
                    "Project pack run state remains the operational checkpoint; taskReport was not duplicated."
                ]).Distinct(StringComparer.Ordinal).ToArray());
            jobStore.Update(updated);
            return true;
        }
        catch (Exception exception) when (IsJobStoreException(exception) || exception is ProjectPackContractException)
        {
            error = "Correlated project pack job record could not be updated safely.";
            return false;
        }
    }

    private static bool TryUpdateProjectPackAcceptanceJob(
        CliEnvironmentSnapshot snapshot,
        ProjectPackRunRecord record,
        DateTimeOffset nowUtc,
        out string? error)
    {
        error = null;
        if (record.Acceptance is null || record.Correlation.JobId is null)
        {
            error = "Project pack run does not contain a correlated human decision and job id.";
            return false;
        }

        try
        {
            JobRecordStore store = JobRecordStore.Create(snapshot);
            JobRecordReadResult read = store.Read(record.Correlation.JobId);
            if (!read.Succeeded || read.Record is null)
            {
                error = "Correlated project pack job record was not found.";
                return false;
            }

            ProjectPackAcceptanceDecision decision = record.Acceptance;
            List<JobArtifact> artifacts = read.Record.Artifacts
                .Where(artifact => artifact.Kind != JobArtifactKind.ProjectPackAcceptance)
                .ToList();
            artifacts.Add(new JobArtifact(
                JobArtifactKind.ProjectPackAcceptance,
                $"inline:acceptance/{record.RunId}",
                Exists: true,
                Summary: $"outcome={decision.Outcome}; actor={decision.Actor}; verificationArtifactId={decision.VerificationArtifactId}",
                Sha256: decision.VerificationSha256,
                CreatedAtUtc: decision.DecidedAtUtc));
            bool accepted = decision.Outcome == ProjectPackRunState.Accepted;
            JobRecord updated = read.Record.WithStatus(
                accepted ? JobStatus.Succeeded : JobStatus.Failed,
                nowUtc,
                exitCode: accepted ? 0 : 1,
                stopReason: accepted ? "human-accepted" : "human-rejected",
                errorCode: accepted ? null : ProjectPackRunErrorCode.HumanRejected,
                summary: accepted
                    ? "TIFF hard verification evidence was explicitly accepted by a human reviewer."
                    : "TIFF hard verification evidence was explicitly rejected by a human reviewer.",
                taskReport: null,
                artifacts: artifacts,
                warnings: read.Record.Warnings.Concat(
                [
                    $"packRunId={record.RunId}; acceptance={decision.Outcome}; verificationArtifactId={decision.VerificationArtifactId}",
                    "Human acceptance is explicit evidence and was not produced by a model or tool.",
                    "Project pack run state remains the operational checkpoint; taskReport was not duplicated."
                ]).Distinct(StringComparer.Ordinal).ToArray());
            store.Update(updated);
            return true;
        }
        catch (Exception exception) when (IsJobStoreException(exception))
        {
            error = "Correlated project pack human decision job evidence could not be updated safely.";
            return false;
        }
    }

    private static bool TryUpdateRecoveredProjectPackCorrelations(
        CliEnvironmentSnapshot snapshot,
        ProjectPackRunRecord record,
        DateTimeOffset nowUtc,
        out string? error)
    {
        error = null;
        try
        {
            if (record.Correlation.JobId is not null)
            {
                JobRecordStore jobStore = JobRecordStore.Create(snapshot);
                JobRecordReadResult jobRead = jobStore.Read(record.Correlation.JobId);
                if (jobRead.Succeeded && jobRead.Record is not null && jobRead.Record.Status == JobStatus.Running)
                {
                    jobStore.Update(jobRead.Record.WithStatus(
                        JobStatus.Failed,
                        nowUtc,
                        exitCode: 1,
                        stopReason: "manual-interrupted-recovery",
                        errorCode: ProjectPackRunErrorCode.RestartRequired,
                        summary: "A human explicitly marked the stale local execute state interrupted; no process was replayed or terminated.",
                        taskReport: null,
                        warnings: jobRead.Record.Warnings.Concat(
                        [
                            $"packRunId={record.RunId}; state=interrupted; restartRequired=true",
                            "Manual recovery does not prove process cleanup; the operator confirmed the execute process was no longer active."
                        ]).Distinct(StringComparer.Ordinal).ToArray()));
                }
                else if (!jobRead.Succeeded && jobRead.Diagnostic?.ErrorCode != "job-not-found")
                {
                    error = "Correlated stale job record could not be read safely.";
                    return false;
                }
            }

            if (record.Correlation.QueueId is not null)
            {
                TaskQueueStore queueStore = TaskQueueStore.Create(snapshot);
                TaskQueueReadResult queueRead = queueStore.Read(record.Correlation.QueueId);
                if (queueRead.Succeeded && queueRead.Item is not null &&
                    queueRead.Item.Status == TaskQueueStatus.Running && queueRead.Item.Attempts.Count > 0)
                {
                    TaskQueueTransitionResult queueResult = queueStore.Complete(
                        record.Correlation.QueueId,
                        queueRead.Item.Attempts[^1].Attempt,
                        nowUtc,
                        exitCode: 1,
                        record.Correlation.JobId,
                        ProjectPackRunErrorCode.RestartRequired,
                        "Correlated queue attempt was explicitly marked failed after stale running recovery.");
                    if (!queueResult.Succeeded)
                    {
                        error = queueResult.Diagnostic?.Summary ?? "Correlated stale queue attempt could not be completed.";
                        return false;
                    }
                }
                else if (!queueRead.Succeeded && queueRead.Diagnostic?.ErrorCode != TaskQueueErrorCode.NotFound)
                {
                    error = "Correlated stale queue record could not be read safely.";
                    return false;
                }
            }

            return true;
        }
        catch (Exception exception) when (IsJobStoreException(exception) || IsQueueStoreException(exception))
        {
            error = "Stale run correlation metadata could not be updated safely.";
            return false;
        }
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static bool IsJobStoreException(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException
            or InvalidOperationException;
    }

    private static bool IsQueueStoreException(Exception exception) => IsJobStoreException(exception);

    private static string ResolveSessionPath(CliEnvironmentSnapshot snapshot, ConversationSessionName sessionName)
    {
        string? userConfigDirectory = Path.GetDirectoryName(snapshot.UserConfigPath);
        string root = string.IsNullOrWhiteSpace(userConfigDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".caicli")
            : userConfigDirectory;
        string sessionDirectory = Path.GetFullPath(Path.Combine(root, "sessions"));
        string path = Path.GetFullPath(Path.Combine(sessionDirectory, $"{sessionName.FileSafeName}.transcript.json"));
        string rootedSessionDirectory = sessionDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? sessionDirectory
            : sessionDirectory + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!path.StartsWith(rootedSessionDirectory, comparison))
        {
            throw new InvalidOperationException("Conversation transcript path must remain inside the session directory.");
        }

        return path;
    }

}
