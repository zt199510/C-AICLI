namespace CSharpAiCli.Core;

public static class McpErrorCode
{
    public const string Timeout = "mcp-timeout";
    public const string InvalidResponse = "mcp-invalid-response";
    public const string JsonRpcError = "mcp-json-rpc-error";
    public const string ProtocolVersionMismatch = "mcp-protocol-version-mismatch";
    public const string CwdDenied = "mcp-cwd-denied";
    public const string StartFailed = "mcp-start-failed";
    public const string ServerExited = "mcp-server-exited";
    public const string Cancelled = "mcp-cancelled";
    public const string ToolError = "mcp-tool-error";
    public const string ClientFailed = "mcp-client-failed";
}
