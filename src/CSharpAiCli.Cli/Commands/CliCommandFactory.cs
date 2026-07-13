using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CSharpAiCli.Core;

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
        execOutputOption.DefaultValueFactory = _ => "text";
        execOutputOption.Validators.Add(result =>
        {
            string outputMode = result.GetValueOrDefault<string>() ?? "text";
            if (!string.Equals(outputMode, "text", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(outputMode, "json", StringComparison.OrdinalIgnoreCase))
            {
                result.AddError("Invalid value for --output. Allowed values are text and json.");
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
            bool sessionSupplied = IsOptionExplicit(parseResult, execSessionOption);
            bool resumeSupplied = IsOptionExplicit(parseResult, execResumeOption);
            bool maxStepsSupplied = IsOptionExplicit(parseResult, execMaxStepsOption);
            bool maxTurnsSupplied = IsOptionExplicit(parseResult, execMaxTurnsOption);
            CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath, cwdPath);
            DiagnosticContext? traceContext = CreateTraceContext(parseResult, snapshot);
            ApprovalMode? cliApprovalMode = GetApprovalOverride(approvalModeValue, parseResult.GetResult(execApprovalOption), approve);
            TryWriteCommandLog(commandLogger, "exec", snapshot);
            WriteVerboseDiagnostics(
                parseResult,
                "exec",
                snapshot,
                humanReadableOutput: !IsJsonOutputRequested(jsonRequested, outputMode));

            int WriteExecResultWithTrace(ExecResult result)
            {
                TryWriteTraceExecResult("exec", snapshot, traceContext, result);

                if (IsJsonOutputRequested(jsonRequested, outputMode))
                {
                    ExecJsonRenderer renderer = new(output);
                    WriteExecOutput(renderer, result);
                }
                else
                {
                    ExecTextRenderer renderer = new(output);
                    WriteExecOutput(renderer, result);
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
                WriteSafeFailure(output, errorCode, summary);
                return failure.ExitCode;
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

            IApprovalPolicy approvalPolicy = ApprovalPolicyResolver.Resolve(snapshot.Configuration.ApprovalMode, cliApprovalMode);
            ToolRegistry registry = CliToolFactory.CreateRegistry(snapshot, approvalPolicy);
            ToolExecutor executor = new(registry, snapshot.Configuration.DisabledTools);
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
                WorkflowConfiguration: WorkflowProfileLoader.Load(snapshot.Configuration));

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
                referenceExecResult = referenceExecResult.WithTaskReport(
                    referenceTaskReport,
                    utcNowProvider());
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
            execResult = execResult.WithTaskReport(
                taskReport,
                utcNowProvider(),
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
        rootCommand.Subcommands.Add(reviewCommand);
        rootCommand.Subcommands.Add(configCommand);
        rootCommand.Subcommands.Add(mcpCommand);
        rootCommand.Subcommands.Add(workflowCommand);
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
