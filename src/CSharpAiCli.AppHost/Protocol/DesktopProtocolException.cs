namespace CSharpAiCli.AppHost.Protocol;

public sealed class DesktopProtocolException : Exception
{
    public DesktopProtocolException(string errorCode, string message)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}
