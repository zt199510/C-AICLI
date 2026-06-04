namespace CSharpAiCli.Core;

public sealed record ChatUnavailableReport(IReadOnlyList<string> Lines)
{
    public static ChatUnavailableReport Create(CliEnvironmentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new ChatUnavailableReport(
        [
            $"{ProductInfo.DisplayName} chat",
            $"workspace: {snapshot.CurrentDirectory}",
            $"workspace status: {FormatWorkspaceStatus(snapshot.WorkspaceStatus)}",
            "status: unavailable in Phase 01",
            "planned phase: Phase 02",
            "reason: model client and streaming renderer are scheduled for weeks 5-6",
            "next: run doctor and config get to verify readiness before Phase 02"
        ]);
    }

    public string ToDisplayText()
    {
        return string.Join(Environment.NewLine, Lines);
    }

    private static string FormatWorkspaceStatus(WorkspaceStatus status)
    {
        return status switch
        {
            WorkspaceStatus.Ready => "ready",
            WorkspaceStatus.Missing => "missing",
            WorkspaceStatus.NotDirectory => "not directory",
            _ => "unknown"
        };
    }
}
