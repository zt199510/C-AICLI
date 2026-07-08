using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using System.Text.Json;
using CSharpAiCli.Core;

namespace CSharpAiCli.Cli;

public static class CliCommandFactory
{
    private const string InvalidConversationTranscriptSummary =
        "Conversation transcript is missing or uses an unsupported schema version.";

    private const string SessionStoreErrorSummary =
        "Conversation session store operation failed.";

    public static RootCommand Create(TextWriter output)
    {
        return Create(
            output,
            workspacePath => CliEnvironmentSnapshot.Create(workspacePath: workspacePath),
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
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(snapshotProvider);
        ArgumentNullException.ThrowIfNull(commandLogger);
        ArgumentNullException.ThrowIfNull(chatModelClientFactory);
        ArgumentNullException.ThrowIfNull(streamingRendererFactory);
        ArgumentNullException.ThrowIfNull(conversationStoreFactory);
        ArgumentNullException.ThrowIfNull(utcNowProvider);
        ArgumentNullException.ThrowIfNull(execAgentRunnerFactory);

        RootCommand rootCommand = new($"{ProductInfo.CommandName} - {ProductInfo.Description}");
        Option<string> workspaceOption = new("--workspace")
        {
            Description = "Use a workspace directory instead of the current directory.",
            Recursive = true,
        };
        rootCommand.Options.Add(workspaceOption);

        Command versionCommand = new("version", "Print product version metadata.");
        versionCommand.SetAction(_ =>
        {
            output.WriteLine($"{ProductInfo.CommandName} {ProductInfo.Version}");
            output.WriteLine($"target framework: {ProductInfo.TargetFramework}");
            output.WriteLine($"release runtime: {ProductInfo.ReleaseRuntime}");
            return 0;
        });

        Command doctorCommand = new("doctor", "Inspect runtime, workspace, and configuration readiness.");
        doctorCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "doctor", snapshot);
            output.WriteLine(DoctorReport.Create(snapshot).ToDisplayText());
            return 0;
        });

        Command configCommand = new("config", "Inspect CLI configuration.");
        Command configGetCommand = new("get", "Print the effective configuration summary.");
        configGetCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "config get", snapshot);
            output.WriteLine(ConfigReport.Create(snapshot).ToDisplayText());
            return 0;
        });
        Command configListCommand = new("list", "List non-secret configuration values and sources.");
        configListCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "config list", snapshot);
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
            CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "config set", snapshot);

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
            CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "config unset", snapshot);

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
            CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "mcp list", snapshot);
            output.WriteLine(McpListReport.Create(snapshot).ToDisplayText());
            return 0;
        });
        mcpCommand.Subcommands.Add(mcpListCommand);
        Command mcpDoctorCommand = new("doctor", "Diagnose configured MCP servers.");
        mcpDoctorCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "mcp doctor", snapshot);
            output.WriteLine(McpDoctorReport.Create(snapshot).ToDisplayText());
            return 0;
        });
        mcpCommand.Subcommands.Add(mcpDoctorCommand);

        Command workflowCommand = new("workflow", "Inspect project workflow profiles.");
        Command workflowListCommand = new("list", "List configured workflow profiles.");
        workflowListCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "workflow list", snapshot);
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
            CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "workflow validate", snapshot);
            output.WriteLine(WorkflowValidateReport.Create(snapshot, profile).ToDisplayText());
            return 0;
        });
        workflowCommand.Subcommands.Add(workflowListCommand);
        workflowCommand.Subcommands.Add(workflowValidateCommand);

        Command toolsCommand = new("tools", "Inspect and invoke local workspace tools.");
        Command toolsListCommand = new("list", "List enabled local tools.");
        toolsListCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "tools list", snapshot);
            ToolRegistry registry = CliToolFactory.CreateRegistry(snapshot, new DefaultDenyApprovalPolicy());
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
        toolsCallCommand.Arguments.Add(toolNameArgument);
        toolsCallCommand.Arguments.Add(toolArgumentsArgument);
        toolsCallCommand.Options.Add(toolsApproveOption);
        toolsCallCommand.Options.Add(toolsApprovalOption);
        toolsCallCommand.Options.Add(toolArgumentsFileOption);
        toolsCallCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            string toolName = parseResult.GetValue(toolNameArgument) ?? string.Empty;
            string argumentsJson = parseResult.GetValue(toolArgumentsArgument) ?? "{}";
            string? argumentsFile = parseResult.GetValue(toolArgumentsFileOption);
            if (!string.IsNullOrWhiteSpace(argumentsFile))
            {
                argumentsJson = File.ReadAllText(argumentsFile);
            }

            bool approve = parseResult.GetValue(toolsApproveOption);
            string? approvalModeValue = parseResult.GetValue(toolsApprovalOption);
            CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath);
            ApprovalMode? cliApprovalMode = GetApprovalOverride(approvalModeValue, parseResult.GetResult(toolsApprovalOption), approve);
            TryWriteCommandLog(commandLogger, "tools call", snapshot);
            ToolRegistry registry = CliToolFactory.CreateRegistry(
                snapshot,
                ApprovalPolicyResolver.Resolve(snapshot.Configuration.ApprovalMode, cliApprovalMode));
            ToolExecutor executor = new(registry);
            ToolExecutionResult result = executor.Execute(
                toolName,
                new ToolExecutionContext("cli_tool_call", snapshot.Workspace, argumentsJson));

            WriteToolResult(output, result);
            return result.Succeeded ? 0 : 1;
        });
        toolsCommand.Subcommands.Add(toolsListCommand);
        toolsCommand.Subcommands.Add(toolsCallCommand);

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
            Description = "Maximum agent loop turns for agentic exec.",
        };
        Option<int?> execMaxToolCallsOption = new("--max-tool-calls")
        {
            Description = "Maximum total tool calls for agentic exec.",
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
        AddPositiveIntegerValidator(execMaxToolCallsOption, "--max-tool-calls");
        AddPositiveIntegerValidator(execTimeoutSecondsOption, "--timeout-seconds");
        execCommand.Arguments.Add(execTaskArgument);
        execCommand.Options.Add(execApproveOption);
        execCommand.Options.Add(execApprovalOption);
        execCommand.Options.Add(execJsonOption);
        execCommand.Options.Add(execOutputOption);
        execCommand.Options.Add(execMaxTurnsOption);
        execCommand.Options.Add(execMaxToolCallsOption);
        execCommand.Options.Add(execTimeoutSecondsOption);
        execCommand.Options.Add(execSessionOption);
        execCommand.Options.Add(execResumeOption);
        execCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            string task = parseResult.GetValue(execTaskArgument) ?? string.Empty;
            bool approve = parseResult.GetValue(execApproveOption);
            string? approvalModeValue = parseResult.GetValue(execApprovalOption);
            bool jsonRequested = parseResult.GetValue(execJsonOption);
            string outputMode = parseResult.GetValue(execOutputOption) ?? "text";
            int? maxTurns = parseResult.GetValue(execMaxTurnsOption);
            int? maxToolCalls = parseResult.GetValue(execMaxToolCallsOption);
            int? timeoutSeconds = parseResult.GetValue(execTimeoutSecondsOption);
            string? session = parseResult.GetValue(execSessionOption);
            string? resume = parseResult.GetValue(execResumeOption);
            bool sessionSupplied = IsOptionExplicit(parseResult, execSessionOption);
            bool resumeSupplied = IsOptionExplicit(parseResult, execResumeOption);
            CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath);
            ApprovalMode? cliApprovalMode = GetApprovalOverride(approvalModeValue, parseResult.GetResult(execApprovalOption), approve);
            TryWriteCommandLog(commandLogger, "exec", snapshot);

            if (sessionSupplied && resumeSupplied)
            {
                return WriteExecLocalValidationFailure(
                    output,
                    "session-option-conflict",
                    "Use either --session or --resume, not both.",
                    jsonRequested,
                    outputMode);
            }

            IApprovalPolicy approvalPolicy = ApprovalPolicyResolver.Resolve(snapshot.Configuration.ApprovalMode, cliApprovalMode);
            ToolRegistry registry = CliToolFactory.CreateRegistry(snapshot, approvalPolicy);
            ToolExecutor executor = new(registry);
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
                    return WriteExecLocalValidationFailure(
                        output,
                        "invalid-session-name",
                        GetSafeSessionNameParseMessage(exception),
                        jsonRequested,
                        outputMode);
                }

                try
                {
                    conversationStore = conversationStoreFactory(snapshot);
                    if (resumeSupplied)
                    {
                        if (!conversationStore.TryLoad(sessionName, out transcript) || transcript is null)
                        {
                            return WriteExecLocalValidationFailure(
                                output,
                                "session-not-found",
                                "Session transcript was not found.",
                                jsonRequested,
                                outputMode);
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
                    return WriteExecConversationStoreFailure(output, exception, jsonRequested, outputMode);
                }
            }

            AgentRunRequest request = new(
                task,
                snapshot.Workspace,
                snapshot.Instructions.Instructions,
                effectiveSession,
                Limits: new AgentRunLimits(
                    MaxTurns: maxTurns,
                    MaxToolCalls: maxToolCalls,
                    ModelCallTimeout: timeoutSeconds is null ? null : TimeSpan.FromSeconds(timeoutSeconds.Value),
                    OverallTimeout: timeoutSeconds is null ? null : TimeSpan.FromSeconds(timeoutSeconds.Value)),
                TranscriptContext: transcriptContext);

            IAgentRunner runner = execAgentRunnerFactory(snapshot, registry, executor);
            AgentRunResult agentResult = runner.Run(request, transcript);
            if (sessionName is not null && transcript is not null && conversationStore is not null)
            {
                try
                {
                    conversationStore.Save(sessionName, transcript);
                }
                catch (Exception exception) when (IsConversationStoreException(exception))
                {
                    return WriteExecConversationStoreFailure(output, exception, jsonRequested, outputMode);
                }
            }

            ExecResult execResult = AgentExecResultAdapter.FromAgentResult(agentResult);

            if (IsJsonOutputRequested(jsonRequested, outputMode))
            {
                ExecJsonRenderer renderer = new(output);
                WriteExecOutput(renderer, execResult);
            }
            else
            {
                ExecTextRenderer renderer = new(output);
                WriteExecOutput(renderer, execResult);
            }

            return execResult.ExitCode;
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
            CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "run", snapshot);
            ApprovalMode? cliApprovalMode = approve ? ApprovalMode.Always : null;
            IApprovalPolicy approvalPolicy = ApprovalPolicyResolver.Resolve(snapshot.Configuration.ApprovalMode, cliApprovalMode);
            ToolRegistry registry = CliToolFactory.CreateRegistry(snapshot, approvalPolicy);
            ToolExecutor executor = new(registry);
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
            CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "session list", snapshot);
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
            CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "session show", snapshot);
            try
            {
                return ShowSession(output, conversationStoreFactory(snapshot), name);
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
            CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "session export", snapshot);
            try
            {
                if (string.Equals(format, "markdown", StringComparison.OrdinalIgnoreCase))
                {
                    return ExportSessionMarkdown(output, conversationStoreFactory(snapshot), name);
                }

                return ExportSessionJson(output, snapshot, name);
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
            CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "session clear", snapshot);
            try
            {
                return DeleteSession(output, conversationStoreFactory(snapshot), name, "cleared", missingExitCode: 0, writeMissingErrorCode: false);
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
            CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "session delete", snapshot);
            try
            {
                return DeleteSession(output, conversationStoreFactory(snapshot), name, "deleted", missingExitCode: 1, writeMissingErrorCode: true);
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
            CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "session rename", snapshot);
            try
            {
                return RenameSession(output, conversationStoreFactory(snapshot), source, destination);
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
        chatCommand.Arguments.Add(promptArgument);
        chatCommand.Options.Add(sessionOption);
        chatCommand.Options.Add(resumeOption);
        chatCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            string prompt = parseResult.GetValue(promptArgument) ?? string.Empty;
            string? session = parseResult.GetValue(sessionOption);
            string? resume = parseResult.GetValue(resumeOption);
            bool sessionSupplied = IsOptionExplicit(parseResult, sessionOption);
            bool resumeSupplied = IsOptionExplicit(parseResult, resumeOption);
            CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "chat", snapshot);

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
        rootCommand.Subcommands.Add(configCommand);
        rootCommand.Subcommands.Add(mcpCommand);
        rootCommand.Subcommands.Add(workflowCommand);
        rootCommand.Subcommands.Add(toolsCommand);
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

        return new StaticAgentRunner(new AgentError(
            "agent-backend-unavailable",
            "Agent backend is unavailable.",
            Retryable: false));
    }

    private static bool IsExecCommand(ParseResult parseResult)
    {
        return string.Equals(parseResult.CommandResult.Command.Name, "exec", StringComparison.Ordinal);
    }

    private static bool IsSupportedApiKeySource(string apiKeySource)
    {
        return apiKeySource is "OPENAI_API_KEY" or "user config";
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

    private static int WriteExecLocalValidationFailure(
        TextWriter output,
        string errorCode,
        string summary,
        bool jsonRequested,
        string outputMode)
    {
        if (IsJsonOutputRequested(jsonRequested, outputMode))
        {
            ExecResult result = ExecResult.Failure(
                ExitCode: 1,
                Summary: summary,
                ErrorCode: errorCode,
                Events: []);
            ExecJsonRenderer renderer = new(output);
            WriteExecOutput(renderer, result);
            return result.ExitCode;
        }

        WriteSafeFailure(output, errorCode, summary);
        return 1;
    }

    private static int WriteExecConversationStoreFailure(
        TextWriter output,
        Exception exception,
        bool jsonRequested,
        string outputMode)
    {
        (string errorCode, string summary) = GetConversationStoreFailure(exception);
        return WriteExecLocalValidationFailure(output, errorCode, summary, jsonRequested, outputMode);
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

    private static int ShowSession(TextWriter output, IConversationStore conversationStore, string name)
    {
        ConversationSessionName sessionName = ConversationSessionName.Parse(name);
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

    private static int ExportSessionJson(TextWriter output, CliEnvironmentSnapshot snapshot, string name)
    {
        ConversationSessionName sessionName = ConversationSessionName.Parse(name);
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

    private static int ExportSessionMarkdown(TextWriter output, IConversationStore conversationStore, string name)
    {
        ConversationSessionName sessionName = ConversationSessionName.Parse(name);
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
        string name,
        string successStatus,
        int missingExitCode,
        bool writeMissingErrorCode)
    {
        ConversationSessionName sessionName = ConversationSessionName.Parse(name);
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

    private static int RenameSession(TextWriter output, IConversationStore conversationStore, string source, string destination)
    {
        ConversationSessionName sourceSessionName = ConversationSessionName.Parse(source);
        ConversationSessionName destinationSessionName = ConversationSessionName.Parse(destination);
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
                ErrorCode: error.LocalErrorCode);
            return AgentRunResult.Failure(error, [], [errorEvent]);
        }
    }
}
