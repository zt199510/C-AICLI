namespace CSharpAiCli.Core;

public sealed record WorkspaceGuardResult(
    bool IsAllowed,
    string? FullPath,
    string? ErrorCode,
    string SafeMessage)
{
    public static WorkspaceGuardResult Allow(string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        return new WorkspaceGuardResult(true, fullPath, null, string.Empty);
    }

    public static WorkspaceGuardResult Deny(string errorCode, string safeMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        return new WorkspaceGuardResult(false, null, errorCode, safeMessage ?? string.Empty);
    }

    public ToolExecutionResult ToFailure()
    {
        if (IsAllowed || string.IsNullOrWhiteSpace(ErrorCode))
        {
            throw new InvalidOperationException("Allowed workspace guard results cannot be converted to failures.");
        }

        return ToolExecutionResult.Failure(ErrorCode, SafeMessage);
    }
}
