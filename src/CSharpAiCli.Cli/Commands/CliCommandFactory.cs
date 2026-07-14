using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CSharpAiCli.Core;
using CSharpAiCli.ProjectPacks;
using CSharpAiCli.ProjectPacks.GerberTiff;

namespace CSharpAiCli.Cli;

public static class CliCommandFactory
{
    private const string InvalidConversationTranscriptSummary =
        "Conversation transcript is missing or uses an unsupported schema version.";

    private const string SessionStoreErrorSummary =
        "Conversation session store operation failed.";

    private const string TrustedMcpRegistrySource = "user config";

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
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(snapshotProvider);
        ArgumentNullException.ThrowIfNull(commandLogger);
        ArgumentNullException.ThrowIfNull(chatModelClientFactory);
        ArgumentNullException.ThrowIfNull(streamingRendererFactory);
        ArgumentNullException.ThrowIfNull(conversationStoreFactory);
        ArgumentNullException.ThrowIfNull(utcNowProvider);
        ArgumentNullException.ThrowIfNull(execAgentRunnerFactory);
        ArgumentNullException.ThrowIfNull(input);
        environmentVariableProvider ??= Environment.GetEnvironmentVariable;

        Func<string?, CliEnvironmentSnapshot> workspaceSnapshotProvider =
            workspacePath => snapshotProvider(workspacePath, null);
        ProjectPackRegistry projectPackRegistry = new([new GerberTiffWorkflowPack()]);

        RootCommand rootCommand = new($"{ProductInfo.CommandName} - {ProductInfo.Description}");
        Option<string> workspaceOption = new("--workspace")
        {
            Description = "Use a workspace directory instead of the current directory.",
            Recursive = true,
        };
        Option<bool> verboseOption = new("--verbose")
        {
            Description = "Show detailed human-readable diagnostics.",
            Recursive = true,
        };
        Option<bool> traceOption = new("--trace")
        {
            Description = "Write trace-level local diagnostics.",
            Recursive = true,
        };
        rootCommand.Options.Add(workspaceOption);
        rootCommand.Options.Add(verboseOption);
        rootCommand.Options.Add(traceOption);

        void WriteVerboseDiagnostics(
            ParseResult parseResult,
            string commandName,
            CliEnvironmentSnapshot snapshot,
            bool humanReadableOutput = true)
        {
            if (!humanReadableOutput || !parseResult.GetValue(verboseOption))
            {
                return;
            }

            DiagnosticContext context = DiagnosticContext.Create(
                workspace: snapshot.CurrentDirectory,
                utcNowProvider: utcNowProvider);
            output.WriteLine(VerboseDiagnosticsReport.Create(commandName, snapshot, context).ToDisplayText());
            output.WriteLine();
        }

        bool IsTraceEnabled(ParseResult parseResult)
        {
            return parseResult.GetValue(traceOption) ||
                string.Equals(environmentVariableProvider("CAICLI_TRACE"), "1", StringComparison.Ordinal);
        }

        DiagnosticContext? CreateTraceContext(ParseResult parseResult, CliEnvironmentSnapshot snapshot)
        {
            if (!IsTraceEnabled(parseResult))
            {
                return null;
            }

            return DiagnosticContext.Create(
                workspace: snapshot.CurrentDirectory,
                utcNowProvider: utcNowProvider);
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
            if (context is null)
            {
                return;
            }

            try
            {
                TraceLogger.AppendCommandEvent(commandName, snapshot, context, type, sequence, status, summary, errorCode, timestampUtc);
            }
            catch
            {
            }
        }

        void TryWriteTraceExecResult(
            string commandName,
            CliEnvironmentSnapshot snapshot,
            DiagnosticContext? context,
            ExecResult result)
        {
            if (context is null)
            {
                return;
            }

            try
            {
                TraceLogger.AppendExecResult(commandName, snapshot, context, result);
            }
            catch
            {
            }
        }

        Command versionCommand = new("version", "Print product version metadata.");
        versionCommand.SetAction(parseResult =>
        {
            if (parseResult.GetValue(verboseOption))
            {
                string? workspacePath = parseResult.GetValue(workspaceOption);
                CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
                WriteVerboseDiagnostics(parseResult, "version", snapshot);
            }

            output.WriteLine($"{ProductInfo.CommandName} {ProductInfo.Version}");
            output.WriteLine($"target framework: {ProductInfo.TargetFramework}");
            output.WriteLine($"release runtime: {ProductInfo.ReleaseRuntime}");
            return 0;
        });

        Command doctorCommand = new("doctor", "Inspect runtime, workspace, and configuration readiness.");
        doctorCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
            DiagnosticContext? traceContext = CreateTraceContext(parseResult, snapshot);
            TryWriteTraceCommandEvent("doctor", snapshot, traceContext, "command.start", 0, "started", timestampUtc: utcNowProvider());
            TryWriteCommandLog(commandLogger, "doctor", snapshot);
            WriteVerboseDiagnostics(parseResult, "doctor", snapshot);
            output.WriteLine(DoctorReport.Create(snapshot).ToDisplayText());
            TryWriteTraceCommandEvent("doctor", snapshot, traceContext, "command.complete", 1, "success", timestampUtc: utcNowProvider());
            return 0;
        });

        Command statusCommand = new("status", "Summarize workspace, git, configuration, and approval status.");
        statusCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "status", snapshot);
            WriteVerboseDiagnostics(parseResult, "status", snapshot);
            GitStatusTool gitStatusTool = new(new WorkspaceGuard());
            ToolExecutionResult gitStatus = gitStatusTool.Execute(new ToolExecutionContext(
                "cli_status",
                snapshot.Workspace,
                "{}"));
            output.WriteLine(StatusReport.Create(snapshot, gitStatus).ToDisplayText());
            return 0;
        });

        Command modelsCommand = new("models", "Show current model configuration and static model examples.");
        modelsCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "models", snapshot);
            WriteVerboseDiagnostics(parseResult, "models", snapshot);
            output.WriteLine(ModelsReport.Create(snapshot).ToDisplayText());
            return 0;
        });

        Command diffCommand = new("diff", "Show current git diff for the workspace.");
        Option<bool> diffStatOption = new("--stat")
        {
            Description = "Show git diff stat instead of the full patch.",
        };
        diffCommand.Options.Add(diffStatOption);
        diffCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            bool stat = parseResult.GetValue(diffStatOption);
            CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "diff", snapshot);
            WriteVerboseDiagnostics(parseResult, "diff", snapshot);
            GitDiffTool gitDiffTool = new(new WorkspaceGuard());
            string argumentsJson = stat ? """{"stat":true}""" : "{}";
            ToolExecutionResult result = gitDiffTool.Execute(new ToolExecutionContext(
                "cli_diff",
                snapshot.Workspace,
                argumentsJson));

            if (result.Succeeded)
            {
                output.WriteLine(result.Summary);
            }
            else
            {
                WriteToolResult(output, result);
            }

            return result.Succeeded ? 0 : 1;
        });

        Command changesCommand = new("changes", "Show a read-only changes view for the workspace.");
        Option<bool> changesJsonOption = new("--json")
        {
            Description = "Write a single JSON changes view object.",
        };
        Option<string> changesOutputOption = new("--output")
        {
            Description = "Select text or json output.",
        };
        Option<string> changesSessionOption = new("--session")
        {
            Description = "Include the latest task report from a named session transcript.",
        };
        changesOutputOption.DefaultValueFactory = _ => "text";
        changesOutputOption.Validators.Add(result =>
        {
            string outputMode = result.GetValueOrDefault<string>() ?? "text";
            if (!string.Equals(outputMode, "text", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(outputMode, "json", StringComparison.OrdinalIgnoreCase))
            {
                result.AddError("Invalid value for --output. Allowed values are text and json.");
            }
        });
        changesCommand.Options.Add(changesJsonOption);
        changesCommand.Options.Add(changesOutputOption);
        changesCommand.Options.Add(changesSessionOption);
        changesCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            bool jsonRequested = parseResult.GetValue(changesJsonOption);
            string outputMode = parseResult.GetValue(changesOutputOption) ?? "text";
            string? session = parseResult.GetValue(changesSessionOption);
            CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
            DiagnosticContext? traceContext = CreateTraceContext(parseResult, snapshot);
            WriteVerboseDiagnostics(
                parseResult,
                "changes",
                snapshot,
                humanReadableOutput: !IsJsonOutputRequested(jsonRequested, outputMode));

            GitStatusTool gitStatusTool = new(new WorkspaceGuard());
            ToolExecutionResult gitStatus = gitStatusTool.Execute(new ToolExecutionContext(
                "changes_git_status",
                snapshot.Workspace,
                "{}",
                ToolExecutionPhase.Planning));
            GitDiffTool gitDiffTool = new(new WorkspaceGuard());
            ToolExecutionResult gitDiffStat = gitDiffTool.Execute(new ToolExecutionContext(
                "changes_git_diff_stat",
                snapshot.Workspace,
                """{"stat":true}""",
                ToolExecutionPhase.Planning));

            ConversationTranscript? transcript = null;
            string? sessionPath = null;
            string? sessionWarning = null;
            if (!string.IsNullOrWhiteSpace(session))
            {
                if (!TryParseSessionName(output, session, out ConversationSessionName sessionName))
                {
                    return 1;
                }

                sessionPath = ResolveSessionPath(snapshot, sessionName);
                try
                {
                    IConversationStore conversationStore = conversationStoreFactory(snapshot);
                    if (!conversationStore.TryLoad(sessionName, out transcript) || transcript is null)
                    {
                        sessionWarning = "Session transcript was not found.";
                    }
                }
                catch (Exception exception) when (IsConversationStoreException(exception))
                {
                    sessionWarning = "Conversation session store operation failed.";
                }
            }

            ChangesViewReport report = ChangesViewReport.Create(
                snapshot.Workspace,
                gitStatus,
                gitDiffStat,
                transcript,
                session,
                sessionPath,
                sessionWarning);
            TryWriteTraceCommandEvent(
                "changes",
                snapshot,
                traceContext,
                "changes.view",
                0,
                report.Status,
                report.Summary,
                timestampUtc: utcNowProvider());

            if (IsJsonOutputRequested(jsonRequested, outputMode))
            {
                new ChangesJsonRenderer(output).Write(report);
            }
            else
            {
                new ChangesTextRenderer(output).Write(report);
            }

            return report.ExitCode;
        });

        Command jobsCommand = new("jobs", "Read local job history records.");
        Command jobsListCommand = new("list", "List local job records.");
        Option<bool> jobsListJsonOption = new("--json")
        {
            Description = "Write a single JSON jobs list object.",
        };
        Option<string> jobsListOutputOption = new("--output")
        {
            Description = "Select text or json output.",
        };
        Option<int?> jobsListLimitOption = new("--limit")
        {
            Description = "Maximum number of job records to list.",
        };
        jobsListOutputOption.DefaultValueFactory = _ => "text";
        jobsListOutputOption.Validators.Add(result =>
        {
            string outputMode = result.GetValueOrDefault<string>() ?? "text";
            if (!string.Equals(outputMode, "text", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(outputMode, "json", StringComparison.OrdinalIgnoreCase))
            {
                result.AddError("Invalid value for --output. Allowed values are text and json.");
            }
        });
        AddPositiveIntegerValidator(jobsListLimitOption, "--limit");
        jobsListCommand.Options.Add(jobsListJsonOption);
        jobsListCommand.Options.Add(jobsListOutputOption);
        jobsListCommand.Options.Add(jobsListLimitOption);
        jobsListCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            bool jsonRequested = parseResult.GetValue(jobsListJsonOption);
            string outputMode = parseResult.GetValue(jobsListOutputOption) ?? "text";
            int? limit = parseResult.GetValue(jobsListLimitOption);
            CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
            WriteVerboseDiagnostics(
                parseResult,
                "jobs list",
                snapshot,
                humanReadableOutput: !IsJsonOutputRequested(jsonRequested, outputMode));

            JobRecordListResult result = JobRecordStore.Create(snapshot).List(limit);
            if (IsJsonOutputRequested(jsonRequested, outputMode))
            {
                new JobsJsonRenderer(output).WriteList(result);
            }
            else
            {
                new JobsTextRenderer(output).WriteList(result);
            }

            return 0;
        });

        Command jobsShowCommand = new("show", "Show one local job record.");
        Argument<string> jobsShowIdArgument = new("job-id")
        {
            Description = "Job id.",
        };
        Option<bool> jobsShowJsonOption = new("--json")
        {
            Description = "Write a single JSON jobs show object.",
        };
        Option<string> jobsShowOutputOption = new("--output")
        {
            Description = "Select text or json output.",
        };
        jobsShowOutputOption.DefaultValueFactory = _ => "text";
        jobsShowOutputOption.Validators.Add(result =>
        {
            string outputMode = result.GetValueOrDefault<string>() ?? "text";
            if (!string.Equals(outputMode, "text", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(outputMode, "json", StringComparison.OrdinalIgnoreCase))
            {
                result.AddError("Invalid value for --output. Allowed values are text and json.");
            }
        });
        jobsShowCommand.Arguments.Add(jobsShowIdArgument);
        jobsShowCommand.Options.Add(jobsShowJsonOption);
        jobsShowCommand.Options.Add(jobsShowOutputOption);
        jobsShowCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            string jobId = parseResult.GetValue(jobsShowIdArgument) ?? string.Empty;
            bool jsonRequested = parseResult.GetValue(jobsShowJsonOption);
            string outputMode = parseResult.GetValue(jobsShowOutputOption) ?? "text";
            CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
            WriteVerboseDiagnostics(
                parseResult,
                "jobs show",
                snapshot,
                humanReadableOutput: !IsJsonOutputRequested(jsonRequested, outputMode));

            JobRecordReadResult result = JobRecordStore.Create(snapshot).Read(jobId);
            if (!result.Succeeded || result.Record is null)
            {
                WriteJobReadFailure(output, result.Diagnostic, IsJsonOutputRequested(jsonRequested, outputMode), "jobs.show");
                return 1;
            }

            if (IsJsonOutputRequested(jsonRequested, outputMode))
            {
                new JobsJsonRenderer(output).WriteShow(result.Record);
            }
            else
            {
                new JobsTextRenderer(output).WriteShow(result.Record);
            }

            return 0;
        });

        Command jobsExportCommand = new("export", "Export one local job record.");
        Argument<string> jobsExportIdArgument = new("job-id")
        {
            Description = "Job id.",
        };
        Option<string> jobsExportFormatOption = new("--format")
        {
            Description = "Select json or markdown export format.",
        };
        jobsExportFormatOption.DefaultValueFactory = _ => "json";
        jobsExportFormatOption.Validators.Add(result =>
        {
            string format = result.GetValueOrDefault<string>() ?? "json";
            if (!string.Equals(format, "json", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(format, "markdown", StringComparison.OrdinalIgnoreCase))
            {
                result.AddError("Invalid value for --format. Allowed values are json and markdown.");
            }
        });
        jobsExportCommand.Arguments.Add(jobsExportIdArgument);
        jobsExportCommand.Options.Add(jobsExportFormatOption);
        jobsExportCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            string jobId = parseResult.GetValue(jobsExportIdArgument) ?? string.Empty;
            string format = parseResult.GetValue(jobsExportFormatOption) ?? "json";
            bool jsonOutput = string.Equals(format, "json", StringComparison.OrdinalIgnoreCase);
            CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
            WriteVerboseDiagnostics(
                parseResult,
                "jobs export",
                snapshot,
                humanReadableOutput: !jsonOutput);

            JobRecordReadResult result = JobRecordStore.Create(snapshot).Read(jobId);
            if (!result.Succeeded || result.Record is null)
            {
                WriteJobReadFailure(output, result.Diagnostic, jsonOutput, "jobs.export");
                return 1;
            }

            if (jsonOutput)
            {
                new JobsJsonRenderer(output).WriteExport(result.Record);
            }
            else
            {
                new JobsTextRenderer(output).WriteMarkdown(result.Record);
            }

            return 0;
        });
        jobsCommand.Subcommands.Add(jobsListCommand);
        jobsCommand.Subcommands.Add(jobsShowCommand);
        jobsCommand.Subcommands.Add(jobsExportCommand);

        Command ciCommand = new("ci", "Generate provider-neutral CI artifacts from local job records.");
        Command ciSummarizeCommand = new("summarize", "Generate a CI JSON or markdown summary for one job.");
        Option<string> ciSummarizeJobOption = new("--job")
        {
            Description = "Job id to summarize.",
        };
        Option<string> ciSummarizeOutputOption = new("--output")
        {
            Description = "Select json or markdown output.",
        };
        Option<string> ciSummarizeMarkdownPathOption = new("--markdown-path")
        {
            Description = "Write the markdown summary to a new workspace-local file.",
        };
        ciSummarizeOutputOption.DefaultValueFactory = _ => "json";
        ciSummarizeJobOption.Validators.Add(result =>
        {
            if (string.IsNullOrWhiteSpace(result.GetValueOrDefault<string>()))
            {
                result.AddError("Option --job is required.");
            }
        });
        ciSummarizeOutputOption.Validators.Add(result =>
        {
            string value = result.GetValueOrDefault<string>() ?? "json";
            if (!string.Equals(value, "json", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(value, "markdown", StringComparison.OrdinalIgnoreCase))
            {
                result.AddError("Invalid value for --output. Allowed values are json and markdown.");
            }
        });
        ciSummarizeCommand.Options.Add(ciSummarizeJobOption);
        ciSummarizeCommand.Options.Add(ciSummarizeOutputOption);
        ciSummarizeCommand.Options.Add(ciSummarizeMarkdownPathOption);
        ciSummarizeCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            string jobId = parseResult.GetValue(ciSummarizeJobOption) ?? string.Empty;
            string outputMode = parseResult.GetValue(ciSummarizeOutputOption) ?? "json";
            string? markdownPath = parseResult.GetValue(ciSummarizeMarkdownPathOption);
            bool jsonOutput = string.Equals(outputMode, "json", StringComparison.OrdinalIgnoreCase);
            CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
            WriteVerboseDiagnostics(parseResult, "ci summarize", snapshot, !jsonOutput);

            JobRecordReadResult readResult = JobRecordStore.Create(snapshot).Read(jobId);
            if (!readResult.Succeeded || readResult.Record is null)
            {
                WriteCiFailure(
                    output,
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
                        output,
                        writeResult.ErrorCode ?? "ci-markdown-write-failed",
                        writeResult.Summary ?? "CI markdown summary could not be written.",
                        jsonOutput,
                        jobId);
                    return CiExitCodePolicy.ConfigError;
                }
            }

            if (jsonOutput)
            {
                new CiJsonRenderer(output).Write(artifact);
            }
            else
            {
                markdownRenderer.Write(output, artifact);
            }

            return string.Equals(artifact.Check.Outcome, CiCheckOutcome.ConfigError, StringComparison.Ordinal)
                ? CiExitCodePolicy.ConfigError
                : CiExitCodePolicy.Success;
        });

        Command ciCheckCommand = new("check", "Evaluate one job using the deterministic CI exit-code policy.");
        Option<string> ciCheckJobOption = new("--job")
        {
            Description = "Job id to check.",
        };
        Option<string> ciCheckOutputOption = new("--output")
        {
            Description = "Select json or markdown output.",
        };
        Option<string> ciCheckFailOnOption = new("--fail-on")
        {
            Description = "Promote none, warnings, or remaining risks to a failed check.",
        };
        ciCheckOutputOption.DefaultValueFactory = _ => "json";
        ciCheckFailOnOption.DefaultValueFactory = _ => "none";
        ciCheckJobOption.Validators.Add(result =>
        {
            if (string.IsNullOrWhiteSpace(result.GetValueOrDefault<string>()))
            {
                result.AddError("Option --job is required.");
            }
        });
        ciCheckOutputOption.Validators.Add(result =>
        {
            string value = result.GetValueOrDefault<string>() ?? "json";
            if (!string.Equals(value, "json", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(value, "markdown", StringComparison.OrdinalIgnoreCase))
            {
                result.AddError("Invalid value for --output. Allowed values are json and markdown.");
            }
        });
        ciCheckFailOnOption.Validators.Add(result =>
        {
            string value = result.GetValueOrDefault<string>() ?? "none";
            if (!string.Equals(value, "none", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(value, "warnings", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(value, "risks", StringComparison.OrdinalIgnoreCase))
            {
                result.AddError("Invalid value for --fail-on. Allowed values are none, warnings, and risks.");
            }
        });
        ciCheckCommand.Options.Add(ciCheckJobOption);
        ciCheckCommand.Options.Add(ciCheckOutputOption);
        ciCheckCommand.Options.Add(ciCheckFailOnOption);
        ciCheckCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            string jobId = parseResult.GetValue(ciCheckJobOption) ?? string.Empty;
            string outputMode = parseResult.GetValue(ciCheckOutputOption) ?? "json";
            string failOn = parseResult.GetValue(ciCheckFailOnOption) ?? "none";
            bool jsonOutput = string.Equals(outputMode, "json", StringComparison.OrdinalIgnoreCase);
            CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
            WriteVerboseDiagnostics(parseResult, "ci check", snapshot, !jsonOutput);

            JobRecordReadResult readResult = JobRecordStore.Create(snapshot).Read(jobId);
            if (!readResult.Succeeded || readResult.Record is null)
            {
                WriteCiFailure(
                    output,
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
                new CiJsonRenderer(output).Write(artifact);
            }
            else
            {
                new CiMarkdownRenderer().Write(output, artifact);
            }

            return artifact.Check.RecommendedExitCode;
        });
        ciCommand.Subcommands.Add(ciSummarizeCommand);
        ciCommand.Subcommands.Add(ciCheckCommand);

        Command daemonCommand = new("daemon", "Inspect or start the localhost-only API daemon Preview.");
        Command daemonDoctorCommand = new("doctor", "Inspect the API daemon Preview boundary without starting it.");
        Option<string> daemonDoctorOutputOption = new("--output")
        {
            Description = "Select text or json output.",
            DefaultValueFactory = _ => "text",
        };
        AddTextJsonOutputValidator(daemonDoctorOutputOption);
        daemonDoctorCommand.Options.Add(daemonDoctorOutputOption);
        daemonDoctorCommand.SetAction(parseResult =>
        {
            string outputMode = parseResult.GetValue(daemonDoctorOutputOption) ?? "text";
            if (string.Equals(outputMode, "json", StringComparison.OrdinalIgnoreCase))
            {
                new LocalApiPreviewJsonRenderer(output).WriteDoctor();
            }
            else
            {
                new LocalApiDaemonTextRenderer(output).WriteDoctor();
            }

            return 0;
        });

        Command daemonStartCommand = new("start", "Start the localhost-only read-only API daemon Preview.");
        Option<bool> daemonStartPreviewOption = new("--preview")
        {
            Description = "Acknowledge and explicitly enable the unauthenticated local Preview.",
        };
        Option<string> daemonStartBindOption = new("--bind")
        {
            Description = "Bind address. Only localhost or 127.0.0.1 is accepted.",
            DefaultValueFactory = _ => "localhost",
        };
        Option<int> daemonStartPortOption = new("--port")
        {
            Description = $"Loopback port ({LocalApiPreviewConstants.MinimumPort}-{LocalApiPreviewConstants.MaximumPort}).",
            DefaultValueFactory = _ => LocalApiPreviewConstants.DefaultPort,
        };
        daemonStartCommand.Options.Add(daemonStartPreviewOption);
        daemonStartCommand.Options.Add(daemonStartBindOption);
        daemonStartCommand.Options.Add(daemonStartPortOption);
        daemonStartCommand.SetAction(parseResult =>
        {
            if (!parseResult.GetValue(daemonStartPreviewOption))
            {
                output.WriteLine("daemon start refused: --preview is required because the local API is disabled by default.");
                return 2;
            }

            string requestedBind = parseResult.GetValue(daemonStartBindOption) ?? "localhost";
            if (!LocalApiDaemonBindPolicy.TryNormalize(requestedBind, out _))
            {
                output.WriteLine("daemon start refused: only localhost or 127.0.0.1 is allowed; remote and wildcard binds are disabled.");
                return 2;
            }

            int port = parseResult.GetValue(daemonStartPortOption);
            if (!LocalApiDaemonBindPolicy.IsValidPort(port))
            {
                output.WriteLine(
                    $"daemon start refused: --port must be between {LocalApiPreviewConstants.MinimumPort} and {LocalApiPreviewConstants.MaximumPort}.");
                return 2;
            }

            string? workspacePath = parseResult.GetValue(workspaceOption);
            CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
            return LocalApiDaemonHost.RunAsync(snapshot, port, output).GetAwaiter().GetResult();
        });
        daemonCommand.Subcommands.Add(daemonDoctorCommand);
        daemonCommand.Subcommands.Add(daemonStartCommand);

        Command apiCommand = new("api", "Inspect the local HTTP API Preview contract.");
        Command apiRoutesCommand = new("routes", "List the local HTTP API Preview routes without starting a listener.");
        Option<string> apiRoutesOutputOption = new("--output")
        {
            Description = "Select text or json output.",
            DefaultValueFactory = _ => "text",
        };
        AddTextJsonOutputValidator(apiRoutesOutputOption);
        apiRoutesCommand.Options.Add(apiRoutesOutputOption);
        apiRoutesCommand.SetAction(parseResult =>
        {
            string outputMode = parseResult.GetValue(apiRoutesOutputOption) ?? "text";
            if (string.Equals(outputMode, "json", StringComparison.OrdinalIgnoreCase))
            {
                new LocalApiPreviewJsonRenderer(output).WriteRoutes();
            }
            else
            {
                new LocalApiPreviewTextRenderer(output).WriteRoutes();
            }

            return 0;
        });
        apiCommand.Subcommands.Add(apiRoutesCommand);

        Command apiSmokeCommand = new("smoke", "Check an explicitly started localhost API daemon Preview.");
        Option<int> apiSmokePortOption = new("--port")
        {
            Description = $"Loopback port ({LocalApiPreviewConstants.MinimumPort}-{LocalApiPreviewConstants.MaximumPort}).",
            DefaultValueFactory = _ => LocalApiPreviewConstants.DefaultPort,
        };
        apiSmokeCommand.Options.Add(apiSmokePortOption);
        apiSmokeCommand.SetAction(parseResult =>
        {
            int port = parseResult.GetValue(apiSmokePortOption);
            if (!LocalApiDaemonBindPolicy.IsValidPort(port))
            {
                output.WriteLine(
                    $"api smoke refused: --port must be between {LocalApiPreviewConstants.MinimumPort} and {LocalApiPreviewConstants.MaximumPort}.");
                return 2;
            }

            return LocalApiDaemonClient.SmokeAsync(port, output).GetAwaiter().GetResult();
        });
        apiCommand.Subcommands.Add(apiSmokeCommand);

        Command queueCommand = new("queue", "Manage the local task queue.");
        Command queueAddCommand = new("add", "Add a pending request to the local task queue.");
        Command queueAddExecCommand = new("exec", "Add a pending exec request.");
        Argument<string[]> queueAddExecTaskArgument = new("task")
        {
            Description = "Task text. Tokens after -- are joined so quoting is optional.",
            Arity = ArgumentArity.ZeroOrMore,
        };
        Option<string> queueAddExecCwdOption = new("--cwd")
        {
            Description = "Use a working context path when the queue item runs.",
        };
        Option<string> queueAddExecExpertOption = new("--expert")
        {
            Description = "Select a local expert profile when the queue item runs.",
        };
        Option<string> queueAddExecReportOption = new("--report")
        {
            Description = "Select none or markdown report output when the queue item runs.",
            DefaultValueFactory = _ => "none",
        };
        Option<bool> queueAddExecJsonOption = new("--json")
        {
            Description = "Write a single JSON queue add object.",
        };
        Option<string> queueAddExecOutputOption = new("--output")
        {
            Description = "Select text or json output.",
            DefaultValueFactory = _ => "text",
        };
        AddTextJsonOutputValidator(queueAddExecOutputOption);
        queueAddExecExpertOption.Validators.Add(result =>
        {
            string? expert = result.GetValueOrDefault<string>();
            if (!string.IsNullOrWhiteSpace(expert) && !ExpertProfileCatalog.TryGet(expert, out _))
            {
                result.AddError("Invalid value for --expert. Allowed values are bugfix, refactor, reviewer, security, and tester.");
            }
        });
        queueAddExecReportOption.Validators.Add(result =>
        {
            string report = result.GetValueOrDefault<string>() ?? "none";
            if (!ExecReportModeParser.TryParse(report, out _))
            {
                result.AddError("Invalid value for --report. Allowed values are none and markdown.");
            }
        });
        queueAddExecCommand.Arguments.Add(queueAddExecTaskArgument);
        queueAddExecCommand.Options.Add(queueAddExecCwdOption);
        queueAddExecCommand.Options.Add(queueAddExecExpertOption);
        queueAddExecCommand.Options.Add(queueAddExecReportOption);
        queueAddExecCommand.Options.Add(queueAddExecJsonOption);
        queueAddExecCommand.Options.Add(queueAddExecOutputOption);
        queueAddExecCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            string? cwd = parseResult.GetValue(queueAddExecCwdOption);
            string task = string.Join(" ", parseResult.GetValue(queueAddExecTaskArgument) ?? []).Trim();
            string? expert = parseResult.GetValue(queueAddExecExpertOption);
            string report = parseResult.GetValue(queueAddExecReportOption) ?? "none";
            bool jsonOutput = IsJsonOutputRequested(
                parseResult.GetValue(queueAddExecJsonOption),
                parseResult.GetValue(queueAddExecOutputOption) ?? "text");
            CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath, cwd);
            WriteVerboseDiagnostics(parseResult, "queue add exec", snapshot, !jsonOutput);
            if (string.IsNullOrWhiteSpace(task))
            {
                WriteQueueFailure(output, TaskQueueErrorCode.InvalidRequest, "Queue task is empty.", jsonOutput, "queue.add");
                return 1;
            }

            try
            {
                DateTimeOffset nowUtc = utcNowProvider();
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
                if (jsonOutput)
                {
                    new TaskQueueJsonRenderer(output).WriteAdded(item);
                }
                else
                {
                    new TaskQueueTextRenderer(output).WriteAdded(item);
                }

                return 0;
            }
            catch (Exception exception) when (IsQueueStoreException(exception))
            {
                WriteQueueFailure(output, TaskQueueErrorCode.RecordWriteFailed, "Queue record could not be created.", jsonOutput, "queue.add");
                return 1;
            }
        });

        Command queueAddSkillCommand = new("skill", "Add a pending skills run request.");
        Argument<string> queueAddSkillNameArgument = new("name")
        {
            Description = "Skill pack name.",
        };
        Argument<string[]> queueAddSkillTaskArgument = new("task")
        {
            Description = "Task text. Tokens after -- are joined so quoting is optional.",
            Arity = ArgumentArity.ZeroOrMore,
        };
        Option<string> queueAddSkillCwdOption = new("--cwd")
        {
            Description = "Use a working context path when the queue item runs.",
        };
        Option<string> queueAddSkillReportOption = new("--report")
        {
            Description = "Override the skill report mode: none or markdown.",
        };
        Option<bool> queueAddSkillJsonOption = new("--json")
        {
            Description = "Write a single JSON queue add object.",
        };
        Option<string> queueAddSkillOutputOption = new("--output")
        {
            Description = "Select text or json output.",
            DefaultValueFactory = _ => "text",
        };
        AddTextJsonOutputValidator(queueAddSkillOutputOption);
        queueAddSkillReportOption.Validators.Add(result =>
        {
            string? report = result.GetValueOrDefault<string>();
            if (!string.IsNullOrWhiteSpace(report) && !ExecReportModeParser.TryParse(report, out _))
            {
                result.AddError("Invalid value for --report. Allowed values are none and markdown.");
            }
        });
        queueAddSkillCommand.Arguments.Add(queueAddSkillNameArgument);
        queueAddSkillCommand.Arguments.Add(queueAddSkillTaskArgument);
        queueAddSkillCommand.Options.Add(queueAddSkillCwdOption);
        queueAddSkillCommand.Options.Add(queueAddSkillReportOption);
        queueAddSkillCommand.Options.Add(queueAddSkillJsonOption);
        queueAddSkillCommand.Options.Add(queueAddSkillOutputOption);
        queueAddSkillCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            string? cwd = parseResult.GetValue(queueAddSkillCwdOption);
            string skill = parseResult.GetValue(queueAddSkillNameArgument) ?? string.Empty;
            string task = string.Join(" ", parseResult.GetValue(queueAddSkillTaskArgument) ?? []).Trim();
            string? report = parseResult.GetValue(queueAddSkillReportOption);
            bool jsonOutput = IsJsonOutputRequested(
                parseResult.GetValue(queueAddSkillJsonOption),
                parseResult.GetValue(queueAddSkillOutputOption) ?? "text");
            CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath, cwd);
            WriteVerboseDiagnostics(parseResult, "queue add skill", snapshot, !jsonOutput);
            if (string.IsNullOrWhiteSpace(skill) || string.IsNullOrWhiteSpace(task))
            {
                WriteQueueFailure(output, TaskQueueErrorCode.InvalidRequest, "Queue skill name and task are required.", jsonOutput, "queue.add");
                return 1;
            }

            try
            {
                DateTimeOffset nowUtc = utcNowProvider();
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
                if (jsonOutput)
                {
                    new TaskQueueJsonRenderer(output).WriteAdded(item);
                }
                else
                {
                    new TaskQueueTextRenderer(output).WriteAdded(item);
                }

                return 0;
            }
            catch (Exception exception) when (IsQueueStoreException(exception))
            {
                WriteQueueFailure(output, TaskQueueErrorCode.RecordWriteFailed, "Queue record could not be created.", jsonOutput, "queue.add");
                return 1;
            }
        });
        queueAddCommand.Subcommands.Add(queueAddExecCommand);
        queueAddCommand.Subcommands.Add(queueAddSkillCommand);

        Command queueListCommand = new("list", "List local queue items.");
        Option<bool> queueListJsonOption = new("--json")
        {
            Description = "Write a single JSON queue list object.",
        };
        Option<string> queueListOutputOption = new("--output")
        {
            Description = "Select text or json output.",
            DefaultValueFactory = _ => "text",
        };
        Option<int?> queueListLimitOption = new("--limit")
        {
            Description = "Maximum number of queue items to list.",
        };
        Option<string> queueListStatusOption = new("--status")
        {
            Description = "Filter by pending, running, succeeded, failed, or canceled status.",
        };
        AddTextJsonOutputValidator(queueListOutputOption);
        AddPositiveIntegerValidator(queueListLimitOption, "--limit");
        queueListStatusOption.Validators.Add(result =>
        {
            string? status = result.GetValueOrDefault<string>();
            if (!string.IsNullOrWhiteSpace(status) && !TaskQueueStatus.IsKnown(status))
            {
                result.AddError("Invalid value for --status. Allowed values are pending, running, succeeded, failed, and canceled.");
            }
        });
        queueListCommand.Options.Add(queueListJsonOption);
        queueListCommand.Options.Add(queueListOutputOption);
        queueListCommand.Options.Add(queueListLimitOption);
        queueListCommand.Options.Add(queueListStatusOption);
        queueListCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            bool jsonOutput = IsJsonOutputRequested(
                parseResult.GetValue(queueListJsonOption),
                parseResult.GetValue(queueListOutputOption) ?? "text");
            int? limit = parseResult.GetValue(queueListLimitOption);
            string? status = parseResult.GetValue(queueListStatusOption);
            CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
            WriteVerboseDiagnostics(parseResult, "queue list", snapshot, !jsonOutput);
            TaskQueueListResult result = TaskQueueStore.Create(snapshot).List(limit, status);
            if (jsonOutput)
            {
                new TaskQueueJsonRenderer(output).WriteList(result);
            }
            else
            {
                new TaskQueueTextRenderer(output).WriteList(result);
            }

            return 0;
        });

        Command queueShowCommand = new("show", "Show one local queue item.");
        Argument<string> queueShowIdArgument = new("queue-id")
        {
            Description = "Queue id.",
        };
        Option<bool> queueShowJsonOption = new("--json")
        {
            Description = "Write a single JSON queue show object.",
        };
        Option<string> queueShowOutputOption = new("--output")
        {
            Description = "Select text or json output.",
            DefaultValueFactory = _ => "text",
        };
        AddTextJsonOutputValidator(queueShowOutputOption);
        queueShowCommand.Arguments.Add(queueShowIdArgument);
        queueShowCommand.Options.Add(queueShowJsonOption);
        queueShowCommand.Options.Add(queueShowOutputOption);
        queueShowCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            string queueId = parseResult.GetValue(queueShowIdArgument) ?? string.Empty;
            bool jsonOutput = IsJsonOutputRequested(
                parseResult.GetValue(queueShowJsonOption),
                parseResult.GetValue(queueShowOutputOption) ?? "text");
            CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
            WriteVerboseDiagnostics(parseResult, "queue show", snapshot, !jsonOutput);
            TaskQueueReadResult result = TaskQueueStore.Create(snapshot).Read(queueId);
            if (!result.Succeeded || result.Item is null)
            {
                WriteQueueFailure(output, result.Diagnostic, jsonOutput, "queue.show");
                return 1;
            }

            if (jsonOutput)
            {
                new TaskQueueJsonRenderer(output).WriteShow(result.Item);
            }
            else
            {
                new TaskQueueTextRenderer(output).WriteShow(result.Item);
            }

            return 0;
        });

        Command queueCancelCommand = new("cancel", "Cancel one pending queue item.");
        Argument<string> queueCancelIdArgument = new("queue-id")
        {
            Description = "Queue id.",
        };
        Option<bool> queueCancelJsonOption = new("--json")
        {
            Description = "Write a single JSON queue cancel object.",
        };
        Option<string> queueCancelOutputOption = new("--output")
        {
            Description = "Select text or json output.",
            DefaultValueFactory = _ => "text",
        };
        AddTextJsonOutputValidator(queueCancelOutputOption);
        queueCancelCommand.Arguments.Add(queueCancelIdArgument);
        queueCancelCommand.Options.Add(queueCancelJsonOption);
        queueCancelCommand.Options.Add(queueCancelOutputOption);
        queueCancelCommand.SetAction(parseResult =>
        {
            string queueId = parseResult.GetValue(queueCancelIdArgument) ?? string.Empty;
            bool jsonOutput = IsJsonOutputRequested(
                parseResult.GetValue(queueCancelJsonOption),
                parseResult.GetValue(queueCancelOutputOption) ?? "text");
            string? workspacePath = parseResult.GetValue(workspaceOption);
            CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
            WriteVerboseDiagnostics(parseResult, "queue cancel", snapshot, !jsonOutput);
            TaskQueueTransitionResult result = TaskQueueStore.Create(snapshot).Cancel(queueId, utcNowProvider());
            if (!result.Succeeded || result.Item is null)
            {
                WriteQueueFailure(output, result.Diagnostic, jsonOutput, "queue.cancel");
                return 1;
            }

            if (jsonOutput)
            {
                new TaskQueueJsonRenderer(output).WriteTransition("queue.cancel", result.Item);
            }
            else
            {
                new TaskQueueTextRenderer(output).WriteTransition("C# AI CLI queue item canceled", result.Item);
            }

            return 0;
        });

        Command queueCleanupCommand = new("cleanup", "Delete old terminal queue items.");
        Option<string> queueCleanupStatusOption = new("--status")
        {
            Description = "Terminal status to delete: succeeded, failed, or canceled.",
        };
        Option<int?> queueCleanupOlderThanDaysOption = new("--older-than-days")
        {
            Description = "Delete matching items completed at least this many days ago.",
            DefaultValueFactory = _ => 30,
        };
        Option<bool> queueCleanupJsonOption = new("--json")
        {
            Description = "Write a single JSON queue cleanup object.",
        };
        Option<string> queueCleanupOutputOption = new("--output")
        {
            Description = "Select text or json output.",
            DefaultValueFactory = _ => "text",
        };
        queueCleanupStatusOption.Validators.Add(result =>
        {
            string? status = result.GetValueOrDefault<string>();
            if (!string.IsNullOrWhiteSpace(status) && !TaskQueueStatus.IsTerminal(status))
            {
                result.AddError("Invalid value for --status. Allowed values are succeeded, failed, and canceled.");
            }
        });
        AddPositiveIntegerValidator(queueCleanupOlderThanDaysOption, "--older-than-days");
        AddTextJsonOutputValidator(queueCleanupOutputOption);
        queueCleanupCommand.Options.Add(queueCleanupStatusOption);
        queueCleanupCommand.Options.Add(queueCleanupOlderThanDaysOption);
        queueCleanupCommand.Options.Add(queueCleanupJsonOption);
        queueCleanupCommand.Options.Add(queueCleanupOutputOption);
        queueCleanupCommand.SetAction(parseResult =>
        {
            string? status = parseResult.GetValue(queueCleanupStatusOption);
            int olderThanDays = parseResult.GetValue(queueCleanupOlderThanDaysOption) ?? 30;
            bool jsonOutput = IsJsonOutputRequested(
                parseResult.GetValue(queueCleanupJsonOption),
                parseResult.GetValue(queueCleanupOutputOption) ?? "text");
            string? workspacePath = parseResult.GetValue(workspaceOption);
            CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
            WriteVerboseDiagnostics(parseResult, "queue cleanup", snapshot, !jsonOutput);
            if (string.IsNullOrWhiteSpace(status))
            {
                WriteQueueFailure(
                    output,
                    TaskQueueErrorCode.UnsafeCleanupStatus,
                    "Cleanup requires --status succeeded, failed, or canceled.",
                    jsonOutput,
                    "queue.cleanup");
                return 1;
            }

            TaskQueueCleanupResult result = TaskQueueStore.Create(snapshot).Cleanup(
                status,
                utcNowProvider().AddDays(-olderThanDays));
            if (jsonOutput)
            {
                new TaskQueueJsonRenderer(output).WriteCleanup(result);
            }
            else
            {
                new TaskQueueTextRenderer(output).WriteCleanup(result);
            }

            return result.Succeeded ? 0 : 1;
        });
        queueCommand.Subcommands.Add(queueAddCommand);
        queueCommand.Subcommands.Add(queueListCommand);
        queueCommand.Subcommands.Add(queueShowCommand);
        queueCommand.Subcommands.Add(queueCancelCommand);
        queueCommand.Subcommands.Add(queueCleanupCommand);

        Command reviewCommand = new("review", "Review the current git diff with the configured model.");
        Option<bool> reviewJsonOption = new("--json")
        {
            Description = "Write a single JSON review result object.",
        };
        Option<string> reviewOutputOption = new("--output")
        {
            Description = "Select text or json output.",
        };
        reviewOutputOption.DefaultValueFactory = _ => "text";
        reviewOutputOption.Validators.Add(result =>
        {
            string outputMode = result.GetValueOrDefault<string>() ?? "text";
            if (!string.Equals(outputMode, "text", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(outputMode, "json", StringComparison.OrdinalIgnoreCase))
            {
                result.AddError("Invalid value for --output. Allowed values are text and json.");
            }
        });
        reviewCommand.Options.Add(reviewJsonOption);
        reviewCommand.Options.Add(reviewOutputOption);
        reviewCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            bool jsonRequested = parseResult.GetValue(reviewJsonOption);
            string outputMode = parseResult.GetValue(reviewOutputOption) ?? "text";
            CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
            WriteVerboseDiagnostics(
                parseResult,
                "review",
                snapshot,
                humanReadableOutput: !IsJsonOutputRequested(jsonRequested, outputMode));

            GitDiffTool gitDiffTool = new(new WorkspaceGuard());
            ToolExecutionResult gitDiff = gitDiffTool.Execute(new ToolExecutionContext(
                "cli_review",
                snapshot.Workspace,
                "{}"));
            if (!gitDiff.Succeeded)
            {
                WriteReviewReport(output, ReviewReport.ToolFailure(gitDiff), jsonRequested, outputMode);
                return 1;
            }

            string prompt = ReviewPromptBuilder.Build(gitDiff.Summary);
            ChatRequest request = new(prompt, Instructions: snapshot.Instructions.Instructions);
            ChatModelResult result = chatModelClientFactory(snapshot).Send(request);
            if (result.Response is not null)
            {
                ChatResponse response = gitDiff.Summary.Contains(GitDiffTool.TruncationWarning, StringComparison.Ordinal)
                    ? PrependReviewWarning(result.Response, GitDiffTool.TruncationWarning)
                    : result.Response;
                WriteReviewReport(output, ReviewReport.Completed(response), jsonRequested, outputMode);
                return 0;
            }

            WriteReviewReport(output, ReviewReport.ModelFailure(result), jsonRequested, outputMode);
            return 1;
        });

        Command configCommand = new("config", "Inspect CLI configuration.");
        Command configGetCommand = new("get", "Print the effective configuration summary.");
        configGetCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "config get", snapshot);
            WriteVerboseDiagnostics(parseResult, "config get", snapshot);
            output.WriteLine(ConfigReport.Create(snapshot).ToDisplayText());
            return 0;
        });
        Command configListCommand = new("list", "List non-secret configuration values and sources.");
        configListCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "config list", snapshot);
            WriteVerboseDiagnostics(parseResult, "config list", snapshot);
            output.WriteLine(ConfigReport.Create(snapshot).ToDisplayText());
            return 0;
        });
        Command configSetCommand = new("set", "Set a scalar user configuration value.");
        Argument<string> configSetKeyArgument = new("key")
        {
            Description = "The scalar config key to set.",
        };
        Argument<string> configSetValueArgument = new("value")
        {
            Description = "The scalar config value to write.",
        };
        configSetCommand.Arguments.Add(configSetKeyArgument);
        configSetCommand.Arguments.Add(configSetValueArgument);
        configSetCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            string key = parseResult.GetValue(configSetKeyArgument) ?? string.Empty;
            string value = parseResult.GetValue(configSetValueArgument) ?? string.Empty;
            CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "config set", snapshot);
            WriteVerboseDiagnostics(parseResult, "config set", snapshot);

            ConfigFileEditResult result = ConfigFileEditor.SetUserScalar(snapshot.UserConfigPath, key, value);
            WriteConfigEditResult(output, result);
            return result.Succeeded ? 0 : 1;
        });
        Command configUnsetCommand = new("unset", "Unset a scalar user configuration value.");
        Argument<string> configUnsetKeyArgument = new("key")
        {
            Description = "The scalar config key to unset.",
        };
        configUnsetCommand.Arguments.Add(configUnsetKeyArgument);
        configUnsetCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            string key = parseResult.GetValue(configUnsetKeyArgument) ?? string.Empty;
            CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "config unset", snapshot);
            WriteVerboseDiagnostics(parseResult, "config unset", snapshot);

            ConfigFileEditResult result = ConfigFileEditor.UnsetUserScalar(snapshot.UserConfigPath, key);
            WriteConfigEditResult(output, result);
            return result.Succeeded ? 0 : 1;
        });

        configCommand.Subcommands.Add(configGetCommand);
        configCommand.Subcommands.Add(configListCommand);
        configCommand.Subcommands.Add(configSetCommand);
        configCommand.Subcommands.Add(configUnsetCommand);

        Command mcpCommand = new("mcp", "Inspect MCP server configuration.");
        Command mcpListCommand = new("list", "List configured MCP servers.");
        mcpListCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "mcp list", snapshot);
            WriteVerboseDiagnostics(parseResult, "mcp list", snapshot);
            output.WriteLine(McpListReport.Create(snapshot).ToDisplayText());
            return 0;
        });
        mcpCommand.Subcommands.Add(mcpListCommand);
        Command mcpDoctorCommand = new("doctor", "Diagnose configured MCP servers.");
        mcpDoctorCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "mcp doctor", snapshot);
            WriteVerboseDiagnostics(parseResult, "mcp doctor", snapshot);
            output.WriteLine(McpDoctorReport.Create(snapshot).ToDisplayText());
            return 0;
        });
        mcpCommand.Subcommands.Add(mcpDoctorCommand);

        Command workflowCommand = new("workflow", "Inspect project workflow profiles.");
        Command workflowListCommand = new("list", "List configured workflow profiles.");
        workflowListCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "workflow list", snapshot);
            WriteVerboseDiagnostics(parseResult, "workflow list", snapshot);
            output.WriteLine(WorkflowListReport.Create(snapshot).ToDisplayText());
            return 0;
        });
        Command workflowValidateCommand = new("validate", "Suggest the validation command for a workflow profile.");
        Argument<string> workflowProfileArgument = new("profile")
        {
            Description = "The configured workflow profile name.",
        };
        workflowValidateCommand.Arguments.Add(workflowProfileArgument);
        workflowValidateCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            string profile = parseResult.GetValue(workflowProfileArgument) ?? string.Empty;
            CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "workflow validate", snapshot);
            WriteVerboseDiagnostics(parseResult, "workflow validate", snapshot);
            output.WriteLine(WorkflowValidateReport.Create(snapshot, profile).ToDisplayText());
            return 0;
        });
        workflowCommand.Subcommands.Add(workflowListCommand);
        workflowCommand.Subcommands.Add(workflowValidateCommand);

        Command packsCommand = new("packs", "Inspect deterministic project pack contracts and tool dependencies.");
        Command packsListCommand = new("list", "List built-in project pack contracts without running tools.");
        Option<bool> packsListJsonOption = new("--json")
        {
            Description = "Write a single JSON project pack list object.",
        };
        Option<string> packsListOutputOption = new("--output")
        {
            Description = "Select text or json output.",
        };
        packsListOutputOption.DefaultValueFactory = _ => "text";
        AddTextJsonOutputValidator(packsListOutputOption);
        packsListCommand.Options.Add(packsListJsonOption);
        packsListCommand.Options.Add(packsListOutputOption);
        packsListCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            bool jsonRequested = parseResult.GetValue(packsListJsonOption);
            string outputMode = parseResult.GetValue(packsListOutputOption) ?? "text";
            bool jsonOutput = IsJsonOutputRequested(jsonRequested, outputMode);
            CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
            WriteVerboseDiagnostics(parseResult, "packs list", snapshot, humanReadableOutput: !jsonOutput);
            output.WriteLine(jsonOutput
                ? ProjectPackReportRenderer.RenderListJson(projectPackRegistry)
                : ProjectPackReportRenderer.RenderListText(projectPackRegistry));
            return 0;
        });

        Command packsDoctorCommand = new("doctor", "Statically inspect project pack tools; --probe requires approval before execution.");
        Argument<string> packsDoctorPackArgument = new("pack")
        {
            Description = "Registered project pack id.",
        };
        packsDoctorPackArgument.Validators.Add(result =>
        {
            string packId = result.GetValueOrDefault<string>() ?? string.Empty;
            if (!projectPackRegistry.TryGet(packId, out _))
            {
                result.AddError($"Unknown project pack '{packId}'.");
            }
        });
        Option<string[]> packsDoctorToolPathOption = new("--tool-path")
        {
            Description = "Configure [dependency=]absolute-path. A single bare path selects the primary dependency.",
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = true,
        };
        Option<bool> packsDoctorProbeOption = new("--probe")
        {
            Description = "Run fixed version probes after static identity checks and explicit approval.",
        };
        Option<bool> packsDoctorApproveOption = new("--approve")
        {
            Description = "Approve this invocation's fixed external tool probes.",
        };
        Option<string> packsDoctorApprovalOption = new("--approval")
        {
            Description = "Override approval mode for this invocation only.",
        };
        Option<bool> packsDoctorJsonOption = new("--json")
        {
            Description = "Write a single JSON project pack doctor object.",
        };
        Option<string> packsDoctorOutputOption = new("--output")
        {
            Description = "Select text or json output.",
        };
        packsDoctorOutputOption.DefaultValueFactory = _ => "text";
        AddApprovalModeValidator(packsDoctorApprovalOption);
        AddTextJsonOutputValidator(packsDoctorOutputOption);
        packsDoctorCommand.Arguments.Add(packsDoctorPackArgument);
        packsDoctorCommand.Options.Add(packsDoctorToolPathOption);
        packsDoctorCommand.Options.Add(packsDoctorProbeOption);
        packsDoctorCommand.Options.Add(packsDoctorApproveOption);
        packsDoctorCommand.Options.Add(packsDoctorApprovalOption);
        packsDoctorCommand.Options.Add(packsDoctorJsonOption);
        packsDoctorCommand.Options.Add(packsDoctorOutputOption);
        packsDoctorCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            string packId = parseResult.GetValue(packsDoctorPackArgument) ?? string.Empty;
            string[] toolPathValues = parseResult.GetValue(packsDoctorToolPathOption) ?? [];
            bool probe = parseResult.GetValue(packsDoctorProbeOption);
            bool approve = parseResult.GetValue(packsDoctorApproveOption);
            string? approvalModeValue = parseResult.GetValue(packsDoctorApprovalOption);
            bool jsonRequested = parseResult.GetValue(packsDoctorJsonOption);
            string outputMode = parseResult.GetValue(packsDoctorOutputOption) ?? "text";
            bool jsonOutput = IsJsonOutputRequested(jsonRequested, outputMode);
            CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
            WriteVerboseDiagnostics(parseResult, "packs doctor", snapshot, humanReadableOutput: !jsonOutput);

            if (!projectPackRegistry.TryGet(packId, out IProjectPack? pack) || pack is null)
            {
                output.WriteLine(jsonOutput
                    ? JsonSerializer.Serialize(new
                    {
                        type = "packs.doctor",
                        schemaVersion = ProjectPackSchema.CurrentVersion,
                        pack = packId,
                        status = "failed",
                        errorCode = "pack-not-found",
                        summary = "Project pack is not registered."
                    }, JsonOptions)
                    : "Project pack is not registered.");
                return 1;
            }

            if (!TryParseProjectPackToolPaths(pack.Manifest, toolPathValues, out Dictionary<string, string> toolPaths, out string? bindingError))
            {
                output.WriteLine(jsonOutput
                    ? JsonSerializer.Serialize(new
                    {
                        type = "packs.doctor",
                        schemaVersion = ProjectPackSchema.CurrentVersion,
                        pack = packId,
                        status = "failed",
                        errorCode = "pack-tool-binding-invalid",
                        summary = bindingError
                    }, JsonOptions)
                    : bindingError);
                return 2;
            }

            ApprovalMode? cliApprovalMode = GetApprovalOverride(
                approvalModeValue,
                parseResult.GetResult(packsDoctorApprovalOption),
                approve);
            IApprovalPolicy approvalPolicy = ApprovalPolicyResolver.Resolve(
                snapshot.Configuration.ApprovalMode,
                cliApprovalMode);
            DiagnosticContext? traceContext = CreateTraceContext(parseResult, snapshot);
            TryWriteTraceCommandEvent(
                "packs doctor",
                snapshot,
                traceContext,
                "command.start",
                sequence: 1,
                "started",
                summary: probe ? "Project pack doctor probe requested." : "Project pack static doctor requested.");

            ProjectPackDoctorReport report = new ProjectPackDoctorService().Diagnose(
                pack,
                toolPaths,
                trustedHashes: null,
                probe,
                approvalPolicy,
                CancellationToken.None);
            output.WriteLine(jsonOutput
                ? ProjectPackReportRenderer.RenderDoctorJson(report)
                : ProjectPackReportRenderer.RenderDoctorText(report));
            TryWriteTraceCommandEvent(
                "packs doctor",
                snapshot,
                traceContext,
                "command.complete",
                sequence: 2,
                report.Succeeded ? "success" : "failure",
                summary: $"Project pack doctor completed with status {report.Status}.",
                errorCode: report.Succeeded ? null : "pack-doctor-failed");
            return report.Succeeded ? 0 : 1;
        });
        packsCommand.Subcommands.Add(packsListCommand);
        packsCommand.Subcommands.Add(packsDoctorCommand);

        Command packsPlanCommand = new("plan", "Build a bounded deterministic project pack plan without running conversion.");
        Argument<string> packsPlanPackArgument = new("pack")
        {
            Description = "Registered project pack id.",
        };
        packsPlanPackArgument.Validators.Add(result =>
        {
            string packId = result.GetValueOrDefault<string>() ?? string.Empty;
            if (!projectPackRegistry.TryGet(packId, out _))
            {
                result.AddError($"Unknown project pack '{packId}'.");
            }
        });
        Option<string> packsPlanInputOption = new("--input")
        {
            Description = "Use one explicit input directory inside the workspace.",
        };
        packsPlanInputOption.Validators.Add(result =>
        {
            if (result.Implicit || string.IsNullOrWhiteSpace(result.GetValueOrDefault<string>()))
            {
                result.AddError("--input is required.");
            }
        });
        Option<string> packsPlanOutputDirectoryOption = new("--output-dir")
        {
            Description = "Validate one new output directory inside the workspace without creating it.",
        };
        packsPlanOutputDirectoryOption.Validators.Add(result =>
        {
            if (result.Implicit || string.IsNullOrWhiteSpace(result.GetValueOrDefault<string>()))
            {
                result.AddError("--output-dir is required.");
            }
        });
        Option<string[]> packsPlanToolPathOption = new("--tool-path")
        {
            Description = "Statically inspect [dependency=]absolute-path without starting the tool.",
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = true,
        };
        Option<bool> packsPlanJsonOption = new("--json")
        {
            Description = "Write a single JSON project pack plan object.",
        };
        Option<string> packsPlanOutputOption = new("--output")
        {
            Description = "Select text or json output.",
        };
        packsPlanOutputOption.DefaultValueFactory = _ => "text";
        AddTextJsonOutputValidator(packsPlanOutputOption);
        packsPlanCommand.Arguments.Add(packsPlanPackArgument);
        packsPlanCommand.Options.Add(packsPlanInputOption);
        packsPlanCommand.Options.Add(packsPlanOutputDirectoryOption);
        packsPlanCommand.Options.Add(packsPlanToolPathOption);
        packsPlanCommand.Options.Add(packsPlanJsonOption);
        packsPlanCommand.Options.Add(packsPlanOutputOption);
        packsPlanCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            string packId = parseResult.GetValue(packsPlanPackArgument) ?? string.Empty;
            string? inputDirectory = parseResult.GetValue(packsPlanInputOption);
            string? outputDirectory = parseResult.GetValue(packsPlanOutputDirectoryOption);
            string[] toolPathValues = parseResult.GetValue(packsPlanToolPathOption) ?? [];
            bool jsonRequested = parseResult.GetValue(packsPlanJsonOption);
            string outputMode = parseResult.GetValue(packsPlanOutputOption) ?? "text";
            bool jsonOutput = IsJsonOutputRequested(jsonRequested, outputMode);
            CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
            WriteVerboseDiagnostics(parseResult, "packs plan", snapshot, humanReadableOutput: !jsonOutput);

            if (!projectPackRegistry.TryGet(packId, out IProjectPack? registeredPack) ||
                registeredPack is not GerberTiffWorkflowPack gerberTiffPack)
            {
                output.WriteLine(jsonOutput
                    ? JsonSerializer.Serialize(new
                    {
                        type = "packs.plan",
                        schemaVersion = ProjectPackSchema.CurrentVersion,
                        pack = packId,
                        status = "blocked",
                        errorCode = "pack-plan-not-supported",
                        summary = "Project pack does not provide a v1 deterministic plan builder."
                    }, JsonOptions)
                    : "Project pack does not provide a v1 deterministic plan builder.");
                return 1;
            }

            if (!TryParseProjectPackToolPaths(
                gerberTiffPack.Manifest,
                toolPathValues,
                out Dictionary<string, string> toolPaths,
                out string? bindingError))
            {
                output.WriteLine(jsonOutput
                    ? JsonSerializer.Serialize(new
                    {
                        type = "packs.plan",
                        schemaVersion = ProjectPackSchema.CurrentVersion,
                        pack = packId,
                        status = "blocked",
                        errorCode = "pack-tool-binding-invalid",
                        summary = bindingError
                    }, JsonOptions)
                    : bindingError);
                return 2;
            }

            DiagnosticContext? traceContext = CreateTraceContext(parseResult, snapshot);
            TryWriteTraceCommandEvent(
                "packs plan",
                snapshot,
                traceContext,
                "command.start",
                sequence: 1,
                "started",
                summary: "Project pack static plan requested.");
            GerberTiffConversionPlan plan = new GerberTiffConversionPlanBuilder(gerberTiffPack).Build(
                snapshot.Workspace,
                inputDirectory,
                outputDirectory,
                toolPaths,
                trustedHashes: null,
                CancellationToken.None);
            string workspaceSource = string.IsNullOrWhiteSpace(workspacePath) ? "current-directory" : "--workspace";
            output.WriteLine(jsonOutput
                ? GerberTiffPlanRenderer.RenderJson(plan, workspaceSource)
                : GerberTiffPlanRenderer.RenderText(plan, workspaceSource));
            TryWriteTraceCommandEvent(
                "packs plan",
                snapshot,
                traceContext,
                "command.complete",
                sequence: 2,
                plan.Runnable ? "success" : "failure",
                summary: plan.Runnable
                    ? "Project pack runnable plan generated."
                    : "Project pack plan completed without runnable conversion authorization.",
                errorCode: plan.Runnable ? null : "pack-plan-blocked");
            return plan.Runnable ? 0 : 1;
        });
        packsCommand.Subcommands.Add(packsPlanCommand);

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

        Command toolsCommand = new("tools", "Inspect and invoke local workspace tools.");
        Command toolsListCommand = new("list", "List enabled local tools.");
        Option<bool> toolsListJsonOption = new("--json")
        {
            Description = "Write the tool list as JSON.",
        };
        toolsListCommand.Options.Add(toolsListJsonOption);
        toolsListCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "tools list", snapshot);
            ToolRegistry registry = CliToolFactory.CreateRegistry(snapshot, new DefaultDenyApprovalPolicy());
            bool toolsListJsonRequested = parseResult.GetValue(toolsListJsonOption);
            WriteVerboseDiagnostics(
                parseResult,
                "tools list",
                snapshot,
                humanReadableOutput: !toolsListJsonRequested);
            if (toolsListJsonRequested)
            {
                output.WriteLine(JsonSerializer.Serialize(CreateToolsListJson(registry, snapshot.Configuration.DisabledTools), JsonOptions));
                return 0;
            }

            foreach (ToolDefinition definition in registry.List().OrderBy(definition => definition.Name, StringComparer.Ordinal))
            {
                output.WriteLine($"{definition.Name}: {definition.Description}");
            }

            if (snapshot.Configuration.DisabledTools.Count > 0)
            {
                output.WriteLine("disabledTools: " + string.Join(", ", snapshot.Configuration.DisabledTools.Order(StringComparer.Ordinal)));
            }

            return 0;
        });

        Command toolsCallCommand = new("call", "Invoke one enabled local tool with a JSON argument object.");
        Argument<string> toolNameArgument = new("name")
        {
            Description = "The tool name to invoke.",
        };
        Argument<string> toolArgumentsArgument = new("arguments")
        {
            Description = "JSON object arguments for the tool.",
            DefaultValueFactory = _ => "{}",
        };
        Option<bool> toolsApproveOption = new("--approve")
        {
            Description = "Approve file edit or shell actions for this call.",
        };
        Option<string> toolsApprovalOption = new("--approval")
        {
            Description = "Set approval mode for this call: never, on-request, on-failure, or always.",
        };
        AddApprovalModeValidator(toolsApprovalOption);
        Option<string> toolArgumentsFileOption = new("--arguments-file")
        {
            Description = "Read JSON object arguments from a file.",
        };
        Option<bool> toolStdinOption = new("--stdin")
        {
            Description = "Read JSON object arguments from stdin.",
        };
        toolsCallCommand.Arguments.Add(toolNameArgument);
        toolsCallCommand.Arguments.Add(toolArgumentsArgument);
        toolsCallCommand.Options.Add(toolsApproveOption);
        toolsCallCommand.Options.Add(toolsApprovalOption);
        toolsCallCommand.Options.Add(toolArgumentsFileOption);
        toolsCallCommand.Options.Add(toolStdinOption);
        toolsCallCommand.Validators.Add(result =>
        {
            if (!result.GetValue(toolStdinOption))
            {
                return;
            }

            if (result.GetResult(toolArgumentsFileOption) is OptionResult { Implicit: false })
            {
                result.AddError("--stdin cannot be used with --arguments-file.");
            }

            if ((result.GetResult(toolArgumentsArgument)?.Tokens.Count ?? 0) > 0)
            {
                result.AddError("--stdin cannot be used with positional arguments.");
            }
        });
        toolsCallCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            string toolName = parseResult.GetValue(toolNameArgument) ?? string.Empty;
            string argumentsJson = parseResult.GetValue(toolArgumentsArgument) ?? "{}";
            string? argumentsFile = parseResult.GetValue(toolArgumentsFileOption);
            if (parseResult.GetValue(toolStdinOption))
            {
                argumentsJson = input.ReadToEnd();
            }
            else if (!string.IsNullOrWhiteSpace(argumentsFile))
            {
                argumentsJson = File.ReadAllText(argumentsFile);
            }

            bool approve = parseResult.GetValue(toolsApproveOption);
            string? approvalModeValue = parseResult.GetValue(toolsApprovalOption);
            CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
            ApprovalMode? cliApprovalMode = GetApprovalOverride(approvalModeValue, parseResult.GetResult(toolsApprovalOption), approve);
            TryWriteCommandLog(commandLogger, "tools call", snapshot);
            WriteVerboseDiagnostics(parseResult, "tools call", snapshot);
            ToolRegistry registry = CliToolFactory.CreateRegistry(
                snapshot,
                ApprovalPolicyResolver.Resolve(snapshot.Configuration.ApprovalMode, cliApprovalMode));
            ToolExecutor executor = new(registry, snapshot.Configuration.DisabledTools);
            ToolExecutionResult result = executor.Execute(
                toolName,
                new ToolExecutionContext("cli_tool_call", snapshot.Workspace, argumentsJson));
            result = ReplaceUnknownMcpToolWithDiscoveryFailure(result, toolName, snapshot);

            WriteToolResult(output, result);
            return result.Succeeded ? 0 : 1;
        });
        toolsCommand.Subcommands.Add(toolsListCommand);
        toolsCommand.Subcommands.Add(toolsCallCommand);

        Command logsCommand = new("logs", "Inspect CLI log files.");
        Command logsPathCommand = new("path", "Print the resolved CLI log directory.");
        logsPathCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
            string logDirectory = LogPathResolver.ResolveLogDirectory(snapshot);
            if (Directory.Exists(logDirectory))
            {
                TryWriteCommandLog(commandLogger, "logs path", snapshot);
            }

            WriteVerboseDiagnostics(parseResult, "logs path", snapshot);
            output.WriteLine(logDirectory);
            return 0;
        });
        logsCommand.Subcommands.Add(logsPathCommand);

        Command logsShowCommand = new("show", "Print CLI log lines.");
        Option<int?> logsTailOption = new("--tail")
        {
            Description = "Print the last number of log lines.",
        };
        logsTailOption.DefaultValueFactory = _ => 20;
        AddPositiveIntegerValidator(logsTailOption, "--tail");
        logsShowCommand.Options.Add(logsTailOption);
        logsShowCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            int tailCount = parseResult.GetValue(logsTailOption) ?? 20;
            CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
            WriteVerboseDiagnostics(parseResult, "logs show", snapshot);

            string logDirectory = LogPathResolver.ResolveLogDirectory(snapshot);
            if (!Directory.Exists(logDirectory))
            {
                return 0;
            }

            Queue<string> tailLines = new();
            foreach (string logPath in EnumerateLogFilesBestEffort(logDirectory))
            {
                AddLogFileTailLinesBestEffort(tailLines, logPath, tailCount);
            }

            foreach (string line in tailLines)
            {
                output.WriteLine(line);
            }

            return 0;
        });
        logsCommand.Subcommands.Add(logsShowCommand);

        Command logsClearCommand = new("clear", "Delete CLI log files.");
        logsClearCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
            WriteVerboseDiagnostics(parseResult, "logs clear", snapshot);

            string logDirectory = LogPathResolver.ResolveLogDirectory(snapshot);
            if (!IsClearableLogDirectory(logDirectory))
            {
                return 0;
            }

            int clearedCount = 0;
            foreach (string logPath in EnumerateLogFilesBestEffort(logDirectory))
            {
                if (DeleteLogFileBestEffort(logPath))
                {
                    clearedCount++;
                }
            }

            output.WriteLine($"Cleared {clearedCount} log file(s).");
            return 0;
        });
        logsCommand.Subcommands.Add(logsClearCommand);

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

        Command queueRunCommand = new("run", "Run one queued request through the controlled exec or skills path.");
        Argument<string> queueRunIdArgument = new("queue-id")
        {
            Description = "Queue id.",
        };
        Option<bool> queueRunJsonOption = new("--json")
        {
            Description = "Write newline-delimited JSON execution events and queue state.",
        };
        Option<string> queueRunOutputOption = new("--output")
        {
            Description = "Select text or json output.",
            DefaultValueFactory = _ => "text",
        };
        AddTextJsonOutputValidator(queueRunOutputOption);
        queueRunCommand.Arguments.Add(queueRunIdArgument);
        queueRunCommand.Options.Add(queueRunJsonOption);
        queueRunCommand.Options.Add(queueRunOutputOption);
        queueRunCommand.SetAction(parseResult =>
        {
            string queueId = parseResult.GetValue(queueRunIdArgument) ?? string.Empty;
            bool jsonOutput = IsJsonOutputRequested(
                parseResult.GetValue(queueRunJsonOption),
                parseResult.GetValue(queueRunOutputOption) ?? "text");
            string? workspacePath = parseResult.GetValue(workspaceOption);
            CliEnvironmentSnapshot queueSnapshot = workspaceSnapshotProvider(workspacePath);
            WriteVerboseDiagnostics(parseResult, "queue run", queueSnapshot, !jsonOutput);
            TaskQueueStore queueStore = TaskQueueStore.Create(queueSnapshot);
            TaskQueueReadResult read = queueStore.Read(queueId);
            if (!read.Succeeded || read.Item is null)
            {
                WriteQueueFailure(output, read.Diagnostic, jsonOutput, "queue.run");
                return 1;
            }

            TaskQueueTransitionResult started = queueStore.Start(queueId, utcNowProvider());
            if (!started.Succeeded || started.Item is null)
            {
                WriteQueueFailure(output, started.Diagnostic, jsonOutput, "queue.run");
                return 1;
            }

            TaskQueueItem runningItem = started.Item;
            int attempt = runningItem.Attempts[^1].Attempt;
            if (jsonOutput)
            {
                new TaskQueueJsonRenderer(output).WriteTransition("queue.run.started", runningItem);
            }
            else
            {
                new TaskQueueTextRenderer(output).WriteTransition("C# AI CLI queue run started", runningItem);
            }

            JobRecordStore? jobStore = null;
            HashSet<string> existingJobIds = new(StringComparer.Ordinal);
            int innerExitCode = 1;
            string? executionErrorCode = null;
            string? executionSummary = null;
            try
            {
                CliEnvironmentSnapshot executionSnapshot = snapshotProvider(
                    runningItem.Request.WorkspaceRoot,
                    runningItem.Request.Cwd);
                jobStore = JobRecordStore.Create(executionSnapshot);
                existingJobIds = jobStore.List().Records
                    .Select(record => record.JobId)
                    .ToHashSet(StringComparer.Ordinal);
                IReadOnlyList<string> innerArguments = BuildQueuedCommandArguments(
                    runningItem,
                    jsonOutput,
                    parseResult.GetValue(verboseOption),
                    IsTraceEnabled(parseResult));
                innerExitCode = rootCommand.Parse(innerArguments).Invoke();
            }
            catch (Exception)
            {
                executionErrorCode = TaskQueueErrorCode.ExecutionFailed;
                executionSummary = "Queued execution failed before the delegated command reached a terminal result.";
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
                        executionSummary ??= "Queued execution stopped before the delegated command finalized its job record.";
                        JobRecord failedJob = jobRecord.WithStatus(
                            JobStatus.Failed,
                            utcNowProvider(),
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
                catch (Exception exception) when (IsJobStoreException(exception))
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
                utcNowProvider(),
                completionExitCode,
                jobRecord?.JobId,
                completionErrorCode,
                completionSummary);
            if (!completed.Succeeded || completed.Item is null)
            {
                WriteQueueFailure(output, completed.Diagnostic, jsonOutput, "queue.run");
                return 1;
            }

            if (jsonOutput)
            {
                new TaskQueueJsonRenderer(output).WriteTransition("queue.run.completed", completed.Item);
            }
            else
            {
                output.WriteLine();
                new TaskQueueTextRenderer(output).WriteTransition("C# AI CLI queue run completed", completed.Item);
            }

            return completionExitCode;
        });
        queueCommand.Subcommands.Add(queueRunCommand);

        Command automationCommand = new("automation", "Inspect and manually run workspace-local automations.");
        Command automationListCommand = new("list", "List valid workspace-local automation manifests.");
        Option<bool> automationListJsonOption = new("--json")
        {
            Description = "Write a single JSON automation catalog object.",
        };
        Option<string> automationListOutputOption = new("--output")
        {
            Description = "Select text or json output.",
            DefaultValueFactory = _ => "text",
        };
        AddTextJsonOutputValidator(automationListOutputOption);
        automationListCommand.Options.Add(automationListJsonOption);
        automationListCommand.Options.Add(automationListOutputOption);
        automationListCommand.SetAction(parseResult =>
        {
            bool jsonOutput = IsJsonOutputRequested(
                parseResult.GetValue(automationListJsonOption),
                parseResult.GetValue(automationListOutputOption) ?? "text");
            CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(parseResult.GetValue(workspaceOption));
            WriteVerboseDiagnostics(parseResult, "automation list", snapshot, !jsonOutput);
            AutomationCatalog catalog = AutomationCatalog.Load(snapshot.Workspace);
            if (jsonOutput)
            {
                new AutomationJsonRenderer(output).WriteList(catalog);
            }
            else
            {
                new AutomationTextRenderer(output).WriteList(catalog);
            }

            return 0;
        });

        Command automationValidateCommand = new("validate", "Validate all workspace-local automation manifests.");
        Option<bool> automationValidateJsonOption = new("--json")
        {
            Description = "Write a single JSON automation validation object.",
        };
        Option<string> automationValidateOutputOption = new("--output")
        {
            Description = "Select text or json output.",
            DefaultValueFactory = _ => "text",
        };
        AddTextJsonOutputValidator(automationValidateOutputOption);
        automationValidateCommand.Options.Add(automationValidateJsonOption);
        automationValidateCommand.Options.Add(automationValidateOutputOption);
        automationValidateCommand.SetAction(parseResult =>
        {
            bool jsonOutput = IsJsonOutputRequested(
                parseResult.GetValue(automationValidateJsonOption),
                parseResult.GetValue(automationValidateOutputOption) ?? "text");
            CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(parseResult.GetValue(workspaceOption));
            WriteVerboseDiagnostics(parseResult, "automation validate", snapshot, !jsonOutput);
            AutomationCatalog catalog = AutomationCatalog.Load(snapshot.Workspace);
            if (jsonOutput)
            {
                new AutomationJsonRenderer(output).WriteValidation(catalog);
            }
            else
            {
                new AutomationTextRenderer(output).WriteValidation(catalog);
            }

            return catalog.Diagnostics.Count == 0 ? 0 : 1;
        });

        Command automationPlanCommand = new("plan", "Render a local automation and schedule preview without execution.");
        Argument<string> automationPlanNameArgument = new("automation")
        {
            Description = "Workspace-local automation name.",
        };
        Option<bool> automationPlanJsonOption = new("--json")
        {
            Description = "Write a single JSON automation plan object.",
        };
        Option<string> automationPlanOutputOption = new("--output")
        {
            Description = "Select text or json output.",
            DefaultValueFactory = _ => "text",
        };
        AddTextJsonOutputValidator(automationPlanOutputOption);
        automationPlanCommand.Arguments.Add(automationPlanNameArgument);
        automationPlanCommand.Options.Add(automationPlanJsonOption);
        automationPlanCommand.Options.Add(automationPlanOutputOption);
        automationPlanCommand.SetAction(parseResult =>
        {
            string automationName = parseResult.GetValue(automationPlanNameArgument) ?? string.Empty;
            bool jsonOutput = IsJsonOutputRequested(
                parseResult.GetValue(automationPlanJsonOption),
                parseResult.GetValue(automationPlanOutputOption) ?? "text");
            CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(parseResult.GetValue(workspaceOption));
            WriteVerboseDiagnostics(parseResult, "automation plan", snapshot, !jsonOutput);
            AutomationCatalog catalog = AutomationCatalog.Load(snapshot.Workspace);
            if (!catalog.TryGet(automationName, out AutomationCatalogItem? item) || item is null)
            {
                WriteAutomationFailure(
                    output,
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
                new AutomationJsonRenderer(output).WritePlan(plan);
            }
            else
            {
                new AutomationTextRenderer(output).WritePlan(plan);
            }

            return 0;
        });

        Command automationRunCommand = new("run", "Dry-run or manually trigger a workspace-local automation.");
        Argument<string> automationRunNameArgument = new("automation")
        {
            Description = "Workspace-local automation name.",
        };
        Option<bool> automationRunDryRunOption = new("--dry-run")
        {
            Description = "Render the validated automation plan without model, tools, or persistence.",
        };
        Option<bool> automationRunManualOption = new("--manual")
        {
            Description = "Manually execute the target through existing queue or pipeline paths.",
        };
        Option<bool> automationRunJsonOption = new("--json")
        {
            Description = "Write a single JSON automation result object.",
        };
        Option<string> automationRunOutputOption = new("--output")
        {
            Description = "Select text or json output.",
            DefaultValueFactory = _ => "text",
        };
        AddTextJsonOutputValidator(automationRunOutputOption);
        automationRunCommand.Arguments.Add(automationRunNameArgument);
        automationRunCommand.Options.Add(automationRunDryRunOption);
        automationRunCommand.Options.Add(automationRunManualOption);
        automationRunCommand.Options.Add(automationRunJsonOption);
        automationRunCommand.Options.Add(automationRunOutputOption);
        automationRunCommand.SetAction(parseResult =>
        {
            string automationName = parseResult.GetValue(automationRunNameArgument) ?? string.Empty;
            bool dryRun = parseResult.GetValue(automationRunDryRunOption);
            bool manual = parseResult.GetValue(automationRunManualOption);
            bool jsonOutput = IsJsonOutputRequested(
                parseResult.GetValue(automationRunJsonOption),
                parseResult.GetValue(automationRunOutputOption) ?? "text");
            if (dryRun == manual)
            {
                WriteAutomationFailure(
                    output,
                    AutomationErrorCode.InvalidRunMode,
                    "Specify exactly one of --dry-run or --manual.",
                    jsonOutput,
                    "automation.run",
                    automationName);
                return 1;
            }

            CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(parseResult.GetValue(workspaceOption));
            WriteVerboseDiagnostics(parseResult, "automation run", snapshot, !jsonOutput);
            AutomationCatalog catalog = AutomationCatalog.Load(snapshot.Workspace);
            if (!catalog.TryGet(automationName, out AutomationCatalogItem? item) || item is null)
            {
                WriteAutomationFailure(
                    output,
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
                    new AutomationJsonRenderer(output).WriteDryRun(plan);
                }
                else
                {
                    new AutomationTextRenderer(output).WriteDryRun(plan);
                }

                return 0;
            }

            TryWriteCommandLog(commandLogger, "automation run", snapshot);
            string automationRunId = AutomationRunIdGenerator.Create(utcNowProvider());
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
                    ? ExecutePipelineTarget(item.Manifest.Target, metadata)
                    : ExecuteQueueTarget(item.Manifest.Target, metadata);
            }
            catch (Exception exception) when (IsQueueStoreException(exception) || exception is JsonException)
            {
                WriteAutomationFailure(
                    output,
                    AutomationErrorCode.ExecutionFailed,
                    "Manual automation could not create or read its queue/job artifacts.",
                    jsonOutput,
                    "automation.run",
                    automationName);
                return 1;
            }

            if (jsonOutput)
            {
                new AutomationJsonRenderer(output).WriteRunResult(result);
            }
            else
            {
                new AutomationTextRenderer(output).WriteRunResult(result);
            }

            return result.ExitCode;

            AutomationRunResult ExecuteQueueTarget(AutomationTarget target, AutomationRunMetadata runMetadata)
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
                DateTimeOffset nowUtc = utcNowProvider();
                TaskQueueItem pending = TaskQueueItem.CreatePending(
                    TaskQueueIdGenerator.Create(nowUtc),
                    nowUtc,
                    request,
                    warnings: [$"automation={runMetadata.Automation};run={runMetadata.RunId};target={runMetadata.TargetType}"]);
                TaskQueueStore queueStore = TaskQueueStore.Create(snapshot);
                queueStore.Create(pending);

                using StringWriter delegatedOutput = new(CultureInfo.InvariantCulture);
                RootCommand delegatedRoot = Create(
                    delegatedOutput,
                    snapshotProvider,
                    commandLogger,
                    chatModelClientFactory,
                    streamingRendererFactory,
                    conversationStoreFactory,
                    utcNowProvider,
                    execAgentRunnerFactory,
                    input,
                    environmentVariableProvider);
                List<string> arguments = [
                    "queue", "run", pending.QueueId,
                    "--workspace", snapshot.Workspace.RootPath,
                    "--output", "json"
                ];
                if (parseResult.GetValue(verboseOption))
                {
                    arguments.Add("--verbose");
                }

                if (IsTraceEnabled(parseResult))
                {
                    arguments.Add("--trace");
                }

                int exitCode = delegatedRoot.Parse(arguments).Invoke();
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

            AutomationRunResult ExecutePipelineTarget(AutomationTarget target, AutomationRunMetadata runMetadata)
            {
                using StringWriter delegatedOutput = new(CultureInfo.InvariantCulture);
                RootCommand delegatedRoot = Create(
                    delegatedOutput,
                    snapshotProvider,
                    commandLogger,
                    chatModelClientFactory,
                    streamingRendererFactory,
                    conversationStoreFactory,
                    utcNowProvider,
                    execAgentRunnerFactory,
                    input,
                    environmentVariableProvider);
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

                if (parseResult.GetValue(verboseOption))
                {
                    arguments.Add("--verbose");
                }

                if (IsTraceEnabled(parseResult))
                {
                    arguments.Add("--trace");
                }

                arguments.Add("--");
                arguments.Add(target.Task!);
                int exitCode = delegatedRoot.Parse(arguments).Invoke();
                using JsonDocument document = JsonDocument.Parse(delegatedOutput.ToString());
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
                            if (role.TryGetProperty("queueId", out JsonElement queueId) && !string.IsNullOrWhiteSpace(queueId.GetString()))
                            {
                                queueIds.Add(queueId.GetString()!);
                            }

                            if (role.TryGetProperty("jobId", out JsonElement jobId) && !string.IsNullOrWhiteSpace(jobId.GetString()))
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
                    exitCode == 0 ? "succeeded" : "failed",
                    exitCode,
                    queueIds,
                    jobIds,
                    pipelineRunId,
                    exitCode == 0 ? "Pipeline automation completed." : "Pipeline automation failed.",
                    warnings);
            }
        });
        automationCommand.Subcommands.Add(automationListCommand);
        automationCommand.Subcommands.Add(automationValidateCommand);
        automationCommand.Subcommands.Add(automationPlanCommand);
        automationCommand.Subcommands.Add(automationRunCommand);

        Command pipelineCommand = new("pipeline", "Plan and run built-in local multi-role pipelines.");
        Command pipelineListCommand = new("list", "List built-in pipelines without invoking a model or tools.");
        Option<bool> pipelineListJsonOption = new("--json")
        {
            Description = "Write a single JSON pipeline catalog object.",
        };
        Option<string> pipelineListOutputOption = new("--output")
        {
            Description = "Select text or json output.",
            DefaultValueFactory = _ => "text",
        };
        AddTextJsonOutputValidator(pipelineListOutputOption);
        pipelineListCommand.Options.Add(pipelineListJsonOption);
        pipelineListCommand.Options.Add(pipelineListOutputOption);
        pipelineListCommand.SetAction(parseResult =>
        {
            bool jsonOutput = IsJsonOutputRequested(
                parseResult.GetValue(pipelineListJsonOption),
                parseResult.GetValue(pipelineListOutputOption) ?? "text");
            IReadOnlyList<PipelineManifest> pipelines = BuiltInPipelineCatalog.List();
            if (jsonOutput)
            {
                new PipelineJsonRenderer(output).WriteList(pipelines);
            }
            else
            {
                new PipelineTextRenderer(output).WriteList(pipelines);
            }

            return 0;
        });

        Command pipelinePlanCommand = new("plan", "Render an auditable pipeline plan without invoking a model or tools.");
        Argument<string> pipelinePlanNameArgument = new("pipeline")
        {
            Description = "Built-in pipeline name.",
        };
        Argument<string[]> pipelinePlanTaskArgument = new("task")
        {
            Description = "Task text. Tokens after -- are joined so quoting is optional.",
            Arity = ArgumentArity.ZeroOrMore,
        };
        Option<string> pipelinePlanCwdOption = new("--cwd")
        {
            Description = "Use a working context path when the pipeline runs.",
        };
        Option<bool> pipelinePlanJsonOption = new("--json")
        {
            Description = "Write a single JSON pipeline plan object.",
        };
        Option<string> pipelinePlanOutputOption = new("--output")
        {
            Description = "Select text or json output.",
            DefaultValueFactory = _ => "text",
        };
        AddTextJsonOutputValidator(pipelinePlanOutputOption);
        pipelinePlanCommand.Arguments.Add(pipelinePlanNameArgument);
        pipelinePlanCommand.Arguments.Add(pipelinePlanTaskArgument);
        pipelinePlanCommand.Options.Add(pipelinePlanCwdOption);
        pipelinePlanCommand.Options.Add(pipelinePlanJsonOption);
        pipelinePlanCommand.Options.Add(pipelinePlanOutputOption);
        pipelinePlanCommand.SetAction(parseResult =>
        {
            string pipelineName = parseResult.GetValue(pipelinePlanNameArgument) ?? string.Empty;
            string task = string.Join(" ", parseResult.GetValue(pipelinePlanTaskArgument) ?? []).Trim();
            bool jsonOutput = IsJsonOutputRequested(
                parseResult.GetValue(pipelinePlanJsonOption),
                parseResult.GetValue(pipelinePlanOutputOption) ?? "text");
            if (!BuiltInPipelineCatalog.TryGet(pipelineName, out PipelineManifest? pipeline) || pipeline is null)
            {
                WritePipelineFailure(output, "pipeline-not-found", "Built-in pipeline was not found.", jsonOutput, "pipeline.plan", pipelineName);
                return 1;
            }

            if (string.IsNullOrWhiteSpace(task))
            {
                WritePipelineFailure(output, "pipeline-invalid-request", "Pipeline task is empty.", jsonOutput, "pipeline.plan", pipelineName);
                return 1;
            }

            string? workspacePath = parseResult.GetValue(workspaceOption);
            string? cwd = parseResult.GetValue(pipelinePlanCwdOption);
            CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath, cwd);
            WriteVerboseDiagnostics(parseResult, "pipeline plan", snapshot, !jsonOutput);
            PipelinePlan plan = new(pipeline, task, snapshot.Workspace.RootPath, cwd);
            if (jsonOutput)
            {
                new PipelineJsonRenderer(output).WritePlan(plan);
            }
            else
            {
                new PipelineTextRenderer(output).WritePlan(plan);
            }

            return 0;
        });
        Command pipelineRunCommand = new("run", "Run a built-in pipeline sequentially through queue, job, exec, and skills boundaries.");
        Argument<string> pipelineRunNameArgument = new("pipeline")
        {
            Description = "Built-in pipeline name.",
        };
        Argument<string[]> pipelineRunTaskArgument = new("task")
        {
            Description = "Task text. Tokens after -- are joined so quoting is optional.",
            Arity = ArgumentArity.ZeroOrMore,
        };
        Option<string> pipelineRunCwdOption = new("--cwd")
        {
            Description = "Use a working context path for every role.",
        };
        Option<string> pipelineRunReportOption = new("--report")
        {
            Description = "Select none or markdown aggregate report output.",
            DefaultValueFactory = _ => "none",
        };
        pipelineRunReportOption.Validators.Add(result =>
        {
            string report = result.GetValueOrDefault<string>() ?? "none";
            if (!ExecReportModeParser.TryParse(report, out _))
            {
                result.AddError("Invalid value for --report. Allowed values are none and markdown.");
            }
        });
        Option<bool> pipelineRunJsonOption = new("--json")
        {
            Description = "Write a single JSON pipeline result object.",
        };
        Option<string> pipelineRunOutputOption = new("--output")
        {
            Description = "Select text or json output.",
            DefaultValueFactory = _ => "text",
        };
        Option<string> pipelineRunAutomationNameOption = new("--automation-name")
        {
            Description = "Carry local automation correlation metadata into pipeline queue/job artifacts.",
        };
        Option<string> pipelineRunAutomationRunIdOption = new("--automation-run-id")
        {
            Description = "Carry a validated local automation run id.",
        };
        Option<string> pipelineRunAutomationSourceOption = new("--automation-source")
        {
            Description = "Carry the redacted workspace-local automation source path.",
        };
        Option<string> pipelineRunAutomationTargetOption = new("--automation-target")
        {
            Description = "Carry the local automation target type.",
        };
        AddTextJsonOutputValidator(pipelineRunOutputOption);
        pipelineRunCommand.Arguments.Add(pipelineRunNameArgument);
        pipelineRunCommand.Arguments.Add(pipelineRunTaskArgument);
        pipelineRunCommand.Options.Add(pipelineRunCwdOption);
        pipelineRunCommand.Options.Add(pipelineRunReportOption);
        pipelineRunCommand.Options.Add(pipelineRunJsonOption);
        pipelineRunCommand.Options.Add(pipelineRunOutputOption);
        pipelineRunCommand.Options.Add(pipelineRunAutomationNameOption);
        pipelineRunCommand.Options.Add(pipelineRunAutomationRunIdOption);
        pipelineRunCommand.Options.Add(pipelineRunAutomationSourceOption);
        pipelineRunCommand.Options.Add(pipelineRunAutomationTargetOption);
        pipelineRunCommand.SetAction(parseResult =>
        {
            string pipelineName = parseResult.GetValue(pipelineRunNameArgument) ?? string.Empty;
            string task = string.Join(" ", parseResult.GetValue(pipelineRunTaskArgument) ?? []).Trim();
            bool jsonOutput = IsJsonOutputRequested(
                parseResult.GetValue(pipelineRunJsonOption),
                parseResult.GetValue(pipelineRunOutputOption) ?? "text");
            if (!BuiltInPipelineCatalog.TryGet(pipelineName, out PipelineManifest? pipeline) || pipeline is null)
            {
                WritePipelineFailure(output, "pipeline-not-found", "Built-in pipeline was not found.", jsonOutput, "pipeline.run", pipelineName);
                return 1;
            }

            if (string.IsNullOrWhiteSpace(task))
            {
                WritePipelineFailure(output, "pipeline-invalid-request", "Pipeline task is empty.", jsonOutput, "pipeline.run", pipelineName);
                return 1;
            }

            string? workspacePath = parseResult.GetValue(workspaceOption);
            string? cwd = parseResult.GetValue(pipelineRunCwdOption);
            string reportMode = parseResult.GetValue(pipelineRunReportOption) ?? "none";
            string? automationName = parseResult.GetValue(pipelineRunAutomationNameOption);
            string? automationRunId = parseResult.GetValue(pipelineRunAutomationRunIdOption);
            string? automationSource = parseResult.GetValue(pipelineRunAutomationSourceOption);
            string? automationTarget = parseResult.GetValue(pipelineRunAutomationTargetOption);
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
                WritePipelineFailure(
                    output,
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
            CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath, cwd);
            TryWriteCommandLog(commandLogger, "pipeline run", snapshot);
            WriteVerboseDiagnostics(parseResult, "pipeline run", snapshot, !jsonOutput);
            PipelinePlan plan = new(pipeline, task, snapshot.Workspace.RootPath, cwd);
            DelegatePipelineRoleExecutor roleExecutor = new(
                request => ExecutePipelineRole(
                    request,
                    parseResult.GetValue(verboseOption),
                    IsTraceEnabled(parseResult)));
            PipelineFinalReport report;
            try
            {
                report = new PipelineRunner(roleExecutor, utcNowProvider).Run(plan);
            }
            catch (Exception exception) when (IsQueueStoreException(exception))
            {
                WritePipelineFailure(
                    output,
                    "pipeline-execution-failed",
                    "Pipeline execution could not create or read its queue/job records.",
                    jsonOutput,
                    "pipeline.run",
                    pipelineName);
                return 1;
            }

            if (jsonOutput)
            {
                new PipelineJsonRenderer(output).WriteFinalReport(report);
            }
            else
            {
                PipelineTextRenderer renderer = new(output);
                renderer.WriteFinalReport(report);
                if (string.Equals(reportMode, "markdown", StringComparison.OrdinalIgnoreCase))
                {
                    output.WriteLine();
                    renderer.WriteMarkdown(report);
                }
            }

            return string.Equals(report.Status, PipelineStatus.Succeeded, StringComparison.Ordinal) ? 0 : 1;

            PipelineRoleExecutionResult ExecutePipelineRole(
                PipelineRoleExecutionRequest request,
                bool verbose,
                bool trace)
            {
                CliEnvironmentSnapshot roleSnapshot = snapshotProvider(
                    request.Plan.WorkspaceRoot,
                    request.Plan.Cwd);
                TaskQueueStore queueStore = TaskQueueStore.Create(roleSnapshot);
                DateTimeOffset nowUtc = utcNowProvider();
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

                using StringWriter delegatedOutput = new(CultureInfo.InvariantCulture);
                RootCommand delegatedRoot = Create(
                    delegatedOutput,
                    snapshotProvider,
                    commandLogger,
                    chatModelClientFactory,
                    streamingRendererFactory,
                    conversationStoreFactory,
                    utcNowProvider,
                    execAgentRunnerFactory,
                    input,
                    environmentVariableProvider);
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

                delegatedRoot.Parse(arguments).Invoke();
                TaskQueueItem completed = queueStore.Read(pending.QueueId).Item ??
                    throw new InvalidOperationException("Pipeline queue item could not be read after execution.");
                JobRecord? job = null;
                if (!string.IsNullOrWhiteSpace(completed.LatestJobId))
                {
                    job = JobRecordStore.Create(roleSnapshot).Read(completed.LatestJobId).Record;
                }

                return new PipelineRoleExecutionResult(completed, job);
            }
        });
        pipelineCommand.Subcommands.Add(pipelineListCommand);
        pipelineCommand.Subcommands.Add(pipelinePlanCommand);
        pipelineCommand.Subcommands.Add(pipelineRunCommand);

        Command runCommand = new("run", "Run a deterministic local workspace task through the direct tool layer.");
        Argument<string> taskArgument = new("task")
        {
            Description = "Task text. Supported smoke tasks: create smoke note, read <path>, shell <command>.",
        };
        Option<bool> runApproveOption = new("--approve")
        {
            Description = "Approve patch or shell tools used by this run.",
        };
        runCommand.Arguments.Add(taskArgument);
        runCommand.Options.Add(runApproveOption);
        runCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            string task = parseResult.GetValue(taskArgument) ?? string.Empty;
            bool approve = parseResult.GetValue(runApproveOption);
            CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "run", snapshot);
            WriteVerboseDiagnostics(parseResult, "run", snapshot);
            ApprovalMode? cliApprovalMode = approve ? ApprovalMode.Always : null;
            IApprovalPolicy approvalPolicy = ApprovalPolicyResolver.Resolve(snapshot.Configuration.ApprovalMode, cliApprovalMode);
            ToolRegistry registry = CliToolFactory.CreateRegistry(snapshot, approvalPolicy);
            ToolExecutor executor = new(registry, snapshot.Configuration.DisabledTools);
            ExecRunner runner = new(approvalPolicy);
            ExecRequest request = new(task, WorkspaceRoot: snapshot.Workspace.RootPath);
            ExecResult result = runner.Run(request, snapshot.Workspace, executor);

            WriteRunResult(output, result);
            return result.ExitCode;
        });

        Command sessionCommand = new("session", "Manage local conversation transcripts.");
        Command sessionListCommand = new("list", "List local session summaries.");
        Command sessionShowCommand = new("show", "Show one local session summary.");
        Command sessionExportCommand = new("export", "Print one session transcript.");
        Command sessionClearCommand = new("clear", "Delete one session transcript.");
        Command sessionDeleteCommand = new("delete", "Delete one local session transcript.");
        Command sessionRenameCommand = new("rename", "Rename one local session transcript.");
        Argument<string> sessionNameArgument = new("name")
        {
            Description = "The session name.",
        };
        Option<string> sessionExportFormatOption = new("--format")
        {
            Description = "Select export format: json or markdown.",
            DefaultValueFactory = _ => "json",
        };
        sessionExportFormatOption.Validators.Add(result =>
        {
            string format = result.GetValueOrDefault<string>() ?? "json";
            if (!string.Equals(format, "json", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(format, "markdown", StringComparison.OrdinalIgnoreCase))
            {
                result.AddError("Invalid value for --format. Allowed values are json and markdown.");
            }
        });
        Argument<string> sessionRenameSourceArgument = new("old")
        {
            Description = "The current session name.",
        };
        Argument<string> sessionRenameDestinationArgument = new("new")
        {
            Description = "The new session name.",
        };
        sessionShowCommand.Arguments.Add(sessionNameArgument);
        sessionExportCommand.Arguments.Add(sessionNameArgument);
        sessionExportCommand.Options.Add(sessionExportFormatOption);
        sessionClearCommand.Arguments.Add(sessionNameArgument);
        sessionDeleteCommand.Arguments.Add(sessionNameArgument);
        sessionRenameCommand.Arguments.Add(sessionRenameSourceArgument);
        sessionRenameCommand.Arguments.Add(sessionRenameDestinationArgument);
        sessionListCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "session list", snapshot);
            WriteVerboseDiagnostics(parseResult, "session list", snapshot);
            try
            {
                WriteSessionList(output, conversationStoreFactory(snapshot).ListSummaries());
                return 0;
            }
            catch (Exception exception) when (IsConversationStoreException(exception))
            {
                return WriteSessionConversationStoreFailure(output, exception);
            }
        });
        sessionShowCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            string name = parseResult.GetValue(sessionNameArgument) ?? string.Empty;
            CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "session show", snapshot);
            WriteVerboseDiagnostics(parseResult, "session show", snapshot);
            if (!TryParseSessionName(output, name, out ConversationSessionName sessionName))
            {
                return 1;
            }

            try
            {
                return ShowSession(output, conversationStoreFactory(snapshot), sessionName);
            }
            catch (Exception exception) when (IsConversationStoreException(exception))
            {
                return WriteSessionConversationStoreFailure(output, exception);
            }
        });
        sessionExportCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            string name = parseResult.GetValue(sessionNameArgument) ?? string.Empty;
            string format = parseResult.GetValue(sessionExportFormatOption) ?? "json";
            CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "session export", snapshot);
            WriteVerboseDiagnostics(
                parseResult,
                "session export",
                snapshot,
                humanReadableOutput: string.Equals(format, "markdown", StringComparison.OrdinalIgnoreCase));
            if (!TryParseSessionName(output, name, out ConversationSessionName sessionName))
            {
                return 1;
            }

            try
            {
                if (string.Equals(format, "markdown", StringComparison.OrdinalIgnoreCase))
                {
                    return ExportSessionMarkdown(output, conversationStoreFactory(snapshot), sessionName);
                }

                return ExportSessionJson(output, snapshot, sessionName);
            }
            catch (Exception exception) when (IsConversationStoreException(exception))
            {
                return WriteSessionConversationStoreFailure(output, exception);
            }
        });
        sessionClearCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            string name = parseResult.GetValue(sessionNameArgument) ?? string.Empty;
            CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "session clear", snapshot);
            WriteVerboseDiagnostics(parseResult, "session clear", snapshot);
            if (!TryParseSessionName(output, name, out ConversationSessionName sessionName))
            {
                return 1;
            }

            try
            {
                return DeleteSession(output, conversationStoreFactory(snapshot), sessionName, "cleared", missingExitCode: 0, writeMissingErrorCode: false);
            }
            catch (Exception exception) when (IsConversationStoreException(exception))
            {
                return WriteSessionConversationStoreFailure(output, exception);
            }
        });
        sessionDeleteCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            string name = parseResult.GetValue(sessionNameArgument) ?? string.Empty;
            CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "session delete", snapshot);
            WriteVerboseDiagnostics(parseResult, "session delete", snapshot);
            if (!TryParseSessionName(output, name, out ConversationSessionName sessionName))
            {
                return 1;
            }

            try
            {
                return DeleteSession(output, conversationStoreFactory(snapshot), sessionName, "deleted", missingExitCode: 1, writeMissingErrorCode: true);
            }
            catch (Exception exception) when (IsConversationStoreException(exception))
            {
                return WriteSessionConversationStoreFailure(output, exception);
            }
        });
        sessionRenameCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            string source = parseResult.GetValue(sessionRenameSourceArgument) ?? string.Empty;
            string destination = parseResult.GetValue(sessionRenameDestinationArgument) ?? string.Empty;
            CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "session rename", snapshot);
            WriteVerboseDiagnostics(parseResult, "session rename", snapshot);
            if (!TryParseSessionName(output, source, out ConversationSessionName sourceSessionName) ||
                !TryParseSessionName(output, destination, out ConversationSessionName destinationSessionName))
            {
                return 1;
            }

            try
            {
                return RenameSession(output, conversationStoreFactory(snapshot), sourceSessionName, destinationSessionName);
            }
            catch (Exception exception) when (IsConversationStoreException(exception))
            {
                return WriteSessionConversationStoreFailure(output, exception);
            }
        });
        sessionCommand.Subcommands.Add(sessionListCommand);
        sessionCommand.Subcommands.Add(sessionShowCommand);
        sessionCommand.Subcommands.Add(sessionExportCommand);
        sessionCommand.Subcommands.Add(sessionClearCommand);
        sessionCommand.Subcommands.Add(sessionDeleteCommand);
        sessionCommand.Subcommands.Add(sessionRenameCommand);

        Command chatCommand = new("chat", "Send one prompt to the configured model.");
        Argument<string> promptArgument = new("prompt")
        {
            Description = "The user message to send to the model.",
        };
        Option<string> sessionOption = new("--session")
        {
            Description = "Create or append to a named chat transcript.",
        };
        Option<string> resumeOption = new("--resume")
        {
            Description = "Resume an existing named chat session.",
        };
        Option<string> chatCwdOption = new("--cwd")
        {
            Description = "Use a working context path for hierarchical instruction discovery.",
        };
        chatCommand.Arguments.Add(promptArgument);
        chatCommand.Options.Add(sessionOption);
        chatCommand.Options.Add(resumeOption);
        chatCommand.Options.Add(chatCwdOption);
        chatCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            string? cwdPath = parseResult.GetValue(chatCwdOption);
            string prompt = parseResult.GetValue(promptArgument) ?? string.Empty;
            string? session = parseResult.GetValue(sessionOption);
            string? resume = parseResult.GetValue(resumeOption);
            bool sessionSupplied = IsOptionExplicit(parseResult, sessionOption);
            bool resumeSupplied = IsOptionExplicit(parseResult, resumeOption);
            CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath, cwdPath);
            TryWriteCommandLog(commandLogger, "chat", snapshot);
            WriteVerboseDiagnostics(parseResult, "chat", snapshot);

            if (sessionSupplied && resumeSupplied)
            {
                WriteSessionOptionConflict(output);
                return 1;
            }

            string? effectiveSession = resumeSupplied ? resume : session;
            ConversationSessionName? sessionName = null;
            ConversationTranscript? transcript = null;
            ConversationTranscript? transcriptContext = null;
            IConversationStore? conversationStore = null;
            DateTimeOffset nowUtc = default;
            if (sessionSupplied || resumeSupplied)
            {
                try
                {
                    sessionName = ConversationSessionName.Parse(effectiveSession);
                }
                catch (ArgumentException exception)
                {
                    WriteSafeFailure(output, "invalid-session-name", GetSafeSessionNameParseMessage(exception));
                    return 1;
                }

                try
                {
                    conversationStore = conversationStoreFactory(snapshot);
                    nowUtc = utcNowProvider();
                    if (resumeSupplied)
                    {
                        if (!conversationStore.TryLoad(sessionName, out transcript) || transcript is null)
                        {
                            WriteSessionNotFound(output);
                            return 1;
                        }

                        transcriptContext = transcript;
                    }
                    else
                    {
                        transcript = conversationStore.LoadOrCreate(sessionName, nowUtc);
                    }
                }
                catch (Exception exception) when (IsConversationStoreException(exception))
                {
                    return WriteChatConversationStoreFailure(output, exception);
                }
            }

            IChatModelClient chatModelClient = chatModelClientFactory(snapshot);
            IChatStreamingRenderer renderer = streamingRendererFactory(output);
            ChatRequest request = new(prompt, effectiveSession, snapshot.Instructions.Instructions, transcriptContext);

            ChatModelResult result = chatModelClient.SendStreaming(request, renderer);
            if (sessionName is not null && transcript is not null && conversationStore is not null)
            {
                ConversationTranscriptRecorder.RecordTurn(transcript, prompt, result, nowUtc);
                try
                {
                    conversationStore.Save(sessionName, transcript);
                }
                catch (Exception exception) when (IsConversationStoreException(exception))
                {
                    return WriteChatConversationStoreFailure(output, exception);
                }
            }

            return result.IsSuccess ? 0 : 1;
        });

        rootCommand.Subcommands.Add(versionCommand);
        rootCommand.Subcommands.Add(doctorCommand);
        rootCommand.Subcommands.Add(statusCommand);
        rootCommand.Subcommands.Add(modelsCommand);
        rootCommand.Subcommands.Add(diffCommand);
        rootCommand.Subcommands.Add(changesCommand);
        rootCommand.Subcommands.Add(jobsCommand);
        rootCommand.Subcommands.Add(ciCommand);
        rootCommand.Subcommands.Add(daemonCommand);
        rootCommand.Subcommands.Add(apiCommand);
        rootCommand.Subcommands.Add(queueCommand);
        rootCommand.Subcommands.Add(automationCommand);
        rootCommand.Subcommands.Add(pipelineCommand);
        rootCommand.Subcommands.Add(reviewCommand);
        rootCommand.Subcommands.Add(configCommand);
        rootCommand.Subcommands.Add(mcpCommand);
        rootCommand.Subcommands.Add(workflowCommand);
        rootCommand.Subcommands.Add(packsCommand);
        rootCommand.Subcommands.Add(skillsCommand);
        rootCommand.Subcommands.Add(toolsCommand);
        rootCommand.Subcommands.Add(logsCommand);
        rootCommand.Subcommands.Add(execCommand);
        rootCommand.Subcommands.Add(runCommand);
        rootCommand.Subcommands.Add(sessionCommand);
        rootCommand.Subcommands.Add(chatCommand);

        return rootCommand;
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

    private static IReadOnlyList<string> EnumerateLogFilesBestEffort(string logDirectory)
    {
        List<string> logPaths = [];
        IEnumerator<string>? enumerator = null;
        try
        {
            enumerator = Directory.EnumerateFiles(logDirectory, "*.log").GetEnumerator();
            while (true)
            {
                try
                {
                    if (!enumerator.MoveNext())
                    {
                        break;
                    }
                }
                catch (Exception exception) when (IsBestEffortLogFileException(exception))
                {
                    break;
                }

                logPaths.Add(enumerator.Current);
            }
        }
        catch (Exception exception) when (IsBestEffortLogFileException(exception))
        {
        }
        finally
        {
            enumerator?.Dispose();
        }

        logPaths.Sort(StringComparer.Ordinal);
        return logPaths;
    }

    private static void AddLogFileTailLinesBestEffort(Queue<string> tailLines, string logPath, int tailCount)
    {
        try
        {
            foreach (string line in File.ReadLines(logPath))
            {
                tailLines.Enqueue(line);
                while (tailLines.Count > tailCount)
                {
                    tailLines.Dequeue();
                }
            }
        }
        catch (Exception exception) when (IsBestEffortLogFileException(exception))
        {
        }
    }

    private static bool DeleteLogFileBestEffort(string logPath)
    {
        try
        {
            if (!File.Exists(logPath))
            {
                return false;
            }

            File.Delete(logPath);
            return !File.Exists(logPath);
        }
        catch (Exception exception) when (IsBestEffortLogFileException(exception))
        {
            return false;
        }
    }

    private static bool IsClearableLogDirectory(string logDirectory)
    {
        try
        {
            string fullPath = Path.GetFullPath(logDirectory);
            if (!Directory.Exists(fullPath))
            {
                return false;
            }

            string? root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrEmpty(root) || IsSymlinkOrReparsePoint(root))
            {
                return false;
            }

            string relativePath = Path.GetRelativePath(root, fullPath);
            if (string.Equals(relativePath, ".", StringComparison.Ordinal))
            {
                return true;
            }

            string currentPath = root;
            foreach (string segment in relativePath.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries))
            {
                currentPath = Path.Combine(currentPath, segment);
                if (IsSymlinkOrReparsePoint(currentPath))
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception exception) when (IsBestEffortLogFileException(exception)
            || exception is ArgumentException
            || exception is NotSupportedException)
        {
            return false;
        }
    }

    private static bool IsSymlinkOrReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception exception) when (IsBestEffortLogFileException(exception))
        {
            return true;
        }
    }

    private static bool IsBestEffortLogFileException(Exception exception)
    {
        if (exception is FileNotFoundException)
        {
            return true;
        }

        if (exception is DirectoryNotFoundException)
        {
            return true;
        }

        return exception is IOException or UnauthorizedAccessException;
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

    private static IReadOnlyList<string> BuildQueuedCommandArguments(
        TaskQueueItem item,
        bool jsonOutput,
        bool verbose,
        bool trace)
    {
        ArgumentNullException.ThrowIfNull(item);
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

        if (item.Request.Family == TaskQueueCommandFamily.Exec && !string.IsNullOrWhiteSpace(item.Request.Expert))
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

    private static ToolExecutionResult ReplaceUnknownMcpToolWithDiscoveryFailure(
        ToolExecutionResult result,
        string toolName,
        CliEnvironmentSnapshot snapshot)
    {
        if (!string.Equals(result.ErrorCode, ToolErrorCode.UnknownTool, StringComparison.Ordinal) ||
            snapshot.Configuration.DisabledTools.Contains(toolName) ||
            !TryGetMcpToolServerSegment(toolName, out string serverSegment))
        {
            return result;
        }

        McpServerDefinition? server = McpConfigurationLoader
            .Load(snapshot.Configuration)
            .Servers
            .FirstOrDefault(candidate =>
                ShouldDiscoverForMcpToolCallDiagnostic(candidate) &&
                string.Equals(
                    NormalizeMcpNameSegment(candidate.Name, "server"),
                    serverSegment,
                    StringComparison.Ordinal));
        if (server is null)
        {
            return result;
        }

        WorkspaceGuard workspaceGuard = new();
        McpStdioClientSessionFactory sessionFactory = new(new McpStdioTransport(
            workspaceGuard,
            snapshot.Configuration.ShellPolicy));
        McpStdioToolDiscoverer discoverer = new(sessionFactory);
        McpToolsListResult discovery = discoverer.DiscoverTools(server, snapshot.Workspace);
        return discovery.Succeeded
            ? result
            : ToolExecutionResult.Failure(
                discovery.ErrorCode ?? McpErrorCode.ClientFailed,
                discovery.SafeMessage);
    }

    private static bool TryGetMcpToolServerSegment(string toolName, out string serverSegment)
    {
        string[] parts = toolName.Split('.', StringSplitOptions.None);
        if (parts.Length != 3 ||
            !string.Equals(parts[0], "mcp", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(parts[1]) ||
            string.IsNullOrWhiteSpace(parts[2]))
        {
            serverSegment = string.Empty;
            return false;
        }

        serverSegment = parts[1];
        return true;
    }

    private static bool ShouldDiscoverForMcpToolCallDiagnostic(McpServerDefinition server)
    {
        return server.Enabled &&
            string.Equals(server.Status, "configured", StringComparison.Ordinal) &&
            string.Equals(server.Transport, "stdio", StringComparison.Ordinal) &&
            string.Equals(server.Source, TrustedMcpRegistrySource, StringComparison.Ordinal);
    }

    private static string NormalizeMcpNameSegment(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        StringBuilder builder = new(value.Length);
        bool lastWasSeparator = false;
        foreach (char character in value.Trim().ToLowerInvariant())
        {
            if (character is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(character);
                lastWasSeparator = false;
                continue;
            }

            if (character is '_' or '-')
            {
                builder.Append(character);
                lastWasSeparator = false;
                continue;
            }

            if (!lastWasSeparator)
            {
                builder.Append('_');
                lastWasSeparator = true;
            }
        }

        string normalized = builder.ToString().Trim('_', '-');
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    private static IAgentRunner CreateDefaultExecAgentRunner(
        CliEnvironmentSnapshot snapshot,
        ToolRegistry registry,
        IToolExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(executor);

        string model = snapshot.Configuration.Model;
        if (string.IsNullOrWhiteSpace(model)
            || string.Equals(model, "not configured", StringComparison.OrdinalIgnoreCase))
        {
            return new StaticAgentRunner(new AgentError(
                "missing-model",
                "Model is not configured. Set model in .caicli/config.json before running exec.",
                Retryable: false));
        }

        SecretValue? apiKey = snapshot.Configuration.ApiKey;
        if (apiKey is null)
        {
            return new StaticAgentRunner(new AgentError(
                "missing-openai-api-key",
                "OpenAI API key is missing. Set OPENAI_API_KEY or user config apiKey.",
                Retryable: false));
        }

        if (!IsSupportedApiKeySource(snapshot.Configuration.ApiKeySource))
        {
            return new StaticAgentRunner(new AgentError(
                "unsupported-api-key-source",
                "Workspace config apiKey is not used for model calls. Set OPENAI_API_KEY or user config apiKey.",
                Retryable: false));
        }

        SdkOpenAiResponsesGateway gateway = new(apiKey.Value, snapshot.Configuration.BaseUrl);
        return new OpenAiAgentRunner(
            model,
            snapshot.Instructions.Instructions,
            registry,
            gateway,
            executor);
    }

    private static bool IsExecCommand(ParseResult parseResult)
    {
        return string.Equals(parseResult.CommandResult.Command.Name, "exec", StringComparison.Ordinal);
    }

    private static bool IsSupportedApiKeySource(string apiKeySource)
    {
        return apiKeySource is "OPENAI_API_KEY" or "user config";
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

    private static JsonObject CreateToolsListJson(ToolRegistry registry, IReadOnlySet<string> disabledTools)
    {
        JsonArray tools = [];
        foreach (ToolDefinition definition in registry.List().OrderBy(definition => definition.Name, StringComparer.Ordinal))
        {
            RenderedToolDefinition rendered = ToolSchemaRenderer.Render(definition);
            tools.Add(new JsonObject
            {
                ["name"] = rendered.Name,
                ["description"] = rendered.Description,
                ["riskLevel"] = rendered.RiskLevel,
                ["parameters"] = ToolSchemaRenderer.RenderParametersSchemaObject(definition)
            });
        }

        JsonArray disabledToolNames = [];
        foreach (string toolName in disabledTools.OrderBy(toolName => toolName, StringComparer.Ordinal))
        {
            disabledToolNames.Add(toolName);
        }

        return new JsonObject
        {
            ["type"] = "tools.list",
            ["tools"] = tools,
            ["disabledTools"] = disabledToolNames
        };
    }

    private static void WriteToolResult(TextWriter output, ToolExecutionResult result)
    {
        output.WriteLine(result.Succeeded ? "status: succeeded" : "status: failed");
        output.WriteLine($"approvalStatus: {result.ApprovalStatus}");
        if (!string.IsNullOrWhiteSpace(result.ErrorCode))
        {
            output.WriteLine($"errorCode: {result.ErrorCode}");
        }

        output.WriteLine("summary:");
        output.WriteLine(result.Summary);
    }

    private static void WriteReviewReport(
        TextWriter output,
        ReviewReport report,
        bool jsonRequested,
        string outputMode)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(report);

        output.WriteLine(IsJsonOutputRequested(jsonRequested, outputMode)
            ? report.ToJson()
            : report.ToDisplayText());
    }

    private static ChatResponse PrependReviewWarning(ChatResponse response, string warning)
    {
        string text = string.IsNullOrWhiteSpace(response.Text)
            ? warning
            : warning + Environment.NewLine + Environment.NewLine + response.Text.TrimStart('\r', '\n');
        return response with { Text = text };
    }

    private static void WriteConfigEditResult(TextWriter output, ConfigFileEditResult result)
    {
        output.WriteLine($"status: {result.Status}");
        if (result.Succeeded)
        {
            output.WriteLine($"key: {result.Key}");
            output.WriteLine("scope: user");
            output.WriteLine($"path: {result.Path}");
            return;
        }

        output.WriteLine($"errorCode: {result.ErrorCode}");
        output.WriteLine("summary:");
        output.WriteLine(result.Summary);
    }

    private static void WriteRunResult(TextWriter output, ExecResult result)
    {
        output.WriteLine(result.IsSuccess ? "status: succeeded" : "status: failed");
        output.WriteLine($"approvalStatus: {result.ApprovalStatus ?? "not-required"}");
        if (!string.IsNullOrWhiteSpace(result.ErrorCode))
        {
            output.WriteLine($"errorCode: {result.ErrorCode}");
        }

        output.WriteLine("summary:");
        output.WriteLine(result.Summary);
    }

    private static int WriteChatConversationStoreFailure(TextWriter output, Exception exception)
    {
        (string errorCode, string summary) = GetConversationStoreFailure(exception);
        WriteSafeFailure(output, errorCode, summary);
        return 1;
    }

    private static int WriteSessionConversationStoreFailure(TextWriter output, Exception exception)
    {
        (string errorCode, string summary) = GetConversationStoreFailure(exception);
        WriteSafeFailure(output, errorCode, summary);
        return 1;
    }

    private static int WriteSessionNameParseFailure(TextWriter output, ArgumentException exception)
    {
        WriteSafeFailure(output, "invalid-session-name", GetSafeSessionNameParseMessage(exception));
        return 1;
    }

    private static bool TryParseSessionName(TextWriter output, string name, out ConversationSessionName sessionName)
    {
        try
        {
            sessionName = ConversationSessionName.Parse(name);
            return true;
        }
        catch (ArgumentException exception)
        {
            WriteSessionNameParseFailure(output, exception);
            sessionName = null!;
            return false;
        }
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

    private static void WriteSessionNotFound(TextWriter output)
    {
        WriteSafeFailure(output, "session-not-found", "Session transcript was not found.");
    }

    private static void WriteSessionOptionConflict(TextWriter output)
    {
        WriteSafeFailure(output, "session-option-conflict", "Use either --session or --resume, not both.");
    }

    private static void WriteSafeFailure(TextWriter output, string errorCode, string summary)
    {
        output.WriteLine("status: failed");
        output.WriteLine($"errorCode: {errorCode}");
        output.WriteLine("summary:");
        output.WriteLine(summary);
    }

    private static void WriteJobReadFailure(
        TextWriter output,
        JobRecordDiagnostic? diagnostic,
        bool jsonOutput,
        string type)
    {
        string errorCode = diagnostic?.ErrorCode ?? "job-not-found";
        string summary = diagnostic?.Summary ?? "Job record was not found.";
        if (jsonOutput)
        {
            output.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object?>
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

        WriteSafeFailure(output, errorCode, summary);
    }

    private static void WriteCiFailure(
        TextWriter output,
        string errorCode,
        string summary,
        bool jsonOutput,
        string? jobId)
    {
        string safeErrorCode = DiagnosticSecretRedactor.Redact(errorCode);
        string safeSummary = DiagnosticSecretRedactor.Redact(summary);
        if (jsonOutput)
        {
            output.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object?>
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

        output.WriteLine("# C-AICLI CI summary");
        output.WriteLine();
        output.WriteLine("- Outcome: **config-error**");
        output.WriteLine("- Exit code: " + CiExitCodePolicy.ConfigError.ToString(CultureInfo.InvariantCulture));
        output.WriteLine("- Error code: " + safeErrorCode);
        output.WriteLine();
        output.WriteLine(safeSummary);
    }

    private static void WriteQueueFailure(
        TextWriter output,
        TaskQueueDiagnostic? diagnostic,
        bool jsonOutput,
        string type)
    {
        WriteQueueFailure(
            output,
            diagnostic?.ErrorCode ?? TaskQueueErrorCode.NotFound,
            diagnostic?.Summary ?? "Queue item was not found.",
            jsonOutput,
            type,
            diagnostic?.QueueId,
            diagnostic?.Path);
    }

    private static void WriteQueueFailure(
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
                ["queueId"] = string.IsNullOrWhiteSpace(queueId) ? null : DiagnosticSecretRedactor.Redact(queueId),
                ["path"] = string.IsNullOrWhiteSpace(path) ? null : DiagnosticSecretRedactor.Redact(path)
            }, JsonOptions));
            return;
        }

        WriteSafeFailure(output, errorCode, summary);
    }

    private static void WritePipelineFailure(
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

        WriteSafeFailure(output, errorCode, summary);
    }

    private static void WriteAutomationFailure(
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

    private static bool IsJobStoreException(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException
            or InvalidOperationException;
    }

    private static bool IsQueueStoreException(Exception exception) => IsJobStoreException(exception);

    private static void WriteSessionList(TextWriter output, IReadOnlyList<ConversationTranscriptSummary> summaries)
    {
        output.WriteLine("C# AI CLI sessions");
        if (summaries.Count == 0)
        {
            output.WriteLine("status: empty");
            return;
        }

        foreach (ConversationTranscriptSummary summary in summaries.OrderBy(summary => summary.Name, StringComparer.Ordinal))
        {
            output.WriteLine(
                $"- {summary.Name} created={summary.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)} updated={summary.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture)} turns={summary.TurnCount} toolCalls={summary.ToolCallCount}");
        }
    }

    private static int ShowSession(TextWriter output, IConversationStore conversationStore, ConversationSessionName sessionName)
    {
        if (!conversationStore.TryGetSummary(sessionName, out ConversationTranscriptSummary? summary) || summary is null)
        {
            output.WriteLine("status: failed");
            output.WriteLine("errorCode: session-not-found");
            output.WriteLine("summary:");
            output.WriteLine("Session transcript was not found.");
            return 1;
        }

        output.WriteLine("C# AI CLI session");
        output.WriteLine($"name: {summary.Name}");
        output.WriteLine($"createdAtUtc: {summary.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)}");
        output.WriteLine($"updatedAtUtc: {summary.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture)}");
        output.WriteLine($"turnCount: {summary.TurnCount}");
        output.WriteLine($"toolCallCount: {summary.ToolCallCount}");
        return 0;
    }

    private static int ExportSessionJson(TextWriter output, CliEnvironmentSnapshot snapshot, ConversationSessionName sessionName)
    {
        FileConversationStore conversationStore = FileConversationStore.Create(snapshot);
        if (!conversationStore.TryLoad(sessionName, out ConversationTranscript? transcript) || transcript is null)
        {
            WriteSessionNotFound(output);
            return 1;
        }

        string path = ResolveSessionPath(snapshot, sessionName);
        output.Write(File.ReadAllText(path));
        return 0;
    }

    private static int ExportSessionMarkdown(TextWriter output, IConversationStore conversationStore, ConversationSessionName sessionName)
    {
        if (!conversationStore.TryLoad(sessionName, out ConversationTranscript? transcript) || transcript is null)
        {
            WriteSessionNotFound(output);
            return 1;
        }

        output.WriteLine(ConversationTranscriptMarkdownFormatter.Format(transcript));
        return 0;
    }

    private static int DeleteSession(
        TextWriter output,
        IConversationStore conversationStore,
        ConversationSessionName sessionName,
        string successStatus,
        int missingExitCode,
        bool writeMissingErrorCode)
    {
        if (!conversationStore.Delete(sessionName))
        {
            output.WriteLine("status: not-found");
            if (writeMissingErrorCode)
            {
                output.WriteLine("errorCode: session-not-found");
            }

            return missingExitCode;
        }

        output.WriteLine($"status: {successStatus}");
        output.WriteLine($"session: {sessionName.Value}");
        return 0;
    }

    private static int RenameSession(
        TextWriter output,
        IConversationStore conversationStore,
        ConversationSessionName sourceSessionName,
        ConversationSessionName destinationSessionName)
    {
        if (conversationStore.Rename(sourceSessionName, destinationSessionName))
        {
            output.WriteLine("status: renamed");
            output.WriteLine($"from: {sourceSessionName.Value}");
            output.WriteLine($"to: {destinationSessionName.Value}");
            return 0;
        }

        output.WriteLine("status: failed");
        output.WriteLine("errorCode: session-rename-failed");
        output.WriteLine("summary:");
        output.WriteLine("Session could not be renamed because the source is missing or the destination already exists.");
        return 1;
    }

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

    private sealed class StaticAgentRunner(AgentError error) : IAgentRunner
    {
        public AgentRunResult Run(
            AgentRunRequest request,
            ConversationTranscript? transcript = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            AgentRunEvent errorEvent = new(
                Type: "agent.error",
                Sequence: 0,
                Timestamp: DateTimeOffset.UtcNow,
                Message: error.SafeMessage,
                ErrorCode: error.LocalErrorCode,
                Status: "failure",
                StopReason: AgentStopReason.FromErrorCode(error.LocalErrorCode));
            return AgentRunResult.Failure(
                error,
                [],
                [errorEvent],
                stopReason: AgentStopReason.FromErrorCode(error.LocalErrorCode),
                status: "failure");
        }
    }
}
