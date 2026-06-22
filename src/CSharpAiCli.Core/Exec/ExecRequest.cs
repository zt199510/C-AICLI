namespace CSharpAiCli.Core;

public sealed record ExecRequest
{
    public ExecRequest(
        string Task,
        string? WorkspaceRoot = null,
        ExecOutputMode OutputMode = ExecOutputMode.Text,
        string? ApprovalPolicy = null,
        int? MaxTurns = null,
        int? MaxToolCalls = null,
        int? TimeoutSeconds = null)
    {
        ArgumentNullException.ThrowIfNull(Task);
        if (MaxTurns is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxTurns), "Max turns must be greater than zero.");
        }

        if (MaxToolCalls is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxToolCalls), "Max tool calls must be greater than zero.");
        }

        if (TimeoutSeconds is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(TimeoutSeconds), "Timeout seconds must be greater than zero.");
        }

        this.Task = Task;
        this.WorkspaceRoot = WorkspaceRoot;
        this.OutputMode = OutputMode;
        this.ApprovalPolicy = ApprovalPolicy;
        this.MaxTurns = MaxTurns;
        this.MaxToolCalls = MaxToolCalls;
        this.TimeoutSeconds = TimeoutSeconds;
    }

    public string Task { get; }

    public string? WorkspaceRoot { get; }

    public ExecOutputMode OutputMode { get; }

    public string? ApprovalPolicy { get; }

    public int? MaxTurns { get; }

    public int? MaxToolCalls { get; }

    public int? TimeoutSeconds { get; }
}
