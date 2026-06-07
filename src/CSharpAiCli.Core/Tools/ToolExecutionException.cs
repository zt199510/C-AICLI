namespace CSharpAiCli.Core;

public class ToolExecutionException : Exception
{
    public ToolExecutionException(string errorCode, string safeMessage, bool retryable = false)
        : base(safeMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);

        ErrorCode = errorCode;
        SafeMessage = safeMessage;
        Retryable = retryable;
    }

    public string ErrorCode { get; }
    public string SafeMessage { get; }
    public bool Retryable { get; }
}
