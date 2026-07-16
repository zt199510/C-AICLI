using System.Text;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ThreadContractTests
{
    [Fact]
    public void Typed_ids_are_lowercase_fixed_width_and_deterministic_ids_are_stable()
    {
        string threadId = ThreadIdentity.CreateThreadId();
        string turnId = ThreadIdentity.CreateTurnId();
        string itemId = ThreadIdentity.CreateItemId();

        Assert.True(ThreadIdentity.IsThreadId(threadId));
        Assert.True(ThreadIdentity.IsTurnId(turnId));
        Assert.True(ThreadIdentity.IsItemId(itemId));
        Assert.Equal(31, threadId.Length);
        Assert.Equal(
            ThreadIdentity.CreateDeterministicThreadId("session", "daily", new string('a', 64)),
            ThreadIdentity.CreateDeterministicThreadId("session", "daily", new string('a', 64)));
        Assert.False(ThreadIdentity.IsThreadId(threadId.ToUpperInvariant()));
    }

    [Theory]
    [InlineData(TurnStatus.Queued, TurnStatus.Running, true)]
    [InlineData(TurnStatus.Queued, TurnStatus.Canceled, true)]
    [InlineData(TurnStatus.Running, TurnStatus.WaitingForApproval, true)]
    [InlineData(TurnStatus.WaitingForApproval, TurnStatus.Running, true)]
    [InlineData(TurnStatus.Canceling, TurnStatus.Failed, true)]
    [InlineData(TurnStatus.Completed, TurnStatus.Running, false)]
    [InlineData(TurnStatus.Failed, TurnStatus.Completed, false)]
    public void Turn_state_machine_freezes_terminal_and_active_transitions(string current, string next, bool expected)
    {
        Assert.Equal(expected, ThreadStateMachine.CanTransitionTurn(current, next));
    }

    [Fact]
    public void Validator_rejects_non_utc_timestamps_and_utf8_title_overflow()
    {
        ThreadRecord nonUtc = CreateRecord() with
        {
            CreatedAtUtc = new DateTimeOffset(2026, 7, 16, 9, 0, 0, TimeSpan.FromHours(8))
        };
        ThreadContractException timestamp = Assert.Throws<ThreadContractException>(() => ThreadContractValidator.ValidateThread(nonUtc));
        Assert.Equal(ThreadErrorCode.ThreadRecordCorrupt, timestamp.ErrorCode);

        string oversized = new('界', ThreadPersistenceLimits.MaxTitleBytes / Encoding.UTF8.GetByteCount("界") + 1);
        ThreadContractException title = Assert.Throws<ThreadContractException>(() => ThreadContractValidator.ValidateTitle(oversized));
        Assert.Equal(ThreadErrorCode.ThreadTitleInvalid, title.ErrorCode);
    }

    [Fact]
    public void Timeline_payload_is_typed_and_source_pointer_kind_is_allowlisted()
    {
        TimelineItemRecord item = new()
        {
            ItemId = ThreadIdentity.CreateItemId(),
            ThreadId = ThreadIdentity.CreateThreadId(),
            TurnId = ThreadIdentity.CreateTurnId(),
            Sequence = 1,
            TimestampUtc = DateTimeOffset.Parse("2026-07-16T01:00:00Z"),
            Type = TimelineItemType.UserMessage,
            Source = new ThreadSourcePointerRecord { Kind = "arbitrary", SourceId = "x" },
            Status = "recorded",
            Summary = "safe",
            Payload = new TimelinePayloadRecord
            {
                Message = new TimelineMessagePayloadRecord("safe"),
                Warning = new TimelineWarningPayloadRecord("wrong-union-member")
            }
        };

        ThreadContractException exception = Assert.Throws<ThreadContractException>(() => ThreadContractValidator.ValidateTimelineItem(item));
        Assert.Equal(ThreadErrorCode.TimelineRecordCorrupt, exception.ErrorCode);
    }

    private static ThreadRecord CreateRecord()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-16T01:00:00Z");
        return new ThreadRecord
        {
            ThreadId = ThreadIdentity.CreateThreadId(),
            WorkspaceId = "ws_0123456789abcdef01234567",
            WorkspaceRootIdentity = "D:/workspace",
            Title = "Thread",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }
}
