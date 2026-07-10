using System.Text.Json;

namespace CSharpAiCli.Core;

public sealed class WorkspaceShellTool : ITool
{
    private const string ToolName = "workspace.run_shell";

    public const int DefaultTimeoutMilliseconds = 30_000;
    public const int DefaultMaxOutputBytes = 32 * 1024;

    private readonly IShellRunner shellRunner;
    private readonly IApprovalPolicy approvalPolicy;
    private readonly ShellPolicyConfiguration shellPolicy;

    public WorkspaceShellTool(
        IShellRunner shellRunner,
        IApprovalPolicy approvalPolicy,
        ShellPolicyConfiguration? shellPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(shellRunner);
        ArgumentNullException.ThrowIfNull(approvalPolicy);

        this.shellRunner = shellRunner;
        this.approvalPolicy = approvalPolicy;
        this.shellPolicy = shellPolicy ?? ShellPolicyConfiguration.Default;
    }

    public ToolDefinition Definition { get; } = new(
        ToolName,
        "Run an approved shell command inside the current workspace.",
        """{"type":"object","properties":{"command":{"type":"string"},"cwd":{"type":"string"},"timeoutMilliseconds":{"type":"integer"},"maxStdoutBytes":{"type":"integer"},"maxStderrBytes":{"type":"integer"}},"required":["command"]}""",
        ToolRiskLevel.Shell);

    public ToolExecutionResult Execute(
        ToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!TryReadRequest(context.ArgumentsJson, out ShellCommandRequest request, out ToolExecutionResult? failure))
        {
            return failure;
        }

        DangerousCommandDetection detection = DangerousCommandDetector.Detect(request.Command);
        string commandRiskSummary = FormatCommandRiskSummary(detection);
        string? matchedRule = detection.IsDangerous ? detection.MatchedRule : null;
        ShellPolicyDecision shellPolicyDecision = ShellCommandPolicy.Evaluate(shellPolicy, request);
        if (!shellPolicyDecision.Allowed)
        {
            return ToolExecutionResult.Failure(
                ToolErrorCode.ShellPolicyDenied,
                AppendCommandRiskSummary(shellPolicyDecision.SafeMessage, commandRiskSummary),
                approvalStatus: ToolErrorCode.ShellPolicyDenied,
                structuredPayload: CreateShellPayload(
                    request,
                    ToolErrorCode.ShellPolicyDenied,
                    commandRiskSummary,
                    errorCode: ToolErrorCode.ShellPolicyDenied,
                    matchedRule: matchedRule,
                    policyDecision: shellPolicyDecision));
        }

        ToolRiskLevel riskLevel = detection.IsDangerous ? ToolRiskLevel.DangerousShell : Definition.RiskLevel;
        string approvalReason = detection.IsDangerous
            ? detection.Reason
            : "Shell command execution requires approval.";
        Dictionary<string, string> metadata = new(StringComparer.Ordinal)
        {
            ["command"] = request.Command,
            ["cwd"] = request.WorkingDirectory,
            ["reason"] = approvalReason,
            ["commandRiskSummary"] = commandRiskSummary
        };
        if (detection.IsDangerous)
        {
            metadata["matchedRule"] = detection.MatchedRule;
        }

        ApprovalDiagnosticResult approval = ApprovalDiagnostics.Request(approvalPolicy, new ApprovalRequest(
            Operation: Definition.Name,
            Summary: $"Run shell command in workspace: {request.Command}",
            Diff: null,
            IsDirtyWorkspace: false,
            Metadata: metadata,
            RiskLevel: riskLevel));

        if (!approval.Decision.Approved)
        {
            return ToolExecutionResult.Failure(
                ToolErrorCode.ApprovalDenied,
                AppendCommandRiskSummary(approval.Decision.SafeMessage, commandRiskSummary),
                approvalStatus: approval.Decision.Status,
                structuredPayload: CreateShellPayload(
                    request,
                    approval.Decision.Status,
                    commandRiskSummary,
                    errorCode: ToolErrorCode.ApprovalDenied,
                    matchedRule: matchedRule),
                approvalDurationMs: approval.DurationMs);
        }

        ShellCommandResult shellResult = shellRunner.Run(context.Workspace, request, cancellationToken);
        string summary = FormatSummary(shellResult, commandRiskSummary);
        return shellResult.Succeeded
            ? ToolExecutionResult.Success(
                summary,
                approval.Decision.Status,
                structuredPayload: CreateShellPayload(
                    request,
                    approval.Decision.Status,
                    commandRiskSummary,
                    shellResult,
                    matchedRule: matchedRule),
                approvalDurationMs: approval.DurationMs)
            : ToolExecutionResult.Failure(
                shellResult.ErrorCode ?? ToolErrorCode.ShellCommandFailed,
                summary,
                approvalStatus: approval.Decision.Status,
                structuredPayload: CreateShellPayload(
                    request,
                    approval.Decision.Status,
                    commandRiskSummary,
                    shellResult,
                    shellResult.ErrorCode ?? ToolErrorCode.ShellCommandFailed,
                    matchedRule),
                approvalDurationMs: approval.DurationMs);
    }

    private static string FormatSummary(ShellCommandResult result, string commandRiskSummary)
    {
        List<string> lines =
        [
            FormatResultSummary(result),
            $"commandRisk: {commandRiskSummary}"
        ];

        if (result.ExitCode is not null)
        {
            lines.Add($"exitCode: {result.ExitCode}");
        }

        lines.Add($"timedOut: {result.TimedOut}");
        lines.Add($"stdoutTruncated: {result.StdoutTruncated}");
        lines.Add($"stderrTruncated: {result.StderrTruncated}");
        if (!string.IsNullOrEmpty(result.Stdout))
        {
            lines.Add("stdout:");
            lines.Add(result.Stdout);
        }

        if (!string.IsNullOrEmpty(result.Stderr))
        {
            lines.Add("stderr:");
            lines.Add(result.Stderr);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatResultSummary(ShellCommandResult result)
    {
        return string.Equals(result.ErrorCode, ToolErrorCode.DangerousCommandDenied, StringComparison.Ordinal)
            ? "Shell command denied before execution."
            : result.Summary;
    }

    private static string AppendCommandRiskSummary(string summary, string commandRiskSummary)
    {
        return string.Join(Environment.NewLine, summary, $"commandRisk: {commandRiskSummary}");
    }

    private static string FormatCommandRiskSummary(DangerousCommandDetection detection)
    {
        if (!detection.IsDangerous)
        {
            return "normal shell risk: no dangerous shell pattern matched; approval may still be required.";
        }

        return string.IsNullOrWhiteSpace(detection.MatchedRule)
            ? $"dangerous shell risk: {detection.Reason}"
            : $"dangerous shell risk: {detection.Reason} Matched rule: {detection.MatchedRule}.";
    }

    private static bool TryReadRequest(
        string argumentsJson,
        out ShellCommandRequest request,
        out ToolExecutionResult failure)
    {
        request = null!;
        failure = null!;

        try
        {
            using JsonDocument document = JsonDocument.Parse(argumentsJson);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                failure = ToolExecutionResult.Failure(
                    ToolErrorCode.InvalidToolArguments,
                    "Tool arguments must be a JSON object.",
                    structuredPayload: ToolStructuredPayload.InvalidArguments(ToolName, "arguments"));
                return false;
            }

            if (!TryReadRequiredString(root, "command", out string command))
            {
                failure = ToolExecutionResult.Failure(
                    ToolErrorCode.InvalidToolArguments,
                    "Shell arguments must include a non-empty command.",
                    structuredPayload: ToolStructuredPayload.InvalidArguments(ToolName, "command"));
                return false;
            }

            string cwd = ".";
            if (root.TryGetProperty("cwd", out JsonElement cwdElement))
            {
                if (cwdElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(cwdElement.GetString()))
                {
                    failure = ToolExecutionResult.Failure(
                        ToolErrorCode.InvalidToolArguments,
                        "Shell cwd must be a non-empty string.",
                        structuredPayload: ToolStructuredPayload.InvalidArguments(ToolName, "cwd"));
                    return false;
                }

                cwd = cwdElement.GetString()!;
            }

            if (!TryReadOptionalPositiveInt(root, "timeoutMilliseconds", DefaultTimeoutMilliseconds, out int timeoutMilliseconds, out failure) ||
                !TryReadOptionalPositiveInt(root, "maxStdoutBytes", DefaultMaxOutputBytes, out int maxStdoutBytes, out failure) ||
                !TryReadOptionalPositiveInt(root, "maxStderrBytes", DefaultMaxOutputBytes, out int maxStderrBytes, out failure))
            {
                return false;
            }

            request = new ShellCommandRequest(command, cwd, timeoutMilliseconds, maxStdoutBytes, maxStderrBytes);
            return true;
        }
        catch (JsonException)
        {
            failure = ToolExecutionResult.Failure(
                ToolErrorCode.InvalidToolArguments,
                "Tool arguments must be valid JSON.",
                structuredPayload: ToolStructuredPayload.InvalidArguments(ToolName, "arguments"));
            return false;
        }
    }

    private static bool TryReadRequiredString(JsonElement root, string propertyName, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(propertyName, out JsonElement element) ||
            element.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(element.GetString()))
        {
            return false;
        }

        value = element.GetString()!;
        return true;
    }

    private static bool TryReadOptionalPositiveInt(
        JsonElement root,
        string propertyName,
        int defaultValue,
        out int value,
        out ToolExecutionResult failure)
    {
        value = defaultValue;
        failure = null!;

        if (!root.TryGetProperty(propertyName, out JsonElement element))
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out value) || value <= 0)
        {
            failure = ToolExecutionResult.Failure(
                ToolErrorCode.InvalidToolArguments,
                $"{propertyName} must be a positive integer.",
                structuredPayload: ToolStructuredPayload.InvalidArguments(ToolName, propertyName));
            return false;
        }

        return true;
    }

    private static IReadOnlyDictionary<string, JsonElement> CreateShellPayload(
        ShellCommandRequest request,
        string approvalStatus,
        string commandRiskSummary,
        ShellCommandResult? result = null,
        string? errorCode = null,
        string? matchedRule = null,
        ShellPolicyDecision? policyDecision = null)
    {
        List<(string Name, object? Value)> properties =
        [
            ("command", request.Command),
            ("cwd", request.WorkingDirectory),
            ("commandRiskSummary", commandRiskSummary),
            ("exitCode", result?.ExitCode),
            ("timedOut", result?.TimedOut ?? false),
            ("stdoutTruncated", result?.StdoutTruncated ?? false),
            ("stderrTruncated", result?.StderrTruncated ?? false),
            ("approvalStatus", approvalStatus)
        ];

        if (errorCode is not null)
        {
            properties.Add(("errorCode", errorCode));
        }

        if (!string.IsNullOrWhiteSpace(matchedRule))
        {
            properties.Add(("matchedRule", matchedRule));
        }

        if (policyDecision is not null)
        {
            properties.Add(("policyReason", policyDecision.Reason));
            if (!string.IsNullOrWhiteSpace(policyDecision.MatchedEntry))
            {
                properties.Add(("policyMatchedEntry", policyDecision.MatchedEntry));
            }

            if (!string.IsNullOrWhiteSpace(policyDecision.Source))
            {
                properties.Add(("policySource", policyDecision.Source));
            }

            if (policyDecision.MaxTimeoutMilliseconds is not null)
            {
                properties.Add(("policyMaxTimeoutMilliseconds", policyDecision.MaxTimeoutMilliseconds));
            }
        }

        return ToolStructuredPayload.Create([.. properties]);
    }
}
