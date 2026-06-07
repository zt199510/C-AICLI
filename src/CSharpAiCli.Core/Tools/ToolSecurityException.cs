namespace CSharpAiCli.Core;

public sealed class ToolSecurityException : ToolExecutionException
{
    public ToolSecurityException(string errorCode, string safeMessage)
        : base(errorCode, safeMessage, retryable: false)
    {
    }
}
