using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CSharpAiCli.Core;

namespace CSharpAiCli.Cli;

internal sealed class ToolsCommandModule : ICliCommandModule
{
    private const string TrustedMcpRegistrySource = "user config";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Command Create(CliCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Command command = new("tools", "Inspect and invoke local workspace tools.");
        Command listCommand = CreateListCommand(context);
        Command callCommand = CreateCallCommand(context);
        command.Subcommands.Add(listCommand);
        command.Subcommands.Add(callCommand);
        return command;
    }

    private static Command CreateListCommand(CliCommandContext context)
    {
        Command command = new("list", "List enabled local tools.");
        Option<bool> jsonOption = new("--json")
        {
            Description = "Write the tool list as JSON.",
        };
        command.Options.Add(jsonOption);
        command.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(context.GlobalOptions.Workspace);
            CliEnvironmentSnapshot snapshot = context.WorkspaceSnapshotProvider(workspacePath);
            context.TryWriteCommandLog("tools list", snapshot);
            ToolRegistry registry = CliToolFactory.CreateRegistry(snapshot, new DefaultDenyApprovalPolicy());
            bool jsonRequested = parseResult.GetValue(jsonOption);
            context.WriteVerboseDiagnostics(
                parseResult,
                "tools list",
                snapshot,
                humanReadableOutput: !jsonRequested);
            if (jsonRequested)
            {
                context.Output.WriteLine(JsonSerializer.Serialize(
                    CreateListJson(registry, snapshot.Configuration.DisabledTools),
                    JsonOptions));
                return 0;
            }

            foreach (ToolDefinition definition in registry.List().OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                context.Output.WriteLine($"{definition.Name}: {definition.Description}");
            }

            if (snapshot.Configuration.DisabledTools.Count > 0)
            {
                context.Output.WriteLine(
                    "disabledTools: " +
                    string.Join(", ", snapshot.Configuration.DisabledTools.Order(StringComparer.Ordinal)));
            }

            return 0;
        });
        return command;
    }

    private static Command CreateCallCommand(CliCommandContext context)
    {
        Command command = new("call", "Invoke one enabled local tool with a JSON argument object.");
        Argument<string> nameArgument = new("name")
        {
            Description = "The tool name to invoke.",
        };
        Argument<string> argumentsArgument = new("arguments")
        {
            Description = "JSON object arguments for the tool.",
            DefaultValueFactory = _ => "{}",
        };
        Option<bool> approveOption = new("--approve")
        {
            Description = "Approve file edit or shell actions for this call.",
        };
        Option<string> approvalOption = new("--approval")
        {
            Description = "Set approval mode for this call: never, on-request, on-failure, or always.",
        };
        AddApprovalModeValidator(approvalOption);
        Option<string> argumentsFileOption = new("--arguments-file")
        {
            Description = "Read JSON object arguments from a file.",
        };
        Option<bool> stdinOption = new("--stdin")
        {
            Description = "Read JSON object arguments from stdin.",
        };
        command.Arguments.Add(nameArgument);
        command.Arguments.Add(argumentsArgument);
        command.Options.Add(approveOption);
        command.Options.Add(approvalOption);
        command.Options.Add(argumentsFileOption);
        command.Options.Add(stdinOption);
        command.Validators.Add(result =>
        {
            if (!result.GetValue(stdinOption))
            {
                return;
            }

            if (result.GetResult(argumentsFileOption) is OptionResult { Implicit: false })
            {
                result.AddError("--stdin cannot be used with --arguments-file.");
            }

            if ((result.GetResult(argumentsArgument)?.Tokens.Count ?? 0) > 0)
            {
                result.AddError("--stdin cannot be used with positional arguments.");
            }
        });
        command.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(context.GlobalOptions.Workspace);
            string toolName = parseResult.GetValue(nameArgument) ?? string.Empty;
            string argumentsJson = parseResult.GetValue(argumentsArgument) ?? "{}";
            string? argumentsFile = parseResult.GetValue(argumentsFileOption);
            if (parseResult.GetValue(stdinOption))
            {
                argumentsJson = context.Input.ReadToEnd();
            }
            else if (!string.IsNullOrWhiteSpace(argumentsFile))
            {
                argumentsJson = File.ReadAllText(argumentsFile);
            }

            bool approve = parseResult.GetValue(approveOption);
            string? approvalModeValue = parseResult.GetValue(approvalOption);
            CliEnvironmentSnapshot snapshot = context.WorkspaceSnapshotProvider(workspacePath);
            ApprovalMode? cliApprovalMode = GetApprovalOverride(
                approvalModeValue,
                parseResult.GetResult(approvalOption),
                approve);
            context.TryWriteCommandLog("tools call", snapshot);
            context.WriteVerboseDiagnostics(parseResult, "tools call", snapshot);
            ToolRegistry registry = CliToolFactory.CreateRegistry(
                snapshot,
                ApprovalPolicyResolver.Resolve(snapshot.Configuration.ApprovalMode, cliApprovalMode));
            ToolExecutor executor = new(registry, snapshot.Configuration.DisabledTools);
            ToolExecutionResult result = executor.Execute(
                toolName,
                new ToolExecutionContext("cli_tool_call", snapshot.Workspace, argumentsJson));
            result = ReplaceUnknownMcpToolWithDiscoveryFailure(result, toolName, snapshot);

            context.WriteToolResult(result);
            return result.Succeeded ? 0 : 1;
        });
        return command;
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
                result.AddError(
                    "Invalid value for --approval. Allowed values are never, on-request, on-failure, and always.");
            }
        });
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

    private static JsonObject CreateListJson(ToolRegistry registry, IReadOnlySet<string> disabledTools)
    {
        JsonArray tools = [];
        foreach (ToolDefinition definition in registry.List().OrderBy(item => item.Name, StringComparer.Ordinal))
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
        foreach (string toolName in disabledTools.OrderBy(item => item, StringComparer.Ordinal))
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

        McpStdioClientSessionFactory sessionFactory = new(new McpStdioTransport(
            new WorkspaceGuard(),
            snapshot.Configuration.ShellPolicy));
        McpToolsListResult discovery = new McpStdioToolDiscoverer(sessionFactory)
            .DiscoverTools(server, snapshot.Workspace);
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
}
