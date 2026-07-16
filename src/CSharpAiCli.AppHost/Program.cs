using CSharpAiCli.AppHost.Protocol;

try
{
    DesktopRpcServer server = new();
    await server.RunAsync(
        Console.OpenStandardInput(),
        Console.OpenStandardOutput(),
        CancellationToken.None);
    return 0;
}
catch (DesktopProtocolException exception)
{
    Console.Error.WriteLine($"apphost protocol error: {exception.ErrorCode}");
    return 2;
}
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine("apphost transport error");
    return 3;
}
