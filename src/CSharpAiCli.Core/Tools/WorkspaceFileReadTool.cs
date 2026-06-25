using System.Text.Json;

namespace CSharpAiCli.Core;

public sealed class WorkspaceFileReadTool : ITool
{
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
        "workspace.read_text",
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
            return guardResult.ToFailure();
        }

        string fullPath = guardResult.FullPath!;
        if (!File.Exists(fullPath))
        {
            return ToolExecutionResult.Failure(
                "file-not-found",
                "File was not found in the workspace.");
        }

        FileInfo fileInfo = new(fullPath);
        if (fileInfo.Length > maxFileBytes)
        {
            return ToolExecutionResult.Failure(
                "file-too-large",
                $"File exceeds the {maxFileBytes} byte read limit.");
        }

        if (TextFileUtilities.IsLikelyBinary(fullPath))
        {
            return ToolExecutionResult.Failure(
                "binary-file-not-supported",
                "Binary files cannot be read as text.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        string text = File.ReadAllText(fullPath);
        return ToolExecutionResult.Success(text);
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
            if (!document.RootElement.TryGetProperty("path", out JsonElement pathElement) ||
                pathElement.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(pathElement.GetString()))
            {
                failure = ToolExecutionResult.Failure(
                    "invalid-tool-arguments",
                    "Tool arguments must include a non-empty path.");
                return false;
            }

            path = pathElement.GetString()!;
            return true;
        }
        catch (JsonException)
        {
            failure = ToolExecutionResult.Failure(
                "invalid-tool-arguments",
                "Tool arguments must be valid JSON.");
            return false;
        }
    }
}
