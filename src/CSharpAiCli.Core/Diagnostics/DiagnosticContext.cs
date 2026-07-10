namespace CSharpAiCli.Core;

public sealed record DiagnosticContext
{
    public DiagnosticContext(
        string CommandId,
        string SessionId,
        string Workspace,
        DateTimeOffset TimestampUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(CommandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(SessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(Workspace);

        this.CommandId = CommandId;
        this.SessionId = SessionId;
        this.Workspace = Workspace;
        this.TimestampUtc = TimestampUtc.ToUniversalTime();
    }

    public string CommandId { get; }

    public string SessionId { get; }

    public string Workspace { get; }

    public DateTimeOffset TimestampUtc { get; }

    public static DiagnosticContext Create(
        string workspace,
        Func<string>? commandIdProvider = null,
        Func<string>? sessionIdProvider = null,
        Func<DateTimeOffset>? utcNowProvider = null)
    {
        commandIdProvider ??= CreateId;
        sessionIdProvider ??= CreateId;
        utcNowProvider ??= () => DateTimeOffset.UtcNow;

        return new DiagnosticContext(
            CommandId: commandIdProvider(),
            SessionId: sessionIdProvider(),
            Workspace: workspace,
            TimestampUtc: utcNowProvider());
    }

    private static string CreateId()
    {
        return Guid.NewGuid().ToString("N");
    }
}
