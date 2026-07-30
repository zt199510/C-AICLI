using CSharpAiCli.Core;

namespace CSharpAiCli.Application;

public static class TurnExecutionLimits
{
    public const int MaxEventBatchItems = 32;
    public const int MaxEventBatchBytes = 256 * 1024;
    public const int MaxAssistantPreviewBytes = 8 * 1024;
    public const int MaxApprovalSummaryBytes = 4 * 1024;
    public const int MaxFinalSummaryBytes = 16 * 1024;
    public static readonly TimeSpan ApprovalLifetime = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan CancelAcknowledgementTarget = TimeSpan.FromSeconds(5);
}

public static class TurnRuntimeEventKind
{
    public const string Plan = "plan";
    public const string Model = "model";
    public const string ProviderProgress = "provider-progress";
    public const string ToolStarted = "tool-started";
    public const string ToolCompleted = "tool-completed";
    public const string CommandStarted = "command-started";
    public const string CommandCompleted = "command-completed";
    public const string Verification = "verification";
    public const string Changes = "changes";
    public const string Assistant = "assistant";
    public const string Final = "final";
    public const string Warning = "warning";
}

public sealed record TurnRuntimeEvent(
    long EventSequence,
    string CorrelationId,
    string Kind,
    string Status,
    string Summary,
    DateTimeOffset TimestampUtc,
    string? Name = null,
    bool? Succeeded = null,
    string? ErrorCode = null,
    int? ChangedFileCount = null,
    string? OutputPointer = null,
    int? ProviderAttempt = null,
    int? MaxAdditionalRetries = null,
    string? ProviderPhase = null,
    bool? AttemptHasStreamContent = null,
    string? AssistantMessageId = null,
    string? ErrorCategory = null,
    bool? Retryable = null,
    string? SafeErrorMessage = null,
    bool RetryExhausted = false);

public sealed record InteractiveApprovalAction(
    string PolicyIdentity,
    string PolicyRevision,
    string Risk,
    string Operation,
    string TargetClass,
    string CanonicalActionSha256,
    string SafeSummary);

public sealed record InteractiveApprovalDecision(
    string Decision,
    string ClientMutationId,
    long ExpectedTurnRevision,
    long ExpectedApprovalRevision);

public sealed record DurableApprovalProjection(
    string RequestId,
    string WorkspaceId,
    string ThreadId,
    string TurnId,
    long TurnRevision,
    long ApprovalRevision,
    string PolicyIdentity,
    string PolicyRevision,
    string Risk,
    string Operation,
    string TargetClass,
    string SafeSummary,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc);

public interface ITurnExecutionEventSink
{
    ValueTask EmitAsync(TurnRuntimeEvent runtimeEvent, CancellationToken cancellationToken);
}

public interface IInteractiveApprovalGateway
{
    ValueTask<InteractiveApprovalDecision> RequestAsync(InteractiveApprovalAction action, CancellationToken cancellationToken);
}

public interface IInteractiveApprovalWaiter
{
    ValueTask<InteractiveApprovalDecision> WaitAsync(DurableApprovalProjection request, CancellationToken cancellationToken);
}

public interface ITurnExecutionRuntime
{
    Task<TurnRuntimeResult> ExecuteAsync(
        TurnExecutionInput input,
        ITurnExecutionEventSink eventSink,
        IInteractiveApprovalGateway approvalGateway,
        CancellationToken cancellationToken);
}

public sealed record TurnExecutionInput(
    CliEnvironmentSnapshot Snapshot,
    string WorkspaceId,
    string WorkspaceRootIdentity,
    string ThreadId,
    string TurnId,
    string CanonicalInputSha256,
    PendingComposerIntentRecord Intent,
    string? ResumeCheckpointId = null);

public sealed record TurnRuntimeResult(
    string Status,
    string StopReason,
    string? ErrorCode,
    string FinalSummary,
    TurnCheckpointRecord? Checkpoint = null);

public sealed record TurnStartRequest(CliEnvironmentSnapshot Snapshot, string ThreadId, long ExpectedThreadRevision, long ExpectedQueueRevision, string ClientMutationId);
public sealed record TurnCancelRequest(CliEnvironmentSnapshot Snapshot, string ThreadId, string TurnId, long ExpectedThreadRevision, long ExpectedTurnRevision, string ClientMutationId);
public sealed record ApprovalResolveRequest(CliEnvironmentSnapshot Snapshot, string ThreadId, string TurnId, string RequestId, string Decision, long ExpectedThreadRevision, long ExpectedTurnRevision, long ExpectedApprovalRevision, string ClientMutationId);
public sealed record TurnRestartRequest(CliEnvironmentSnapshot Snapshot, string ThreadId, string SourceTurnId, long ExpectedThreadRevision, long ExpectedSourceTurnRevision, bool Confirmed, string ClientMutationId);

public sealed record TurnExecutionStateProjection(
    string WorkspaceId,
    string ThreadId,
    string TurnId,
    long ThreadRevision,
    long TurnRevision,
    string Status,
    long CommittedSequence,
    bool RecoveryRequired,
    bool Idempotent,
    DurableApprovalProjection? Approval = null);

public sealed class DeterministicFakeTurnExecutionRuntime : ITurnExecutionRuntime
{
    private readonly Func<DateTimeOffset> clock;
    public DeterministicFakeTurnExecutionRuntime() : this(() => DateTimeOffset.UtcNow) { }
    internal DeterministicFakeTurnExecutionRuntime(Func<DateTimeOffset> clock) => this.clock = clock;

    public async Task<TurnRuntimeResult> ExecuteAsync(TurnExecutionInput input, ITurnExecutionEventSink eventSink,
        IInteractiveApprovalGateway approvalGateway, CancellationToken cancellationToken)
    {
        long sequence = 1;
        await Emit(TurnRuntimeEventKind.Plan, "planned", "Prepared a bounded execution plan.").ConfigureAwait(false);
        await Emit(TurnRuntimeEventKind.Model, "streaming", "Analyzing the requested change.").ConfigureAwait(false);
        if (input.Intent.Prompt.Contains("[approval]", StringComparison.OrdinalIgnoreCase))
        {
            string actionHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(input.CanonicalInputSha256 + "\0fake-write"))).ToLowerInvariant();
            InteractiveApprovalDecision decision = await approvalGateway.RequestAsync(new InteractiveApprovalAction(
                "desktop-fake-policy", "v1", "write", "apply controlled workspace change", "workspace-file",
                actionHash, "Allow the deterministic fixture to apply a controlled workspace change?"), cancellationToken).ConfigureAwait(false);
            if (decision.Decision == "deny")
                return new TurnRuntimeResult("failed", "approval-denied", "approval-denied", "The action was denied; no write was performed.");
        }
        if (input.Intent.Prompt.Contains("[pause]", StringComparison.OrdinalIgnoreCase))
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        await Emit(TurnRuntimeEventKind.ToolStarted, "running", "Started the controlled fixture tool.", "fixture.write").ConfigureAwait(false);
        await Emit(TurnRuntimeEventKind.ToolCompleted, "completed", "The controlled fixture tool completed.", "fixture.write", true).ConfigureAwait(false);
        await Emit(TurnRuntimeEventKind.Verification, "completed", "Verification completed successfully.", "fixture.verify", true).ConfigureAwait(false);
        await Emit(TurnRuntimeEventKind.Changes, "completed", "Recorded the bounded change summary.", changedFileCount: 1).ConfigureAwait(false);
        await Emit(TurnRuntimeEventKind.Final, "completed", "Task completed successfully.").ConfigureAwait(false);
        return new TurnRuntimeResult("completed", "completed", null, "Task completed successfully.");

        async ValueTask Emit(string kind, string status, string summary, string? name = null, bool? succeeded = null, int? changedFileCount = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await eventSink.EmitAsync(new TurnRuntimeEvent(sequence, $"fake-{sequence}", kind, status, summary,
                clock().ToUniversalTime(), name, succeeded, ChangedFileCount: changedFileCount), cancellationToken).ConfigureAwait(false);
            sequence++;
        }
    }
}
