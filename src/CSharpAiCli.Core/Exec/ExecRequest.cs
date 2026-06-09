namespace CSharpAiCli.Core;

public sealed record ExecRequest
{
    public ExecRequest(
        string Task,
        string? WorkspaceRoot = null,
        ExecOutputMode OutputMode = ExecOutputMode.Text,
        string? ApprovalPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(Task);

        this.Task = Task;
        this.WorkspaceRoot = WorkspaceRoot;
        this.OutputMode = OutputMode;
        this.ApprovalPolicy = ApprovalPolicy;
    }

    public string Task { get; }

    public string? WorkspaceRoot { get; }

    public ExecOutputMode OutputMode { get; }

    public string? ApprovalPolicy { get; }
}
