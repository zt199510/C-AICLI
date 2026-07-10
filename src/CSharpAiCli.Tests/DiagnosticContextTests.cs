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

    [Fact]
    public void Create_without_providers_generates_ids_and_utc_timestamp()
    {
        DateTimeOffset before = DateTimeOffset.UtcNow;

        DiagnosticContext context = DiagnosticContext.Create(workspace: @"D:\repo");

        DateTimeOffset after = DateTimeOffset.UtcNow;
        Assert.False(string.IsNullOrWhiteSpace(context.CommandId));
        Assert.False(string.IsNullOrWhiteSpace(context.SessionId));
        Assert.Equal(@"D:\repo", context.Workspace);
        Assert.Equal(TimeSpan.Zero, context.TimestampUtc.Offset);
        Assert.True(context.TimestampUtc >= before);
        Assert.True(context.TimestampUtc <= after);
    }

    [Fact]
    public void Create_rejects_missing_workspace_before_invoking_providers()
    {
        bool commandIdProviderInvoked = false;
        bool sessionIdProviderInvoked = false;
        bool utcNowProviderInvoked = false;

        Assert.Throws<ArgumentException>(() =>
            DiagnosticContext.Create(
                workspace: "   ",
                commandIdProvider: () =>
                {
                    commandIdProviderInvoked = true;
                    throw new InvalidOperationException("Command ID provider should not be invoked.");
                },
                sessionIdProvider: () =>
                {
                    sessionIdProviderInvoked = true;
                    throw new InvalidOperationException("Session ID provider should not be invoked.");
                },
                utcNowProvider: () =>
                {
                    utcNowProviderInvoked = true;
                    throw new InvalidOperationException("Timestamp provider should not be invoked.");
                }));

        Assert.False(commandIdProviderInvoked);
        Assert.False(sessionIdProviderInvoked);
        Assert.False(utcNowProviderInvoked);
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
