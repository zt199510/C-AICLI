using System.Text.Json;

namespace CSharpAiCli.Core;

public sealed class WorkspaceFileReadTool : ITool
{
    private const string ToolName = "workspace.read_text";

    public const long DefaultMaxFileBytes = 256 * 1024;

    private readonly IWorkspaceGuard workspaceGuard;
    private readonly long maxFileBytes;

    public WorkspaceFileReadTool(IWorkspaceGuard workspaceGuard, long maxFileBytes = DefaultMaxFileBytes)
    {
        ArgumentNullException.ThrowIfNull(workspaceGuard);
        if (maxFileBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFileBytes), "File size limit must be greater than zero.");
        }

        this.workspaceGuard = workspaceGuard;
        this.maxFileBytes = maxFileBytes;
    }

    public ToolDefinition Definition { get; } = new(
        ToolName,
        "Read a UTF-8 text file from the current workspace.",
        """{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}""",
        ToolRiskLevel.Read);

    public ToolExecutionResult Execute(
        ToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!TryReadPath(context.ArgumentsJson, out string path, out ToolExecutionResult? failure))
        {
            return failure;
        }

        WorkspaceGuardResult guardResult = workspaceGuard.ResolvePath(context.Workspace, path);
        if (!guardResult.IsAllowed)
        {
            return ToolExecutionResult.Failure(
                guardResult.ErrorCode ?? ToolErrorCode.WorkspaceBoundaryDenied,
                guardResult.SafeMessage,
                structuredPayload: CreatePathPayload(path));
        }

        string fullPath = guardResult.FullPath!;
        if (!File.Exists(fullPath))
        {
            return ToolExecutionResult.Failure(
                ToolErrorCode.FileNotFound,
                "File was not found in the workspace.",
                structuredPayload: CreatePathPayload(path));
        }

        FileInfo fileInfo = new(fullPath);
        if (fileInfo.Length > maxFileBytes)
        {
            return ToolExecutionResult.Failure(
                ToolErrorCode.FileTooLarge,
                $"File exceeds the {maxFileBytes} byte read limit.",
                structuredPayload: CreateFilePayload(path, fileInfo.Length, maxFileBytes));
        }

        if (TextFileUtilities.IsLikelyBinary(fullPath))
        {
            return ToolExecutionResult.Failure(
                ToolErrorCode.BinaryFileNotSupported,
                "Binary files cannot be read as text.",
                structuredPayload: CreateFilePayload(path, fileInfo.Length));
        }

        cancellationToken.ThrowIfCancellationRequested();
        string text = File.ReadAllText(fullPath);
        return ToolExecutionResult.Success(
            text,
            structuredPayload: ToolStructuredPayload.Create(
                ("path", path),
                ("byteCount", fileInfo.Length),
                ("characterCount", text.Length)));
    }

    private static bool TryReadPath(
        string argumentsJson,
        out string path,
        out ToolExecutionResult failure)
    {
        path = string.Empty;
        failure = null!;

        try
        {
            using JsonDocument document = JsonDocument.Parse(argumentsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                failure = ToolExecutionResult.Failure(
                    ToolErrorCode.InvalidToolArguments,
                    "Tool arguments must be a JSON object.",
                    structuredPayload: ToolStructuredPayload.InvalidArguments(ToolName, "arguments"));
                return false;
            }

            if (!document.RootElement.TryGetProperty("path", out JsonElement pathElement) ||
                pathElement.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(pathElement.GetString()))
            {
                failure = ToolExecutionResult.Failure(
                    ToolErrorCode.InvalidToolArguments,
                    "Tool arguments must include a non-empty path.",
                    structuredPayload: ToolStructuredPayload.InvalidArguments(ToolName, "path"));
                return false;
            }

            path = pathElement.GetString()!;
            return true;
        }
        catch (JsonException)
        {
            failure = ToolExecutionResult.Failure(
                ToolErrorCode.InvalidToolArguments,
                "Tool arguments must be valid JSON.",
                structuredPayload: ToolStructuredPayload.InvalidArguments(ToolName, "arguments"));
            return false;
        }
    }

    private static IReadOnlyDictionary<string, JsonElement> CreatePathPayload(string path)
    {
        return ToolStructuredPayload.Create(("path", path));
    }

    private static IReadOnlyDictionary<string, JsonElement> CreateFilePayload(
        string path,
        long byteCount,
        long? maxFileBytes = null)
    {
        return maxFileBytes is null
            ? ToolStructuredPayload.Create(
                ("path", path),
                ("byteCount", byteCount))
            : ToolStructuredPayload.Create(
                ("path", path),
                ("byteCount", byteCount),
                ("maxFileBytes", maxFileBytes.Value));
    }
}
