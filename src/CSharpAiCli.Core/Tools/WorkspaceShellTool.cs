using System.Text.Json;

namespace CSharpAiCli.Core;

public sealed class WorkspaceShellTool : ITool
{
    public const int DefaultTimeoutMilliseconds = 30_000;
    public const int DefaultMaxOutputBytes = 32 * 1024;

    private readonly IShellRunner shellRunner;
    private readonly IApprovalPolicy approvalPolicy;

    public WorkspaceShellTool(IShellRunner shellRunner, IApprovalPolicy approvalPolicy)
    {
        ArgumentNullException.ThrowIfNull(shellRunner);
        ArgumentNullException.ThrowIfNull(approvalPolicy);

        this.shellRunner = shellRunner;
        this.approvalPolicy = approvalPolicy;
    }

    public ToolDefinition Definition { get; } = new(
        "workspace.run_shell",
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

        ApprovalDecision approval = approvalPolicy.RequestApproval(new ApprovalRequest(
            Operation: Definition.Name,
            Summary: $"Run shell command in workspace: {request.Command}",
            Diff: null,
            IsDirtyWorkspace: false,
            Metadata: new Dictionary<string, string>
            {
                ["command"] = request.Command,
                ["cwd"] = request.WorkingDirectory
            }));

        if (!approval.Approved)
        {
            return ToolExecutionResult.Failure(
                "approval-denied",
                approval.SafeMessage,
                approvalStatus: approval.Status);
        }

        ShellCommandResult shellResult = shellRunner.Run(context.Workspace, request, cancellationToken);
        string summary = FormatSummary(shellResult);
        return shellResult.Succeeded
            ? ToolExecutionResult.Success(summary, approval.Status)
            : ToolExecutionResult.Failure(
                shellResult.ErrorCode ?? "shell-command-failed",
                summary,
                approvalStatus: approval.Status);
    }

    private static string FormatSummary(ShellCommandResult result)
    {
        List<string> lines =
        [
            result.Summary
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
            if (!TryReadRequiredString(root, "command", out string command))
            {
                failure = ToolExecutionResult.Failure(
                    "invalid-tool-arguments",
                    "Shell arguments must include a non-empty command.");
                return false;
            }

            string cwd = ".";
            if (root.TryGetProperty("cwd", out JsonElement cwdElement))
            {
                if (cwdElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(cwdElement.GetString()))
                {
                    failure = ToolExecutionResult.Failure(
                        "invalid-tool-arguments",
                        "Shell cwd must be a non-empty string.");
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
                "invalid-tool-arguments",
                "Tool arguments must be valid JSON.");
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
                "invalid-tool-arguments",
                $"{propertyName} must be a positive integer.");
            return false;
        }

        return true;
    }
}
