using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.Json;
using CSharpAiCli.Core;

namespace CSharpAiCli.Cli;

public static class CliCommandFactory
{
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
            Description = "Resume or create a named agentic exec transcript.",
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
            CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath);
            ApprovalMode? cliApprovalMode = GetApprovalOverride(approvalModeValue, parseResult.GetResult(execApprovalOption), approve);
            TryWriteCommandLog(commandLogger, "exec", snapshot);

            IApprovalPolicy approvalPolicy = ApprovalPolicyResolver.Resolve(snapshot.Configuration.ApprovalMode, cliApprovalMode);
            ToolRegistry registry = CliToolFactory.CreateRegistry(snapshot, approvalPolicy);
            ToolExecutor executor = new(registry);
            AgentRunRequest request = new(
                task,
                snapshot.Workspace,
                snapshot.Instructions.Instructions,
                session,
                Limits: new AgentRunLimits(
                    MaxTurns: maxTurns,
                    MaxToolCalls: maxToolCalls,
                    ModelCallTimeout: timeoutSeconds is null ? null : TimeSpan.FromSeconds(timeoutSeconds.Value),
                    OverallTimeout: timeoutSeconds is null ? null : TimeSpan.FromSeconds(timeoutSeconds.Value)));
            ConversationSessionName? sessionName = null;
            ConversationTranscript? transcript = null;
            IConversationStore? conversationStore = null;
            if (!string.IsNullOrWhiteSpace(session))
            {
                sessionName = ConversationSessionName.Parse(session);
                conversationStore = conversationStoreFactory(snapshot);
                transcript = conversationStore.LoadOrCreate(sessionName, utcNowProvider());
            }

            IAgentRunner runner = execAgentRunnerFactory(snapshot, registry, executor);
            AgentRunResult agentResult = runner.Run(request, transcript);
            if (sessionName is not null && transcript is not null && conversationStore is not null)
            {
                conversationStore.Save(sessionName, transcript);
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
            IApprovalPolicy approvalPolicy = approve
                ? new AlwaysApproveApprovalPolicy()
                : new DefaultDenyApprovalPolicy();
            ToolRegistry registry = CliToolFactory.CreateRegistry(snapshot, approvalPolicy);
            ToolExecutor executor = new(registry);
            ExecRunner runner = new(approvalPolicy);
            ExecRequest request = new(task, WorkspaceRoot: snapshot.Workspace.RootPath);
            ExecResult result = runner.Run(request, snapshot.Workspace, executor);

            WriteRunResult(output, result);
            return result.ExitCode;
        });

        Command sessionCommand = new("session", "Manage local conversation transcripts.");
        Command sessionExportCommand = new("export", "Print one session transcript JSON.");
        Command sessionClearCommand = new("clear", "Delete one session transcript.");
        Argument<string> sessionNameArgument = new("name")
        {
            Description = "The session name.",
        };
        sessionExportCommand.Arguments.Add(sessionNameArgument);
        sessionClearCommand.Arguments.Add(sessionNameArgument);
        sessionExportCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            string name = parseResult.GetValue(sessionNameArgument) ?? string.Empty;
            CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "session export", snapshot);
            return ExportSession(output, snapshot, name);
        });
        sessionClearCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            string name = parseResult.GetValue(sessionNameArgument) ?? string.Empty;
            CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "session clear", snapshot);
            return ClearSession(output, snapshot, name);
        });
        sessionCommand.Subcommands.Add(sessionExportCommand);
        sessionCommand.Subcommands.Add(sessionClearCommand);

        Command chatCommand = new("chat", "Send one prompt to the configured model.");
        Argument<string> promptArgument = new("prompt")
        {
            Description = "The user message to send to the model.",
        };
        Option<string> sessionOption = new("--session")
        {
            Description = "Resume or create a named chat session.",
        };
        chatCommand.Arguments.Add(promptArgument);
        chatCommand.Options.Add(sessionOption);
        chatCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            string prompt = parseResult.GetValue(promptArgument) ?? string.Empty;
            string? session = parseResult.GetValue(sessionOption);
            CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "chat", snapshot);

            IChatModelClient chatModelClient = chatModelClientFactory(snapshot);
            IChatStreamingRenderer renderer = streamingRendererFactory(output);
            ChatRequest request = new(prompt, session, snapshot.Instructions.Instructions);

            ConversationSessionName? sessionName = null;
            ConversationTranscript? transcript = null;
            IConversationStore? conversationStore = null;
            DateTimeOffset nowUtc = default;
            if (!string.IsNullOrWhiteSpace(session))
            {
                sessionName = ConversationSessionName.Parse(session);
                conversationStore = conversationStoreFactory(snapshot);
                nowUtc = utcNowProvider();
                transcript = conversationStore.LoadOrCreate(sessionName, nowUtc);
            }

            ChatModelResult result = chatModelClient.SendStreaming(request, renderer);
            if (sessionName is not null && transcript is not null && conversationStore is not null)
            {
                ConversationTranscriptRecorder.RecordTurn(transcript, prompt, result, nowUtc);
                conversationStore.Save(sessionName, transcript);
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

    private static int ExportSession(TextWriter output, CliEnvironmentSnapshot snapshot, string name)
    {
        ConversationSessionName sessionName = ConversationSessionName.Parse(name);
        string path = ResolveSessionPath(snapshot, sessionName);
        if (!File.Exists(path))
        {
            output.WriteLine("status: failed");
            output.WriteLine("errorCode: session-not-found");
            output.WriteLine("summary:");
            output.WriteLine("Session transcript was not found.");
            return 1;
        }

        output.Write(File.ReadAllText(path));
        return 0;
    }

    private static int ClearSession(TextWriter output, CliEnvironmentSnapshot snapshot, string name)
    {
        ConversationSessionName sessionName = ConversationSessionName.Parse(name);
        string path = ResolveSessionPath(snapshot, sessionName);
        if (!File.Exists(path))
        {
            output.WriteLine("status: not-found");
            return 0;
        }

        File.Delete(path);
        output.WriteLine("status: cleared");
        output.WriteLine($"session: {sessionName.Value}");
        return 0;
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
