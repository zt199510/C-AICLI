namespace CSharpAiCli.Core;

public static class AgentExecResultAdapter
{
    public static ExecResult FromAgentResult(AgentRunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        IReadOnlyList<ExecEvent> events = result.Events
            .Select(ToExecEvent)
            .ToArray();

        if (result.IsSuccess)
        {
            return ExecResult.Success(result.Text, events, FindLastApprovalStatus(events));
        }

        AgentError error = result.Error!;
        return ExecResult.Failure(
            ExitCode: 1,
            Summary: error.SafeMessage,
            ErrorCode: error.LocalErrorCode,
            Events: events,
            ApprovalStatus: FindLastApprovalStatus(events));
    }

    private static ExecEvent ToExecEvent(AgentRunEvent agentEvent)
    {
        return new ExecEvent(
            Type: agentEvent.Type,
            Sequence: agentEvent.Sequence,
            Timestamp: agentEvent.Timestamp,
            Message: agentEvent.Message,
            Summary: agentEvent.Summary,
            Payload: agentEvent.Payload,
            ErrorCode: agentEvent.ErrorCode,
            ApprovalStatus: agentEvent.ApprovalStatus);
    }

    private static string? FindLastApprovalStatus(IReadOnlyList<ExecEvent> events)
    {
        for (int index = events.Count - 1; index >= 0; index--)
        {
            if (!string.IsNullOrWhiteSpace(events[index].ApprovalStatus))
            {
                return events[index].ApprovalStatus;
            }
        }

        return null;
    }
}
