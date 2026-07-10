using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class DiagnosticContextTests
{
    [Fact]
    public void Create_uses_injected_ids_workspace_and_utc_timestamp()
    {
        DateTimeOffset hongKongTimestamp = new(2026, 7, 10, 16, 15, 0, TimeSpan.FromHours(8));

        DiagnosticContext context = DiagnosticContext.Create(
            workspace: @"D:\repo",
            commandIdProvider: () => "cmd-001",
            sessionIdProvider: () => "session-abc",
            utcNowProvider: () => hongKongTimestamp);

        Assert.Equal("cmd-001", context.CommandId);
        Assert.Equal("session-abc", context.SessionId);
        Assert.Equal(@"D:\repo", context.Workspace);
        Assert.Equal(new DateTimeOffset(2026, 7, 10, 8, 15, 0, TimeSpan.Zero), context.TimestampUtc);
        Assert.Equal(TimeSpan.Zero, context.TimestampUtc.Offset);
    }

    [Theory]
    [InlineData(null, "session-abc", @"D:\repo")]
    [InlineData("", "session-abc", @"D:\repo")]
    [InlineData("   ", "session-abc", @"D:\repo")]
    [InlineData("cmd-001", null, @"D:\repo")]
    [InlineData("cmd-001", "", @"D:\repo")]
    [InlineData("cmd-001", "   ", @"D:\repo")]
    [InlineData("cmd-001", "session-abc", null)]
    [InlineData("cmd-001", "session-abc", "")]
    [InlineData("cmd-001", "session-abc", "   ")]
    public void Constructor_rejects_missing_required_fields(
        string? commandId,
        string? sessionId,
        string? workspace)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new DiagnosticContext(
                CommandId: commandId!,
                SessionId: sessionId!,
                Workspace: workspace!,
                TimestampUtc: DateTimeOffset.UnixEpoch));
    }
}
