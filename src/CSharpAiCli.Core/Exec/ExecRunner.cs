using System.Text.Json;

namespace CSharpAiCli.Core;

public sealed class ExecRunner : IExecRunner
{
    private const string SmokeNotePath = "caicli-smoke.txt";
    private const string SmokeNoteSeedContent = "status: pending";
    private readonly IApprovalPolicy approvalPolicy;
    private readonly Func<DateTimeOffset> utcNowProvider;

    public ExecRunner()
        : this(new DefaultDenyApprovalPolicy())
    {
    }

    public ExecRunner(IApprovalPolicy approvalPolicy)
        : this(approvalPolicy, null)
    {
    }

    public ExecRunner(IApprovalPolicy approvalPolicy, Func<DateTimeOffset>? utcNowProvider)
    {
        ArgumentNullException.ThrowIfNull(approvalPolicy);
        this.approvalPolicy = approvalPolicy;
        this.utcNowProvider = utcNowProvider ?? (() => DateTimeOffset.UtcNow);
    }

    public ExecResult Run(
        ExecRequest request,
        WorkspaceContext workspace,
        IToolExecutor toolExecutor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(toolExecutor);

        string trimmedTask = request.Task.Trim();
        ExecEvent started = CreateStartedEvent(trimmedTask, null);

        if (string.IsNullOrWhiteSpace(trimmedTask))
        {
            return CreateFailureResult(
                started,
                Summary: "Run task is empty.",
                ErrorCode: "empty-run-task");
        }

        if (trimmedTask.Contains("create", StringComparison.OrdinalIgnoreCase) &&
            trimmedTask.Contains("smoke", StringComparison.OrdinalIgnoreCase))
        {
            started = CreateStartedEvent(trimmedTask, CreateToolPayload("workspace.apply_patch", path: SmokeNotePath));
            ToolExecutionResult toolResult = ApplySmokeNotePatch(workspace, toolExecutor, cancellationToken);

            if (string.Equals(toolResult.ErrorCode, "file-not-found", StringComparison.Ordinal))
            {
                ToolExecutionResult seedResult = TryCreateSmokeNoteSeed(workspace, cancellationToken);
                if (!seedResult.Succeeded)
                {
                    return CreateResult(
                        started,
                        seedResult,
                        CreateToolPayload("workspace.apply_patch", path: SmokeNotePath));
                }

                toolResult = ApplySmokeNotePatch(workspace, toolExecutor, cancellationToken);
            }

            return CreateResult(
                started,
                toolResult,
                CreateToolPayload("workspace.apply_patch", path: SmokeNotePath));
        }

        if (trimmedTask.StartsWith("read ", StringComparison.OrdinalIgnoreCase))
        {
            string path = trimmedTask["read ".Length..].Trim();
            started = CreateStartedEvent(trimmedTask, CreateToolPayload("workspace.read_text", path: path));
            ToolExecutionResult toolResult = toolExecutor.Execute(
                "workspace.read_text",
                new ToolExecutionContext(
                    "cli_exec_read",
                    workspace,
                    JsonSerializer.Serialize(new Dictionary<string, string>
                    {
                        ["path"] = path
                    })),
                cancellationToken);

            return CreateResult(
                started,
                toolResult,
                CreateToolPayload("workspace.read_text", path: path));
        }

        if (trimmedTask.StartsWith("shell ", StringComparison.OrdinalIgnoreCase))
        {
            string command = trimmedTask["shell ".Length..].Trim();
            started = CreateStartedEvent(trimmedTask, CreateToolPayload("workspace.run_shell", command: command));
            ToolExecutionResult toolResult = toolExecutor.Execute(
                "workspace.run_shell",
                new ToolExecutionContext(
                    "cli_exec_shell",
                    workspace,
                    JsonSerializer.Serialize(new Dictionary<string, object>
                    {
                        ["command"] = command,
                        ["timeoutMilliseconds"] = 10_000
                    })),
                cancellationToken);

            return CreateResult(
                started,
                toolResult,
                CreateToolPayload("workspace.run_shell", command: command));
        }

        return CreateFailureResult(
            started,
            Summary: "Supported run tasks are: create smoke note, read <path>, shell <command>.",
            ErrorCode: "unsupported-run-task");
    }

    private ExecEvent CreateStartedEvent(string task, IReadOnlyDictionary<string, string>? payload)
    {
        return new ExecEvent(
            Type: "task.started",
            Sequence: 0,
            Timestamp: utcNowProvider(),
            Message: "Task started.",
            Payload: payload is null
                ? new Dictionary<string, string>
                {
                    ["task"] = task
                }
                : new Dictionary<string, string>(payload)
                {
                    ["task"] = task
                },
            Status: DiagnosticEventStatus.Started);
    }

    private ExecResult CreateResult(
        ExecEvent started,
        ToolExecutionResult toolResult,
        IReadOnlyDictionary<string, string> payload)
    {
        DateTimeOffset completedTimestamp = utcNowProvider();
        ExecEvent completed = new(
            Type: toolResult.Succeeded ? "task.completed" : "task.failed",
            Sequence: 1,
            Timestamp: completedTimestamp,
            Summary: toolResult.Summary,
            Payload: payload,
            ErrorCode: toolResult.ErrorCode,
            ApprovalStatus: toolResult.ApprovalStatus,
            Status: toolResult.Succeeded ? DiagnosticEventStatus.Success : DiagnosticEventStatus.Failure,
            DurationMs: CalculateDurationMs(started.Timestamp, completedTimestamp),
            ApprovalDurationMs: toolResult.ApprovalDurationMs);

        IReadOnlyList<ExecEvent> events = new[] { started, completed };
        return toolResult.Succeeded
            ? ExecResult.Success(
                Summary: toolResult.Summary,
                Events: events,
                ApprovalStatus: toolResult.ApprovalStatus)
            : ExecResult.Failure(
                ExitCode: 1,
                Summary: toolResult.Summary,
                ErrorCode: toolResult.ErrorCode ?? "exec-failed",
                Events: events,
                ApprovalStatus: toolResult.ApprovalStatus);
    }

    private ExecResult CreateFailureResult(
        ExecEvent started,
        string Summary,
        string ErrorCode)
    {
        DateTimeOffset failedTimestamp = utcNowProvider();
        ExecEvent failed = new(
            Type: "task.failed",
            Sequence: 1,
            Timestamp: failedTimestamp,
            Summary: Summary,
            ErrorCode: ErrorCode,
            Status: DiagnosticEventStatus.Failure,
            DurationMs: CalculateDurationMs(started.Timestamp, failedTimestamp));

        IReadOnlyList<ExecEvent> events = new[] { started, failed };
        return ExecResult.Failure(
            ExitCode: 1,
            Summary: Summary,
            ErrorCode: ErrorCode,
            Events: events);
    }

    private static IReadOnlyDictionary<string, string> CreateToolPayload(
        string toolName,
        string? path = null,
        string? command = null)
    {
        Dictionary<string, string> payload = new()
        {
            ["toolName"] = toolName
        };

        if (!string.IsNullOrWhiteSpace(path))
        {
            payload["path"] = path;
        }

        if (!string.IsNullOrWhiteSpace(command))
        {
            payload["command"] = command;
        }

        return payload;
    }

    private static long CalculateDurationMs(DateTimeOffset startedUtc, DateTimeOffset completedUtc)
    {
        return Math.Max(0, (long)(completedUtc - startedUtc).TotalMilliseconds);
    }

    private static ToolExecutionResult ApplySmokeNotePatch(
        WorkspaceContext workspace,
        IToolExecutor toolExecutor,
        CancellationToken cancellationToken)
    {
        return toolExecutor.Execute(
            "workspace.apply_patch",
            new ToolExecutionContext(
                "cli_exec_patch",
                workspace,
                """{"path":"caicli-smoke.txt","find":"status: pending","replace":"status: completed"}"""),
            cancellationToken);
    }

    private ToolExecutionResult TryCreateSmokeNoteSeed(
        WorkspaceContext workspace,
        CancellationToken cancellationToken)
    {
        DateTimeOffset approvalStartedUtc = utcNowProvider();
        ApprovalDecision approval = approvalPolicy.RequestApproval(new ApprovalRequest(
            Operation: "workspace.apply_patch",
            Summary: "Create caicli-smoke.txt seed file for smoke note patch.",
            Diff: string.Join(
                Environment.NewLine,
                [
                    "--- /dev/null",
                    "+++ b/caicli-smoke.txt",
                    "@@",
                    "+status: pending"
                ]),
            IsDirtyWorkspace: false,
            Metadata: new Dictionary<string, string>
            {
                ["path"] = SmokeNotePath,
                ["reason"] = "Smoke note seed creation writes a workspace file and requires approval."
            },
            RiskLevel: ToolRiskLevel.Write));
        long approvalDurationMs = CalculateDurationMs(approvalStartedUtc, utcNowProvider());

        if (!approval.Approved)
        {
            return ToolExecutionResult.Failure(
                "approval-denied",
                approval.SafeMessage,
                approvalStatus: approval.Status,
                approvalDurationMs: approvalDurationMs);
        }

        cancellationToken.ThrowIfCancellationRequested();

        string path = Path.Combine(workspace.RootPath, SmokeNotePath);
        if (!File.Exists(path))
        {
            File.WriteAllText(path, SmokeNoteSeedContent + Environment.NewLine);
        }

        return ToolExecutionResult.Success(
            "Created caicli-smoke.txt seed file.",
            approval.Status,
            approvalDurationMs: approvalDurationMs);
    }
}
